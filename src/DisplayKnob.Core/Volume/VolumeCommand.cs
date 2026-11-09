namespace DisplayKnob.Core.Volume;

/// <summary>What a volume command does.</summary>
public enum VolumeAction
{
    /// <summary>Raise the volume by one step.</summary>
    Up,

    /// <summary>Lower the volume by one step.</summary>
    Down,

    /// <summary>Toggle mute.</summary>
    ToggleMute,

    /// <summary>Re-read the monitor's volume and mute state without changing them.</summary>
    Refresh,
}

/// <summary>
/// A volume command from any source (keys, tray wheel, ...).
/// </summary>
/// <param name="Action">What to do.</param>
/// <param name="Fine">Step by 1 instead of the configured step size.</param>
public readonly record struct VolumeCommand(VolumeAction Action, bool Fine = false);

/// <summary>
/// Accepts volume commands. Implementations must return immediately (callers include the keyboard hook).
/// </summary>
public interface IVolumeCommandSink
{
    /// <summary>
    /// Queues a command if a DDC-volume monitor is the current target.
    /// </summary>
    /// <returns><see langword="true"/> if the command was accepted (the caller should swallow the key);
    /// <see langword="false"/> if the key should pass through to Windows.</returns>
    bool TryPost(VolumeCommand command);
}

/// <summary>
/// Produces volume commands, e.g. from the keyboard. Kept abstract so a driver-based source can be added later.
/// </summary>
public interface IVolumeSource : IDisposable
{
    /// <summary>Starts delivering commands to <paramref name="sink"/>.</summary>
    void Start(IVolumeCommandSink sink);
}
