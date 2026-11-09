namespace DisplayKnob.Core.Audio;

/// <summary>The Windows (software) volume of the default render endpoint changed.</summary>
public sealed class EndpointVolumeChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public EndpointVolumeChangedEventArgs(string endpointId, int level, bool muted, bool isOwnChange)
    {
        EndpointId = endpointId;
        Level = level;
        Muted = muted;
        IsOwnChange = isOwnChange;
    }

    /// <summary>The endpoint whose volume changed.</summary>
    public string EndpointId { get; }

    /// <summary>The new Windows volume, 0–100 as shown by Windows.</summary>
    public int Level { get; }

    /// <summary>Whether the endpoint is muted in Windows.</summary>
    public bool Muted { get; }

    /// <summary><see langword="true"/> when DisplayKnob itself made the change (see <see cref="IAudioEndpointWatcher.TrySetVolume"/>).</summary>
    public bool IsOwnChange { get; }
}
