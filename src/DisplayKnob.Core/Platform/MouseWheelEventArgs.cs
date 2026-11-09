namespace DisplayKnob.Core.Platform;

/// <summary>A mouse wheel movement anywhere on the desktop.</summary>
public sealed class MouseWheelEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public MouseWheelEventArgs(int delta, int x, int y)
    {
        Delta = delta;
        X = x;
        Y = y;
    }

    /// <summary>Wheel delta; one notch is 120 (positive = away from the user). High-resolution wheels send less.</summary>
    public int Delta { get; }

    /// <summary>Cursor X in physical screen coordinates.</summary>
    public int X { get; }

    /// <summary>Cursor Y in physical screen coordinates.</summary>
    public int Y { get; }
}
