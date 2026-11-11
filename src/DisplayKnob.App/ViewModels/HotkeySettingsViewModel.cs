using CommunityToolkit.Mvvm.ComponentModel;
using DisplayKnob.Core.Input;
using DisplayKnob.Core.Settings;

namespace DisplayKnob.App.ViewModels;

/// <summary>The keyboard shortcuts section of the settings window. Every change is saved immediately.</summary>
internal sealed partial class HotkeySettingsViewModel : ObservableObject, IDisposable
{
    // Same order as HotkeyPreset, so the combo box index is the enum value.
    public static IReadOnlyList<string> PresetNames { get; } =
        ["Ctrl + Alt + ↑ / ↓ / M / G", "Ctrl + Alt + Win + ↑ / ↓ / M / G", "Custom"];

    private readonly ISettingsStore _settings;
    private readonly IHotkeyRecorder _recorder;
    private readonly IHotkeyConflictChecker _conflicts;
    private CancellationTokenSource? _recording;
    private bool _loading;

    public HotkeySettingsViewModel(ISettingsStore settings, IHotkeyRecorder recorder, IHotkeyConflictChecker conflicts)
    {
        _settings = settings;
        _recorder = recorder;
        _conflicts = conflicts;
        CustomItems =
        [
            new HotkeyItemViewModel("Volume up", HotkeySlot.Up, RecordAsync, Clear),
            new HotkeyItemViewModel("Volume down", HotkeySlot.Down, RecordAsync, Clear),
            new HotkeyItemViewModel("Mute", HotkeySlot.Mute, RecordAsync, Clear),
            new HotkeyItemViewModel("Turn volume group on or off", HotkeySlot.ToggleGroup, RecordAsync, Clear),
        ];
    }

    public IReadOnlyList<HotkeyItemViewModel> CustomItems { get; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial int PresetIndex { get; set; }

    [ObservableProperty]
    public partial bool IsCustom { get; set; }

    /// <summary>Why the last recorded shortcut was refused; empty when there is nothing to report.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    /// <summary>A shortcut that another application also registered; empty when there is no conflict.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    public partial string ConflictText { get; set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    public bool HasConflict => ConflictText.Length > 0;

    /// <summary>Reads the current state into the view model. Call each time the window opens.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            var hotkeys = _settings.Current.Hotkeys;
            Enabled = hotkeys.Enabled;
            PresetIndex = (int)hotkeys.Preset;
            IsCustom = hotkeys.Preset == HotkeyPreset.Custom;
            CustomItems[(int)HotkeySlot.Up].Value = hotkeys.CustomUp;
            CustomItems[(int)HotkeySlot.Down].Value = hotkeys.CustomDown;
            CustomItems[(int)HotkeySlot.Mute].Value = hotkeys.CustomMute;
            CustomItems[(int)HotkeySlot.ToggleGroup].Value = hotkeys.CustomToggleGroup;
        }
        finally
        {
            _loading = false;
        }

        RefreshConflicts();
    }

    public void Dispose() => _recording?.Cancel();

    partial void OnEnabledChanged(bool value) => Save(h => h with { Enabled = value });

    partial void OnPresetIndexChanged(int value)
    {
        if (value < 0 || value >= PresetNames.Count)
        {
            return; // The combo box clears its selection while its items change.
        }

        IsCustom = value == (int)HotkeyPreset.Custom;
        Save(h => h with { Preset = (HotkeyPreset)value });
    }

    private async Task RecordAsync(HotkeySlot slot)
    {
        _recording?.Cancel(); // Starting another recording replaces the pending one.
        using var cts = new CancellationTokenSource();
        _recording = cts;
        var item = CustomItems[(int)slot];
        ErrorText = string.Empty;
        item.IsRecording = true;

        Hotkey? recorded;
        try
        {
            recorded = await _recorder.RecordAsync(cts.Token);
        }
        finally
        {
            item.IsRecording = false;
            if (ReferenceEquals(_recording, cts))
            {
                _recording = null;
            }
        }

        if (recorded is { } hotkey)
        {
            Assign(slot, hotkey);
        }
    }

    private void Assign(HotkeySlot slot, Hotkey hotkey)
    {
        if (!hotkey.HasSystemModifier)
        {
            ErrorText = "A shortcut needs Ctrl, Alt or the Windows key, or it would get in the way of typing.";
            return;
        }

        var clash = CustomItems.FirstOrDefault(i => i.Slot != slot && i.Value == hotkey);
        if (clash is not null)
        {
            ErrorText = $"{hotkey} is already used for \"{clash.Name}\".";
            return;
        }

        CustomItems[(int)slot].Value = hotkey;
        Save(h => slot switch
        {
            HotkeySlot.Up => h with { CustomUp = hotkey },
            HotkeySlot.Down => h with { CustomDown = hotkey },
            HotkeySlot.Mute => h with { CustomMute = hotkey },
            _ => h with { CustomToggleGroup = hotkey },
        });
    }

    private void Clear(HotkeySlot slot)
    {
        CustomItems[(int)slot].Value = null;
        Save(h => slot switch
        {
            HotkeySlot.Up => h with { CustomUp = null },
            HotkeySlot.Down => h with { CustomDown = null },
            HotkeySlot.Mute => h with { CustomMute = null },
            _ => h with { CustomToggleGroup = null },
        });
    }

    private void Save(Func<HotkeySettings, HotkeySettings> change)
    {
        if (_loading)
        {
            return;
        }

        ErrorText = string.Empty;
        _settings.Update(s => s with { Hotkeys = change(s.Hotkeys) });
        RefreshConflicts();
    }

    private void RefreshConflicts()
    {
        var taken = _settings.Current.Hotkeys.Resolve() is { } bindings
            ? new[] { bindings.Up, bindings.Down, bindings.Mute, bindings.ToggleGroup }.OfType<Hotkey>().Where(_conflicts.IsTakenByAnotherApp).Select(h => h.ToString()).ToList()
            : [];

        ConflictText = taken.Count == 0
            ? string.Empty
            : $"{string.Join(", ", taken)} {(taken.Count == 1 ? "is" : "are")} also registered by another application. " +
              "DisplayKnob gets the keys first, so that application's shortcut stops working while DisplayKnob runs.";
    }
}
