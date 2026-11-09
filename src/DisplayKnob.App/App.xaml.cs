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

        // UI-thread objects must be created here, not lazily from a background thread.
        _services.GetRequiredService<OsdPresenter>();

        _ = StartCoreAsync(_services);
    }

    private static async Task StartCoreAsync(IServiceProvider services)
    {
        try
        {
            // The mapper subscribes to audio/monitor changes on construction, so create it before the first refresh.
            services.GetRequiredService<AudioMonitorMapper>();
            await services.GetRequiredService<MonitorRegistry>().RefreshAsync();

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
