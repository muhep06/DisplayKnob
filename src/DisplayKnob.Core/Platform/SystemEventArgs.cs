namespace DisplayKnob.Core.Platform;

/// <summary>Data for <see cref="ISystemEventSource.SystemEventOccurred"/>.</summary>
public sealed class SystemEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public SystemEventArgs(SystemEventKind kind)
    {
        Kind = kind;
    }

    /// <summary>What happened.</summary>
    public SystemEventKind Kind { get; }
}
