namespace DisplayKnob.Core.Platform;

/// <summary>Starting DisplayKnob when the user signs in.</summary>
public interface IStartupRegistration
{
    /// <summary>Whether DisplayKnob starts with Windows (for this executable path).</summary>
    bool IsEnabled { get; }

    /// <summary>Turns starting with Windows on or off. Returns <see langword="false"/> if the change failed.</summary>
    bool TrySetEnabled(bool enabled);
}
