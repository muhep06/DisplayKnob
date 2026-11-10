using System.Threading.Channels;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Settings;
using Serilog;

namespace DisplayKnob.Core.Volume;

/// <summary>
/// Applies volume commands to the current target monitor over DDC.
/// </summary>
/// <remarks>
/// <para>The volume is tracked on a 0–100 user scale that maps onto the monitor's usable VCP range
/// (<see cref="MonitorSettings.VolumeMin"/>/<see cref="MonitorSettings.VolumeMax"/>). Steps update the
/// tracked level immediately and queue a coalesced DDC write, so holding a key never waits on the bus.</para>
/// <para>Before the first step, and again after <see cref="ResyncAfterIdle"/> without use, the current value
/// is read from the monitor, because another machine (e.g. a Mac on the same monitor) or the monitor's own
/// buttons may have changed it. It is also re-read whenever the target changes (monitors re-enumerated after a
/// display change, KVM switch or resume), and every <see cref="DisplayKnobSettings.VolumePollSeconds"/> while idle.</para>
/// <para>When the target stops answering DDC, the keys pass through to Windows so volume control keeps working.
/// The monitor is re-checked with a growing interval, and on a key press, until it answers again.</para>
/// </remarks>
public sealed class VolumeController : IVolumeCommandSink, IAsyncDisposable
{
    /// <summary>Re-read the monitor's volume if no command arrived for this long.</summary>
    public static readonly TimeSpan ResyncAfterIdle = TimeSpan.FromSeconds(3);

    /// <summary>First re-check of a monitor that stopped answering; doubles up to <see cref="MaxProbeDelay"/>.</summary>
    public static readonly TimeSpan FirstProbeDelay = TimeSpan.FromSeconds(5);

    /// <summary>Longest interval between re-checks of a monitor that doesn't answer.</summary>
    public static readonly TimeSpan MaxProbeDelay = TimeSpan.FromMinutes(5);

    /// <summary>A key press re-checks a non-answering monitor at most this often.</summary>
    public static readonly TimeSpan KeyProbeInterval = TimeSpan.FromSeconds(5);

    // MCCS 0x8D: 1 = muted, 2 = unmuted. Some monitors use (and report) 0 for unmuted.
    private const uint MuteOn = 1;
    private const uint MuteOff = 2;
    private const uint MuteOffAlternative = 0;

    private readonly AudioMonitorMapper _mapper;
    private readonly ISettingsStore _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<VolumeCommand> _commands = Channel.CreateUnbounded<VolumeCommand>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly Lock _healthLock = new();
    private readonly ITimer _probeTimer;
    private readonly ITimer _pollTimer;
    private long _lastActivity; // Timestamp of the last processed command.
    private int _pollSeconds = -1;
    private DdcWorker? _unresponsive; // The monitor that stopped answering; written under _healthLock.
    private TimeSpan _probeDelay;
    private long _lastKeyProbe;
    private MonitorState? _state; // Only touched by the processing loop.
    private DdcWorker? _watchedWorker; // Only touched by the processing loop.

