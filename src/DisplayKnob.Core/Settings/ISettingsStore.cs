namespace DisplayKnob.Core.Settings;

/// <summary>
/// Holds the current settings and persists changes.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>The current settings snapshot (immutable).</summary>
    DisplayKnobSettings Current { get; }

    /// <summary>Applies a change, saves it, and raises <see cref="Changed"/>.</summary>
    void Update(Func<DisplayKnobSettings, DisplayKnobSettings> change);
}
