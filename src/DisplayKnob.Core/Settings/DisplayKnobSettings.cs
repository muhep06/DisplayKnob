namespace DisplayKnob.Core.Settings;

/// <summary>
/// User settings, persisted as JSON in <c>%AppData%\DisplayKnob\settings.json</c>.
/// </summary>
public sealed record DisplayKnobSettings
{
    /// <summary>Defaults used when no settings file exists.</summary>
    public static DisplayKnobSettings Default { get; } = new();

    /// <summary>
    /// Manual audio endpoint → monitor mapping, keyed by endpoint ID. The value is a
    /// <see cref="Displays.MonitorIdentity.Key"/>, or an empty string to mean "this endpoint is not a monitor".
    /// Endpoints not listed here are matched automatically by name.
    /// </summary>
    public IReadOnlyDictionary<string, string> EndpointMonitorOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When the default audio endpoint is a DDC-controlled monitor, keep its Windows (software)
    /// volume at 100% and unmuted so the monitor's hardware volume is the only attenuation.
    /// </summary>
    public bool PinEndpointVolume { get; init; } = true;
}
