using DisplayKnob.Core.Ddc;

namespace DisplayKnob.Core.Audio;

/// <summary>
/// The monitor whose hardware volume the volume keys should control right now.
/// </summary>
/// <param name="Endpoint">The default audio endpoint that plays through this monitor.</param>
/// <param name="Monitor">The monitor's DDC worker.</param>
/// <param name="IsOverride"><see langword="true"/> when chosen by a manual mapping rather than by name.</param>
public sealed record VolumeTarget(AudioEndpoint Endpoint, DdcWorker Monitor, bool IsOverride);