    /// <summary>Creates the controller and starts its processing loop.</summary>
    public VolumeController(AudioMonitorMapper mapper, ISettingsStore settings, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _mapper = mapper;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger.ForContext<VolumeController>();
        _probeDelay = FirstProbeDelay;
        _probeTimer = _timeProvider.CreateTimer(_ => PostRefresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _pollTimer = _timeProvider.CreateTimer(_ => OnPoll(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        UpdatePollTimer();
        _loop = Task.Run(() => RunAsync(_stop.Token));
        _mapper.TargetChanged += OnTargetChanged;
        _settings.Changed += OnSettingsChanged;
    }

    private enum MuteMode
    {
        /// <summary>VCP 0x8D works.</summary>
        Native,

        /// <summary>No working 0x8D: mute by setting the volume to 0 and restoring it later.</summary>
        Emulated,
    }

    /// <summary>Raised after every applied command and every re-read. Raised on a background thread.</summary>
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <summary>Raised when <see cref="IsTargetResponding"/> may have changed. Raised on a background thread.</summary>
    public event EventHandler? TargetRespondingChanged;

    /// <summary>Raised after the loop finished a command (tests use it to wait for background work).</summary>
    internal event EventHandler<VolumeCommand>? CommandProcessed;

    /// <summary>
    /// <see langword="false"/> while the current target doesn't answer DDC (the keys then pass through to Windows).
    /// </summary>
    public bool IsTargetResponding => _mapper.Current is not { } target || !ReferenceEquals(target.Monitor, Volatile.Read(ref _unresponsive));

    /// <inheritdoc/>
    public bool TryPost(VolumeCommand command)
    {
        // Called from the keyboard hook: volatile reads and a lock-free enqueue, nothing else.
        if (_mapper.Current is not { } target || _stop.IsCancellationRequested)
        {
            return false;
        }

        if (ReferenceEquals(target.Monitor, Volatile.Read(ref _unresponsive)))
        {
            // Let Windows handle this key, but use it as a hint to check whether the monitor is back.
            var now = _timeProvider.GetTimestamp();
            var last = Interlocked.Read(ref _lastKeyProbe);
            if ((last == 0 || _timeProvider.GetElapsedTime(last, now) >= KeyProbeInterval)
                && Interlocked.CompareExchange(ref _lastKeyProbe, now, last) == last)
            {
                PostRefresh();
            }

            return false;
        }

        return _commands.Writer.TryWrite(command);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _mapper.TargetChanged -= OnTargetChanged;
        _settings.Changed -= OnSettingsChanged;
        await _probeTimer.DisposeAsync().ConfigureAwait(false);
        await _pollTimer.DisposeAsync().ConfigureAwait(false);
        _commands.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _stop.Dispose();

        if (_watchedWorker is not null)
        {
            _watchedWorker.WriteFailed -= OnWriteFailed;
        }
    }

    /// <summary>Applies one command. Exposed for tests; the loop calls it sequentially.</summary>
    internal async Task ProcessAsync(VolumeCommand command, CancellationToken cancellationToken)
    {
        var target = _mapper.Current;
        if (target is null)
        {
            return; // The target went away between the key press and now.
        }

        var refresh = command.Action == VolumeAction.Refresh;
        var previous = _state;
        var state = await SyncAsync(target.Monitor, force: refresh, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return;
        }

        if (refresh)
        {
            // Polling re-reads every few seconds; only report what actually changed.
            if (!SameReading(previous, state))
            {
                VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(state.Monitor.DisplayName, state.Monitor.GdiDeviceName, state.Level, state.Muted, userInitiated: false, state.Monitor.Identity.Key));
            }

            return;
        }

        Interlocked.Exchange(ref _lastActivity, _timeProvider.GetTimestamp());

        if (command.Action == VolumeAction.ToggleMute)
        {
            await SetMutedAsync(state, !state.Muted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Like macOS: any volume key unmutes. So does moving a slider.
            if (state.Muted)
            {
                await SetMutedAsync(state, false, cancellationToken).ConfigureAwait(false);
            }

            var step = command.Fine ? 1 : Math.Clamp(_settings.Current.VolumeStep, 1, 100);
            var level = command.Action switch
            {
                VolumeAction.Set => Math.Clamp(command.Level, 0, 100),
                VolumeAction.Up => Math.Clamp(state.Level + step, 0, 100),
                _ => Math.Clamp(state.Level - step, 0, 100),
            };
            // Also write when the monitor sits above the volume limit (set from its own buttons, or from another
            // machine): at level 100 the level doesn't change, but the sound is louder than the limit allows.
            var raw = ToRaw(state, level);
            if (level != state.Level || raw != state.Raw)
            {
                state.Level = level;
                state.Raw = raw;
                state.Monitor.WriteVcp(VcpCodes.AudioVolume, raw);
            }
        }

        state.LastUsed = _timeProvider.GetUtcNow();
        _logger.Debug("{Monitor}: volume {Level}%{Muted}", state.Monitor.DisplayName, state.Level, state.Muted ? " (muted)" : string.Empty);

        // A slider already shows the level it sets; the OSD would only cover the screen while dragging.
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(state.Monitor.DisplayName, state.Monitor.GdiDeviceName, state.Level, state.Muted, userInitiated: command.Action != VolumeAction.Set, state.Monitor.Identity.Key));
    }

    private async Task RunAsync(CancellationToken stopToken)
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(stopToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessAsync(command, stopToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A bug in one command must not stop volume control for the rest of the session.
                    _logger.Error(ex, "Volume command {Command} failed", command);
                }

                CommandProcessed?.Invoke(this, command);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task<MonitorState?> SyncAsync(DdcWorker monitor, bool force, CancellationToken cancellationToken)
    {
        WatchWrites(monitor);

        var previous = _state;
        if (!force && previous is not null && ReferenceEquals(previous.Monitor, monitor) && _timeProvider.GetUtcNow() - previous.LastUsed < ResyncAfterIdle)
        {
            return previous;
        }

        var volume = await monitor.ReadVcpAsync(VcpCodes.AudioVolume, cancellationToken).ConfigureAwait(false);
        if (volume is not { Maximum: > 0 } v)
        {
            MarkUnresponsive(monitor);
            return null;
        }

        MarkResponding(monitor);
        var state = new MonitorState(monitor, v.Maximum) { Raw = v.Current, LastUsed = _timeProvider.GetUtcNow() };

        // Re-enumeration creates a new worker for the same monitor; compare identities so an emulated mute survives.
        var sameMonitor = previous is not null && previous.Monitor.Identity.Key == monitor.Identity.Key;
        if (sameMonitor && previous!.MuteMode == MuteMode.Emulated) // sameMonitor implies non-null.
        {
            // Our emulated mute leaves the volume at 0; keep the remembered level unless someone changed it since.
            state.MuteMode = MuteMode.Emulated;
            state.Muted = previous.Muted && v.Current == 0;
            state.Level = state.Muted ? previous.Level : FromRaw(state, v.Current);
        }
        else
        {
            var mute = await monitor.ReadVcpAsync(VcpCodes.AudioMute, cancellationToken).ConfigureAwait(false);
            state.MuteMode = mute is null ? MuteMode.Emulated : MuteMode.Native;
            state.Muted = mute?.Current == MuteOn;
            state.Level = FromRaw(state, v.Current);
        }

        _state = state;
        if (SameReading(previous, state))
        {
            return state;
        }

        _logger.Debug(
            "Synced {Monitor}: raw volume {Raw}/{Max} = {Level}%, muted={Muted}, mute={MuteMode}",
            monitor.DisplayName,
            v.Current,
            v.Maximum,
            state.Level,
            state.Muted,
            state.MuteMode);
        return state;
    }

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        // A new target (or a new worker for the same monitor after re-enumeration): its volume may have changed
        // while we couldn't see it, e.g. on the other side of a KVM.
        if (_mapper.Current is not null)
        {
            PostRefresh();
        }

        TargetRespondingChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameReading(MonitorState? a, MonitorState b) =>
        a is not null && ReferenceEquals(a.Monitor, b.Monitor) && a.Level == b.Level && a.Muted == b.Muted;

    private void UpdatePollTimer()
    {
        var seconds = Math.Max(0, _settings.Current.VolumePollSeconds);
        if (Interlocked.Exchange(ref _pollSeconds, seconds) == seconds)
        {
            return;
        }

        var period = seconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(seconds);
        _pollTimer.Change(period, period);
    }

    // Light polling: picks up changes made with the monitor's own buttons or by another machine. Skipped while
    // the user is pressing keys (we know the value then) and while the monitor isn't answering (the probe handles that).
    private void OnPoll()
    {
        var last = Interlocked.Read(ref _lastActivity);
        if (_mapper.Current is null
            || !IsTargetResponding
            || (last != 0 && _timeProvider.GetElapsedTime(last) < TimeSpan.FromSeconds(Volatile.Read(ref _pollSeconds))))
        {
            return;
        }

        PostRefresh();
    }

    // The usable range may have changed, which changes what the current raw value means on the 0–100 scale.
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        UpdatePollTimer();
        if (_mapper.Current is not null)
        {
            PostRefresh();
        }
    }

    private void PostRefresh() => _commands.Writer.TryWrite(new VolumeCommand(VolumeAction.Refresh));

    private void WatchWrites(DdcWorker monitor)
    {
        if (ReferenceEquals(_watchedWorker, monitor))
        {
            return;
        }

        if (_watchedWorker is not null)
        {
            _watchedWorker.WriteFailed -= OnWriteFailed;
        }

        monitor.WriteFailed += OnWriteFailed;
        _watchedWorker = monitor;
    }

    // Raised on the monitor's worker loop. Mute writes are left out: a failing 0x8D just means "emulate mute".
    private void OnWriteFailed(object? sender, VcpWriteFailedEventArgs e)
    {
        if (e.Code == VcpCodes.AudioVolume && sender is DdcWorker monitor)
        {
            MarkUnresponsive(monitor);
        }
    }

    private void MarkUnresponsive(DdcWorker monitor)
    {
        bool newOutage;
        TimeSpan delay;
        lock (_healthLock)
        {
            newOutage = !ReferenceEquals(_unresponsive, monitor);
            _probeDelay = newOutage ? FirstProbeDelay : TimeSpan.FromTicks(Math.Min(_probeDelay.Ticks * 2, MaxProbeDelay.Ticks));
            delay = _probeDelay;
            Volatile.Write(ref _unresponsive, monitor);
            _probeTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        if (newOutage)
        {
            _logger.Warning("{Monitor} doesn't answer DDC; volume keys control Windows until it does", monitor.DisplayName);
            TargetRespondingChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _logger.Debug("{Monitor} still doesn't answer DDC; next check in {Delay}", monitor.DisplayName, delay);
        }
    }

    private void MarkResponding(DdcWorker monitor)
    {
        lock (_healthLock)
        {
            if (!ReferenceEquals(_unresponsive, monitor))
            {
                return;
            }

            Volatile.Write(ref _unresponsive, null);
            _probeDelay = FirstProbeDelay;
            _probeTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        _logger.Information("{Monitor} answers DDC again; volume keys control the monitor", monitor.DisplayName);

        // Keys that went to Windows meanwhile may have lowered the software volume.
        _mapper.EnsureEndpointPinned();
        TargetRespondingChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SetMutedAsync(MonitorState state, bool muted, CancellationToken cancellationToken)
    {
        if (state.MuteMode == MuteMode.Native)
        {
            if (await TryWriteNativeMuteAsync(state.Monitor, muted, cancellationToken).ConfigureAwait(false))
            {
                state.Muted = muted;
                return;
            }

            _logger.Information("{Monitor} doesn't apply VCP 0x8D writes; emulating mute with volume 0", state.Monitor.DisplayName);
            state.MuteMode = MuteMode.Emulated;
        }

        // Raw 0 is silent regardless of the user range; the tracked level is what unmute restores.
        state.Raw = muted ? 0 : ToRaw(state, state.Level);
        state.Monitor.WriteVcp(VcpCodes.AudioVolume, state.Raw);
        state.Muted = muted;
    }

    // Verifies by reading back, because some monitors accept the write and ignore it.
    private static async Task<bool> TryWriteNativeMuteAsync(DdcWorker monitor, bool muted, CancellationToken cancellationToken)
    {
        uint[] candidates = muted ? [MuteOn] : [MuteOff, MuteOffAlternative];
        foreach (var value in candidates)
        {
            monitor.WriteVcp(VcpCodes.AudioMute, value);
            var readBack = await monitor.ReadVcpAsync(VcpCodes.AudioMute, cancellationToken).ConfigureAwait(false);
            if (readBack is { } r && (r.Current == MuteOn) == muted)
            {
                return true;
            }
        }

        return false;
    }

    private (uint Min, uint Max) GetRange(MonitorState state) =>
        _settings.Current.ForMonitor(state.Monitor.Identity.Key).GetVolumeRange(state.RawMax);

    private uint ToRaw(MonitorState state, int level)
    {
        var (min, max) = GetRange(state);
        return VcpScale.ToRaw(level, min, max);
    }

    private int FromRaw(MonitorState state, uint raw)
    {
        var (min, max) = GetRange(state);
        return VcpScale.ToPercent(raw, min, max);
    }

    private sealed class MonitorState
    {
        public MonitorState(DdcWorker monitor, uint rawMax)
        {
            Monitor = monitor;
            RawMax = rawMax;
        }

        public DdcWorker Monitor { get; }

        public uint RawMax { get; }

        /// <summary>The raw VCP 0x62 value as last read or written.</summary>
        public uint Raw { get; set; }

        public int Level { get; set; }

        public bool Muted { get; set; }

        public MuteMode MuteMode { get; set; }

        public DateTimeOffset LastUsed { get; set; }
    }
}
