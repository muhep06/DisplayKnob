namespace DisplayKnob.Core.Displays;

/// <summary>
/// A display's work area (desktop minus taskbar) in physical pixels, plus its scale factor.
/// </summary>
/// <param name="Left">Left edge, physical pixels.</param>
/// <param name="Top">Top edge, physical pixels.</param>
/// <param name="Width">Width, physical pixels.</param>
/// <param name="Height">Height, physical pixels.</param>
/// <param name="Scale">DPI scale factor (1.0 = 96 DPI).</param>
public readonly record struct DisplayWorkArea(int Left, int Top, int Width, int Height, double Scale);
