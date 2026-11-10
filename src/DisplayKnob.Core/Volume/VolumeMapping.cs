using DisplayKnob.Core.Ddc;

namespace DisplayKnob.Core.Volume;

/// <summary>
/// How one monitor's raw volume maps onto the 0–100 user scale: the usable raw range and the curve.
/// Everything that converts between the two goes through here, so the keys, the sliders and the tray agree.
/// </summary>
/// <param name="Min">Raw value of 0%.</param>
/// <param name="Max">Raw value of 100%.</param>
/// <param name="Exponent">Curve shape; see <see cref="VcpScale.ToRaw"/>.</param>
public readonly record struct VolumeMapping(uint Min, uint Max, double Exponent)
{
    /// <summary>Raw value for a position on the 0–100 scale.</summary>
    public uint ToRaw(int level) => VcpScale.ToRaw(level, Min, Max, Exponent);

    /// <summary>Position on the 0–100 scale for a raw value.</summary>
    public int ToPercent(uint raw) => VcpScale.ToPercent(raw, Min, Max, Exponent);
}
