namespace DisplayKnob.Core.Platform;

/// <summary>A system change that can invalidate monitor handles or the monitor's state.</summary>
public enum SystemEventKind
{
    /// <summary>The display configuration changed (resolution, monitors added/removed, KVM switch).</summary>
    DisplaysChanged,

    /// <summary>A device was added or removed (a monitor may have come or gone).</summary>
    DevicesChanged,

    /// <summary>The system resumed from sleep or the displays turned back on.</summary>
    Resumed,
}
