using DisplayKnob.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DisplayKnob.Core.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDisplayKnobCore_RegistersSystemTimeProvider()
    {
        var services = new ServiceCollection();

        services.AddDisplayKnobCore();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDisplayKnobCore_NullServices_Throws()
    {
        IServiceCollection services = null!; // Deliberately null to test argument validation.

        Assert.Throws<ArgumentNullException>(() => services.AddDisplayKnobCore());
    }
}
