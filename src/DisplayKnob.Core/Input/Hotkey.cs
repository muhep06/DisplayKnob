using System.Globalization;
using System.Text;

namespace DisplayKnob.Core.Input;

/// <summary>
/// A keyboard shortcut: modifier keys plus one other key.
/// </summary>
/// <param name="Modifiers">The modifier keys that must be held.</param>
/// <param name="VirtualKey">The Windows virtual-key code of the main key.</param>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, ushort VirtualKey)
{
    /// <summary>
    /// True when the shortcut has Ctrl, Alt or Win. Shift alone would hijack ordinary typing, so it is not enough.
    /// </summary>
    public bool HasSystemModifier => (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    /// <summary>The shortcut as users read it, e.g. <c>Ctrl+Alt+Up</c>.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        AppendIf(text, Modifiers, HotkeyModifiers.Control, "Ctrl");
        AppendIf(text, Modifiers, HotkeyModifiers.Alt, "Alt");
        AppendIf(text, Modifiers, HotkeyModifiers.Shift, "Shift");
        AppendIf(text, Modifiers, HotkeyModifiers.Win, "Win");
        text.Append(KeyName(VirtualKey));
        return text.ToString();
    }

    private static void AppendIf(StringBuilder text, HotkeyModifiers modifiers, HotkeyModifiers flag, string name)
    {
        if (modifiers.HasFlag(flag))
        {
            text.Append(name).Append('+');
        }
    }

    private static string KeyName(ushort vk) => vk switch
    {
        0x21 => "PgUp",
        0x22 => "PgDn",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => "Num" + (vk - 0x60).ToString(CultureInfo.InvariantCulture),
        0x6A => "Num*",
        0x6B => "Num+",
        0x6D => "Num-",
        0x6E => "Num.",
        0x6F => "Num/",
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture),
        _ => "Key 0x" + vk.ToString("X2", CultureInfo.InvariantCulture),
    };
}
