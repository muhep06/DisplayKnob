using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Settings;
using DisplayKnob.Core.Tests.Fakes;
using DisplayKnob.Core.Volume;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Volume;

public sealed class VolumeGroupTests : IAsyncDisposable
{
    private static readonly AudioEndpoint s_firstMonitorAudio = new("{hdmi}", "S34C65xU (NVIDIA High Definition Audio)");
    private static readonly AudioEndpoint s_headphones = new("{phones}", "Headphones (Realtek(R) Audio)");

    // The audio plays through the first monitor, so it is the target; the second one is the other group member.
    private readonly FakeMonitorController _first = new("S34C65xU");
    private readonly FakeMonitorController _second = new("DELL U2720Q");
    private readonly FakeAudioEndpointWatcher _watcher = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly FakeTimeProvider _time = new();
    private readonly MonitorRegistry _registry;
    private readonly AudioMonitorMapper _mapper;
    private readonly VolumeController _controller;

    public VolumeGroupTests()
    {
        _first.SetValue(VcpCodes.AudioVolume, 30, 100);
        _first.SetValue(VcpCodes.AudioMute, 2);
        _second.SetValue(VcpCodes.AudioVolume, 100, 200); // 50% on a different raw scale.
        _second.SetValue(VcpCodes.AudioMute, 2);
        _settings.Update(s => s with { VolumePollSeconds = 0 });
        _registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [_first, _second]), TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
        _mapper = new AudioMonitorMapper(_watcher, _registry, _settings, Logger.None);
        _controller = new VolumeController(_mapper, _registry, _settings, _time, Logger.None);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        _mapper.Dispose();
        await _registry.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_GroupDisabled_WritesOnlyTheTarget()
    {
        SetGroup(enabled: false, firstIn: true, secondIn: true);
        await StartAsync();

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(32u, _first.GetCurrent(VcpCodes.AudioVolume));
        Assert.DoesNotContain(_second.Writes, w => w.Code == VcpCodes.AudioVolume);
    }

    [Fact]
    public async Task ProcessAsync_GroupEnabled_SetsMembersToTheTargetLevelOnTheirOwnScale()
    {
        SetGroup(enabled: true, firstIn: true, secondIn: true);
        await StartAsync();

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(32u, _first.GetCurrent(VcpCodes.AudioVolume)); // 32% of 100
        Assert.Equal(64u, _second.GetCurrent(VcpCodes.AudioVolume)); // 32% of 200
    }

