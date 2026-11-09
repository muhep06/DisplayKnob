using DisplayKnob.App.Tray;
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
