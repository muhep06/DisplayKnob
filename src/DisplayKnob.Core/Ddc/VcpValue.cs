namespace DisplayKnob.Core.Ddc;

/// <summary>
/// A VCP feature reading.
/// </summary>
/// <param name="Current">Current value.</param>
/// <param name="Maximum">Maximum value reported by the monitor.</param>
public readonly record struct VcpValue(uint Current, uint Maximum);
