namespace DisplayKnob.Core.Settings;

/// <summary>
/// User settings, persisted as JSON in <c>%AppData%\DisplayKnob\settings.json</c>.
/// </summary>
/// <remarks>
/// Treat instances as immutable and change them with <c>with</c>. The properties use <c>set</c>, not <c>init</c>,
/// on purpose: the System.Text.Json source generator passes <c>init</c> properties through a constructor-style
/// creator and gives every property missing from the file <c>default</c> instead of its initializer. A settings
/// file written by an older version would then load with every new option off or zero.
/// </remarks>
public sealed record DisplayKnobSettings
{
    /// <summary>Defaults used when no settings file exists.</summary>
    public static DisplayKnobSettings Default { get; } = new();

    /// <summary>
    /// Manual audio endpoint → monitor mapping, keyed by endpoint ID. The value is a
    /// <see cref="Displays.MonitorIdentity.Key"/>, or an empty string to mean "this endpoint is not a monitor".
    /// Endpoints not listed here are matched automatically by name.
    /// </summary>
    public IReadOnlyDictionary<string, string> EndpointMonitorOverrides { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// When the default audio endpoint is a DDC-controlled monitor, keep its Windows (software)
    /// volume at 100% and unmuted so the monitor's hardware volume is the only attenuation.
    /// </summary>
    public bool PinEndpointVolume { get; set; } = true;

    /// <summary>Volume step per key press, in percent of the user scale. Shift/Alt + key always steps by 1.</summary>
    public int VolumeStep { get; set; } = 2;

    /// <summary>
    /// Apply changes of the Windows volume (wheel over the Windows volume icon, the Quick Settings slider,
    /// other apps) to the monitor, then return the Windows volume to its rest level. Off by default: the Windows
    /// volume UI then shows the rest level (98) instead of the monitor's volume, which is confusing.
    /// </summary>
    public bool MirrorWindowsVolume { get; set; }

    /// <summary>
    /// Re-read the monitor's volume this often (seconds) while it is the target, so changes made with the
    /// monitor's buttons or from another machine show up. 0 turns polling off.
    /// </summary>
    public int VolumePollSeconds { get; set; } = 10;

    /// <summary>Show the on-screen volume indicator when the volume changes.</summary>
    public bool ShowOsd { get; set; } = true;

    /// <summary>Keyboard shortcuts for keyboards without media keys.</summary>
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>Per-monitor settings keyed by monitor identity key.</summary>
    public IReadOnlyDictionary<string, MonitorSettings> Monitors { get; set; } = new Dictionary<string, MonitorSettings>();

    /// <summary>Settings for a monitor, or defaults if none are saved.</summary>
    public MonitorSettings ForMonitor(string monitorKey) =>
        Monitors.TryGetValue(monitorKey, out var settings) ? settings : MonitorSettings.Default;
}
