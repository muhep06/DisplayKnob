using System.Runtime.InteropServices;
using DisplayKnob.Core.Ddc;
using Serilog;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Enumerates physical monitors via dxva2 and identifies them via DisplayConfig + EDID.
/// </summary>
internal sealed class DxvaMonitorEnumerator : IMonitorEnumerator
{
    private readonly ILogger _logger;

    public DxvaMonitorEnumerator(ILogger logger)
    {
        _logger = logger.ForContext<DxvaMonitorEnumerator>();
    }

    public IReadOnlyList<IMonitorController> Enumerate()
    {
        var targets = DisplayConfigReader.ReadActiveTargets(_logger);
        var controllers = new List<IMonitorController>();

        foreach (var hMonitor in EnumerateHMonitors())
        {
            if (!TryGetGdiDeviceName(hMonitor, out var gdiName))
            {
                _logger.Debug("GetMonitorInfo failed: Win32 error 0x{Error:X8}", Marshal.GetLastPInvokeError());
                continue;
            }

            // With duplicated displays one GDI source drives several monitors; pair them in order.
            var sourceTargets = targets.Where(t => string.Equals(t.SourceGdiName, gdiName, StringComparison.OrdinalIgnoreCase)).ToList();
            var physicalMonitors = GetPhysicalMonitors(hMonitor, gdiName);
            if (sourceTargets.Count != physicalMonitors.Length)
            {
                _logger.Debug(
                    "{GdiName}: {PhysicalCount} physical monitor(s) but {TargetCount} DisplayConfig target(s); pairing by order",
                    gdiName,
                    physicalMonitors.Length,
                    sourceTargets.Count);
            }

            for (var i = 0; i < physicalMonitors.Length; i++)
            {
                var target = i < sourceTargets.Count ? sourceTargets[i] : null;
                controllers.Add(CreateController(physicalMonitors[i], target, $"{gdiName}#{i}"));
            }
        }

        return controllers;
    }

    private DxvaMonitorController CreateController(PHYSICAL_MONITOR physical, DisplayTarget? target, string fallbackLocation)
    {
        var handle = new PhysicalMonitorHandle(physical.hPhysicalMonitor);
        var edid = target is null ? null : EdidRegistryReader.TryRead(target.DevicePath, _logger);
        var identity = MonitorIdentity.Create(target?.DevicePath, edid, fallbackLocation);

        var description = Win32Text.FromBuffer(physical.szPhysicalMonitorDescription.AsReadOnlySpan());
        var name = FirstNonEmpty(edid?.Name, target?.FriendlyName, description, fallbackLocation);

        return new DxvaMonitorController(handle, identity, name, _logger);
    }

    private PHYSICAL_MONITOR[] GetPhysicalMonitors(HMONITOR hMonitor, string gdiName)
    {
        if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
        {
            _logger.Debug("{GdiName}: no physical monitors (Win32 error 0x{Error:X8})", gdiName, Marshal.GetLastPInvokeError());
            return [];
        }

        var monitors = new PHYSICAL_MONITOR[count];
        if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(hMonitor, monitors))
        {
            _logger.Debug("{GdiName}: GetPhysicalMonitorsFromHMONITOR failed (Win32 error 0x{Error:X8})", gdiName, Marshal.GetLastPInvokeError());
            return [];
        }

        return monitors;
    }

    private static unsafe List<HMONITOR> EnumerateHMonitors()
    {
        var result = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(default, (RECT?)null, (hMonitor, _, _, _) =>
        {
            result.Add(hMonitor);
            return true;
        }, default);
        return result;
    }

    private static unsafe bool TryGetGdiDeviceName(HMONITOR hMonitor, out string gdiName)
    {
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(hMonitor, (MONITORINFO*)&info))
        {
            gdiName = string.Empty;
            return false;
        }

        gdiName = Win32Text.FromBuffer(info.szDevice.AsReadOnlySpan());
        return true;
    }

    private static string FirstNonEmpty(params ReadOnlySpan<string?> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
