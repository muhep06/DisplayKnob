namespace DisplayKnob.Core.Audio;

/// <summary>
/// Tracks the default audio render endpoint (the one the volume keys affect).
/// </summary>
public interface IAudioEndpointWatcher : IDisposable
{
    /// <summary>Raised (on a thread-pool thread) after <see cref="DefaultRenderEndpoint"/> changes.</summary>
    event EventHandler? DefaultRenderEndpointChanged;

    /// <summary>The current default render endpoint, or <see langword="null"/> if there is none.</summary>
    AudioEndpoint? DefaultRenderEndpoint { get; }

    /// <summary>
    /// Sets the endpoint's Windows (software) volume to 100% and unmutes it.
    /// </summary>
    /// <returns><see langword="false"/> if the endpoint couldn't be reached.</returns>
    bool TryPinVolumeToMax(string endpointId);
}
