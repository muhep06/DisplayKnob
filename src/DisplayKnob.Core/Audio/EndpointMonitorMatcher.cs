namespace DisplayKnob.Core.Audio;

/// <summary>
/// Auto-matches an audio endpoint to a monitor by name.
/// </summary>
/// <remarks>
/// Windows names HDMI/DisplayPort audio endpoints after the monitor's EDID name,
/// e.g. <c>S34C65xU (NVIDIA High Definition Audio)</c>, so the endpoint name contains the monitor name.
/// </remarks>
public static class EndpointMonitorMatcher
{
    /// <summary>
    /// Returns the key of the monitor whose name appears in <paramref name="endpointName"/>, or
    /// <see langword="null"/> if none or more than one equally good candidate matches.
    /// </summary>
    /// <param name="endpointName">Audio endpoint friendly name.</param>
    /// <param name="monitors">Candidate monitors as (key, display name).</param>
    public static string? Match(string endpointName, IEnumerable<(string Key, string Name)> monitors)
    {
        ArgumentNullException.ThrowIfNull(endpointName);
        ArgumentNullException.ThrowIfNull(monitors);

        var candidates = monitors
            .Where(m => !string.IsNullOrWhiteSpace(m.Name) && endpointName.Contains(m.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // Prefer the most specific (longest) name; a tie means e.g. two identical monitor models,
        // which only a manual override can tell apart.
        var bestLength = candidates.Max(c => c.Name.Trim().Length);
        var best = candidates.Where(c => c.Name.Trim().Length == bestLength).ToList();
        return best.Count == 1 ? best[0].Key : null;
    }
}
