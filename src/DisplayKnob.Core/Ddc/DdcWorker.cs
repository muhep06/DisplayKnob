using DisplayKnob.Core.Displays;
using Serilog;

namespace DisplayKnob.Core.Ddc;

/// <summary>
/// Serializes all DDC I/O for one physical monitor on a single sequential loop.
/// </summary>
/// <remarks>
/// <para>Writes are coalesced per VCP code: if several writes for the same code queue up while the
/// bus is busy, only the latest value is sent. Pending writes run before pending reads, so a read
/// observes every write requested before it.</para>
/// <para>Failed operations are retried with linear backoff; failures after the last attempt are
/// logged and reported as <see langword="null"/>/<see langword="false"/>, never thrown.</para>
/// <para>Commands are spaced (<see cref="DdcWorkerOptions.CommandSpacing"/>): a monitor drops a command that
/// follows the previous one too closely, and the call still reports success.</para>
/// </remarks>
public sealed class DdcWorker : IAsyncDisposable
{
    private readonly IMonitorController _monitor;
    private readonly TimeProvider _timeProvider;
    private readonly DdcWorkerOptions _options;
    private readonly ILogger _logger;

    private readonly Lock _lock = new();
    private readonly Dictionary<byte, uint> _pendingWrites = [];
    private readonly Queue<IReadItem> _pendingReads = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private bool _failing; // Only touched by the loop.
    private long? _lastCommandEnd; // Only touched by the loop: when the previous DDC command finished.
    private long _spacingTicks; // Written by the loop only; read from anywhere through CommandSpacing.

    // Write verification (see DdcWorkerOptions.VerifiedCodes). All of it is only touched by the loop.
    private readonly Dictionary<byte, uint> _lastKnown = []; // Last value seen or written per verified code.
    private readonly Dictionary<byte, WriteCheck> _toVerify = [];
    private readonly Dictionary<byte, int> _oddMismatches = [];
    private readonly HashSet<byte> _notVerified = [];

    /// <summary>
    /// Creates the worker and starts its loop. The worker takes ownership of <paramref name="monitor"/>.
    /// </summary>
    /// <param name="monitor">The monitor to talk to.</param>
    /// <param name="timeProvider">Clock for the spacing, retries and verification.</param>
    /// <param name="options">Retry, pacing and verification policy.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="initialSpacing">
    /// The command spacing learned for this monitor earlier, if any. It is used when it is longer than
    /// <see cref="DdcWorkerOptions.CommandSpacing"/>.
    /// </param>
    public DdcWorker(IMonitorController monitor, TimeProvider timeProvider, DdcWorkerOptions options, ILogger logger, TimeSpan? initialSpacing = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxAttempts, 1);

        _monitor = monitor;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger.ForContext<DdcWorker>();
        _spacingTicks = (initialSpacing is { } learned && learned > options.CommandSpacing ? learned : options.CommandSpacing).Ticks;
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>What the worker expects a verified code to read after its last write.</summary>
    /// <param name="Expected">The value last written.</param>
    /// <param name="Previous">The value the monitor held before it, if known: what it still reports if the write was dropped.</param>
    private readonly record struct WriteCheck(uint Expected, uint? Previous);

    private interface IReadItem
    {
        string Operation { get; }

        bool IsCompleted { get; }

        bool TryExecute(IMonitorController monitor);

        void Complete(bool succeeded);

        void Cancel();
    }

    /// <summary>
    /// Raised on the worker's loop when a write still fails after all retries. Writes are fire-and-forget,
    /// so this is the only way to learn about it.
    /// </summary>
    public event EventHandler<VcpWriteFailedEventArgs>? WriteFailed;

    /// <summary>
    /// Raised on the worker's loop after it learned that this monitor needs a longer pause between commands; the
    /// argument is the new spacing. Callers persist it so the next start doesn't have to find out again.
    /// </summary>
    public event EventHandler<TimeSpan>? CommandSpacingChanged;

    /// <summary>The pause currently kept between DDC commands.</summary>
    public TimeSpan CommandSpacing => TimeSpan.FromTicks(Volatile.Read(ref _spacingTicks));

    /// <summary>Stable identity of the monitor.</summary>
    public MonitorIdentity Identity => _monitor.Identity;

    /// <summary>Human-readable monitor name.</summary>
    public string DisplayName => _monitor.DisplayName;

