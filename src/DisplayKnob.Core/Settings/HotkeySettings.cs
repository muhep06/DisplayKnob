using DisplayKnob.Core.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace DisplayKnob.Core.Settings;

/// <summary>
/// Keyboard shortcuts for keyboards without media keys. They are on by default.
/// </summary>
public sealed record HotkeySettings
{
    private const ushort KeyUp = (ushort)VIRTUAL_KEY.VK_UP;
    private const ushort KeyDown = (ushort)VIRTUAL_KEY.VK_DOWN;
    private const ushort KeyMute = (ushort)VIRTUAL_KEY.VK_M;
    private const ushort KeyToggleGroup = (ushort)VIRTUAL_KEY.VK_G;

    /// <summary>When <see langword="false"/> the shortcuts do nothing and reach the focused application.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which shortcuts are used.</summary>
    public HotkeyPreset Preset { get; set; } = HotkeyPreset.CtrlAlt;

    /// <summary>Volume up, when <see cref="Preset"/> is <see cref="HotkeyPreset.Custom"/>.</summary>
    public Hotkey? CustomUp { get; set; }

    /// <summary>Volume down, when <see cref="Preset"/> is <see cref="HotkeyPreset.Custom"/>.</summary>
    public Hotkey? CustomDown { get; set; }

    /// <summary>Mute, when <see cref="Preset"/> is <see cref="HotkeyPreset.Custom"/>.</summary>
    public Hotkey? CustomMute { get; set; }

    /// <summary>Turns the volume group on or off, when <see cref="Preset"/> is <see cref="HotkeyPreset.Custom"/>.</summary>
    public Hotkey? CustomToggleGroup { get; set; }

    /// <summary>The shortcuts to listen for, or <see langword="null"/> when they are turned off.</summary>
    public HotkeyBindings? Resolve() => !Enabled ? null : Preset switch
    {
        HotkeyPreset.CtrlAlt => Fixed(HotkeyModifiers.Control | HotkeyModifiers.Alt),
        HotkeyPreset.CtrlAltWin => Fixed(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win),
        _ => new HotkeyBindings(CustomUp, CustomDown, CustomMute, CustomToggleGroup),
    };

    private static HotkeyBindings Fixed(HotkeyModifiers modifiers) =>
        new(new Hotkey(modifiers, KeyUp), new Hotkey(modifiers, KeyDown), new Hotkey(modifiers, KeyMute), new Hotkey(modifiers, KeyToggleGroup));
}
