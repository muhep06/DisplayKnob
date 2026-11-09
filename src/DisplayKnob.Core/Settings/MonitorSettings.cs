namespace DisplayKnob.Core.Settings;

/// <summary>
/// Per-monitor settings, keyed by <see cref="Displays.MonitorIdentity.Key"/>.
/// </summary>
public sealed record MonitorSettings
{
    /// <summary>Defaults for a monitor with no saved settings.</summary>
    public static MonitorSettings Default { get; } = new();

    /// <summary>When <see langword="false"/>, volume keys pass through even if audio plays through this monitor.</summary>
    public bool DdcVolumeEnabled { get; init; } = true;

    /// <summary>
    /// Raw VCP 0x62 value that maps to 0% on the user scale. <see langword="null"/> means 0.
    /// Raise it when the bottom of the monitor's range is inaudible.
    /// </summary>
    public uint? VolumeMin { get; init; }

    /// <summary>
    /// Raw VCP 0x62 value that maps to 100% on the user scale. <see langword="null"/> means the monitor's reported maximum.
    /// Lower it when the top of the monitor's range is uncomfortably loud.
    /// </summary>
    public uint? VolumeMax { get; init; }

    /// <summary>The usable raw volume range, given the maximum the monitor reports.</summary>
    public (uint Min, uint Max) GetVolumeRange(uint reportedMax)
    {
        var max = Math.Min(VolumeMax ?? reportedMax, reportedMax);
        var min = Math.Min(VolumeMin ?? 0, max);
        return (min, max);
    }
}
