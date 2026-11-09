using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Tests.Fakes;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Audio;

public sealed class AudioMonitorMapperTests : IAsyncDisposable
{
    private static readonly AudioEndpoint s_monitorAudio = new("{hdmi}", "S34C65xU (NVIDIA High Definition Audio)");
    private static readonly AudioEndpoint s_headphones = new("{phones}", "Headphones (Realtek(R) Audio)");

    private readonly FakeAudioEndpointWatcher _watcher = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly MonitorRegistry _registry;

    public AudioMonitorMapperTests()
    {
        var enumerator = new FakeMonitorEnumerator(() => [new FakeMonitorController("S34C65xU"), new FakeMonitorController("DELL U2723QE")]);
        _registry = new MonitorRegistry(enumerator, TimeProvider.System, new DdcWorkerOptions(1, TimeSpan.Zero), Logger.None);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _registry.DisposeAsync();

    [Fact]
    public async Task Current_DefaultIsMonitorAudio_TargetsThatMonitor()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_monitorAudio);
        using var mapper = CreateMapper();

        Assert.Equal("S34C65xU", mapper.Current?.Monitor.DisplayName);
        Assert.False(mapper.Current!.IsOverride);
    }

    [Fact]
    public async Task Current_DefaultIsHeadphones_IsNull()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_headphones);
        using var mapper = CreateMapper();

        Assert.Null(mapper.Current);
    }

    [Fact]
    public async Task DefaultEndpointChanges_TargetFollowsAndEventFires()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_headphones);
        using var mapper = CreateMapper();
        var raised = 0;
        mapper.TargetChanged += (_, _) => raised++;

        _watcher.SetDefault(s_monitorAudio);

        Assert.NotNull(mapper.Current);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Monitors_EnumeratedAfterMapper_TargetAppears()
    {
        _watcher.SetDefault(s_monitorAudio);
        using var mapper = CreateMapper();
        Assert.Null(mapper.Current);

        await _registry.RefreshAsync(Ct);

        Assert.Equal("S34C65xU", mapper.Current?.Monitor.DisplayName);
    }

    [Fact]
    public async Task Override_PointsToOtherMonitor_Wins()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_monitorAudio);
        using var mapper = CreateMapper();
        var dell = _registry.Monitors.Single(m => m.DisplayName == "DELL U2723QE");

        _settings.Update(s => s with { EndpointMonitorOverrides = new Dictionary<string, string> { [s_monitorAudio.Id] = dell.Identity.Key } });

        Assert.Same(dell, mapper.Current?.Monitor);
        Assert.True(mapper.Current!.IsOverride);
    }

    [Fact]
    public async Task Override_Empty_DisablesAutoMatch()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_monitorAudio);
        using var mapper = CreateMapper();

        _settings.Update(s => s with { EndpointMonitorOverrides = new Dictionary<string, string> { [s_monitorAudio.Id] = string.Empty } });

        Assert.Null(mapper.Current);
    }

    [Fact]
    public async Task DdcVolumeDisabledForMonitor_PassesThrough()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_monitorAudio);
        using var mapper = CreateMapper();
        var samsung = _registry.Monitors.Single(m => m.DisplayName == "S34C65xU");

        _settings.Update(s => s with
        {
            Monitors = new Dictionary<string, Core.Settings.MonitorSettings> { [samsung.Identity.Key] = new() { DdcVolumeEnabled = false } },
        });

        Assert.Null(mapper.Current);
    }

    [Fact]
    public async Task PinEndpointVolume_On_PinsMappedEndpointOnly()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_headphones);
        using var mapper = CreateMapper();

        _watcher.SetDefault(s_monitorAudio);

        Assert.Equal([s_monitorAudio.Id], _watcher.PinnedEndpoints);
    }

    [Fact]
    public async Task PinEndpointVolume_Off_DoesNotPin()
    {
        await _registry.RefreshAsync(Ct);
        _settings.Update(s => s with { PinEndpointVolume = false });
        _watcher.SetDefault(s_monitorAudio);
        using var mapper = CreateMapper();

        Assert.Empty(_watcher.PinnedEndpoints);
    }

    [Fact]
    public async Task RegistryRefresh_SameMonitor_TargetGetsNewWorker()
    {
        await _registry.RefreshAsync(Ct);
        _watcher.SetDefault(s_monitorAudio);
        using var mapper = CreateMapper();
        var before = mapper.Current!.Monitor;

        await _registry.RefreshAsync(Ct);

        Assert.NotSame(before, mapper.Current?.Monitor);
        Assert.Equal("S34C65xU", mapper.Current?.Monitor.DisplayName);
    }

    private AudioMonitorMapper CreateMapper() => new(_watcher, _registry, _settings, Logger.None);
}
