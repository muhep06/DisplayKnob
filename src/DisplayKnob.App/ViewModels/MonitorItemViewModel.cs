using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayKnob.Core.Settings;

namespace DisplayKnob.App.ViewModels;

/// <summary>One monitor in the settings window.</summary>
internal sealed partial class MonitorItemViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly string _key;

    public MonitorItemViewModel(ISettingsStore settings, string key, string name)
    {
        _settings = settings;
        _key = key;
        Name = name;

        // The EDID part of the identity (before the device path) is what users can recognize.
        Details = key.Split('|')[0];
        var saved = settings.Current.ForMonitor(key);
        DdcVolumeEnabled = saved.DdcVolumeEnabled;
        InVolumeGroup = saved.InVolumeGroup;
        MaxVolume = Math.Clamp(saved.MaxVolumePercent, MonitorSettings.MinMaxVolumePercent, 100);
        CurveIndex = (int)saved.VolumeCurve;
    }

    /// <summary>The volume curves in the order of <see cref="VolumeCurve"/>, so the combo box index is the enum value.</summary>
    public IReadOnlyList<string> CurveNames { get; } = ["Linear", "Logarithmic"];

    public string Name { get; }

    public string Details { get; }

    [ObservableProperty]
    public partial bool DdcVolumeEnabled { get; set; }

    /// <summary>Part of the volume group (see <see cref="MonitorSettings.InVolumeGroup"/>).</summary>
    [ObservableProperty]
    public partial bool InVolumeGroup { get; set; }

    /// <summary>The volume limit in percent of the monitor's range (see <see cref="MonitorSettings.MaxVolumePercent"/>).</summary>
    [ObservableProperty]
    public partial double MaxVolume { get; set; }

    [ObservableProperty]
    public partial int CurveIndex { get; set; }

    partial void OnCurveIndexChanged(int value)
    {
        if (value < 0 || value >= CurveNames.Count)
        {
            return; // The combo box clears its selection while its items change.
        }

        var curve = (VolumeCurve)value;
        if (_settings.Current.ForMonitor(_key).VolumeCurve != curve)
        {
            Save(m => m with { VolumeCurve = curve });
        }
    }

    partial void OnDdcVolumeEnabledChanged(bool value)
    {
        if (_settings.Current.ForMonitor(_key).DdcVolumeEnabled == value)
        {
            return; // Initial load.
        }

        Save(m => m with { DdcVolumeEnabled = value });
    }

    partial void OnInVolumeGroupChanged(bool value)
    {
        if (_settings.Current.ForMonitor(_key).InVolumeGroup == value)
        {
            return; // Initial load.
        }

        Save(m => m with { InVolumeGroup = value });
    }

    partial void OnMaxVolumeChanged(double value) => SaveMaxVolume(value);

    // The NumberBox hands over its value on Enter or when it loses focus. Clicking this button moves the focus,
    // so the value arrives first; saving again is harmless and makes the button do what it looks like it does.
    [RelayCommand]
    private void ApplyMaxVolume() => SaveMaxVolume(MaxVolume);

    private void SaveMaxVolume(double value)
    {
        // NumberBox reports NaN while the box is empty.
        if (double.IsNaN(value))
        {
            return;
        }

        var percent = Math.Clamp((int)Math.Round(value), MonitorSettings.MinMaxVolumePercent, 100);
        if (_settings.Current.ForMonitor(_key).MaxVolumePercent != percent)
        {
            Save(m => m with { MaxVolumePercent = percent });
        }
    }

    private void Save(Func<MonitorSettings, MonitorSettings> change) =>
        _settings.Update(s =>
        {
            var monitors = new Dictionary<string, MonitorSettings>(s.Monitors)
            {
                [_key] = change(s.ForMonitor(_key)),
            };
            return s with { Monitors = monitors };
        });
}
