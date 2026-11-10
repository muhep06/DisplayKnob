namespace DisplayKnob.Core.Input;

/// <summary>
/// Captures the next keyboard shortcut the user presses, for the settings UI.
/// </summary>
/// <remarks>
/// Recording goes through the global keyboard hook, so it sees combinations a window can't (Win+key) and
/// the pressed key never reaches the application. Modifier keys alone are not a shortcut: recording waits for a
/// key that is not a modifier. Right Alt is ignored because it is AltGr on many layouts.
/// </remarks>
public interface IHotkeyRecorder
{
    /// <summary>Waits for the user to press a shortcut.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>
    /// The shortcut, or <see langword="null"/> when recording was cancelled, replaced by another recording,
    /// the user pressed Esc, or the keyboard hook is not running.
    /// </returns>
    Task<Hotkey?> RecordAsync(CancellationToken cancellationToken = default);
}
