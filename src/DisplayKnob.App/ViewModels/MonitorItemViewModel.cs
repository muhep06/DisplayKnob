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
        DdcVolumeEnabled = settings.Current.ForMonitor(key).DdcVolumeEnabled;
    }

    public string Name { get; }

    public string Details { get; }

    [ObservableProperty]
    public partial bool DdcVolumeEnabled { get; set; }

    partial void OnDdcVolumeEnabledChanged(bool value)
    {
        if (_settings.Current.ForMonitor(_key).DdcVolumeEnabled == value)
        {
            return; // Initial load.
        }

        _settings.Update(s =>
        {
            var monitors = new Dictionary<string, MonitorSettings>(s.Monitors)
            {
                [_key] = s.ForMonitor(_key) with { DdcVolumeEnabled = value },
            };
            return s with { Monitors = monitors };
        });
    }
}
