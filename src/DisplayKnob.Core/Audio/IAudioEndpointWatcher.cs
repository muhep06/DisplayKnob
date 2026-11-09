namespace DisplayKnob.Core.Audio;

/// <summary>
/// Tracks the default audio render endpoint (the one the volume keys affect) and its Windows volume.
/// </summary>
public interface IAudioEndpointWatcher : IDisposable
{
    /// <summary>Raised (on a thread-pool thread) after <see cref="DefaultRenderEndpoint"/> changes.</summary>
    event EventHandler? DefaultRenderEndpointChanged;

    /// <summary>Raised (on a thread-pool thread) when the default endpoint's Windows volume or mute changes.</summary>
    event EventHandler<EndpointVolumeChangedEventArgs>? DefaultEndpointVolumeChanged;

    /// <summary>The current default render endpoint, or <see langword="null"/> if there is none.</summary>
    AudioEndpoint? DefaultRenderEndpoint { get; }

    /// <summary>
    /// Sets the endpoint's Windows (software) volume to <paramref name="level"/> (0–100) and unmutes it.
    /// The resulting <see cref="DefaultEndpointVolumeChanged"/> is flagged <see cref="EndpointVolumeChangedEventArgs.IsOwnChange"/>.
    /// </summary>
    /// <returns><see langword="false"/> if the endpoint couldn't be reached.</returns>
    bool TrySetVolume(string endpointId, int level);
}
