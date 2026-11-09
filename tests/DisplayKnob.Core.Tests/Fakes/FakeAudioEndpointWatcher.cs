using DisplayKnob.Core.Audio;

namespace DisplayKnob.Core.Tests.Fakes;

internal sealed class FakeAudioEndpointWatcher : IAudioEndpointWatcher
{
    public event EventHandler? DefaultRenderEndpointChanged;

    public event EventHandler<EndpointVolumeChangedEventArgs>? DefaultEndpointVolumeChanged;

    public AudioEndpoint? DefaultRenderEndpoint { get; private set; }

    public List<string> PinnedEndpoints { get; } = [];

    public List<AudioEndpoint> Endpoints { get; } = [];

    public IReadOnlyList<AudioEndpoint> GetRenderEndpoints() => Endpoints;

    public List<int> PinnedLevels { get; } = [];

    public void SetDefault(AudioEndpoint? endpoint)
    {
        DefaultRenderEndpoint = endpoint;
        DefaultRenderEndpointChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Simulates the user changing the Windows volume.</summary>
    public void RaiseVolume(string endpointId, int level, bool muted = false, bool own = false) =>
        DefaultEndpointVolumeChanged?.Invoke(this, new EndpointVolumeChangedEventArgs(endpointId, level, muted, own));

    public bool TrySetVolume(string endpointId, int level)
    {
        PinnedEndpoints.Add(endpointId);
        PinnedLevels.Add(level);
        return true;
    }

    public void Dispose()
    {
    }
}
