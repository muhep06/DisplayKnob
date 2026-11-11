using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Settings;
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

    [Fact]
    public async Task RefreshAsync_SpacingLearnedEarlier_StartsTheWorkerWithIt()
    {
        var monitor = new FakeMonitorController("A");
        var settings = new InMemorySettingsStore();
        settings.Update(s => s with { Monitors = new Dictionary<string, MonitorSettings> { [monitor.Identity.Key] = new() { CommandSpacingMs = 200 } } });
        var options = new DdcWorkerOptions(1, TimeSpan.Zero, CommandSpacing: TimeSpan.FromMilliseconds(80));
        await using var registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [monitor]), TimeProvider.System, options, Logger.None, settings);

        await registry.RefreshAsync(Ct);

        Assert.Equal(TimeSpan.FromMilliseconds(200), registry.Monitors[0].CommandSpacing);
    }

    [Fact]
    public async Task Worker_LearnsALongerSpacing_TheRegistrySavesItInTheSettings()
    {
        var time = new ManualTimeProvider();
        var monitor = new FakeMonitorController("A", time) { DropWritesWithin = TimeSpan.FromMilliseconds(150) };
        monitor.SetValue(VcpCodes.Brightness, 5, 50);
        var settings = new InMemorySettingsStore();
        var options = new DdcWorkerOptions(
            1,
            TimeSpan.Zero,
            CommandSpacing: TimeSpan.FromMilliseconds(80),
            VerifyDelay: TimeSpan.FromMilliseconds(150),
            MaxCommandSpacing: TimeSpan.FromMilliseconds(400),
            VerifiedCodes: new HashSet<byte> { VcpCodes.Brightness });
        await using var registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [monitor]), time, options, Logger.None, settings);
        await registry.RefreshAsync(Ct);
        var worker = registry.Monitors[0];

        worker.WriteVcp(VcpCodes.Brightness, 10);
        await time.RunUntilAsync(() => monitor.Writes.Count == 1);
        worker.WriteVcp(VcpCodes.Brightness, 20); // Follows too closely and is dropped.
        await time.RunUntilAsync(() => settings.Current.ForMonitor(monitor.Identity.Key).CommandSpacingMs is not null);

        Assert.Equal((int)worker.CommandSpacing.TotalMilliseconds, settings.Current.ForMonitor(monitor.Identity.Key).CommandSpacingMs);
        Assert.True(worker.CommandSpacing >= TimeSpan.FromMilliseconds(150));
    }

    private static MonitorRegistry CreateRegistry(FakeMonitorEnumerator enumerator) =>
        new(enumerator, TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
}
