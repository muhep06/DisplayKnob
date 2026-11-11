using DisplayKnob.App.Osd;
using DisplayKnob.App.Tray;
using DisplayKnob.App.Updates;
using DisplayKnob.App.ViewModels;
using DisplayKnob.App.Views;
using DisplayKnob.Core;
using DisplayKnob.Core.Ddc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace DisplayKnob.App;

/// <summary>
/// The only place where services are wired together.
/// </summary>
internal static class Composition
{
    public static ServiceProvider Build(Serilog.ILogger logger)
    {
        var services = new ServiceCollection();

        services.AddSingleton(logger);
        services.AddDisplayKnobCore();
        OverrideDdcSpacingIfRequested(services);
        AddFakeMonitorsIfRequested(services);
        services.AddSingleton<UpdateService>();
        services.AddSingleton<TrayIconHost>();
        services.AddSingleton<StartupOffer>();

        // Launching the app again opens the tray flyout. The provider disposes the listener on shutdown.
        services.AddSingleton(sp => new LaunchedAgainListener(
            DispatcherQueue.GetForCurrentThread(),
            () => sp.GetRequiredService<TrayFlyoutPresenter>().Toggle(sp.GetRequiredService<TrayIconHost>().IconId)));
        services.AddSingleton<TrayWheelHandler>();
        services.AddSingleton<TrayFlyoutViewModel>();
        services.AddSingleton<TrayFlyoutPresenter>();
        services.AddSingleton<SettingsWindowHost>();
        services.AddSingleton<OsdPresenter>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    // Debug builds only: DISPLAYKNOB_FAKE_MONITORS=<n> (1–4) adds n simulated DDC monitors next to the real ones, so the
    // volume group can be tried on a machine with one monitor.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void AddFakeMonitorsIfRequested(IServiceCollection services)
    {
#if DEBUG
        if (int.TryParse(Environment.GetEnvironmentVariable("DISPLAYKNOB_FAKE_MONITORS"), System.Globalization.CultureInfo.InvariantCulture, out var count) && count > 0)
        {
            Diagnostics.FakeMonitors.Register(services, Math.Min(count, 4));
        }
#endif
    }

    // Debug builds only: DISPLAYKNOB_DDC_SPACING_MS=<ms> starts every monitor with that pause between DDC commands,
    // so "a too short pause is noticed and widened" can be tried on real hardware.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void OverrideDdcSpacingIfRequested(IServiceCollection services)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("DISPLAYKNOB_DDC_SPACING_MS"), System.Globalization.CultureInfo.InvariantCulture, out var ms) && ms >= 0)
        {
            services.AddSingleton(DdcWorkerOptions.Default with { CommandSpacing = TimeSpan.FromMilliseconds(ms) });
        }
    }
}
