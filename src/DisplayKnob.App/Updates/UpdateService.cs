using Serilog;
using Velopack;
using Velopack.Sources;

namespace DisplayKnob.App.Updates;

/// <summary>
/// Checks GitHub Releases for a newer version and applies it (Velopack).
/// Only does anything in an installed build; a dev build or a plain bin folder has nothing to update.
/// </summary>
internal sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/muhep06/DisplayKnob";

    private readonly UpdateManager _manager;
    private readonly ILogger _logger;
    private UpdateInfo? _pending;

    public UpdateService(ILogger logger)
    {
        _logger = logger.ForContext<UpdateService>();

        // No access token: this works once the repository is public. A private repository answers 404.
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    }

    /// <summary>True when running from a Velopack install, so updating is possible.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>The version found by the last check, or <see langword="null"/> when up to date or not checked.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Asks GitHub whether a newer version exists. Returns <see langword="false"/> on failure.</summary>
    public async Task<bool> CheckAsync()
    {
        if (!IsInstalled)
        {
            return false;
        }

        try
        {
            _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
            _logger.Information("Update check: {Result}", PendingVersion is { } v ? $"version {v} available" : "up to date");
            return _pending is not null;
        }
        catch (Exception ex)
        {
            // Offline, rate limited, or repository not reachable: updating is optional, so log and carry on.
            _logger.Warning(ex, "Update check failed");
            return false;
        }
    }

    /// <summary>Downloads the pending update and restarts into it. Returns only if it failed.</summary>
    public async Task<bool> ApplyPendingAsync()
    {
        if (_pending is null)
        {
            return false;
        }

        try
        {
            await _manager.DownloadUpdatesAsync(_pending).ConfigureAwait(true);
            _logger.Information("Restarting to install {Version}", PendingVersion);
            _manager.ApplyUpdatesAndRestart(_pending);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Installing the update failed");
            return false;
        }
    }
}
