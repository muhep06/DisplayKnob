using DisplayKnob.App.Osd;
using DisplayKnob.App.Tray;
using DisplayKnob.App.Views;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Volume;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;

namespace DisplayKnob.App;

/// <summary>
/// Tray-only application: no main window is created at startup.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private TrayIconHost? _tray;

    public App()
    {
        InitializeComponent();

        // There is no main window; keep running until the user picks "Exit".
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Logger = LoggingSetup.CreateLogger();
        Log.Information("DisplayKnob {Version} starting", typeof(App).Assembly.GetName().Version);

        _services = Composition.Build(Log.Logger);

        _tray = _services.GetRequiredService<TrayIconHost>();
        _tray.ExitRequested += async (_, _) => await ShutdownAsync();
        _tray.Show();
        _services.GetRequiredService<TrayWheelHandler>();

        var settingsWindow = _services.GetRequiredService<SettingsWindowHost>();
        var flyout = _services.GetRequiredService<TrayFlyoutPresenter>();
        _tray.SettingsRequested += (_, _) => settingsWindow.Show();
        _tray.FlyoutRequested += (_, _) => flyout.Toggle(_tray.IconId);
        flyout.SettingsRequested += (_, _) => settingsWindow.Show();

        // Starting DisplayKnob again (e.g. from the Start menu) opens the tray flyout, as a click on the icon would.
        _services.GetRequiredService<LaunchedAgainListener>();
        OpenWindowIfRequested(settingsWindow, flyout, _tray);

        // UI-thread objects must be created here, not lazily from a background thread.
        var osd = _services.GetRequiredService<OsdPresenter>();
        PreviewOsdIfRequested(osd);

        _ = StartCoreAsync(_services);
        _services.GetRequiredService<StartupOffer>().OfferOnFirstRun();
        _ = _tray.CheckForUpdatesInBackgroundAsync();
    }

    // Debug builds only: DISPLAYKNOB_OSD_PREVIEW=<level> or "muted" shows the OSD once at startup,
    // so its look can be checked without a DDC monitor as the audio output.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void PreviewOsdIfRequested(OsdPresenter osd)
    {
        var value = Environment.GetEnvironmentVariable("DISPLAYKNOB_OSD_PREVIEW");
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var muted = value.Equals("muted", StringComparison.OrdinalIgnoreCase);
        var level = int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 42;
        osd.Preview(Math.Clamp(level, 0, 100), muted);
    }

    // Debug builds only: DISPLAYKNOB_OPEN=settings or flyout opens that window at startup (for checking the look).
    [System.Diagnostics.Conditional("DEBUG")]
    private static void OpenWindowIfRequested(SettingsWindowHost settings, TrayFlyoutPresenter flyout, TrayIconHost tray)
    {
        switch (Environment.GetEnvironmentVariable("DISPLAYKNOB_OPEN"))
        {
            case "settings":
                settings.Show();
                break;
            case "flyout":
                // After the first monitor enumeration, so there is something to show.
                s_debugTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                s_debugTimer.Interval = TimeSpan.FromSeconds(2);
                s_debugTimer.IsRepeating = false;
                s_debugTimer.Tick += (_, _) => flyout.Toggle(tray.IconId);
                s_debugTimer.Start();
                break;
        }
    }

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? s_debugTimer; // Kept alive until it fires.

    private static async Task StartCoreAsync(IServiceProvider services)
    {
        try
        {
            // The mapper subscribes to audio/monitor changes on construction, so create it before the first refresh.
            services.GetRequiredService<AudioMonitorMapper>();
            services.GetRequiredService<EndpointVolumeMirror>();
            await services.GetRequiredService<MonitorRegistry>().RefreshAsync();

            // From here on, display changes, KVM switches and resume re-enumerate the monitors.
            services.GetRequiredService<DisplayChangeWatcher>();

            services.GetRequiredService<IVolumeSource>().Start(services.GetRequiredService<IVolumeCommandSink>());
        }
        catch (Exception ex)
        {
            // Keep the tray alive so the user can still probe and read the log.
            Log.Error(ex, "Startup failed");
        }
    }

    private async Task ShutdownAsync()
    {
        Log.Information("DisplayKnob exiting");

        // The provider owns the tray icon and Core services (some are IAsyncDisposable only).
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        Log.CloseAndFlush();

        Exit();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception");
        Log.CloseAndFlush();
    }
}
