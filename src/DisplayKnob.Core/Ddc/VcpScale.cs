namespace DisplayKnob.Core.Ddc;

/// <summary>Maps raw VCP values onto a 0–100 percent scale over a usable raw range.</summary>
public static class VcpScale
{
    /// <summary>Raw value for <paramref name="percent"/> (clamped to 0–100) within <paramref name="min"/>..<paramref name="max"/>.</summary>
    public static uint ToRaw(int percent, uint min, uint max)
    {
        if (max <= min)
        {
            return min;
        }

        return min + (uint)Math.Round(Math.Clamp(percent, 0, 100) * (max - min) / 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Percent (0–100) for <paramref name="raw"/> within <paramref name="min"/>..<paramref name="max"/>.</summary>
    public static int ToPercent(uint raw, uint min, uint max)
    {
        if (raw <= min || max <= min)
        {
            return 0;
        }

        return raw >= max ? 100 : (int)Math.Round((raw - min) * 100.0 / (max - min), MidpointRounding.AwayFromZero);
    }
}
