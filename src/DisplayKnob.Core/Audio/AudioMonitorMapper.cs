using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Settings;
using Serilog;

namespace DisplayKnob.Core.Audio;

/// <summary>
/// Decides which monitor (if any) the default audio endpoint plays through.
/// </summary>
/// <remarks>
/// Re-evaluates whenever the default endpoint, the monitor set, or the settings change.
/// A manual override in settings wins over the automatic name match.
/// </remarks>
public sealed class AudioMonitorMapper : IDisposable
{
    private readonly IAudioEndpointWatcher _watcher;
    private readonly MonitorRegistry _registry;
    private readonly ISettingsStore _settings;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private VolumeTarget? _current;
    private bool _resolvedOnce;

    /// <summary>Creates the mapper and computes the initial target.</summary>
    public AudioMonitorMapper(IAudioEndpointWatcher watcher, MonitorRegistry registry, ISettingsStore settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _watcher = watcher;
        _registry = registry;
        _settings = settings;
        _logger = logger.ForContext<AudioMonitorMapper>();

        _watcher.DefaultRenderEndpointChanged += OnInputsChanged;
        _registry.MonitorsChanged += OnInputsChanged;
        _settings.Changed += OnInputsChanged;

        Update();
    }

    /// <summary>Raised after <see cref="Current"/> changes. May be raised on any thread.</summary>
    public event EventHandler? TargetChanged;

    /// <summary>
    /// The monitor to control, or <see langword="null"/> when the default audio endpoint
    /// isn't a known monitor (then the volume keys must pass through untouched).
    /// </summary>
    public VolumeTarget? Current => Volatile.Read(ref _current);

    /// <inheritdoc/>
    public void Dispose()
    {
        _watcher.DefaultRenderEndpointChanged -= OnInputsChanged;
        _registry.MonitorsChanged -= OnInputsChanged;
        _settings.Changed -= OnInputsChanged;
    }

    /// <summary>
    /// Windows volume kept on a mirrored endpoint. Not 100: at 100 a "volume up" from the Windows UI changes
    /// nothing, so it couldn't be noticed. 98% is about -0.3 dB, inaudible.
    /// </summary>
    public const int MirrorRestLevel = 98;

    /// <summary>
    /// Sets the Windows volume of the target's audio endpoint back to its pinned level (100%, or
    /// <see cref="MirrorRestLevel"/> when mirroring) and unmutes it, if pinning or mirroring is enabled.
    /// Used after the volume keys passed through to Windows while DDC was unavailable, and by the mirror.
    /// </summary>
    public void EnsureEndpointPinned()
    {
        // Software attenuation would stack with the monitor's volume; keep it out of the way.
        var settings = _settings.Current;
        if (Current is { } target && (settings.PinEndpointVolume || settings.MirrorWindowsVolume))
        {
            _watcher.TrySetVolume(target.Endpoint.Id, settings.MirrorWindowsVolume ? MirrorRestLevel : 100);
        }
    }

    private void OnInputsChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        VolumeTarget? target;
        bool changed;
        lock (_lock)
        {
            target = Resolve();

            // The first resolution always counts as a change, so the initial state is logged.
            changed = !_resolvedOnce || !SameTarget(target, _current);
            _resolvedOnce = true;
            if (changed)
            {
                Volatile.Write(ref _current, target);
            }
        }

        EnsureEndpointPinned();

        if (!changed)
        {
            return;
        }

        if (target is null)
        {
            _logger.Information("Volume keys: pass through (default audio output {Endpoint} is not a DDC-volume monitor)", _watcher.DefaultRenderEndpoint?.FriendlyName ?? "(none)");
        }
        else
        {
            _logger.Information(
                "Volume keys: control {Monitor} via DDC ({How} match for {Endpoint})",
                target.Monitor.DisplayName,
                target.IsOverride ? "manual" : "automatic",
                target.Endpoint.FriendlyName);
        }

        TargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private VolumeTarget? Resolve()
    {
        var target = ResolveMapping();
        return target is not null && _settings.Current.ForMonitor(target.Monitor.Identity.Key).DdcVolumeEnabled ? target : null;
    }

    private VolumeTarget? ResolveMapping()
    {
        var endpoint = _watcher.DefaultRenderEndpoint;
        if (endpoint is null)
        {
            return null;
        }

        var monitors = _registry.Monitors;
        if (_settings.Current.EndpointMonitorOverrides.TryGetValue(endpoint.Id, out var overrideKey))
        {
            // An empty override means "this endpoint is not a monitor".
            var manual = monitors.FirstOrDefault(m => m.Identity.Key == overrideKey);
            return manual is null ? null : new VolumeTarget(endpoint, manual, IsOverride: true);
        }

        var key = EndpointMonitorMatcher.Match(endpoint.FriendlyName, monitors.Select(m => (m.Identity.Key, m.DisplayName)));
        var matched = key is null ? null : monitors.FirstOrDefault(m => m.Identity.Key == key);
        return matched is null ? null : new VolumeTarget(endpoint, matched, IsOverride: false);
    }

    // After a registry refresh the same monitor gets a new worker; that counts as a change.
    private static bool SameTarget(VolumeTarget? a, VolumeTarget? b) =>
        a is null ? b is null : b is not null && a.Endpoint == b.Endpoint && ReferenceEquals(a.Monitor, b.Monitor) && a.IsOverride == b.IsOverride;
}
