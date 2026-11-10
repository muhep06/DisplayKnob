namespace DisplayKnob.App.Tray;

/// <summary>How the tray icon is drawn: the colors and what sits on the knob.</summary>
internal enum KnobIconMode
{
    /// <summary>Violet; the pointer shows the volume.</summary>
    Normal,

    /// <summary>Grey with a slash through the knob.</summary>
    Muted,

    /// <summary>Orange with an exclamation mark: the monitor doesn't answer DDC.</summary>
    Warning,
}
