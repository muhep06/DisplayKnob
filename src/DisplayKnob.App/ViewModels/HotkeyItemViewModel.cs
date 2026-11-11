using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayKnob.Core.Input;

namespace DisplayKnob.App.ViewModels;

/// <summary>Which volume action a custom shortcut triggers.</summary>
internal enum HotkeySlot
{
    Up,
    Down,
    Mute,
    ToggleGroup,
}

/// <summary>One recordable shortcut in the settings window (for the Custom preset).</summary>
internal sealed partial class HotkeyItemViewModel : ObservableObject
{
    public HotkeyItemViewModel(string name, HotkeySlot slot, Func<HotkeySlot, Task> record, Action<HotkeySlot> clear)
    {
        Name = name;
        Slot = slot;
        RecordCommand = new AsyncRelayCommand(() => record(slot));
        ClearCommand = new RelayCommand(() => clear(slot));
    }

    public string Name { get; }

    public HotkeySlot Slot { get; }

    public IAsyncRelayCommand RecordCommand { get; }

    public IRelayCommand ClearCommand { get; }

    /// <summary>The recorded shortcut, or <see langword="null"/> when none is set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    [NotifyPropertyChangedFor(nameof(HasValue))]
    public partial Hotkey? Value { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsRecording { get; set; }

    public bool HasValue => Value is not null;

    public string ButtonText => IsRecording ? "Press a shortcut…" : Value?.ToString() ?? "Not set";
}
