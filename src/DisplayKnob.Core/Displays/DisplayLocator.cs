using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Finds where a display sits on the virtual desktop.
/// </summary>
public static class DisplayLocator
{
    /// <summary>
    /// Gets the work area of the display with the given GDI device name, or of the primary display
    /// when <paramref name="gdiDeviceName"/> is <see langword="null"/> or no longer exists.
    /// </summary>
    public static bool TryGetWorkArea(string? gdiDeviceName, out DisplayWorkArea area)
    {
        DisplayWorkArea? match = null;
        DisplayWorkArea? primary = null;

        foreach (var (name, info, scale) in EnumerateDisplays())
        {
            var work = info.monitorInfo.rcWork;
            var candidate = new DisplayWorkArea(work.left, work.top, work.right - work.left, work.bottom - work.top, scale);
            if ((info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0)
            {
                primary = candidate;
            }

            if (gdiDeviceName is not null && string.Equals(name, gdiDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                match = candidate;
            }
        }

        area = match ?? primary ?? default;
        return match is not null || primary is not null;
    }

    /// <summary>
    /// Gets the work area of the display containing the point (physical pixels), or of the primary display
    /// when no display contains it.
    /// </summary>
    public static bool TryGetWorkAreaAt(int x, int y, out DisplayWorkArea area)
    {
        foreach (var (name, info, _) in EnumerateDisplays())
        {
            var bounds = info.monitorInfo.rcMonitor;
            if (x >= bounds.left && x < bounds.right && y >= bounds.top && y < bounds.bottom)
            {
                return TryGetWorkArea(name, out area);
            }
        }

        return TryGetWorkArea(null, out area);
    }

    private static unsafe List<(string Name, MONITORINFOEXW Info, double Scale)> EnumerateDisplays()
    {
        var result = new List<(string, MONITORINFOEXW, double)>();
        PInvoke.EnumDisplayMonitors(default, (RECT?)null, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (PInvoke.GetMonitorInfo(hMonitor, (MONITORINFO*)&info))
            {
                var scale = PInvoke.GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded
                    ? dpiX / 96.0
                    : 1.0;
                result.Add((Win32Text.FromBuffer(info.szDevice.AsReadOnlySpan()), info, scale));
            }

            return true;
        }, default);
        return result;
    }
}
