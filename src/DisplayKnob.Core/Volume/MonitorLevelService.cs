using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Settings;

namespace DisplayKnob.Core.Volume;

/// <summary>
/// Reads and sets brightness and volume of any monitor, for UI sliders.
/// </summary>
/// <remarks>
/// Volume changes on the current target go through <see cref="VolumeController"/>, so its tracked level, mute
/// handling and the tray icon stay in sync. Other monitors are written directly. Writes are coalesced by the
/// monitor's <see cref="DdcWorker"/>, so a dragged slider can call the setters on every change.
/// </remarks>
public sealed class MonitorLevelService
{
    private const uint MuteOn = 1;

    private readonly VolumeController _volume;
    private readonly AudioMonitorMapper _mapper;
    private readonly ISettingsStore _settings;

    /// <summary>Creates the service.</summary>
    public MonitorLevelService(VolumeController volume, AudioMonitorMapper mapper, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(settings);

        _volume = volume;
        _mapper = mapper;
        _settings = settings;
    }

    /// <summary>Whether <paramref name="monitor"/> is the monitor the volume keys currently control.</summary>
    public bool IsVolumeTarget(DdcWorker monitor) => ReferenceEquals(_mapper.Current?.Monitor, monitor);

    /// <summary>Reads brightness, volume and mute. Unanswered codes come back as <see langword="null"/>.</summary>
    public async Task<MonitorLevels> ReadAsync(DdcWorker monitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var brightness = await monitor.ReadVcpAsync(VcpCodes.Brightness, cancellationToken).ConfigureAwait(false);
        var volume = await monitor.ReadVcpAsync(VcpCodes.AudioVolume, cancellationToken).ConfigureAwait(false);
        var mute = volume is null ? null : await monitor.ReadVcpAsync(VcpCodes.AudioMute, cancellationToken).ConfigureAwait(false);

        return new MonitorLevels(
            brightness is { Maximum: > 0 } b ? VcpScale.ToPercent(b.Current, 0, b.Maximum) : null,
            brightness,
            volume is { Maximum: > 0 } v ? VolumePercent(monitor, v) : null,
            volume,
            mute?.Current == MuteOn);
    }

    /// <summary>Sets the brightness (0–100) given the raw maximum from <see cref="ReadAsync"/>.</summary>
    public static void SetBrightness(DdcWorker monitor, int percent, uint rawMaximum)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        monitor.WriteVcp(VcpCodes.Brightness, VcpScale.ToRaw(percent, 0, rawMaximum));
    }

    /// <summary>Sets the volume (0–100 on the monitor's user scale) given the raw maximum from <see cref="ReadAsync"/>.</summary>
    public void SetVolume(DdcWorker monitor, int percent, uint rawMaximum)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (IsVolumeTarget(monitor) && _volume.TryPost(new VolumeCommand(VolumeAction.Set, Level: percent)))
        {
            return;
        }

        var (min, max) = _settings.Current.ForMonitor(monitor.Identity.Key).GetVolumeRange(rawMaximum);
        monitor.WriteVcp(VcpCodes.AudioVolume, VcpScale.ToRaw(percent, min, max));
    }

    /// <summary>Toggles mute on the current volume target. Returns <see langword="false"/> for other monitors.</summary>
    public bool TryToggleMute(DdcWorker monitor) =>
        IsVolumeTarget(monitor) && _volume.TryPost(new VolumeCommand(VolumeAction.ToggleMute));

    private int VolumePercent(DdcWorker monitor, VcpValue value)
    {
        var (min, max) = _settings.Current.ForMonitor(monitor.Identity.Key).GetVolumeRange(value.Maximum);
        return VcpScale.ToPercent(value.Current, min, max);
    }
}
