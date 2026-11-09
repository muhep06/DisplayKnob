using DisplayHub.App.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;

namespace DisplayHub.App;

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
        Log.Information("DisplayHub {Version} starting", typeof(App).Assembly.GetName().Version);

        _services = Composition.Build(Log.Logger);

        _tray = _services.GetRequiredService<TrayIconHost>();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.Show();
    }

    private void Shutdown()
    {
        Log.Information("DisplayHub exiting");

        _tray?.Dispose();
        _services?.Dispose();
        Log.CloseAndFlush();

        Exit();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception");
        Log.CloseAndFlush();
    }
}
