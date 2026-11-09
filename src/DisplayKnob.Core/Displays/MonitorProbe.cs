using System.Globalization;
using DisplayKnob.Core.Ddc;
using Serilog;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Diagnostic: re-enumerates monitors and logs each one's identity, capabilities, and key VCP values.
/// </summary>
public sealed class MonitorProbe
{
    private readonly MonitorRegistry _registry;
    private readonly ILogger _logger;

    /// <summary>Creates the probe.</summary>
    public MonitorProbe(MonitorRegistry registry, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _logger = logger.ForContext<MonitorProbe>();
    }

    /// <summary>
    /// Runs the probe. Results go to the log; DDC failures are reported there, not thrown.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.Information("Monitor probe started");

        await _registry.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var monitors = _registry.Monitors;

        // Each monitor has its own worker, so they can be probed in parallel.
        await Task.WhenAll(monitors.Select((m, i) => ProbeAsync(m, i + 1, cancellationToken))).ConfigureAwait(false);

        _logger.Information("Monitor probe finished ({Count} monitor(s))", monitors.Count);
    }

    private async Task ProbeAsync(DdcWorker monitor, int index, CancellationToken cancellationToken)
    {
        var capabilities = await monitor.ReadCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        var brightness = await monitor.ReadVcpAsync(VcpCodes.Brightness, cancellationToken).ConfigureAwait(false);
        var volume = await monitor.ReadVcpAsync(VcpCodes.AudioVolume, cancellationToken).ConfigureAwait(false);
        var mute = await monitor.ReadVcpAsync(VcpCodes.AudioMute, cancellationToken).ConfigureAwait(false);

        // One log call per monitor so parallel probes don't interleave lines.
        _logger.Information(
            "Monitor #{Index}: {Name}{NewLine}" +
            "  id:           {MonitorId}{NewLine}" +
            "  brightness:   {Brightness} (VCP 0x10){NewLine}" +
            "  volume:       {Volume} (VCP 0x62){NewLine}" +
            "  mute:         {Mute} (VCP 0x8D){NewLine}" +
            "  capabilities: {Capabilities}",
            index,
            monitor.DisplayName,
            Environment.NewLine,
            monitor.Identity.Key,
            Environment.NewLine,
            Format(brightness),
            Environment.NewLine,
            Format(volume),
            Environment.NewLine,
            FormatMute(mute),
            Environment.NewLine,
            capabilities ?? "no reply");
    }

    private static string Format(VcpValue? value) =>
        value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v.Current}/{v.Maximum}") : "no reply";

    // MCCS: 1 = muted, 2 = unmuted.
    private static string FormatMute(VcpValue? value) => value?.Current switch
    {
        null => "no reply",
        1 => "muted (1)",
        2 => "unmuted (2)",
        var other => string.Create(CultureInfo.InvariantCulture, $"unexpected value {other}"),
    };
}
