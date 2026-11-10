namespace DisplayKnob.Core.Ddc;

/// <summary>Maps raw VCP values onto a 0–100 percent scale over a usable raw range.</summary>
public static class VcpScale
{
    /// <summary>
    /// Raw value for <paramref name="percent"/> (clamped to 0–100) within <paramref name="min"/>..<paramref name="max"/>.
    /// </summary>
    /// <param name="percent">Position on the 0–100 scale.</param>
    /// <param name="min">Raw value of 0%.</param>
    /// <param name="max">Raw value of 100%.</param>
    /// <param name="exponent">Shape of the curve: 1 is linear, above 1 takes finer steps at the bottom of the scale.</param>
    public static uint ToRaw(int percent, uint min, uint max, double exponent = 1.0)
    {
        if (max <= min)
        {
            return min;
        }

        var fraction = Math.Clamp(percent, 0, 100) / 100.0;
        var shaped = exponent == 1.0 ? fraction : Math.Pow(fraction, exponent);
        return min + (uint)Math.Round(shaped * (max - min), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Percent (0–100) for <paramref name="raw"/> within <paramref name="min"/>..<paramref name="max"/>;
    /// the inverse of <see cref="ToRaw"/> for the same <paramref name="exponent"/>.
    /// </summary>
    public static int ToPercent(uint raw, uint min, uint max, double exponent = 1.0)
    {
        if (raw <= min || max <= min)
        {
            return 0;
        }

        if (raw >= max)
        {
            return 100;
        }

        var fraction = (raw - min) / (double)(max - min);
        var unshaped = exponent == 1.0 ? fraction : Math.Pow(fraction, 1.0 / exponent);
        return (int)Math.Round(unshaped * 100.0, MidpointRounding.AwayFromZero);
    }
}
