using Serilog;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// An active display path: which GDI source (e.g. <c>\\.\DISPLAY1</c>) drives which monitor.
/// </summary>
/// <param name="SourceGdiName">GDI device name of the source, matches <c>MONITORINFOEX.szDevice</c>.</param>
/// <param name="DevicePath">Monitor device path (stable per monitor and connector).</param>
/// <param name="FriendlyName">Monitor friendly name (usually from EDID).</param>
internal sealed record DisplayTarget(string SourceGdiName, string DevicePath, string FriendlyName);

/// <summary>
/// Reads active display paths with QueryDisplayConfig / DisplayConfigGetDeviceInfo.
/// </summary>
internal static class DisplayConfigReader
{
    private const int MaxAttempts = 3;

    public static IReadOnlyList<DisplayTarget> ReadActiveTargets(ILogger logger)
    {
        if (!TryQueryPaths(logger, out var paths))
        {
            return [];
        }

        var targets = new List<DisplayTarget>(paths.Length);
        foreach (var path in paths)
        {
            if (TryGetSourceName(path.sourceInfo, out var sourceName) &&
                TryGetTargetName(path.targetInfo, out var devicePath, out var friendlyName))
            {
                targets.Add(new DisplayTarget(sourceName, devicePath, friendlyName));
            }
            else
            {
                logger.Debug("DisplayConfigGetDeviceInfo failed for path source {SourceId} / target {TargetId}", path.sourceInfo.id, path.targetInfo.id);
            }
        }

        return targets;
    }

    private static bool TryQueryPaths(ILogger logger, out DISPLAYCONFIG_PATH_INFO[] paths)
    {
        const QUERY_DISPLAY_CONFIG_FLAGS Flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;

        // The topology can change between the size query and the data query; retry on ERROR_INSUFFICIENT_BUFFER.
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var error = PInvoke.GetDisplayConfigBufferSizes(Flags, out var pathCount, out var modeCount);
            if (error != WIN32_ERROR.ERROR_SUCCESS)
            {
                logger.Warning("GetDisplayConfigBufferSizes failed: {Error}", error);
                break;
            }

            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            error = PInvoke.QueryDisplayConfig(Flags, ref pathCount, paths, ref modeCount, modes);
            if (error == WIN32_ERROR.ERROR_SUCCESS)
            {
                Array.Resize(ref paths, (int)pathCount);
                return true;
            }

            if (error != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
            {
                logger.Warning("QueryDisplayConfig failed: {Error}", error);
                break;
            }
        }

        paths = [];
        return false;
    }

    private static unsafe bool TryGetSourceName(DISPLAYCONFIG_PATH_SOURCE_INFO source, out string gdiName)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        request.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
        request.header.adapterId = source.adapterId;
        request.header.id = source.id;

        if (PInvoke.DisplayConfigGetDeviceInfo(ref request.header) != 0)
        {
            gdiName = string.Empty;
            return false;
        }

        gdiName = Win32Text.FromBuffer(request.viewGdiDeviceName.AsReadOnlySpan());
        return true;
    }

    private static unsafe bool TryGetTargetName(DISPLAYCONFIG_PATH_TARGET_INFO target, out string devicePath, out string friendlyName)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        request.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
        request.header.adapterId = target.adapterId;
        request.header.id = target.id;

        if (PInvoke.DisplayConfigGetDeviceInfo(ref request.header) != 0)
        {
            devicePath = friendlyName = string.Empty;
            return false;
        }

        devicePath = Win32Text.FromBuffer(request.monitorDevicePath.AsReadOnlySpan());
        friendlyName = Win32Text.FromBuffer(request.monitorFriendlyDeviceName.AsReadOnlySpan());
        return true;
    }
}
