namespace DisplayKnob.Core.Volume;

/// <summary>
/// The volume state after a command was applied (for the OSD and tray icon).
/// </summary>
public sealed class VolumeChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public VolumeChangedEventArgs(string monitorName, int level, bool muted)
    {
        MonitorName = monitorName;
        Level = level;
        Muted = muted;
    }

    /// <summary>Monitor display name.</summary>
    public string MonitorName { get; }

    /// <summary>Level on the 0–100 user scale.</summary>
    public int Level { get; }

    /// <summary>Whether the monitor is muted.</summary>
    public bool Muted { get; }
}