    /// <summary>GDI device name of the display showing this monitor, if known.</summary>
    public string? GdiDeviceName => _monitor.GdiDeviceName;

    /// <summary>
    /// Requests a VCP write. Returns immediately; rapid repeats for the same code are coalesced.
    /// </summary>
    public void WriteVcp(byte code, uint value)
    {
        ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);

        lock (_lock)
        {
            _pendingWrites[code] = value;
        }

        _signal.Release();
    }

    /// <summary>
    /// Reads a VCP feature. Returns <see langword="null"/> when the monitor doesn't answer.
    /// </summary>
    public Task<VcpValue?> ReadVcpAsync(byte code, CancellationToken cancellationToken = default) =>
        EnqueueRead<VcpValue?>(
            $"read VCP 0x{code:X2}",
            m =>
            {
                if (!m.TryGetVcp(code, out var value))
                {
                    return (false, null);
                }

                if (Verifies(code))
                {
                    _lastKnown[code] = value.Current; // Runs on the loop, like every other use of the dictionary.
                }

                return (true, value);
            },
            cancellationToken);

    /// <summary>
    /// Reads the MCCS capabilities string. Returns <see langword="null"/> when the monitor doesn't answer.
    /// </summary>
    public Task<string?> ReadCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        EnqueueRead(
            "read capabilities",
            m => m.TryGetCapabilities(out var caps) ? (true, caps) : (false, null),
            cancellationToken);

    /// <summary>
    /// Stops the loop, cancels pending reads, and releases the monitor handle.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);

        _monitor.Dispose();
        _signal.Dispose();
        _stop.Dispose();
    }

    private Task<T?> EnqueueRead<T>(string operation, Func<IMonitorController, (bool Ok, T? Value)> read, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);

        var item = new ReadItem<T>(operation, read, cancellationToken);
        lock (_lock)
        {
            _pendingReads.Enqueue(item);
        }

        _signal.Release();
        return item.Task;
    }

    private async Task RunAsync(CancellationToken stopToken)
    {
        try
        {
            // One wait task is kept across iterations, so a signal that arrives while the loop is verifying isn't lost.
            var signal = _signal.WaitAsync(stopToken);
            while (true)
            {
                if (_toVerify.Count == 0)
                {
                    await signal.ConfigureAwait(false);
                    signal = _signal.WaitAsync(stopToken);
                    await DrainAsync(stopToken).ConfigureAwait(false);
                    continue;
                }

                // Writes have just been sent. Once nothing new has come in for a while, check that the last one stuck.
                using var quietCts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
                var quiet = Task.Delay(_options.VerifyDelay, _timeProvider, quietCts.Token);
                if (await Task.WhenAny(signal, quiet).ConfigureAwait(false) == signal)
                {
                    await quietCts.CancelAsync().ConfigureAwait(false); // The wait for quiet starts over after the new work.
                    await signal.ConfigureAwait(false);
                    signal = _signal.WaitAsync(stopToken);
                    await DrainAsync(stopToken).ConfigureAwait(false);
                }
                else
                {
                    await quiet.ConfigureAwait(false);
                    await VerifyAsync(stopToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // A bug, not a DDC failure (those are return values). Keep the app alive; this monitor stops responding.
            _logger.Error(ex, "DDC worker for {Monitor} crashed", DisplayName);
        }
        finally
        {
            CancelPendingReads();
        }
    }

    private async Task DrainAsync(CancellationToken stopToken)
    {
        while (true)
        {
            KeyValuePair<byte, uint>[] writes;
            IReadItem[] reads;
            lock (_lock)
            {
                writes = [.. _pendingWrites];
                _pendingWrites.Clear();
                reads = [.. _pendingReads];
                _pendingReads.Clear();
            }

            if (writes.Length == 0 && reads.Length == 0)
            {
                return;
            }

            try
            {
                foreach (var (code, value) in writes)
                {
                    stopToken.ThrowIfCancellationRequested();
                    if (await ExecuteWithRetryAsync($"write VCP 0x{code:X2}={value}", m => m.TrySetVcp(code, value), stopToken).ConfigureAwait(false))
                    {
                        TrackWrite(code, value);
                    }
                    else
                    {
                        WriteFailed?.Invoke(this, new VcpWriteFailedEventArgs(code, value));
                    }
                }

                foreach (var read in reads)
                {
                    stopToken.ThrowIfCancellationRequested();
                    if (read.IsCompleted)
                    {
                        continue; // Cancelled by the caller while queued.
                    }

                    var ok = await ExecuteWithRetryAsync(read.Operation, read.TryExecute, stopToken).ConfigureAwait(false);
                    read.Complete(ok);
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                // These reads were already dequeued, so CancelPendingReads won't see them.
                foreach (var read in reads)
                {
                    read.Cancel();
                }

                throw;
            }
        }
    }

    private async Task<bool> ExecuteWithRetryAsync(string operation, Func<IMonitorController, bool> attempt, CancellationToken stopToken)
    {
        for (var i = 1; ; i++)
        {
            await WaitForCommandSpacingAsync(stopToken).ConfigureAwait(false);
            var started = _timeProvider.GetTimestamp();
            var succeeded = attempt(_monitor);
            _lastCommandEnd = _timeProvider.GetTimestamp();
            _logger.Debug(
                "DDC {Operation} on {Monitor}: {Result} in {Elapsed:F0} ms",
                operation,
                DisplayName,
                succeeded ? "ok" : "failed",
                _timeProvider.GetElapsedTime(started, _lastCommandEnd.Value).TotalMilliseconds);
            if (succeeded)
            {
                if (_failing)
                {
                    _failing = false;
                    _logger.Information("{Monitor} answers DDC again", DisplayName);
                }

                return true;
            }

            if (i >= _options.MaxAttempts)
            {
                // A monitor that stopped answering (e.g. switched to another machine) fails every call; warn once.
                if (_failing)
                {
                    _logger.Debug("DDC {Operation} failed on {Monitor} after {Attempts} attempt(s)", operation, DisplayName, i);
                }
                else
                {
                    _failing = true;
                    _logger.Warning("DDC {Operation} failed on {Monitor} after {Attempts} attempt(s)", operation, DisplayName, i);
                }

                return false;
            }

            await Task.Delay(_options.RetryDelay * i, _timeProvider, stopToken).ConfigureAwait(false);
        }
    }

    // See DdcWorkerOptions.CommandSpacing: a monitor ignores a command that comes too soon after the previous one.
    private async Task WaitForCommandSpacingAsync(CancellationToken stopToken)
    {
        var spacing = CommandSpacing;
        if (_lastCommandEnd is not { } last || spacing <= TimeSpan.Zero)
        {
            return;
        }

        var wait = spacing - _timeProvider.GetElapsedTime(last);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _timeProvider, stopToken).ConfigureAwait(false);
        }
    }

    private bool Verifies(byte code) =>
        _options.VerifyDelay > TimeSpan.Zero && _options.VerifiedCodes is { } codes && codes.Contains(code);

    // A write went out: remember what the monitor should now report, and what it reported before.
    private void TrackWrite(byte code, uint value)
    {
        if (!Verifies(code) || _notVerified.Contains(code))
        {
            return;
        }

        // If the previous write of this series was dropped, the monitor still reports the one before it.
        uint? previous = _toVerify.TryGetValue(code, out var pending) ? pending.Expected
            : _lastKnown.TryGetValue(code, out var known) ? known
            : null;
        _toVerify[code] = new WriteCheck(value, previous);
        _lastKnown[code] = value;
    }

    // The writes have stopped: read back what was written last. Runs on the loop.
    private async Task VerifyAsync(CancellationToken stopToken)
    {
        foreach (var (code, check) in _toVerify.ToArray())
        {
            lock (_lock)
            {
                if (_pendingWrites.ContainsKey(code))
                {
                    continue; // A newer write is waiting; that one will be checked instead.
                }
            }

            _toVerify.Remove(code);
            await VerifyCodeAsync(code, check, stopToken).ConfigureAwait(false);

            lock (_lock)
            {
                if (_pendingWrites.Count > 0 || _pendingReads.Count > 0)
                {
                    return; // Real work came in; the rest is checked the next time it's quiet.
                }
            }
        }
    }

    private async Task VerifyCodeAsync(byte code, WriteCheck check, CancellationToken stopToken)
    {
        var actual = await ReadBackAsync(code, stopToken).ConfigureAwait(false);
        if (actual is null)
        {
            return; // The monitor isn't answering; the callers find out through their own reads and writes.
        }

        if (actual == check.Expected)
        {
            _oddMismatches.Remove(code);
            return;
        }

        // A dropped write leaves the monitor at the value before it. Anything else (a monitor that rounds or clamps,
        // or someone pressing its buttons) says nothing about timing, so the spacing is left alone.
        if (check.Previous is { } previous && actual != previous)
        {
            var odd = _oddMismatches.GetValueOrDefault(code) + 1;
            _oddMismatches[code] = odd;
            if (odd >= 2)
            {
                _notVerified.Add(code);
                _logger.Debug("{Monitor} reports other values than written for VCP 0x{Code:X2}; not verifying it any more", DisplayName, code);
            }

            return;
        }

        var startSpacing = CommandSpacing;
        for (var attempt = 1; attempt <= MaxResends; attempt++)
        {
            GrowSpacing();
            if (!await ExecuteWithRetryAsync($"re-send VCP 0x{code:X2}={check.Expected}", m => m.TrySetVcp(code, check.Expected), stopToken).ConfigureAwait(false))
            {
                SetSpacing(startSpacing);
                return;
            }

            actual = await ReadBackAsync(code, stopToken).ConfigureAwait(false);
            if (actual == check.Expected)
            {
                _oddMismatches.Remove(code);
                _lastKnown[code] = check.Expected;
                _logger.Information(
                    "{Monitor} ignored a DDC write sent {Start} ms after the previous command; keeping {Spacing} ms between commands",
                    DisplayName,
                    startSpacing.TotalMilliseconds,
                    CommandSpacing.TotalMilliseconds);
                CommandSpacingChanged?.Invoke(this, CommandSpacing);
                return;
            }

            if (actual is null)
            {
                SetSpacing(startSpacing);
                return;
            }
        }

        // A longer pause didn't help, so the pause isn't the problem: undo it and stop checking this code.
        SetSpacing(startSpacing);
        _notVerified.Add(code);
        _logger.Warning("{Monitor} doesn't apply writes to VCP 0x{Code:X2} even after longer pauses", DisplayName, code);
    }

    private const int MaxResends = 3;

    private async Task<uint?> ReadBackAsync(byte code, CancellationToken stopToken)
    {
        uint? value = null;
        var ok = await ExecuteWithRetryAsync(
            $"verify VCP 0x{code:X2}",
            m =>
            {
                if (!m.TryGetVcp(code, out var read))
                {
                    return false;
                }

                value = read.Current;
                return true;
            },
            stopToken).ConfigureAwait(false);

        if (ok && value is { } current)
        {
            _lastKnown[code] = current;
        }

        return ok ? value : null;
    }

    // Half again, at least 40 ms more, up to the limit: 80, 120, 180, 270, 400 ms.
    private void GrowSpacing()
    {
        var current = CommandSpacing;
        var next = current + TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(40).Ticks, current.Ticks / 2));
        SetSpacing(next < _options.MaxCommandSpacing ? next : TimeSpan.FromTicks(Math.Max(current.Ticks, _options.MaxCommandSpacing.Ticks)));
    }

    private void SetSpacing(TimeSpan spacing) => Volatile.Write(ref _spacingTicks, spacing.Ticks);

    private void CancelPendingReads()
    {
        IReadItem[] reads;
        lock (_lock)
        {
            reads = [.. _pendingReads];
            _pendingReads.Clear();
        }

        foreach (var read in reads)
        {
            read.Cancel();
        }
    }

    private sealed class ReadItem<T> : IReadItem
    {
        private readonly Func<IMonitorController, (bool Ok, T? Value)> _read;
        private readonly TaskCompletionSource<T?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        private T? _value;

        public ReadItem(string operation, Func<IMonitorController, (bool Ok, T? Value)> read, CancellationToken cancellationToken)
        {
            Operation = operation;
            _read = read;
            _registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        }

        public string Operation { get; }

        public bool IsCompleted => _completion.Task.IsCompleted;

        public Task<T?> Task => _completion.Task;

        public bool TryExecute(IMonitorController monitor)
        {
            var (ok, value) = _read(monitor);
            _value = value;
            return ok;
        }

        public void Complete(bool succeeded)
        {
            _registration.Dispose();
            _completion.TrySetResult(succeeded ? _value : default);
        }

        public void Cancel()
        {
            _registration.Dispose();
            _completion.TrySetCanceled();
        }
    }
}
