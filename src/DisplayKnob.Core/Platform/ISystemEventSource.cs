namespace DisplayKnob.Core.Platform;

/// <summary>Reports display, device and power changes.</summary>
public interface ISystemEventSource
{
    /// <summary>Raised on a background thread. Bursts are common; subscribers should debounce.</summary>
    event EventHandler<SystemEventArgs>? SystemEventOccurred;
}
