using CommunityToolkit.Mvvm.ComponentModel;
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
        MaxVolume = Math.Clamp(saved.MaxVolumePercent, MonitorSettings.MinMaxVolumePercent, 100);
    }

    public string Name { get; }

    public string Details { get; }

    [ObservableProperty]
    public partial bool DdcVolumeEnabled { get; set; }

    /// <summary>The volume limit in percent of the monitor's range (see <see cref="MonitorSettings.MaxVolumePercent"/>).</summary>
    [ObservableProperty]
    public partial double MaxVolume { get; set; }

    partial void OnDdcVolumeEnabledChanged(bool value)
    {
        if (_settings.Current.ForMonitor(_key).DdcVolumeEnabled == value)
        {
            return; // Initial load.
        }

        Save(m => m with { DdcVolumeEnabled = value });
    }

    partial void OnMaxVolumeChanged(double value)
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
