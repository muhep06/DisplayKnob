namespace DisplayKnob.Core.Input;

/// <summary>
/// The shortcuts that are currently active, one per volume action. A <see langword="null"/> entry is unassigned.
/// </summary>
/// <param name="Up">Raises the volume.</param>
/// <param name="Down">Lowers the volume.</param>
/// <param name="Mute">Toggles mute.</param>
/// <param name="ToggleGroup">Turns the volume group on or off.</param>
public sealed record HotkeyBindings(Hotkey? Up, Hotkey? Down, Hotkey? Mute, Hotkey? ToggleGroup = null)
{
    /// <summary>The distinct virtual keys used by the bindings, so the hook can skip every other key cheaply.</summary>
    internal bool UsesKey(ushort virtualKey) =>
        Up?.VirtualKey == virtualKey || Down?.VirtualKey == virtualKey || Mute?.VirtualKey == virtualKey
        || ToggleGroup?.VirtualKey == virtualKey;
}
