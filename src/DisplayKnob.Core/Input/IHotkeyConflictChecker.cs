namespace DisplayKnob.Core.Input;

/// <summary>
/// Finds out whether another application already owns a keyboard shortcut.
/// </summary>
public interface IHotkeyConflictChecker
{
    /// <summary>
    /// True when another application registered <paramref name="hotkey"/> with the system.
    /// </summary>
    /// <remarks>
    /// Only shortcuts registered through <c>RegisterHotKey</c> (what most applications and drivers use) can be
    /// detected. Applications that watch the keyboard with their own hook are invisible to this check.
    /// </remarks>
    bool IsTakenByAnotherApp(Hotkey hotkey);
}
