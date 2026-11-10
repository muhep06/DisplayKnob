using System.Text.Json.Serialization;

namespace DisplayKnob.Core.Input;

/// <summary>Modifier keys of a <see cref="Hotkey"/>.</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<HotkeyModifiers>))]
public enum HotkeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Either Ctrl key.</summary>
    Control = 1,

    /// <summary>Either Alt key.</summary>
    Alt = 2,

    /// <summary>Either Shift key.</summary>
    Shift = 4,

    /// <summary>Either Windows key.</summary>
    Win = 8,
}
