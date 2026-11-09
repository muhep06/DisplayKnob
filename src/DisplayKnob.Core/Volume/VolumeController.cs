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
/// buttons may have changed it.</para>
/// </remarks>
public sealed class VolumeController : IVolumeCommandSink, IAsyncDisposable
{
    /// <summary>Re-read the monitor's volume if no command arrived for this long.</summary>
    public static readonly TimeSpan ResyncAfterIdle = TimeSpan.FromSeconds(10);

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
    private MonitorState? _state; // Only touched by the processing loop.

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
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    private enum MuteMode
    {
        /// <summary>VCP 0x8D works.</summary>
        Native,

        /// <summary>No working 0x8D: mute by setting the volume to 0 and restoring it later.</summary>
        Emulated,
    }

    /// <summary>Raised after every applied command. Raised on a background thread.</summary>
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <inheritdoc/>
    public bool TryPost(VolumeCommand command)
    {
        // Called from the keyboard hook: a volatile read and a lock-free enqueue, nothing else.
        if (_mapper.Current is null || _stop.IsCancellationRequested)
        {
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

        _commands.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
    }

    /// <summary>Applies one command. Exposed for tests; the loop calls it sequentially.</summary>
    internal async Task ProcessAsync(VolumeCommand command, CancellationToken cancellationToken)
    {
        var target = _mapper.Current;
        if (target is null)
        {
            return; // The target went away between the key press and now.
        }

        var state = await SyncAsync(target.Monitor, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return;
        }

        if (command.Action == VolumeAction.ToggleMute)
        {
            await SetMutedAsync(state, !state.Muted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Like macOS: any volume key unmutes.
            if (state.Muted)
            {
                await SetMutedAsync(state, false, cancellationToken).ConfigureAwait(false);
            }

            var step = command.Fine ? 1 : Math.Clamp(_settings.Current.VolumeStep, 1, 100);
            var level = Math.Clamp(state.Level + (command.Action == VolumeAction.Up ? step : -step), 0, 100);
            if (level != state.Level)
            {
                state.Level = level;
                state.Monitor.WriteVcp(VcpCodes.AudioVolume, ToRaw(state, level));
            }
        }

        state.LastUsed = _timeProvider.GetUtcNow();
        _logger.Debug("{Monitor}: volume {Level}%{Muted}", state.Monitor.DisplayName, state.Level, state.Muted ? " (muted)" : string.Empty);
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(state.Monitor.DisplayName, state.Level, state.Muted));
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
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task<MonitorState?> SyncAsync(DdcWorker monitor, CancellationToken cancellationToken)
    {
        var previous = _state;
        var sameMonitor = previous is not null && ReferenceEquals(previous.Monitor, monitor);
        if (sameMonitor && _timeProvider.GetUtcNow() - previous!.LastUsed < ResyncAfterIdle) // sameMonitor implies non-null.
        {
            return previous;
        }

        var volume = await monitor.ReadVcpAsync(VcpCodes.AudioVolume, cancellationToken).ConfigureAwait(false);
        if (volume is not { Maximum: > 0 } v)
        {
            _logger.Warning("Could not read the volume of {Monitor}; key ignored", monitor.DisplayName);
            return null;
        }

        var state = new MonitorState(monitor, v.Maximum) { LastUsed = _timeProvider.GetUtcNow() };

        if (sameMonitor && previous!.MuteMode == MuteMode.Emulated)
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
        state.Monitor.WriteVcp(VcpCodes.AudioVolume, muted ? 0 : ToRaw(state, state.Level));
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

    private (uint Min, uint Max) GetRange(MonitorState state)
    {
        var settings = _settings.Current.ForMonitor(state.Monitor.Identity.Key);
        var max = Math.Min(settings.VolumeMax ?? state.RawMax, state.RawMax);
        var min = Math.Min(settings.VolumeMin ?? 0, max);
        return (min, max);
    }

    private uint ToRaw(MonitorState state, int level)
    {
        var (min, max) = GetRange(state);
        return min + (uint)Math.Round(level * (max - min) / 100.0, MidpointRounding.AwayFromZero);
    }

    private int FromRaw(MonitorState state, uint raw)
    {
        var (min, max) = GetRange(state);
        if (raw <= min || max == min)
        {
            return 0;
        }

        return raw >= max ? 100 : (int)Math.Round((raw - min) * 100.0 / (max - min), MidpointRounding.AwayFromZero);
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

        public int Level { get; set; }

        public bool Muted { get; set; }

        public MuteMode MuteMode { get; set; }

        public DateTimeOffset LastUsed { get; set; }
    }
}
