using System.Security;
using Microsoft.Win32;
using Serilog;

namespace DisplayKnob.Core.Platform;

/// <summary>
/// <see cref="IStartupRegistration"/> via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>: per user, no admin rights.
/// </summary>
internal sealed class RunKeyStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplayKnob";

    private readonly ILogger _logger;
    private readonly string? _command;

    public RunKeyStartupRegistration(ILogger logger)
    {
        _logger = logger.ForContext<RunKeyStartupRegistration>();
        _command = Environment.ProcessPath is { } path ? $"\"{path}\"" : null;
    }

    // Only counts as enabled if it points at this executable; a stale entry from an old location doesn't.
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return _command is not null && string.Equals(key?.GetValue(ValueName) as string, _command, StringComparison.OrdinalIgnoreCase);
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
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, _command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

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
}
