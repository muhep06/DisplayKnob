namespace DisplayKnob.Core.Platform;

/// <summary>Reports mouse wheel movements system-wide, without intercepting them.</summary>
public interface IMouseWheelSource
{
    /// <summary>Raised on a background thread for every wheel movement. Handlers must be quick.</summary>
    event EventHandler<MouseWheelEventArgs>? WheelScrolled;
}
