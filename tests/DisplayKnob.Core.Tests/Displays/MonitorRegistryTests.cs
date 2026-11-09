using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Tests.Fakes;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Displays;

public sealed class MonitorRegistryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RefreshAsync_PopulatesMonitors()
    {
        var enumerator = new FakeMonitorEnumerator(() => [new FakeMonitorController("A"), new FakeMonitorController("B")]);
        await using var registry = CreateRegistry(enumerator);

        await registry.RefreshAsync(Ct);

        Assert.Equal(["A", "B"], registry.Monitors.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task RefreshAsync_Again_DisposesPreviousHandles()
    {
        var enumerator = new FakeMonitorEnumerator(() => [new FakeMonitorController("A")]);
        await using var registry = CreateRegistry(enumerator);
        await registry.RefreshAsync(Ct);
        var first = enumerator.Created[0];

        await registry.RefreshAsync(Ct);

        Assert.True(first.IsDisposed);
        Assert.False(enumerator.Created[1].IsDisposed);
        Assert.Single(registry.Monitors);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllHandles()
    {
        var enumerator = new FakeMonitorEnumerator(() => [new FakeMonitorController("A"), new FakeMonitorController("B")]);
        var registry = CreateRegistry(enumerator);
        await registry.RefreshAsync(Ct);

        await registry.DisposeAsync();

        Assert.All(enumerator.Created, m => Assert.True(m.IsDisposed));
        Assert.Empty(registry.Monitors);
    }

    [Fact]
    public async Task MonitorProbe_RunAsync_ReadsEveryMonitor()
    {
        var a = new FakeMonitorController("A");
        a.SetValue(VcpCodes.AudioVolume, 20);
        var enumerator = new FakeMonitorEnumerator(() => [a]);
        await using var registry = CreateRegistry(enumerator);
        var probe = new MonitorProbe(registry, Logger.None);

        await probe.RunAsync(Ct);

        // Capabilities + brightness + volume + mute.
        Assert.True(a.CallCount >= 4);
    }

    private static MonitorRegistry CreateRegistry(FakeMonitorEnumerator enumerator) =>
        new(enumerator, TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
}
