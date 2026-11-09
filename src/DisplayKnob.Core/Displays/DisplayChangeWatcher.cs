using DisplayKnob.Core.Platform;
using Serilog;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Re-enumerates monitors after display changes, monitor arrival/removal, KVM switches and resume.
/// </summary>
/// <remarks>
/// Windows sends these notifications in bursts (one display change can produce a dozen messages), so the
/// refresh is debounced. After resume the delay is longer, because monitors often answer DDC only a few
/// seconds after they wake up.
/// </remarks>
public sealed class DisplayChangeWatcher : IDisposable
{
    /// <summary>Quiet time after the last display/device event before monitors are re-enumerated.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1.5);

    /// <summary>Minimum wait after resume before monitors are re-enumerated.</summary>
    public static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(4);

    private readonly ISystemEventSource _events;
    private readonly Func<Task> _refresh;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly ITimer _timer;
    private DateTimeOffset _notBefore;
    private bool _disposed;

    /// <summary>Creates the watcher and subscribes to system events.</summary>
    public DisplayChangeWatcher(ISystemEventSource events, MonitorRegistry registry, TimeProvider timeProvider, ILogger logger)
        : this(events, () => registry.RefreshAsync(), timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }

    /// <summary>Test seam: <paramref name="refresh"/> replaces the registry refresh.</summary>
    internal DisplayChangeWatcher(ISystemEventSource events, Func<Task> refresh, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _events = events;
        _refresh = refresh;
        _timeProvider = timeProvider;
        _logger = logger.ForContext<DisplayChangeWatcher>();
        _timer = timeProvider.CreateTimer(_ => _ = RefreshAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _events.SystemEventOccurred += OnSystemEvent;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _events.SystemEventOccurred -= OnSystemEvent;
            _timer.Dispose();
        }
    }

    private void OnSystemEvent(object? sender, SystemEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            // Every event restarts the debounce, but never shortens a pending resume delay.
            var now = _timeProvider.GetUtcNow();
            if (e.Kind == SystemEventKind.Resumed)
            {
                _notBefore = now + ResumeDelay;
            }

            var delay = Max(Debounce, _notBefore - now);
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            _logger.Information("Display configuration changed; re-enumerating monitors");
            await _refresh().ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            // The app is shutting down and the registry is gone; nothing left to refresh.
            _logger.Debug(ex, "Skipped re-enumeration during shutdown");
        }
        catch (Exception ex)
        {
            // Runs from a timer: an escaping exception would crash the process. Monitors stay as they were.
            _logger.Error(ex, "Re-enumerating monitors failed");
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
