using DisplayKnob.App.Osd;
using DisplayKnob.App.Tray;
using DisplayKnob.Core;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<TrayIconHost>();
        services.AddSingleton<OsdPresenter>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
