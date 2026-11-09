using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using DisplayKnob.Core.Displays;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DisplayKnob.Core.Ddc;

/// <summary>
/// <see cref="IMonitorController"/> over the dxva2 Monitor Configuration API.
/// </summary>
internal sealed class DxvaMonitorController : IMonitorController
{
    private readonly PhysicalMonitorHandle _handle;
    private readonly ILogger _logger;

    public DxvaMonitorController(PhysicalMonitorHandle handle, MonitorIdentity identity, string displayName, string gdiDeviceName, ILogger logger)
    {
        _handle = handle;
        _logger = logger.ForContext<DxvaMonitorController>();
        Identity = identity;
        DisplayName = displayName;
        GdiDeviceName = gdiDeviceName;
    }

    public MonitorIdentity Identity { get; }

    public string DisplayName { get; }

    public string? GdiDeviceName { get; }

    public bool TryGetVcp(byte code, out VcpValue value)
    {
        if (PInvoke.GetVCPFeatureAndVCPFeatureReply(_handle, code, out _, out var current, out var maximum) == 0)
        {
            LogFailure("GetVCPFeatureAndVCPFeatureReply", code);
            value = default;
            return false;
        }

        value = new VcpValue(current, maximum);
        return true;
    }

    public bool TrySetVcp(byte code, uint value)
    {
        if (PInvoke.SetVCPFeature(_handle, code, value) == 0)
        {
            LogFailure("SetVCPFeature", code);
            return false;
        }

        return true;
    }

    public unsafe bool TryGetCapabilities([NotNullWhen(true)] out string? capabilities)
    {
        capabilities = null;
        if (PInvoke.GetCapabilitiesStringLength(_handle, out var length) == 0)
        {
            LogFailure("GetCapabilitiesStringLength", null);
            return false;
        }

        var buffer = new byte[length];
        int ok;
        fixed (byte* p = buffer)
        {
            ok = PInvoke.CapabilitiesRequestAndCapabilitiesReply(_handle, new PSTR(p), length);
        }

        if (ok == 0)
        {
            LogFailure("CapabilitiesRequestAndCapabilitiesReply", null);
            return false;
        }

        var end = Array.IndexOf(buffer, (byte)0);
        capabilities = Encoding.ASCII.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        return true;
    }

    public void Dispose() => _handle.Dispose();

    // Failures are routine (busy bus, unsupported code); Debug level, the worker logs the final outcome.
    private void LogFailure(string api, byte? code)
    {
        var error = Marshal.GetLastPInvokeError();
        _logger.Debug(
            "{Api} failed on {Monitor} (VCP {Code}): Win32 error 0x{Error:X8}",
            api,
            DisplayName,
            code?.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            error);
    }
}
