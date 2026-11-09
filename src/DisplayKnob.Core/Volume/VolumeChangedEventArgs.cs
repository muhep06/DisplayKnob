namespace DisplayKnob.Core.Volume;

/// <summary>
/// The volume state after a command was applied or the monitor was re-read (for the OSD and tray icon).
/// </summary>
public sealed class VolumeChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public VolumeChangedEventArgs(string monitorName, string? gdiDeviceName, int level, bool muted, bool userInitiated = true, string? monitorKey = null)
    {
        MonitorKey = monitorKey;
        UserInitiated = userInitiated;
        MonitorName = monitorName;
        GdiDeviceName = gdiDeviceName;
        Level = level;
        Muted = muted;
    }

    /// <summary>The monitor's <see cref="Displays.MonitorIdentity.Key"/>, if known.</summary>
    public string? MonitorKey { get; }

    /// <summary>Monitor display name.</summary>
    public string MonitorName { get; }

    /// <summary>GDI device name of the display showing the monitor (to place the OSD on it), if known.</summary>
    public string? GdiDeviceName { get; }

    /// <summary>Level on the 0–100 user scale.</summary>
    public int Level { get; }

    /// <summary>Whether the monitor is muted.</summary>
    public bool Muted { get; }

    /// <summary>
    /// <see langword="true"/> when the user changed the volume (show the OSD); <see langword="false"/> when the
    /// value was only re-read, e.g. after a KVM switch or resume (update quiet indicators only).
    /// </summary>
    public bool UserInitiated { get; }
}
