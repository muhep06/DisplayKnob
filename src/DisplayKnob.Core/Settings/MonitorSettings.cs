using DisplayKnob.Core.Volume;

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
    /// Part of the volume group: with <see cref="DisplayKnobSettings.VolumeGroupEnabled"/> on, the volume keys move
    /// this monitor together with the other members. Only counts while <see cref="DdcVolumeEnabled"/> is on.
    /// </summary>
    public bool InVolumeGroup { get; set; }

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

    /// <summary>
    /// The pause, in milliseconds, this monitor needs between DDC commands, as learned by DisplayKnob: a monitor
    /// ignores a command that comes too soon after the previous one. <see langword="null"/> means the default.
    /// Written by the app; delete it to learn again.
    /// </summary>
    public int? CommandSpacingMs { get; set; }

    /// <summary>How the 0–100 scale maps onto the usable range. Monitors respond very differently at the low end.</summary>
    public VolumeCurve VolumeCurve { get; set; } = VolumeCurve.Linear;

    /// <summary>The curve exponent of <see cref="VolumeCurve.Logarithmic"/>: the raw fraction is the square of the percentage.</summary>
    private const double LogarithmicExponent = 2.0;

    /// <summary>The usable raw range together with the curve, given the maximum the monitor reports.</summary>
    public VolumeMapping GetVolumeMapping(uint reportedMax)
    {
        var (min, max) = GetVolumeRange(reportedMax);
        return new VolumeMapping(min, max, VolumeCurve == VolumeCurve.Logarithmic ? LogarithmicExponent : 1.0);
    }

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
