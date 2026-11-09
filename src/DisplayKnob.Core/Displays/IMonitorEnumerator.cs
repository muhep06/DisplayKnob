using DisplayKnob.Core.Ddc;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Discovers the physical monitors currently attached.
/// </summary>
public interface IMonitorEnumerator
{
    /// <summary>
    /// Enumerates physical monitors. The caller owns (and must dispose) the returned controllers.
    /// </summary>
    IReadOnlyList<IMonitorController> Enumerate();
}
