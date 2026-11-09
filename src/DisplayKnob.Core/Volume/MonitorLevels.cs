using DisplayKnob.Core.Ddc;

namespace DisplayKnob.Core.Volume;

/// <summary>Brightness and volume of one monitor as read over DDC (for sliders).</summary>
/// <param name="Brightness">Brightness 0–100, or <see langword="null"/> if the monitor didn't answer VCP 0x10.</param>
/// <param name="BrightnessRaw">The raw brightness reading (its maximum is needed to write).</param>
/// <param name="Volume">Volume 0–100 on the user scale, or <see langword="null"/> if VCP 0x62 isn't available.</param>
/// <param name="VolumeRaw">The raw volume reading.</param>
/// <param name="Muted">Whether VCP 0x8D reports muted.</param>
public sealed record MonitorLevels(int? Brightness, VcpValue? BrightnessRaw, int? Volume, VcpValue? VolumeRaw, bool Muted);
