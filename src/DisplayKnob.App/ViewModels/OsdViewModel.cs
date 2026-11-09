using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace DisplayKnob.App.ViewModels;

/// <summary>
/// State shown by the volume OSD.
/// </summary>
internal sealed partial class OsdViewModel : ObservableObject
{
    // Segoe Fluent Icons.
    private const string GlyphMuted = "";
    private const string GlyphVolume0 = "";
    private const string GlyphVolume1 = "";
    private const string GlyphVolume2 = "";
    private const string GlyphVolume3 = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(LevelText), nameof(FilledLength), nameof(EmptyLength))]
    public partial int Level { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(BarOpacity))]
    public partial bool Muted { get; set; }

    [ObservableProperty]
    public partial string MonitorName { get; set; } = string.Empty;

    /// <summary>Only worth the space when more than one monitor could be the target.</summary>
    [ObservableProperty]
    public partial bool ShowMonitorName { get; set; }

    public string Glyph => Muted ? GlyphMuted : Level switch
    {
        0 => GlyphVolume0,
        < 34 => GlyphVolume1,
        < 67 => GlyphVolume2,
        _ => GlyphVolume3,
    };

    // Like the Windows flyout: the level stays visible while muted; only the icon and the dimmed slider show mute.
    public string LevelText => Level.ToString(CultureInfo.CurrentCulture);

    public double BarOpacity => Muted ? 0.4 : 1.0;

    // The bar is two star-sized columns, so the fill tracks the level at any width.
    public GridLength FilledLength => new(Level, GridUnitType.Star);

    public GridLength EmptyLength => new(100 - Level, GridUnitType.Star);
}
