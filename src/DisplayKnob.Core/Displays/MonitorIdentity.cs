namespace DisplayKnob.Core.Displays;

/// <summary>
/// Stable identity of a physical monitor: EDID model/serial plus the monitor device path.
/// Never derived from the enumeration index.
/// </summary>
/// <param name="Key">The combined key used to persist per-monitor settings.</param>
/// <param name="EdidKey">EDID-derived key (<see cref="EdidInfo.ToKey"/>), when EDID was readable.</param>
/// <param name="DevicePath">Monitor device path from DisplayConfig, when known.</param>
public sealed record MonitorIdentity(string Key, string? EdidKey, string? DevicePath)
{
    /// <summary>
    /// Builds an identity from what could be discovered about the monitor.
    /// </summary>
    /// <param name="devicePath">Monitor device path, or <see langword="null"/> if unknown.</param>
    /// <param name="edid">Parsed EDID, or <see langword="null"/> if unreadable.</param>
    /// <param name="fallback">Used in place of the device path when it is unknown (e.g. the GDI device name).</param>
    public static MonitorIdentity Create(string? devicePath, EdidInfo? edid, string fallback)
    {
        ArgumentException.ThrowIfNullOrEmpty(fallback);

        var edidKey = edid?.ToKey();
        var location = string.IsNullOrEmpty(devicePath) ? fallback : devicePath;

        // Device paths are case-insensitive; normalize so the key is stable.
        var key = $"{edidKey ?? "no-edid"}|{location.ToUpperInvariant()}";
        return new MonitorIdentity(key, edidKey, devicePath);
    }

    /// <inheritdoc/>
    public override string ToString() => Key;
}