    [Fact]
    public async Task ProcessAsync_TargetAtMaximum_MembersCatchUp()
    {
        _first.SetValue(VcpCodes.AudioVolume, 100, 100);
        SetGroup(enabled: true, firstIn: true, secondIn: true);
        await StartAsync();

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(100u, _first.GetCurrent(VcpCodes.AudioVolume));
        Assert.Equal(200u, _second.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task ProcessAsync_MonitorNotInGroup_IsNotWritten()
    {
        SetGroup(enabled: true, firstIn: true, secondIn: false);
        await StartAsync();

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(32u, _first.GetCurrent(VcpCodes.AudioVolume));
        Assert.DoesNotContain(_second.Writes, w => w.Code == VcpCodes.AudioVolume);
    }

    [Fact]
    public async Task ProcessAsync_TargetNotInGroup_ControlsTheTargetOnly()
    {
        SetGroup(enabled: true, firstIn: false, secondIn: true);
        await StartAsync();

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(32u, _first.GetCurrent(VcpCodes.AudioVolume));
        Assert.DoesNotContain(_second.Writes, w => w.Code == VcpCodes.AudioVolume);
    }

    [Fact]
    public async Task ProcessAsync_SetLevel_WritesOnlyTheTarget()
    {
        SetGroup(enabled: true, firstIn: true, secondIn: true);
        await StartAsync();

        await _controller.ProcessAsync(new VolumeCommand(VolumeAction.Set, Level: 70), Ct);
        await FlushAsync();

        Assert.Equal(70u, _first.GetCurrent(VcpCodes.AudioVolume));
        Assert.DoesNotContain(_second.Writes, w => w.Code == VcpCodes.AudioVolume);
    }

    [Fact]
    public async Task ProcessAsync_ToggleMute_MutesEveryMemberNativeOrEmulated()
    {
        _second.SetValue(VcpCodes.AudioMute, 2);
        var emulated = new FakeMonitorController("DELL U2720Q");
        emulated.SetValue(VcpCodes.AudioVolume, 100, 200); // No 0x8D: mute is emulated with volume 0.
        await using var registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [_first, emulated]), TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
        using var mapper = new AudioMonitorMapper(_watcher, registry, _settings, Logger.None);
        await using var controller = new VolumeController(mapper, registry, _settings, _time, Logger.None);
        _settings.Update(s => s with
        {
            VolumeGroupEnabled = true,
            Monitors = new Dictionary<string, MonitorSettings>
            {
                [_first.Identity.Key] = new() { InVolumeGroup = true },
                [emulated.Identity.Key] = new() { InVolumeGroup = true },
            },
        });
        await registry.RefreshAsync(Ct);
        await AfterRefreshAsync(controller, () => _watcher.SetDefault(s_firstMonitorAudio));

        await controller.ProcessAsync(new VolumeCommand(VolumeAction.ToggleMute), Ct);
        await registry.Monitors[0].ReadVcpAsync(VcpCodes.Brightness, Ct);
        await registry.Monitors[1].ReadVcpAsync(VcpCodes.Brightness, Ct);

        Assert.Equal(1u, _first.GetCurrent(VcpCodes.AudioMute));
        Assert.Equal(0u, emulated.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task ProcessAsync_VolumeKeyWhileMuted_UnmutesTheWholeGroup()
    {
        _first.SetValue(VcpCodes.AudioMute, 1);
        _second.SetValue(VcpCodes.AudioMute, 1);
        SetGroup(enabled: true, firstIn: true, secondIn: true);
        await StartAsync();

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(2u, _first.GetCurrent(VcpCodes.AudioMute));
        Assert.Equal(2u, _second.GetCurrent(VcpCodes.AudioMute));
    }

    [Fact]
    public async Task ProcessAsync_MemberDoesNotAnswer_StillStepsTheTarget()
    {
        SetGroup(enabled: true, firstIn: true, secondIn: true);
        await StartAsync();
        _second.FailNextCalls = 100;

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal(32u, _first.GetCurrent(VcpCodes.AudioVolume));
        Assert.DoesNotContain(_second.Writes, w => w.Code == VcpCodes.AudioVolume);
        Assert.True(_controller.IsTargetResponding);
    }

    [Fact]
    public async Task ProcessAsync_GroupEnabled_RaisesVolumeChangedForEveryMonitor()
    {
        SetGroup(enabled: true, firstIn: true, secondIn: true);
        await StartAsync();
        var keys = new List<string?>();
        _controller.VolumeChanged += (_, e) => keys.Add(e.MonitorKey);

        await ProcessAsync(VolumeAction.Up);

        Assert.Equal([_first.Identity.Key, _second.Identity.Key], keys);
    }

    [Fact]
    public async Task ProcessAsync_ToggleGroup_FlipsTheSettingAndRaisesTheEvent()
    {
        SetGroup(enabled: false, firstIn: true, secondIn: true);
        await StartAsync();
        VolumeGroupToggledEventArgs? raised = null;
        _controller.VolumeGroupToggled += (_, e) => raised = e;

        await _controller.ProcessAsync(new VolumeCommand(VolumeAction.ToggleGroup), Ct);

        Assert.True(_settings.Current.VolumeGroupEnabled);
        Assert.NotNull(raised);
        Assert.True(raised.Enabled);
        Assert.Equal(2, raised.MemberCount);
    }

    [Fact]
    public async Task TryPost_ToggleGroupWithTwoMembers_ReturnsTrueEvenWithHeadphones()
    {
        SetGroup(enabled: false, firstIn: true, secondIn: true);
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_headphones);

        Assert.True(_controller.TryPost(new VolumeCommand(VolumeAction.ToggleGroup)));
    }

    [Fact]
    public async Task TryPost_ToggleGroupWithFewerThanTwoMembers_ReturnsFalse()
    {
        SetGroup(enabled: false, firstIn: true, secondIn: false);
        await _registry.RefreshAsync(Ct);

        Assert.False(_controller.TryPost(new VolumeCommand(VolumeAction.ToggleGroup)));
    }

    private void SetGroup(bool enabled, bool firstIn, bool secondIn) =>
        _settings.Update(s => s with
        {
            VolumeGroupEnabled = enabled,
            Monitors = new Dictionary<string, MonitorSettings>
            {
                [_first.Identity.Key] = new() { InVolumeGroup = firstIn },
                [_second.Identity.Key] = new() { InVolumeGroup = secondIn },
            },
        });

    // A new target triggers a background re-read; wait for it so it doesn't race the test's own commands.
    private async Task StartAsync()
    {
        await _registry.RefreshAsync(Ct);
        await AfterRefreshAsync(_controller, () => _watcher.SetDefault(s_firstMonitorAudio));
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

    private async Task ProcessAsync(VolumeAction action)
    {
        await _controller.ProcessAsync(new VolumeCommand(action), Ct);
        await FlushAsync();
    }

    // Writes are queued on each monitor's worker; a read completes only after earlier writes ran.
    private async Task FlushAsync()
    {
        foreach (var monitor in _registry.Monitors)
        {
            await monitor.ReadVcpAsync(VcpCodes.Brightness, Ct);
        }
    }
}
