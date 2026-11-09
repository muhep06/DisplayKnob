namespace DisplayKnob.Core.Ddc;

/// <summary>
/// MCCS VCP (Virtual Control Panel) feature codes used by DisplayKnob.
/// </summary>
public static class VcpCodes
{
    /// <summary>Luminance (brightness).</summary>
    public const byte Brightness = 0x10;

    /// <summary>Contrast.</summary>
    public const byte Contrast = 0x12;

    /// <summary>Input source select.</summary>
    public const byte InputSource = 0x60;

    /// <summary>Audio speaker volume.</summary>
    public const byte AudioVolume = 0x62;

    /// <summary>Audio mute (1 = muted, 2 = unmuted). Not supported by every monitor.</summary>
    public const byte AudioMute = 0x8D;
}
