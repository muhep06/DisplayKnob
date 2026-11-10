namespace DisplayKnob.Core.Settings;

/// <summary>
/// Per-monitor settings, keyed by <see cref="Displays.MonitorIdentity.Key"/>.
/// </summary>
public sealed record MonitorSettings
{
    /// <summary>Defaults for a monitor with no saved settings.</summary>
    public static MonitorSettings Default { get; } = new();

    /// <summary>When <see langword="false"/>, volume keys pass through even if audio plays through this monitor.</summary>
    public bool DdcVolumeEnabled { get; set; } = true;

    /// <summary>
    /// Raw VCP 0x62 value that maps to 0% on the user scale. <see langword="null"/> means 0.
    /// Raise it when the bottom of the monitor's range is inaudible.
    /// </summary>
    public uint? VolumeMin { get; set; }

    /// <summary>
    /// Raw VCP 0x62 value that maps to 100% on the user scale. <see langword="null"/> means the monitor's reported maximum.
    /// Lower it when the top of the monitor's range is uncomfortably loud.
    /// </summary>
    public uint? VolumeMax { get; set; }

    /// <summary>Lowest <see cref="MaxVolumePercent"/> that is honored; below it the volume would be unusably coarse.</summary>
    public const int MinMaxVolumePercent = 10;

    /// <summary>
    /// The volume limit, as a percentage of the monitor's reported range (10–100). The 0–100 user scale is
    /// squeezed into the range up to this limit, so 100% on the keys, sliders and the on-screen indicator
    /// is the limit. A percentage, unlike <see cref="VolumeMax"/>, needs no knowledge of the monitor's raw maximum,
    /// so it can be set while the monitor isn't answering.
    /// </summary>
    public int MaxVolumePercent { get; set; } = 100;

    /// <summary>The usable raw volume range, given the maximum the monitor reports.</summary>
    public (uint Min, uint Max) GetVolumeRange(uint reportedMax)
    {
        var percent = Math.Clamp(MaxVolumePercent, MinMaxVolumePercent, 100);
        var limit = (uint)Math.Round(reportedMax * percent / 100.0, MidpointRounding.AwayFromZero);
        if (reportedMax > 0)
        {
            limit = Math.Max(limit, 1); // A tiny range must not collapse to nothing.
        }

        var max = Math.Min(Math.Min(VolumeMax ?? reportedMax, reportedMax), limit);
        var min = Math.Min(VolumeMin ?? 0, max);
        return (min, max);
    }
}
