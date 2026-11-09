using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Input;
using DisplayKnob.Core.Platform;
using DisplayKnob.Core.Settings;
using DisplayKnob.Core.Volume;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DisplayKnob.Core;

/// <summary>
/// Registers DisplayKnob.Core services with a DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Core services. The host must register a Serilog <c>ILogger</c> separately.
    /// </summary>
    /// <remarks>
    /// Some services are only <see cref="IAsyncDisposable"/>; dispose the provider with <c>DisposeAsync</c>.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDisplayKnobCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(DdcWorkerOptions.Default);
        services.AddSingleton<SystemEventWindow>();
        services.AddSingleton<ISystemEventSource>(sp => sp.GetRequiredService<SystemEventWindow>());
        services.AddSingleton<IMouseWheelSource>(sp => sp.GetRequiredService<SystemEventWindow>());
        services.AddSingleton<IMonitorEnumerator, DxvaMonitorEnumerator>();
        services.AddSingleton<MonitorRegistry>();
        services.AddSingleton<DisplayChangeWatcher>();
        services.AddSingleton<MonitorProbe>();
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(JsonSettingsStore.DefaultPath, sp.GetRequiredService<ILogger>()));
        services.AddSingleton<IAudioEndpointWatcher, NAudioEndpointWatcher>();
        services.AddSingleton<AudioMonitorMapper>();
        services.AddSingleton<VolumeController>();
        services.AddSingleton<IVolumeCommandSink>(sp => sp.GetRequiredService<VolumeController>());
        services.AddSingleton<EndpointVolumeMirror>();
        services.AddSingleton<MonitorLevelService>();
        services.AddSingleton<IStartupRegistration, RunKeyStartupRegistration>();
        services.AddSingleton<KeyboardHookService>();
        services.AddSingleton<IVolumeSource, HookVolumeSource>();

        return services;
    }
}
