namespace DisplayKnob.Core.Input;

/// <summary>A media volume key, or the shortcut that turns the volume group on or off.</summary>
public enum VolumeKey
{
    /// <summary>VK_VOLUME_UP.</summary>
    Up,

    /// <summary>VK_VOLUME_DOWN.</summary>
    Down,

    /// <summary>VK_VOLUME_MUTE.</summary>
    Mute,

    /// <summary>Turns the volume group on or off. Keyboard shortcuts only; there is no media key for it.</summary>
    ToggleGroup,
}

/// <summary>
/// A volume key press (key down, including auto-repeat).
/// </summary>
/// <param name="Key">Which key.</param>
/// <param name="Fine">Shift or Alt was held: step by the smallest amount.</param>
public readonly record struct VolumeKeyPress(VolumeKey Key, bool Fine);
