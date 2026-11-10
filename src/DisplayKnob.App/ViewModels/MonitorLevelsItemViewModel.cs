using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Volume;

namespace DisplayKnob.App.ViewModels;

/// <summary>One monitor in the tray flyout: brightness and volume sliders.</summary>
internal sealed partial class MonitorLevelsItemViewModel : ObservableObject
{
    // Segoe Fluent Icons.
    private const string GlyphMuted = "\uE74F";
    private const string GlyphVolume = "\uE767";

    private readonly DdcWorker _monitor;
    private readonly MonitorLevelService _levels;
    private uint _brightnessMax;
    private uint _volumeMax;
    private const long EchoWindowMs = 600;

    private bool _applying; // Set while values come from the monitor, so they aren't written back.
    private long _lastLocalVolumeChange;

    public MonitorLevelsItemViewModel(DdcWorker monitor, MonitorLevelService levels)
    {
        _monitor = monitor;
        _levels = levels;
        Name = monitor.DisplayName;
        Key = monitor.Identity.Key;
        CanMute = levels.IsVolumeTarget(monitor);
    }

    public string Name { get; }

    public string Key { get; }

    /// <summary>Only the monitor the volume keys control can be muted (mute state is tracked there).</summary>
    public bool CanMute { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrightnessRowVisible))]
    [NotifyPropertyChangedFor(nameof(VolumeRowVisible))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrightnessRowVisible))]
    public partial bool HasBrightness { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrightnessText))]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeRowVisible))]
    public partial bool HasVolume { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    public partial double Volume { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    public partial bool Muted { get; set; }

    [ObservableProperty]
    public partial bool NotResponding { get; set; }

    // The rows are shown while the monitor is still being read, so the flyout is sized for them from the start;
    // a row a monitor doesn't support disappears once the reading is done (and the flyout is resized then).
    public bool BrightnessRowVisible => IsLoading || HasBrightness;

    public bool VolumeRowVisible => IsLoading || HasVolume;

    public string BrightnessText => ((int)Math.Round(Brightness)).ToString(CultureInfo.CurrentCulture);

    public string VolumeText => ((int)Math.Round(Volume)).ToString(CultureInfo.CurrentCulture);

    public string VolumeGlyph => Muted ? GlyphMuted : GlyphVolume;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var levels = await _levels.ReadAsync(_monitor, cancellationToken);

        _applying = true;
        try
        {
            HasBrightness = levels.Brightness is not null;
            _brightnessMax = levels.BrightnessRaw?.Maximum ?? 0;
            Brightness = levels.Brightness ?? 0;

            HasVolume = levels.Volume is not null;
            _volumeMax = levels.VolumeRaw?.Maximum ?? 0;
            Volume = levels.Volume ?? 0;
            Muted = levels.Muted;

            NotResponding = !HasBrightness && !HasVolume;
            IsLoading = false;
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Shows a volume change made elsewhere (keys, wheel) while the flyout is open.</summary>
    public void ShowVolume(int level, bool muted)
    {
        // While the user drags the slider, the controller reports each step back a little later;
        // applying those older values would make the thumb jump backwards.
        if (Environment.TickCount64 - _lastLocalVolumeChange < EchoWindowMs)
        {
            return;
        }

        _applying = true;
        try
        {
            Volume = level;
            Muted = muted;
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnBrightnessChanged(double value)
    {
        if (!_applying && HasBrightness)
        {
            MonitorLevelService.SetBrightness(_monitor, (int)Math.Round(value), _brightnessMax);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_applying && HasVolume)
        {
            _lastLocalVolumeChange = Environment.TickCount64;
            Muted = false; // Moving the slider unmutes, like the keys.
            _levels.SetVolume(_monitor, (int)Math.Round(value), _volumeMax);
        }
    }

    [RelayCommand]
    private void ToggleMute() => _levels.TryToggleMute(_monitor);
}
