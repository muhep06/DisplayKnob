using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Platform;
using DisplayKnob.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Displays;

public sealed class DisplayChangeWatcherTests : IDisposable
{
    private readonly FakeSystemEventSource _events = new();
    private readonly FakeTimeProvider _time = new();
    private readonly DisplayChangeWatcher _watcher;
    private int _refreshes;

    public DisplayChangeWatcherTests()
    {
        _watcher = new DisplayChangeWatcher(
            _events,
            () =>
            {
                _refreshes++;
                return Task.CompletedTask;
            },
            _time,
            Logger.None);
    }

    public void Dispose() => _watcher.Dispose();

    [Fact]
    public void DisplayChange_RefreshesAfterDebounce()
    {
        _events.Raise(SystemEventKind.DisplaysChanged);

        _time.Advance(DisplayChangeWatcher.Debounce);

        Assert.Equal(1, _refreshes);
    }

    [Fact]
    public void Burst_RefreshesOnceAfterLastEvent()
    {
        _events.Raise(SystemEventKind.DisplaysChanged);
        _time.Advance(TimeSpan.FromSeconds(1));
        _events.Raise(SystemEventKind.DevicesChanged);
        _time.Advance(TimeSpan.FromSeconds(1));
        _events.Raise(SystemEventKind.DisplaysChanged);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, _refreshes);

        _time.Advance(DisplayChangeWatcher.Debounce);

        Assert.Equal(1, _refreshes);
    }

    [Fact]
    public void Resume_WaitsForResumeDelay()
    {
        _events.Raise(SystemEventKind.Resumed);

        _time.Advance(DisplayChangeWatcher.Debounce);
        Assert.Equal(0, _refreshes);

        _time.Advance(DisplayChangeWatcher.ResumeDelay - DisplayChangeWatcher.Debounce);
        Assert.Equal(1, _refreshes);
    }

    [Fact]
    public void DisplayChange_AfterResume_DoesNotShortenResumeDelay()
    {
        _events.Raise(SystemEventKind.Resumed);
        _events.Raise(SystemEventKind.DisplaysChanged);

        _time.Advance(DisplayChangeWatcher.Debounce);

        Assert.Equal(0, _refreshes);
    }

    [Fact]
    public void Dispose_StopsRefreshing()
    {
        _events.Raise(SystemEventKind.DisplaysChanged);

        _watcher.Dispose();
        _time.Advance(DisplayChangeWatcher.ResumeDelay);

        Assert.Equal(0, _refreshes);
    }
}
