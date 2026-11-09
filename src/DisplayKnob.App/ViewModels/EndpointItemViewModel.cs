using CommunityToolkit.Mvvm.ComponentModel;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Settings;

namespace DisplayKnob.App.ViewModels;

/// <summary>One audio output in the settings window, with the monitor it plays through.</summary>
/// <remarks>Choices: 0 = automatic (name match), 1 = not a monitor, 2.. = a specific monitor.</remarks>
internal sealed partial class EndpointItemViewModel : ObservableObject
{
    private const int AutomaticIndex = 0;
    private const int NotAMonitorIndex = 1;

    private readonly ISettingsStore _settings;
    private readonly string _endpointId;
    private readonly IReadOnlyList<(string Key, string Name)> _monitors;

    public EndpointItemViewModel(ISettingsStore settings, AudioEndpoint endpoint, bool isDefault, IReadOnlyList<(string Key, string Name)> monitors)
    {
        _settings = settings;
        _endpointId = endpoint.Id;
        _monitors = monitors;
        Name = endpoint.FriendlyName;
        IsDefault = isDefault;

        var automatic = EndpointMonitorMatcher.Match(endpoint.FriendlyName, monitors) is { } key
            ? $"Automatic ({monitors.First(m => m.Key == key).Name})"
            : "Automatic (no matching monitor)";
        Choices = [automatic, "Not a monitor", .. monitors.Select(m => m.Name)];
        SelectedIndex = CurrentIndex();
    }

    public string Name { get; }

    public bool IsDefault { get; }

    public IReadOnlyList<string> Choices { get; }

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    partial void OnSelectedIndexChanged(int value)
    {
        if (value < 0 || value == CurrentIndex())
        {
            return;
        }

        _settings.Update(s =>
        {
            var overrides = new Dictionary<string, string>(s.EndpointMonitorOverrides);
            if (value == AutomaticIndex)
            {
                overrides.Remove(_endpointId);
            }
            else
            {
                // An empty key means "this endpoint is not a monitor".
                overrides[_endpointId] = value == NotAMonitorIndex ? string.Empty : _monitors[value - 2].Key;
            }

            return s with { EndpointMonitorOverrides = overrides };
        });
    }

    private int CurrentIndex()
    {
        if (!_settings.Current.EndpointMonitorOverrides.TryGetValue(_endpointId, out var key))
        {
            return AutomaticIndex;
        }

        var index = _monitors.ToList().FindIndex(m => m.Key == key);
        return index >= 0 ? index + 2 : NotAMonitorIndex;
    }
}
