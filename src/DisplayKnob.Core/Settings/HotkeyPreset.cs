using System.Text.Json.Serialization;

namespace DisplayKnob.Core.Settings;

/// <summary>Which set of keyboard shortcuts controls the volume.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HotkeyPreset>))]
public enum HotkeyPreset
{
    /// <summary>Ctrl+Alt with Up, Down and M.</summary>
    CtrlAlt,

    /// <summary>Ctrl+Alt+Win with Up, Down and M. Rarely used by other software.</summary>
    CtrlAltWin,

    /// <summary>The shortcuts the user recorded.</summary>
    Custom,
}
