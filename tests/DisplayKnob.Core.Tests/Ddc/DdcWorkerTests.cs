using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Tests.Fakes;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Ddc;

public sealed class DdcWorkerTests
{
    // Zero delay keeps retries instant and deterministic.
    private static readonly DdcWorkerOptions s_noDelay = new(MaxAttempts: 3, RetryDelay: TimeSpan.Zero);

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadVcpAsync_MonitorAnswers_ReturnsValue()
    {
        var monitor = new FakeMonitorController();
        monitor.SetValue(VcpCodes.AudioVolume, 30, 100);
        await using var worker = CreateWorker(monitor);

        var value = await worker.ReadVcpAsync(VcpCodes.AudioVolume, Ct).WaitAsync(s_timeout, Ct);

        Assert.Equal(new VcpValue(30, 100), value);
    }

    [Fact]
    public async Task ReadVcpAsync_FailsTwiceThenSucceeds_Retries()
    {
        var monitor = new FakeMonitorController { FailNextCalls = 2 };
        monitor.SetValue(VcpCodes.AudioVolume, 30);
        await using var worker = CreateWorker(monitor);

        var value = await worker.ReadVcpAsync(VcpCodes.AudioVolume, Ct).WaitAsync(s_timeout, Ct);

        Assert.Equal(30u, value?.Current);
        Assert.Equal(3, monitor.CallCount);
    }

    [Fact]
    public async Task ReadVcpAsync_AlwaysFails_ReturnsNullAfterMaxAttempts()
    {
        var monitor = new FakeMonitorController { FailNextCalls = int.MaxValue };
        await using var worker = CreateWorker(monitor);

        var value = await worker.ReadVcpAsync(VcpCodes.AudioVolume, Ct).WaitAsync(s_timeout, Ct);

        Assert.Null(value);
        Assert.Equal(3, monitor.CallCount);
    }

    [Fact]
    public async Task WriteVcp_RapidRepeats_OnlyLatestValueIsWritten()
    {
        using var gate = new ManualResetEventSlim(false);
        var monitor = new FakeMonitorController { Gate = gate };
        await using var worker = CreateWorker(monitor);

        // Hold the worker busy on an unrelated write while key repeats pile up.
        worker.WriteVcp(VcpCodes.Brightness, 50);
        Assert.True(monitor.CallStarted.Wait(s_timeout, Ct));
        worker.WriteVcp(VcpCodes.AudioVolume, 10);
        worker.WriteVcp(VcpCodes.AudioVolume, 12);
        worker.WriteVcp(VcpCodes.AudioVolume, 14);
        gate.Set();
        await worker.ReadVcpAsync(VcpCodes.AudioVolume, Ct).WaitAsync(s_timeout, Ct);

        var volumeWrites = monitor.Writes.Where(w => w.Code == VcpCodes.AudioVolume).Select(w => w.Value).ToList();
        Assert.Equal([14u], volumeWrites);
    }

    [Fact]
    public async Task ReadVcpAsync_AfterWrite_SeesWrittenValue()
    {
        var monitor = new FakeMonitorController();
        monitor.SetValue(VcpCodes.AudioVolume, 30);
        await using var worker = CreateWorker(monitor);

        worker.WriteVcp(VcpCodes.AudioVolume, 44);
        var value = await worker.ReadVcpAsync(VcpCodes.AudioVolume, Ct).WaitAsync(s_timeout, Ct);

        Assert.Equal(44u, value?.Current);
    }

    [Fact]
    public async Task ReadCapabilitiesAsync_ReturnsString()
    {
        var monitor = new FakeMonitorController { Capabilities = "(vcp(10 62))" };
        await using var worker = CreateWorker(monitor);

        var caps = await worker.ReadCapabilitiesAsync(Ct).WaitAsync(s_timeout, Ct);

        Assert.Equal("(vcp(10 62))", caps);
    }

    [Fact]
    public async Task DisposeAsync_DisposesMonitor()
    {
        var monitor = new FakeMonitorController();
        var worker = CreateWorker(monitor);

        await worker.DisposeAsync();

        Assert.True(monitor.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_PendingRead_IsCancelled()
    {
        using var gate = new ManualResetEventSlim(false);
        var monitor = new FakeMonitorController { Gate = gate };
        var worker = CreateWorker(monitor);

        worker.WriteVcp(VcpCodes.Brightness, 1);
        Assert.True(monitor.CallStarted.Wait(s_timeout, Ct)); // Loop is blocked on the gate.
        var read = worker.ReadVcpAsync(VcpCodes.AudioVolume, Ct);
        var dispose = worker.DisposeAsync().AsTask();
        gate.Set();
        await dispose.WaitAsync(s_timeout, Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(s_timeout, Ct));
    }

    private static DdcWorker CreateWorker(FakeMonitorController monitor) =>
        new(monitor, TimeProvider.System, s_noDelay, Logger.None);
}
