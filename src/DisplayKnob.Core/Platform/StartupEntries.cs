using System.Security;
using Microsoft.Win32;

namespace DisplayKnob.Core.Platform;

/// <summary>
/// The registry entries behind "start with Windows": the <c>Run</c> value that starts the app, and the
/// <c>StartupApproved</c> value where Windows (Task Manager's Startup tab) records that the user disabled it.
/// </summary>
public static class StartupEntries
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName = "DisplayKnob";

    /// <summary>
    /// Deletes both entries. The uninstaller calls this so no entry is left pointing at a deleted executable.
    /// </summary>
    /// <returns><see langword="false"/> if the registry refused (the entries may then remain).</returns>
    public static bool Remove() => Remove(RunKeyPath, ApprovedKeyPath);

    internal static bool Remove(string runKeyPath, string approvedKeyPath)
    {
        try
        {
            DeleteValue(runKeyPath);
            DeleteValue(approvedKeyPath);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // Nowhere to log during uninstall; the caller only needs to know it did not work. A leftover entry is
            // harmless: it fails silently at sign-in and the registration treats it as not enabled.
            return false;
        }
    }

    private static void DeleteValue(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
