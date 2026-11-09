using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Settings;
using Serilog;

namespace DisplayKnob.Core.Volume;

/// <summary>
/// Turns changes of the Windows volume of a DDC-controlled monitor's endpoint into DDC steps, then puts the
/// Windows volume back to <see cref="AudioMonitorMapper.MirrorRestLevel"/>.
/// </summary>
/// <remarks>
/// <para>The volume keys never reach Windows (the hook swallows them), but other inputs do: the wheel over the
/// Windows volume icon, the Quick Settings slider, the mute button, other apps. Each change is applied to the
/// monitor as a relative step (Windows +2 → monitor +2), and once the input settles the Windows volume returns
/// to the rest level so the next change can be measured again.</para>
/// <para>Known limit: from the rest level, a single drag of the Windows slider can only go up by 2.
/// The wheel and repeated gestures work in both directions.</para>
/// </remarks>
public sealed class EndpointVolumeMirror : IDisposable
{
    /// <summary>Quiet time after the last Windows volume change before the rest level is restored.</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly IAudioEndpointWatcher _watcher;
    private readonly AudioMonitorMapper _mapper;
    private readonly IVolumeCommandSink _sink;
    private readonly ISettingsStore _settings;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly ITimer _settleTimer;
    private int _lastLevel = AudioMonitorMapper.MirrorRestLevel;
    private bool _lastMuted;

    /// <summary>Creates the mirror and starts listening to Windows volume changes.</summary>
    public EndpointVolumeMirror(IAudioEndpointWatcher watcher, AudioMonitorMapper mapper, IVolumeCommandSink sink, ISettingsStore settings, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _watcher = watcher;
        _mapper = mapper;
        _sink = sink;
        _settings = settings;
        _logger = logger.ForContext<EndpointVolumeMirror>();
        _settleTimer = timeProvider.CreateTimer(_ => _mapper.EnsureEndpointPinned(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher.DefaultEndpointVolumeChanged += OnEndpointVolumeChanged;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _watcher.DefaultEndpointVolumeChanged -= OnEndpointVolumeChanged;
        _settleTimer.Dispose();
    }

    private void OnEndpointVolumeChanged(object? sender, EndpointVolumeChangedEventArgs e)
    {
        lock (_lock)
        {
            var delta = e.Level - _lastLevel;
            var muteClicked = e.Muted && !_lastMuted;
            _lastLevel = e.Level;
            _lastMuted = e.Muted;

            // Our own resets, and anything while mirroring is off or the endpoint isn't a DDC monitor, only move the baseline.
            if (e.IsOwnChange
                || !_settings.Current.MirrorWindowsVolume
                || _mapper.Current is not { } target
                || target.Endpoint.Id != e.EndpointId
                || (delta == 0 && !muteClicked))
            {
                return;
            }

            if (!Apply(delta, muteClicked))
            {
                return; // DDC isn't answering: Windows keeps the change, like the pass-through keys.
            }

            _logger.Debug("Windows volume changed by {Delta}{Mute}; applied to {Monitor}", delta, muteClicked ? " (mute)" : string.Empty, target.Monitor.DisplayName);
            _settleTimer.Change(SettleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private bool Apply(int delta, bool toggleMute)
    {
        if (toggleMute && !_sink.TryPost(new VolumeCommand(VolumeAction.ToggleMute)))
        {
            return false;
        }

        // One fine (1%) command per percent: the controller clamps, coalesces the DDC writes and updates the OSD.
        var action = delta > 0 ? VolumeAction.Up : VolumeAction.Down;
        for (var i = 0; i < Math.Abs(delta); i++)
        {
            if (!_sink.TryPost(new VolumeCommand(action, Fine: true)))
            {
                return toggleMute || i > 0;
            }
        }

        return true;
    }
}
