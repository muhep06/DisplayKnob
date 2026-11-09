using Microsoft.Extensions.DependencyInjection;

namespace DisplayKnob.Core;

/// <summary>
/// Registers DisplayKnob.Core services with a DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Core services. The host must register a Serilog <c>ILogger</c> separately.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDisplayKnobCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
