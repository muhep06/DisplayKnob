using DisplayHub.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DisplayHub.Core.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDisplayHubCore_RegistersSystemTimeProvider()
    {
        var services = new ServiceCollection();

        services.AddDisplayHubCore();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDisplayHubCore_NullServices_Throws()
    {
        IServiceCollection services = null!; // Deliberately null to test argument validation.

        Assert.Throws<ArgumentNullException>(() => services.AddDisplayHubCore());
    }
}
