using System.Security;
using Microsoft.Win32;
using Serilog;

namespace DisplayKnob.Core.Platform;

/// <summary>
/// <see cref="IStartupRegistration"/> via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>: per user, no admin rights.
/// </summary>
/// <remarks>
/// Windows also keeps the Startup tab's enabled/disabled state of each entry in <c>StartupApproved\Run</c>. A user who
/// disables DisplayKnob there keeps the <c>Run</c> value, but it no longer starts; so that flag counts as "off", and
/// turning the setting on clears it. Otherwise the setting would say on while nothing starts, or stay off after the user
/// switched it on.
/// </remarks>
internal sealed class RunKeyStartupRegistration : IStartupRegistration
{
    private readonly ILogger _logger;
    private readonly string? _command;
    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;

    public RunKeyStartupRegistration(ILogger logger)
        : this(logger, Environment.ProcessPath is { } path ? $"\"{path}\"" : null, StartupEntries.RunKeyPath, StartupEntries.ApprovedKeyPath)
    {
    }

    // Tests point this at a key of their own.
    internal RunKeyStartupRegistration(ILogger logger, string? command, string runKeyPath, string approvedKeyPath)
    {
        _logger = logger.ForContext<RunKeyStartupRegistration>();
        _command = command;
        _runKeyPath = runKeyPath;
        _approvedKeyPath = approvedKeyPath;
    }

    // Only counts as enabled if it points at this executable (a stale entry from an old location doesn't)
    // and the user hasn't disabled it in Task Manager.
    public bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            if (_command is null || !string.Equals(run?.GetValue(StartupEntries.ValueName) as string, _command, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath);
            return !IsDisabledByUser(approved?.GetValue(StartupEntries.ValueName));
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        if (_command is null)
        {
            _logger.Warning("Unknown executable path; can't change starting with Windows");
            return false;
        }

        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(_runKeyPath);
            if (enabled)
            {
                run.SetValue(StartupEntries.ValueName, _command);
            }
            else
            {
                run.DeleteValue(StartupEntries.ValueName, throwOnMissingValue: false);
            }

            // No value means "enabled" to Windows; either way a stale "disabled" flag must not outlive this change.
            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
            approved?.DeleteValue(StartupEntries.ValueName, throwOnMissingValue: false);

            _logger.Information("Start with Windows: {Enabled}", enabled);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // Policy can lock the Run key; the toggle just doesn't stick.
            _logger.Warning(ex, "Could not change starting with Windows");
            return false;
        }
    }

    // Windows writes 12 bytes whose first byte is 02 (or 06) when enabled and 03 when the user disabled the entry:
    // an odd first byte means disabled.
    private static bool IsDisabledByUser(object? approved) => approved is byte[] { Length: > 0 } bytes && (bytes[0] & 1) == 1;
}
