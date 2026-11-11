using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Platform;
using DisplayKnob.Core.Settings;
using Microsoft.UI.Dispatching;

namespace DisplayKnob.App.ViewModels;

/// <summary>
/// The settings window. Every change is saved immediately (there is no Apply button).
/// </summary>
internal sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>Polling choices shown in the combo box; 0 = off.</summary>
    public static IReadOnlyList<int> PollChoices { get; } = [0, 5, 10, 30, 60];

    private readonly ISettingsStore _settings;
    private readonly MonitorRegistry _registry;
    private readonly IAudioEndpointWatcher _audio;
    private readonly IStartupRegistration _startup;
    private readonly DispatcherQueue _dispatcher;
    private bool _loading;

    public SettingsViewModel(ISettingsStore settings, MonitorRegistry registry, IAudioEndpointWatcher audio, IStartupRegistration startup, HotkeySettingsViewModel hotkeys)
    {
        _settings = settings;
        _registry = registry;
        _audio = audio;
        _startup = startup;
        Hotkeys = hotkeys;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("SettingsViewModel must be created on the UI thread.");

        // Monitors can come and go (KVM, hotplug) while the window is open.
        _registry.MonitorsChanged += OnMonitorsChanged;
    }

    public void Dispose()
    {
        _registry.MonitorsChanged -= OnMonitorsChanged;
        Hotkeys.Dispose();
    }

    public HotkeySettingsViewModel Hotkeys { get; }

    public ObservableCollection<MonitorItemViewModel> Monitors { get; } = [];

    public ObservableCollection<EndpointItemViewModel> Endpoints { get; } = [];

    public IReadOnlyList<string> PollChoiceNames { get; } =
        [.. PollChoices.Select(s => s == 0 ? "Off" : $"Every {s} seconds")];

    /// <summary>In the order of <see cref="GroupOsdMode"/>, so the combo box index is the enum value.</summary>
    public IReadOnlyList<string> GroupOsdNames { get; } = ["On every monitor in the group", "Only on the audio monitor"];

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool ShowOsd { get; set; }

    [ObservableProperty]
    public partial double VolumeStep { get; set; }

    [ObservableProperty]
    public partial int PollIndex { get; set; }

    [ObservableProperty]
    public partial bool PinEndpointVolume { get; set; }

    [ObservableProperty]
    public partial bool MirrorWindowsVolume { get; set; }

    [ObservableProperty]
    public partial bool VolumeGroupEnabled { get; set; }

    /// <summary>Index into <see cref="GroupOsdNames"/> (the same order as <see cref="GroupOsdMode"/>).</summary>
    [ObservableProperty]
    public partial int GroupOsdIndex { get; set; }

    [ObservableProperty]
    public partial bool HasNoMonitors { get; set; }

    /// <summary>Reads the current state into the view model. Call each time the window opens.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;
            StartWithWindows = _startup.IsEnabled;
            ShowOsd = current.ShowOsd;
            VolumeStep = current.VolumeStep;
            var pollIndex = PollChoices.ToList().IndexOf(current.VolumePollSeconds);
            PollIndex = pollIndex >= 0 ? pollIndex : PollChoices.ToList().IndexOf(10); // A hand-edited value shows as the default.
            PinEndpointVolume = current.PinEndpointVolume;
            MirrorWindowsVolume = current.MirrorWindowsVolume;
            VolumeGroupEnabled = current.VolumeGroupEnabled;
            GroupOsdIndex = (int)current.VolumeGroupOsd;
            Hotkeys.Load();

            var monitors = _registry.Monitors;
            Monitors.Clear();
            foreach (var monitor in monitors)
            {
                Monitors.Add(new MonitorItemViewModel(_settings, monitor.Identity.Key, monitor.DisplayName));
            }

            HasNoMonitors = Monitors.Count == 0;

            var defaultId = _audio.DefaultRenderEndpoint?.Id;
            var choices = monitors.Select(m => (m.Identity.Key, m.DisplayName)).ToList();
            Endpoints.Clear();
            foreach (var endpoint in _audio.GetRenderEndpoints().OrderByDescending(e => e.Id == defaultId).ThenBy(e => e.FriendlyName, StringComparer.CurrentCulture))
            {
                Endpoints.Add(new EndpointItemViewModel(_settings, endpoint, endpoint.Id == defaultId, choices));
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnMonitorsChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Load);

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_loading && !_startup.TrySetEnabled(value))
        {
            // Didn't stick (e.g. blocked by policy); show the real state.
            _loading = true;
            StartWithWindows = _startup.IsEnabled;
            _loading = false;
        }
    }

    partial void OnShowOsdChanged(bool value) => Save(s => s with { ShowOsd = value });

    partial void OnVolumeStepChanged(double value)
    {
        // NumberBox reports NaN while the box is empty.
        if (!double.IsNaN(value))
        {
            Save(s => s with { VolumeStep = Math.Clamp((int)Math.Round(value), 1, 20) });
        }
    }

    partial void OnPollIndexChanged(int value)
    {
        if (value >= 0 && value < PollChoices.Count)
        {
            Save(s => s with { VolumePollSeconds = PollChoices[value] });
        }
    }

    partial void OnPinEndpointVolumeChanged(bool value) => Save(s => s with { PinEndpointVolume = value });

    partial void OnMirrorWindowsVolumeChanged(bool value) => Save(s => s with { MirrorWindowsVolume = value });

    partial void OnVolumeGroupEnabledChanged(bool value) => Save(s => s with { VolumeGroupEnabled = value });

    partial void OnGroupOsdIndexChanged(int value)
    {
        if (value >= 0 && value < GroupOsdNames.Count)
        {
            Save(s => s with { VolumeGroupOsd = (GroupOsdMode)value });
        }
    }

    private void Save(Func<DisplayKnobSettings, DisplayKnobSettings> change)
    {
        if (!_loading)
        {
            _settings.Update(change);
        }
    }
}
