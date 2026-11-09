using Microsoft.Win32;
using Serilog;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Reads a monitor's EDID from the registry (readable without admin rights).
/// </summary>
internal static class EdidRegistryReader
{
    /// <summary>
    /// Maps a device path like <c>\\?\DISPLAY#DEL4123#5&amp;1a2b&amp;0&amp;UID4353#{guid}</c>
    /// to <c>HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\DEL4123\5&amp;1a2b&amp;0&amp;UID4353\Device Parameters</c>.
    /// </summary>
    public static EdidInfo? TryRead(string devicePath, ILogger logger)
    {
        var parts = devicePath.Split('#');
        if (parts.Length < 3 || !parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase))
        {
            logger.Debug("Unrecognized monitor device path {DevicePath}", devicePath);
            return null;
        }

        var keyPath = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("EDID") is byte[] edid && EdidParser.TryParse(edid, out var info))
            {
                return info;
            }

            logger.Debug("No valid EDID at {KeyPath}", keyPath);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Missing EDID only weakens the identity; the device path still identifies the monitor.
            logger.Debug(ex, "Could not read EDID at {KeyPath}", keyPath);
        }

        return null;
    }
}
