using DisplayKnob.App.Osd;
using DisplayKnob.App.Tray;
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

        // UI-thread objects must be created here, not lazily from a background thread.
        var osd = _services.GetRequiredService<OsdPresenter>();
        PreviewOsdIfRequested(osd);

        _ = StartCoreAsync(_services);
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

    private static async Task StartCoreAsync(IServiceProvider services)
    {
        try
        {
            // The mapper subscribes to audio/monitor changes on construction, so create it before the first refresh.
            services.GetRequiredService<AudioMonitorMapper>();
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
