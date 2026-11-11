using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Settings;
using DisplayKnob.Core.Tests.Fakes;
using DisplayKnob.Core.Volume;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Volume;

public sealed class MonitorLevelServiceTests : IAsyncDisposable
{
    private static readonly AudioEndpoint s_monitorAudio = new("{hdmi}", "S34C65xU (NVIDIA High Definition Audio)");

    private readonly FakeMonitorController _target = new("S34C65xU");
    private readonly FakeMonitorController _other = new("DELL U2720Q");
    private readonly FakeAudioEndpointWatcher _watcher = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly MonitorRegistry _registry;
    private readonly AudioMonitorMapper _mapper;
    private readonly VolumeController _controller;
    private readonly MonitorLevelService _service;

    public MonitorLevelServiceTests()
    {
        _target.SetValue(VcpCodes.Brightness, 15, 50);
        _target.SetValue(VcpCodes.AudioVolume, 30, 100);
        _target.SetValue(VcpCodes.AudioMute, 1);
        _other.SetValue(VcpCodes.Brightness, 80, 100);
        _settings.Update(s => s with { VolumePollSeconds = 0 });
        _registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [_target, _other]), TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
        _mapper = new AudioMonitorMapper(_watcher, _registry, _settings, Logger.None);
        _controller = new VolumeController(_mapper, _registry, _settings, new FakeTimeProvider(), Logger.None);
        _service = new MonitorLevelService(_controller, _mapper, _settings);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DdcWorker Target => _registry.Monitors[0];

    private DdcWorker Other => _registry.Monitors[1];

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        _mapper.Dispose();
        await _registry.DisposeAsync();
    }

    [Fact]
    public async Task Read_ReturnsPercentagesAndMute()
    {
        await _registry.RefreshAsync(Ct);

        var levels = await _service.ReadAsync(Target, Ct);

        Assert.Equal(30, levels.Brightness);
        Assert.Equal(30, levels.Volume);
        Assert.True(levels.Muted);
    }

    [Fact]
    public async Task Read_NoVolume_ReturnsNullVolume()
    {
        await _registry.RefreshAsync(Ct);

        var levels = await _service.ReadAsync(Other, Ct);

        Assert.Equal(80, levels.Brightness);
        Assert.Null(levels.Volume);
    }

    [Fact]
    public async Task SetBrightness_WritesRawWithinMaximum()
    {
        await _registry.RefreshAsync(Ct);

        MonitorLevelService.SetBrightness(Target, 60, 50);
        await Target.ReadVcpAsync(VcpCodes.Brightness, Ct);

        Assert.Equal(30u, _target.GetCurrent(VcpCodes.Brightness));
    }

    [Fact]
    public async Task SetVolume_NonTarget_UsesMonitorRange()
    {
        _settings.Update(s => s with { Monitors = new Dictionary<string, MonitorSettings> { [_other.Identity.Key] = new() { VolumeMax = 40 } } });
        await _registry.RefreshAsync(Ct);

        _service.SetVolume(Other, 50, 100);
        await Other.ReadVcpAsync(VcpCodes.Brightness, Ct);

        Assert.Equal(20u, _other.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task SetVolume_NonTarget_HonorsTheMaximumVolumePercent()
    {
        _settings.Update(s => s with { Monitors = new Dictionary<string, MonitorSettings> { [_other.Identity.Key] = new() { MaxVolumePercent = 40 } } });
        await _registry.RefreshAsync(Ct);

        _service.SetVolume(Other, 50, 100);
        await Other.ReadVcpAsync(VcpCodes.Brightness, Ct);

        Assert.Equal(20u, _other.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task SetVolume_NonTarget_FollowsTheVolumeCurve()
    {
        _settings.Update(s => s with { Monitors = new Dictionary<string, MonitorSettings> { [_other.Identity.Key] = new() { VolumeCurve = VolumeCurve.Logarithmic } } });
        await _registry.RefreshAsync(Ct);

        _service.SetVolume(Other, 50, 100);
        await Other.ReadVcpAsync(VcpCodes.Brightness, Ct);

        Assert.Equal(25u, _other.GetCurrent(VcpCodes.AudioVolume));
    }

    [Fact]
    public async Task IsVolumeTarget_FollowsMapper()
    {
        await _registry.RefreshAsync(Ct);

        _watcher.SetDefault(s_monitorAudio);

        Assert.True(_service.IsVolumeTarget(Target));
        Assert.False(_service.IsVolumeTarget(Other));
        Assert.False(_service.TryToggleMute(Other));
    }
}
