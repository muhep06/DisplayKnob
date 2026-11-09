using System.Diagnostics.CodeAnalysis;
using DisplayKnob.Core.Displays;

namespace DisplayKnob.Core.Ddc;

/// <summary>
/// Synchronous DDC/CI access to one physical monitor.
/// </summary>
/// <remarks>
/// Calls block for tens of milliseconds (capabilities: up to seconds) and fail routinely.
/// Only <see cref="DdcWorker"/> should call these, from its single sequential loop.
/// </remarks>
public interface IMonitorController : IDisposable
{
    /// <summary>Stable identity of the monitor.</summary>
    MonitorIdentity Identity { get; }

    /// <summary>Human-readable monitor name.</summary>
    string DisplayName { get; }

    /// <summary>
    /// GDI device name of the display showing this monitor (e.g. <c>\\.\DISPLAY1</c>), used to locate it on the desktop.
    /// May change after a display change; re-enumeration refreshes it.
    /// </summary>
    string? GdiDeviceName { get; }

    /// <summary>Reads a VCP feature.</summary>
    bool TryGetVcp(byte code, out VcpValue value);

    /// <summary>Writes a VCP feature.</summary>
    bool TrySetVcp(byte code, uint value);

    /// <summary>Reads the MCCS capabilities string.</summary>
    bool TryGetCapabilities([NotNullWhen(true)] out string? capabilities);
}
