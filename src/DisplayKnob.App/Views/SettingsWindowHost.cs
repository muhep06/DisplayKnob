using DisplayKnob.App.ViewModels;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Input;
using DisplayKnob.Core.Platform;
using DisplayKnob.Core.Settings;

namespace DisplayKnob.App.Views;

/// <summary>Opens the settings window, or brings the open one to the front.</summary>
internal sealed class SettingsWindowHost : IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly MonitorRegistry _registry;
    private readonly IAudioEndpointWatcher _audio;
    private readonly IStartupRegistration _startup;
    private readonly IHotkeyRecorder _recorder;
    private readonly IHotkeyConflictChecker _conflicts;
    private SettingsWindow? _window;

    public SettingsWindowHost(
        ISettingsStore settings,
        MonitorRegistry registry,
        IAudioEndpointWatcher audio,
        IStartupRegistration startup,
        IHotkeyRecorder recorder,
        IHotkeyConflictChecker conflicts)
    {
        _settings = settings;
        _registry = registry;
        _audio = audio;
        _startup = startup;
        _recorder = recorder;
        _conflicts = conflicts;
    }

    public void Show()
    {
        if (_window is null)
        {
            // A fresh view model each time, so the window shows the current monitors and outputs.
            var hotkeys = new HotkeySettingsViewModel(_settings, _recorder, _conflicts);
            var viewModel = new SettingsViewModel(_settings, _registry, _audio, _startup, hotkeys);
            _window = new SettingsWindow(viewModel);
            _window.Closed += (_, _) =>
            {
                viewModel.Dispose();
                _window = null;
            };
        }

        _window.Activate();
    }

    public void Dispose() => _window?.Close();
}
