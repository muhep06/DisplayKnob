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

    /// <summary>
    /// Creates the worker and starts its loop. The worker takes ownership of <paramref name="monitor"/>.
    /// </summary>
    public DdcWorker(IMonitorController monitor, TimeProvider timeProvider, DdcWorkerOptions options, ILogger logger)
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
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

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
            m => m.TryGetVcp(code, out var value) ? (true, value) : (false, null),
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
            while (true)
            {
                await _signal.WaitAsync(stopToken).ConfigureAwait(false);
                await DrainAsync(stopToken).ConfigureAwait(false);
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
                    if (!await ExecuteWithRetryAsync($"write VCP 0x{code:X2}={value}", m => m.TrySetVcp(code, value), stopToken).ConfigureAwait(false))
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
            if (attempt(_monitor))
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
