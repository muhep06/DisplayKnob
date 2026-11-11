namespace DisplayKnob.Core.Volume;

/// <summary>
/// The volume group was turned on or off with its shortcut (for the confirmation message).
/// </summary>
public sealed class VolumeGroupToggledEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public VolumeGroupToggledEventArgs(bool enabled, int memberCount, string? gdiDeviceName)
    {
        Enabled = enabled;
        MemberCount = memberCount;
        GdiDeviceName = gdiDeviceName;
    }

    /// <summary>Whether the group is now on.</summary>
    public bool Enabled { get; }

    /// <summary>How many monitors are set up as group members.</summary>
    public int MemberCount { get; }

    /// <summary>GDI device name of the display to show the message on (the current target), if there is one.</summary>
    public string? GdiDeviceName { get; }
}
