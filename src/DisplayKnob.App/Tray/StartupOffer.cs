using DisplayKnob.App.Updates;
using DisplayKnob.Core.Platform;
using DisplayKnob.Core.Settings;
using Serilog;

namespace DisplayKnob.App.Tray;

/// <summary>
/// On the first run of an installed copy, offers to start DisplayKnob with Windows: a notification that, when
/// clicked, turns it on. Ignoring it changes nothing; the setting stays in the settings window.
/// </summary>
/// <remarks>
/// Only an installed copy asks: a development build would register its build folder. The offer is made once, so a user
/// who ignored it is not asked again, and one who disabled the entry in Task Manager isn't either.
/// </remarks>
internal sealed class StartupOffer
{
    private const string Tag = "startup-offer";

    private readonly TrayIconHost _tray;
    private readonly ISettingsStore _settings;
    private readonly IStartupRegistration _startup;
    private readonly UpdateService _updates;
    private readonly ILogger _logger;

    public StartupOffer(TrayIconHost tray, ISettingsStore settings, IStartupRegistration startup, UpdateService updates, ILogger logger)
    {
        _tray = tray;
        _settings = settings;
        _startup = startup;
        _updates = updates;
        _logger = logger.ForContext<StartupOffer>();
    }

    /// <summary>Shows the offer if this is the first run of an installed copy. Call on the UI thread, after the tray icon is shown.</summary>
    public void OfferOnFirstRun()
    {
        if (!_updates.IsInstalled || _settings.Current.StartupOfferShown)
        {
            return;
        }

        // Counted as made as soon as it is shown: a toast that Focus Assist swallowed is not worth nagging about.
        _settings.Update(s => s with { StartupOfferShown = true });
        if (_startup.IsEnabled)
        {
            return;
        }

        _tray.NotificationClicked += OnNotificationClicked;
        _tray.Notify("Click here to start DisplayKnob with Windows. You can change this later in Settings.", Tag);
        _logger.Information("Offered to start with Windows");
    }

    private void OnNotificationClicked(object? sender, string? tag)
    {
        if (tag != Tag)
        {
            return;
        }

        _tray.NotificationClicked -= OnNotificationClicked;
        _tray.Notify(
            _startup.TrySetEnabled(true)
                ? "DisplayKnob will start with Windows."
                : "Couldn't turn on starting with Windows. You can try it in Settings.");
    }
}
