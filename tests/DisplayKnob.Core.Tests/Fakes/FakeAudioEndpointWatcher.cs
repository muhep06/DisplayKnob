using DisplayKnob.Core.Audio;

namespace DisplayKnob.Core.Tests.Fakes;

internal sealed class FakeAudioEndpointWatcher : IAudioEndpointWatcher
{
    public event EventHandler? DefaultRenderEndpointChanged;

    public AudioEndpoint? DefaultRenderEndpoint { get; private set; }

    public List<string> PinnedEndpoints { get; } = [];

    public void SetDefault(AudioEndpoint? endpoint)
    {
        DefaultRenderEndpoint = endpoint;
        DefaultRenderEndpointChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryPinVolumeToMax(string endpointId)
    {
        PinnedEndpoints.Add(endpointId);
        return true;
    }

    public void Dispose()
    {
    }
}
