using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Settings;
using DisplayKnob.Core.Tests.Fakes;
using DisplayKnob.Core.Volume;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Volume;

public sealed class VolumeControllerTests : IAsyncDisposable
{
    private static readonly AudioEndpoint s_monitorAudio = new("{hdmi}", "S34C65xU (NVIDIA High Definition Audio)");
    private static readonly AudioEndpoint s_headphones = new("{phones}", "Headphones (Realtek(R) Audio)");

    private readonly FakeMonitorController _monitor = new("S34C65xU");
    private readonly FakeAudioEndpointWatcher _watcher = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly FakeTimeProvider _time = new();
    private readonly MonitorRegistry _registry;
    private readonly AudioMonitorMapper _mapper;
    private readonly VolumeController _controller;

    public VolumeControllerTests()
    {
        _monitor.SetValue(VcpCodes.AudioVolume, 30, 100);
        _monitor.SetValue(VcpCodes.AudioMute, 2);
        _registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [_monitor]), TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
        _mapper = new AudioMonitorMapper(_watcher, _registry, _settings, Logger.None);
        _controller = new VolumeController(_mapper, _settings, _time, Logger.None);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        _mapper.Dispose();
        await _registry.DisposeAsync();
    }

    [Fact]
    public async Task TryPost_NoTarget_ReturnsFalse()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_headphones);

        Assert.False(_controller.TryPost(new VolumeCommand(VolumeAction.Up)));
    }

    [Fact]
    public async Task TryPost_MonitorTarget_ReturnsTrue()
    {
        await StartAsync();

        Assert.True(_controller.TryPost(new VolumeCommand(VolumeAction.Up)));
    }

    [Fact]
    public async Task Up_FirstPress_ReadsCurrentValueThenSteps()
    {
        await StartAsync();

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(32u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Down_Fine_StepsByOne()
    {
        await StartAsync();

        await ProcessAsync(VolumeAction.Down, fine: true);

        Assert.Equal(29u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task StepSize_FromSettings()
    {
        await StartAsync();
        _settings.Update(s => s with { VolumeStep = 5 });

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(35u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Down_AtZero_ClampsWithoutWriting()
    {
        _monitor.SetValue(VcpCodes.AudioVolume, 0, 100);
        await StartAsync();

        await ProcessAsync(VolumeAction.Down);

        Assert.DoesNotContain(_monitor.Writes, w => w.Code == VcpCodes.AudioVolume);
    }

    [Fact]
    public async Task Range_MapsUserScaleOntoUsableRawRange()
    {
        // Raw 0..50 is the usable range; raw 30 is 60% of it.
        await StartAsync();
        await AfterRefreshAsync(_controller, () => SetMonitorSettings(new MonitorSettings { VolumeMax = 50 }));
        var levels = new List<int>();
        _controller.VolumeChanged += (_, e) => levels.Add(e.Level);

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal([62], levels);
        Assert.Equal(31u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Steps_WithinIdleWindow_DoNotReread()
    {
        await StartAsync();
        await ProcessAsync(VolumeAction.Up); // 30 -> 32
        _monitor.SetValue(VcpCodes.AudioVolume, 80, 100); // Changed elsewhere (e.g. the Mac).
        _time.Advance(TimeSpan.FromSeconds(5));

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(34u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Steps_AfterIdle_RereadExternalChange()
    {
        await StartAsync();
        await ProcessAsync(VolumeAction.Up); // 30 -> 32
        _monitor.SetValue(VcpCodes.AudioVolume, 80, 100);
        _time.Advance(VolumeController.ResyncAfterIdle + TimeSpan.FromSeconds(1));

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(82u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Mute_Native_TogglesVcp8D()
    {
        await StartAsync();

        await ProcessAsync(VolumeAction.ToggleMute);
        Assert.Equal(1u, _monitor.GetCurrent(VcpCodes.AudioMute));

        await ProcessAsync(VolumeAction.ToggleMute);
        Assert.Equal(2u, _monitor.GetCurrent(VcpCodes.AudioMute));
        Assert.Equal(30u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Mute_MonitorReportsZeroForUnmuted_IsUnderstood()
    {
        // Samsung S34C65xU: unmuted reads back as 0, not 2.
        _monitor.SetValue(VcpCodes.AudioMute, 0);
        _monitor.ReportedValueAfterWrite = (code, value) => code == VcpCodes.AudioMute && value == 2 ? 0 : value;
        await StartAsync();
        var muted = new List<bool>();
        _controller.VolumeChanged += (_, e) => muted.Add(e.Muted);

        await ProcessAsync(VolumeAction.ToggleMute);
        await ProcessAsync(VolumeAction.ToggleMute);

        Assert.Equal([true, false], muted);
        Assert.Equal(0u, _monitor.GetCurrent(VcpCodes.AudioMute));
    }

    [Fact]
    public async Task Mute_Unsupported_EmulatesWithVolumeZeroAndRestores()
    {
        var monitor = new FakeMonitorController("S34C65xU");
        monitor.SetValue(VcpCodes.AudioVolume, 30, 100); // No 0x8D at all.
        await using var registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [monitor]), TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
        using var mapper = new AudioMonitorMapper(_watcher, registry, _settings, Logger.None);
        await using var controller = new VolumeController(mapper, _settings, _time, Logger.None);
        await registry.RefreshAsync(Ct);
        await AfterRefreshAsync(controller, () => _watcher.SetDefault(s_monitorAudio));

        await controller.ProcessAsync(new VolumeCommand(VolumeAction.ToggleMute), Ct);
        await FlushAsync(registry);
        Assert.Equal(0u, monitor.GetCurrent(VcpCodes.AudioVolume));

        await controller.ProcessAsync(new VolumeCommand(VolumeAction.ToggleMute), Ct);
        await FlushAsync(registry);
        Assert.Equal(30u, monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Mute_MonitorIgnoresVcp8DWrites_FallsBackToEmulation()
    {
        _monitor.IgnoredWriteCodes.Add(VcpCodes.AudioMute);
        await StartAsync();

        await ProcessAsync(VolumeAction.ToggleMute);

        Assert.Equal(0u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Up_WhileMuted_UnmutesAndSteps()
    {
        await StartAsync();
        await ProcessAsync(VolumeAction.ToggleMute);

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(2u, _monitor.GetCurrent(VcpCodes.AudioMute));
        Assert.Equal(32u, _monitor.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task Up_VolumeUnreadable_IgnoresKey()
    {
        _monitor.FailNextCalls = int.MaxValue;
        await StartAsync();

        await _controller.ProcessAsync(new VolumeCommand(VolumeAction.Up), Ct);

        Assert.Empty(_monitor.Writes);
    }

    [Fact]
    public async Task TryPost_ProcessesThroughLoop()
    {
        await StartAsync();
        var changed = new TaskCompletionSource<VolumeChangedEventArgs>();
        _controller.VolumeChanged += (_, e) => changed.TrySetResult(e);

        _controller.TryPost(new VolumeCommand(VolumeAction.Up));
        var args = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(32, args.Level);
        Assert.Equal("S34C65xU", args.MonitorName);
    }

    [Fact]
    public async Task NewTarget_RereadsVolumeQuietly()
    {
        var changes = new List<VolumeChangedEventArgs>();
        _controller.VolumeChanged += (_, e) => changes.Add(e);

        await StartAsync();

        var change = Assert.Single(changes);
        Assert.Equal(30, change.Level);
        Assert.False(change.UserInitiated);
    }

    [Fact]
    public async Task KeyPress_IsUserInitiated()
    {
        await StartAsync();
        var changes = new List<VolumeChangedEventArgs>();
        _controller.VolumeChanged += (_, e) => changes.Add(e);

        await ProcessAsync(VolumeAction.Up);

        Assert.True(Assert.Single(changes).UserInitiated);
    }

    [Fact]
    public async Task TargetNotAnswering_KeysPassThrough()
    {
        _monitor.FailNextCalls = int.MaxValue;

        await StartAsync();

        Assert.False(_controller.IsTargetResponding);
        Assert.False(_controller.TryPost(new VolumeCommand(VolumeAction.Up)));
    }

    [Fact]
    public async Task TargetAnswersAgain_ProbeResumesControl()
    {
        _monitor.FailNextCalls = int.MaxValue;
        await StartAsync();
        _monitor.FailNextCalls = 0;

        await AfterRefreshAsync(_controller, () => _time.Advance(VolumeController.FirstProbeDelay));

        Assert.True(_controller.IsTargetResponding);
        Assert.True(_controller.TryPost(new VolumeCommand(VolumeAction.Up)));
    }

    [Fact]
    public async Task VolumeWriteFails_KeysPassThrough()
    {
        await StartAsync();
        _monitor.FailNextCalls = int.MaxValue;

        await ProcessAsync(VolumeAction.Up);

        Assert.False(_controller.IsTargetResponding);
        Assert.False(_controller.TryPost(new VolumeCommand(VolumeAction.Up)));
    }

    [Fact]
    public async Task KeyWhileNotAnswering_TriggersRecheck()
    {
        _monitor.FailNextCalls = int.MaxValue;
        await StartAsync();
        _monitor.FailNextCalls = 0;

        await AfterRefreshAsync(_controller, () => _controller.TryPost(new VolumeCommand(VolumeAction.Up)));

        Assert.True(_controller.IsTargetResponding);
    }

    // A new target triggers a background re-read; wait for it so it doesn't race the test's own commands.
    private async Task StartAsync()
    {
        await _registry.RefreshAsync(Ct);
        await AfterRefreshAsync(_controller, () => _watcher.SetDefault(s_monitorAudio));
    }

    private static async Task AfterRefreshAsync(VolumeController controller, Action trigger)
    {
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<VolumeCommand> handler = (_, c) =>
        {
            if (c.Action == VolumeAction.Refresh)
            {
                refreshed.TrySetResult();
            }
        };
        controller.CommandProcessed += handler;
        try
        {
            trigger();
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }
        finally
        {
            controller.CommandProcessed -= handler;
        }
    }

    private async Task ProcessAsync(VolumeAction action, bool fine = false)
    {
        await _controller.ProcessAsync(new VolumeCommand(action, fine), Ct);
        await FlushAsync(_registry);
    }

    // Writes are queued on the monitor's worker; a read completes only after earlier writes ran.
    private static async Task FlushAsync(MonitorRegistry registry) => await registry.Monitors[0].ReadVcpAsync(VcpCodes.Brightness, Ct);

    private void SetMonitorSettings(MonitorSettings settings) =>
        _settings.Update(s => s with { Monitors = new Dictionary<string, MonitorSettings> { [_monitor.Identity.Key] = settings } });
}
