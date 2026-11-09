using DisplayHub.App.Tray;
using DisplayHub.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DisplayHub.App;

/// <summary>
/// The only place where services are wired together.
/// </summary>
internal static class Composition
{
    public static ServiceProvider Build(Serilog.ILogger logger)
    {
        var services = new ServiceCollection();

        services.AddSingleton(logger);
        services.AddDisplayHubCore();
        services.AddSingleton<TrayIconHost>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
