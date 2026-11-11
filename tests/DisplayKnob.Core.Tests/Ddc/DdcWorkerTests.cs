using System.Collections.Concurrent;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
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
    public async Task WriteVcp_Consecutive_WaitsForTheCommandSpacing()
    {
        // A monitor drops a command that follows the previous one too closely, so the worker must keep its distance.
        var spacing = TimeSpan.FromMilliseconds(100);
        var time = new FakeTimeProvider();
        var monitor = new FakeMonitorController(clock: time);
        await using var worker = new DdcWorker(monitor, time, new DdcWorkerOptions(3, TimeSpan.Zero, spacing), Logger.None);

        worker.WriteVcp(VcpCodes.Brightness, 10);
        await WaitUntilAsync(() => monitor.Writes.Count == 1);
        worker.WriteVcp(VcpCodes.Brightness, 20);
        time.Advance(spacing);
        await WaitUntilAsync(() => monitor.Writes.Count == 2);

        var times = monitor.WriteTimes;
        Assert.True(times[1] - times[0] >= spacing, $"The second write came {times[1] - times[0]} after the first.");
    }

    [Fact]
    public async Task ReadVcpAsync_AfterWrite_WaitsForTheCommandSpacing()
    {
        var spacing = TimeSpan.FromMilliseconds(100);
        var time = new FakeTimeProvider();
        var monitor = new FakeMonitorController(clock: time);
        monitor.SetValue(VcpCodes.Brightness, 10);
        await using var worker = new DdcWorker(monitor, time, new DdcWorkerOptions(3, TimeSpan.Zero, spacing), Logger.None);

        worker.WriteVcp(VcpCodes.Brightness, 20);
        await WaitUntilAsync(() => monitor.Writes.Count == 1);
        var read = worker.ReadVcpAsync(VcpCodes.Brightness, Ct);
        time.Advance(spacing); // Reads are commands too: the monitor needs the pause before them as well.
        var value = await read.WaitAsync(s_timeout, Ct);

        Assert.Equal(20u, value?.Current);
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

    // --- Writes that a monitor drops, and the spacing learned from them ---

    private static readonly TimeSpan s_spacing = TimeSpan.FromMilliseconds(80);

    private static DdcWorker CreateVerifyingWorker(FakeMonitorController monitor, ManualTimeProvider time, TimeSpan? initialSpacing = null) =>
        new(
            monitor,
            time,
            new DdcWorkerOptions(
                MaxAttempts: 3,
                RetryDelay: TimeSpan.Zero,
                CommandSpacing: s_spacing,
                VerifyDelay: TimeSpan.FromMilliseconds(150),
                MaxCommandSpacing: TimeSpan.FromMilliseconds(400),
                VerifiedCodes: new HashSet<byte> { VcpCodes.Brightness }),
            Logger.None,
            initialSpacing);

    [Fact]
    public async Task WriteDroppedByTheMonitor_IsSentAgainAndTheSpacingGrows()
    {
        // The monitor needs 150 ms between commands, the worker starts with 80: the second write is lost.
        var time = new ManualTimeProvider();
        var monitor = new FakeMonitorController(clock: time) { DropWritesWithin = TimeSpan.FromMilliseconds(150) };
        monitor.SetValue(VcpCodes.Brightness, 5, 50);
        var learned = new ConcurrentQueue<TimeSpan>();
        using var gate = new ManualResetEventSlim(false);
        monitor.Gate = gate;
        await using var worker = CreateVerifyingWorker(monitor, time);
        worker.CommandSpacingChanged += (_, spacing) => learned.Enqueue(spacing);

        // The first write is held inside the monitor while the second is queued behind it. Otherwise the worker's
        // "writes have stopped" timer could already be pending, and the test would move the clock past the second
        // write's spacing before the worker looked at it, so the monitor wouldn't drop it.
        worker.WriteVcp(VcpCodes.Brightness, 10);
        Assert.True(monitor.CallStarted.Wait(s_timeout, Ct));
        worker.WriteVcp(VcpCodes.Brightness, 20);
        gate.Set();
        await time.RunUntilAsync(() => !learned.IsEmpty);

        Assert.Equal(20u, monitor.GetCurrent(VcpCodes.Brightness));
        Assert.True(worker.CommandSpacing >= TimeSpan.FromMilliseconds(150));
        Assert.Equal([worker.CommandSpacing], learned);
    }

    [Fact]
    public async Task MonitorThatKeepsUp_IsNotWrittenToTwiceOrSlowedDown()
    {
        var time = new ManualTimeProvider();
        var monitor = new FakeMonitorController(clock: time);
        monitor.SetValue(VcpCodes.Brightness, 5, 50);
        var learned = new ConcurrentQueue<TimeSpan>();
        await using var worker = CreateVerifyingWorker(monitor, time);
        worker.CommandSpacingChanged += (_, spacing) => learned.Enqueue(spacing);

        worker.WriteVcp(VcpCodes.Brightness, 10);
        await time.RunUntilAsync(() => monitor.Writes.Count == 1);
        worker.WriteVcp(VcpCodes.Brightness, 20);
        await time.RunUntilAsync(() => monitor.CallCount >= 3); // Two writes and the check that reads the value back.
        await time.SettleAsync();

        Assert.Equal(2, monitor.Writes.Count);
        Assert.Equal(20u, monitor.GetCurrent(VcpCodes.Brightness));
        Assert.Equal(s_spacing, worker.CommandSpacing);
        Assert.Empty(learned);
    }

    [Fact]
    public async Task MonitorThatChangesTheValue_StopsBeingChecked()
    {
        // The monitor stores what it was given plus one (rounding, clamping): that says nothing about timing.
        var time = new ManualTimeProvider();
        var monitor = new FakeMonitorController(clock: time) { ReportedValueAfterWrite = (_, value) => value + 1 };
        monitor.SetValue(VcpCodes.Brightness, 5, 50);
        await using var worker = CreateVerifyingWorker(monitor, time);
        await worker.ReadVcpAsync(VcpCodes.Brightness, Ct).WaitAsync(s_timeout, Ct); // The worker now knows the value before its writes.

        worker.WriteVcp(VcpCodes.Brightness, 10);
        await time.RunUntilAsync(() => monitor.CallCount >= 3); // The read, the write and its check: 11 is neither 10 nor 5.
        await time.SettleAsync();
        worker.WriteVcp(VcpCodes.Brightness, 20);
        await time.RunUntilAsync(() => monitor.CallCount >= 5); // The second odd answer: from now on the code isn't checked.
        await time.SettleAsync();
        worker.WriteVcp(VcpCodes.Brightness, 30);
        await time.RunUntilAsync(() => monitor.Writes.Count == 3);
        await time.SettleAsync();

        Assert.Equal(6, monitor.CallCount); // One read, three writes and two checks; the last write isn't checked.
        Assert.Equal(3, monitor.Writes.Count); // Nothing was sent again.
        Assert.Equal(s_spacing, worker.CommandSpacing);
    }

    [Fact]
    public async Task MonitorThatNeverAppliesTheWrite_IsGivenUpOnAndTheSpacingIsKept()
    {
        // No pause helps, so the pause isn't the problem: the spacing goes back to where it was and nothing is learned.
        var time = new ManualTimeProvider();
        var monitor = new FakeMonitorController(clock: time);
        monitor.IgnoredWriteCodes.Add(VcpCodes.Brightness);
        monitor.SetValue(VcpCodes.Brightness, 5, 50);
        var learned = new ConcurrentQueue<TimeSpan>();
        await using var worker = CreateVerifyingWorker(monitor, time);
        worker.CommandSpacingChanged += (_, spacing) => learned.Enqueue(spacing);

        worker.WriteVcp(VcpCodes.Brightness, 10);
        await time.RunUntilAsync(() => monitor.Writes.Count == 4); // The write and three sends again.
        await time.SettleAsync();

        Assert.Equal(4, monitor.Writes.Count);
        Assert.Equal(s_spacing, worker.CommandSpacing);
        Assert.Empty(learned);
    }

    [Fact]
    public async Task InitialSpacing_LongerThanTheDefault_IsUsed()
    {
        var time = new ManualTimeProvider();
        var monitor = new FakeMonitorController(clock: time);
        await using var worker = CreateVerifyingWorker(monitor, time, initialSpacing: TimeSpan.FromMilliseconds(200));

        worker.WriteVcp(VcpCodes.AudioVolume, 10);
        await time.RunUntilAsync(() => monitor.Writes.Count == 1);
        worker.WriteVcp(VcpCodes.AudioVolume, 20);
        await time.RunUntilAsync(() => monitor.Writes.Count == 2);

        var times = monitor.WriteTimes;
        Assert.Equal(TimeSpan.FromMilliseconds(200), times[1] - times[0]);
    }

    [Fact]
    public async Task InitialSpacing_ShorterThanTheDefault_IsIgnored()
    {
        var time = new ManualTimeProvider();
        await using var worker = CreateVerifyingWorker(new FakeMonitorController(clock: time), time, initialSpacing: TimeSpan.FromMilliseconds(20));

        Assert.Equal(s_spacing, worker.CommandSpacing);
    }

    // The worker runs on its own loop; poll for what the test is waiting on instead of sleeping a fixed time.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + (long)s_timeout.TotalMilliseconds;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "Timed out waiting for the worker.");
            await Task.Yield();
        }
    }

    private static DdcWorker CreateWorker(FakeMonitorController monitor) =>
        new(monitor, TimeProvider.System, s_noDelay, Logger.None);
}
