using System.Text.Json.Serialization;

namespace DisplayKnob.Core.Settings;

/// <summary>How the 0–100 volume scale maps onto a monitor's raw volume range.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VolumeCurve>))]
public enum VolumeCurve
{
    /// <summary>Evenly: every percent is the same number of raw steps.</summary>
    Linear,

    /// <summary>
    /// Finer steps at low volume and bigger ones at high volume. A power curve (the raw fraction is the square of the
    /// percentage), close to the "audio taper" of volume knobs; not a true logarithm, because the monitor's
    /// raw-to-decibel response isn't known.
    /// </summary>
    Logarithmic,
}
