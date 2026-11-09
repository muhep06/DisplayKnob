using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Tests.Fakes;
using DisplayKnob.Core.Volume;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Volume;

public sealed class EndpointVolumeMirrorTests : IAsyncDisposable
{
    private static readonly AudioEndpoint s_monitorAudio = new("{hdmi}", "S34C65xU (NVIDIA High Definition Audio)");
    private static readonly AudioEndpoint s_headphones = new("{phones}", "Headphones (Realtek(R) Audio)");
    private const int Rest = AudioMonitorMapper.MirrorRestLevel;

    private readonly FakeAudioEndpointWatcher _watcher = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingCommandSink _sink = new();
    private readonly MonitorRegistry _registry;
    private readonly AudioMonitorMapper _mapper;
    private readonly EndpointVolumeMirror _mirror;

    public EndpointVolumeMirrorTests()
    {
        _settings.Update(s => s with { MirrorWindowsVolume = true }); // Off by default.
        _registry = new MonitorRegistry(new FakeMonitorEnumerator(() => [new FakeMonitorController("S34C65xU")]), TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
        _mapper = new AudioMonitorMapper(_watcher, _registry, _settings, Logger.None);
        _mirror = new EndpointVolumeMirror(_watcher, _mapper, _sink, _settings, _time, Logger.None);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        _mirror.Dispose();
        _mapper.Dispose();
        await _registry.DisposeAsync();
    }

    [Fact]
    public async Task WindowsVolumeUp_PostsFineStepsUp()
    {
        await StartAsync();

        _watcher.RaiseVolume(s_monitorAudio.Id, Rest + 2);

        Assert.Equal([new(VolumeAction.Up, true), new(VolumeAction.Up, true)], _sink.Commands);
    }

    [Fact]
    public async Task WindowsVolumeDown_PostsOneFineStepPerPercent()
    {
        await StartAsync();

        _watcher.RaiseVolume(s_monitorAudio.Id, Rest - 8);

        Assert.Equal(8, _sink.Commands.Count);
        Assert.All(_sink.Commands, c => Assert.Equal(new VolumeCommand(VolumeAction.Down, true), c));
    }

    [Fact]
    public async Task Drag_AppliesOnlyTheNewDelta()
    {
        await StartAsync();

        _watcher.RaiseVolume(s_monitorAudio.Id, Rest - 2);
        _watcher.RaiseVolume(s_monitorAudio.Id, Rest - 5);

        Assert.Equal(5, _sink.Commands.Count);
    }

    [Fact]
    public async Task OwnChange_OnlyMovesBaseline()
    {
        await StartAsync();

        _watcher.RaiseVolume(s_monitorAudio.Id, 50, own: true);
        _watcher.RaiseVolume(s_monitorAudio.Id, 52);

        Assert.Equal(2, _sink.Commands.Count);
    }

    [Fact]
    public async Task AfterSettle_RestoresRestLevel()
    {
        await StartAsync();
        _watcher.PinnedLevels.Clear();
        _watcher.RaiseVolume(s_monitorAudio.Id, 100);

        _time.Advance(EndpointVolumeMirror.SettleDelay);

        Assert.Equal([Rest], _watcher.PinnedLevels);
    }

    [Fact]
    public async Task WindowsMuteClicked_TogglesMonitorMute()
    {
        await StartAsync();

        _watcher.RaiseVolume(s_monitorAudio.Id, Rest, muted: true);

        Assert.Equal([new VolumeCommand(VolumeAction.ToggleMute)], _sink.Commands);
    }

    [Fact]
    public async Task MirrorOff_IgnoresChanges()
    {
        await StartAsync();
        _settings.Update(s => s with { MirrorWindowsVolume = false });

        _watcher.RaiseVolume(s_monitorAudio.Id, 80);

        Assert.Empty(_sink.Commands);
    }

    [Fact]
    public async Task NonMonitorEndpoint_IgnoresChanges()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_headphones);

        _watcher.RaiseVolume(s_headphones.Id, 50);

        Assert.Empty(_sink.Commands);
    }

    [Fact]
    public async Task DdcNotAnswering_LeavesWindowsVolumeAlone()
    {
        await StartAsync();
        _sink.Accept = false;
        _watcher.PinnedLevels.Clear();

        _watcher.RaiseVolume(s_monitorAudio.Id, 80);
        _time.Advance(EndpointVolumeMirror.SettleDelay);

        Assert.Empty(_watcher.PinnedLevels);
    }

    private async Task StartAsync()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_monitorAudio);
    }
}
