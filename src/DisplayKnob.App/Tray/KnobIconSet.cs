using System.Drawing;
using System.Drawing.Drawing2D;

namespace DisplayKnob.App.Tray;

/// <summary>
/// Draws the tray icon: a white knob on a violet rounded square whose pointer turns with the volume.
/// Icons are drawn on demand and kept, so holding a volume key only ever looks up a finished icon.
/// </summary>
/// <remarks>
/// <para>The geometry (a 128-unit canvas) matches <c>scripts/Generate-AppIcon.ps1</c>, which draws the same knob for the
/// application icon; change both together. The pointer sweeps 270 degrees: pointing at 7:30 for 0% and at 4:30 for 100%.</para>
/// <para>At most 101 levels x 3 modes are ever drawn, so the icons are never destroyed; there is no per-update handle leak.
/// Used from the UI thread only.</para>
/// </remarks>
internal sealed class KnobIconSet
{
    /// <summary>Level the idle icon points at (straight up).</summary>
    public const int IdleLevel = 50;

    private const int Supersample = 8; // Drawn this many times larger, then scaled down: smooth edges at 16 px.
    private const float Canvas = 128f;

    private readonly int _size;
    private readonly Dictionary<(int Level, KnobIconMode Mode), Icon> _icons = [];

    /// <summary>Creates a set of icons <paramref name="size"/> pixels square (the system's small icon size).</summary>
    public KnobIconSet(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        _size = size;
    }

    /// <summary>The icon for no volume target: the pointer straight up.</summary>
    public Icon Idle => Get(IdleLevel, KnobIconMode.Normal);

    /// <summary>The icon for a volume level (0–100) in a mode.</summary>
    public Icon Get(int level, KnobIconMode mode)
    {
        var key = (Math.Clamp(level, 0, 100), mode);
        if (!_icons.TryGetValue(key, out var icon))
        {
            icon = Draw(_size, key.Item1, mode);
            _icons[key] = icon;
        }

        return icon;
    }

    private static Icon Draw(int size, int level, KnobIconMode mode)
    {
        var (top, bottom, accent) = mode switch
        {
            KnobIconMode.Muted => (Color.FromArgb(0x9C, 0xA3, 0xAF), Color.FromArgb(0x6B, 0x72, 0x80), Color.FromArgb(0x6B, 0x72, 0x80)),
            KnobIconMode.Warning => (Color.FromArgb(0xFB, 0x92, 0x3C), Color.FromArgb(0xE8, 0x59, 0x0C), Color.FromArgb(0xC2, 0x41, 0x0C)),
            _ => (Color.FromArgb(0x8B, 0x5C, 0xF6), Color.FromArgb(0x6D, 0x28, 0xD9), Color.FromArgb(0x6D, 0x28, 0xD9)),
        };

        var big = size * Supersample;
        var u = big / Canvas;
        using var canvas = new Bitmap(big, big);
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var square = RoundedSquare(2 * u, 124 * u, 26 * u))
            using (var fill = new LinearGradientBrush(new PointF(0, 0), new PointF(big, big), top, bottom))
            {
                g.FillPath(fill, square);
            }

            g.FillEllipse(Brushes.White, (64 - 47) * u, (64 - 47) * u, 94 * u, 94 * u);

            using var pen = new Pen(accent, 16 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            switch (mode)
            {
                case KnobIconMode.Warning:
                    // An exclamation mark instead of the pointer: a bar and a dot.
                    g.DrawLine(pen, 64 * u, 40 * u, 64 * u, 68 * u);
                    using (var dot = new SolidBrush(accent))
                    {
                        g.FillEllipse(dot, (64 - 9) * u, (90 - 9) * u, 18 * u, 18 * u);
                    }

                    break;

                default:
                    var angle = (135 + 270 * level / 100.0) * Math.PI / 180;
                    var (cos, sin) = ((float)Math.Cos(angle), (float)Math.Sin(angle));
                    g.DrawLine(pen, (64 + cos * 5) * u, (64 + sin * 5) * u, (64 + cos * 36) * u, (64 + sin * 36) * u);
                    if (mode == KnobIconMode.Muted)
                    {
                        using var slash = new Pen(Color.White, 11 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                        using var edge = new Pen(accent, 3 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                        g.DrawLine(slash, 22 * u, 106 * u, 106 * u, 22 * u);
                        g.DrawLine(edge, 22 * u, 106 * u, 106 * u, 22 * u);
                    }

                    break;
            }
        }

        using var scaled = new Bitmap(size, size);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(canvas, 0, 0, size, size);
        }

        // The icon wraps the handle without owning it; it lives as long as the cache (see the remarks).
        return Icon.FromHandle(scaled.GetHicon());
    }

    private static GraphicsPath RoundedSquare(float inset, float side, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(inset, inset, d, d, 180, 90);
        path.AddArc(inset + side - d, inset, d, d, 270, 90);
        path.AddArc(inset + side - d, inset + side - d, d, d, 0, 90);
        path.AddArc(inset, inset + side - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
