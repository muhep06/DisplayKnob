using DisplayKnob.App.Osd;
using DisplayKnob.App.Tray;
using DisplayKnob.App.Updates;
using DisplayKnob.App.ViewModels;
using DisplayKnob.App.Views;
using DisplayKnob.Core;
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
}
