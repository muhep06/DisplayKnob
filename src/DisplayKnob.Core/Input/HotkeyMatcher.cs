namespace DisplayKnob.Core.Input;

/// <summary>
/// Decides whether a key press is one of the configured volume shortcuts. Pure, so it can be unit-tested.
/// </summary>
internal static class HotkeyMatcher
{
    /// <summary>
    /// Matches a key-down against <paramref name="bindings"/>.
    /// </summary>
    /// <param name="bindings">The active shortcuts.</param>
    /// <param name="virtualKey">The key that went down.</param>
    /// <param name="held">The modifier keys currently held.</param>
    /// <param name="altGr">
    /// The Alt held is AltGr (right Alt on a layout that has it). AltGr types characters, so it never matches:
    /// otherwise a Ctrl+Alt shortcut would swallow ordinary typing.
    /// </param>
    /// <param name="key">The volume action of the matching shortcut.</param>
    /// <param name="fine">Shift was held although the shortcut doesn't include it: step by the smallest amount.</param>
    /// <returns><see langword="true"/> if the press is a shortcut and should be swallowed.</returns>
    public static bool TryMatch(HotkeyBindings bindings, ushort virtualKey, HotkeyModifiers held, bool altGr, out VolumeKey key, out bool fine)
    {
        key = default;
        fine = false;

        if (altGr)
        {
            return false;
        }

        if (Matches(bindings.Up, virtualKey, held))
        {
            key = VolumeKey.Up;
        }
        else if (Matches(bindings.Down, virtualKey, held))
        {
            key = VolumeKey.Down;
        }
        else if (Matches(bindings.Mute, virtualKey, held))
        {
            key = VolumeKey.Mute;
        }
        else
        {
            return false;
        }

        var binding = key switch
        {
            VolumeKey.Up => bindings.Up,
            VolumeKey.Down => bindings.Down,
            _ => bindings.Mute,
        };

        fine = held.HasFlag(HotkeyModifiers.Shift) && binding is { } b && !b.Modifiers.HasFlag(HotkeyModifiers.Shift);
        return true;
    }

    // Ctrl, Alt and Win must match exactly: Ctrl+Alt+Up must not fire for Ctrl+Alt+Win+Up.
    // Shift may be extra (fine step), but a required Shift must be held.
    private static bool Matches(Hotkey? binding, ushort virtualKey, HotkeyModifiers held)
    {
        if (binding is not { } hotkey || hotkey.VirtualKey != virtualKey)
        {
            return false;
        }

        const HotkeyModifiers Exact = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win;
        if ((held & Exact) != (hotkey.Modifiers & Exact))
        {
            return false;
        }

        return !hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift) || held.HasFlag(HotkeyModifiers.Shift);
    }
}
