using DisplayKnob.Core.Ddc;
using Serilog;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Holds the current set of monitors, each with its own <see cref="DdcWorker"/>.
/// </summary>
/// <remarks>
/// Physical monitor handles are not stable (a KVM switch or display change invalidates them),
/// so <see cref="RefreshAsync"/> releases every old handle and enumerates from scratch.
/// </remarks>
public sealed class MonitorRegistry : IAsyncDisposable
{
    private readonly IMonitorEnumerator _enumerator;
    private readonly TimeProvider _timeProvider;
    private readonly DdcWorkerOptions _workerOptions;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<DdcWorker> _monitors = [];
    private bool _disposed;

    /// <summary>Creates an empty registry; call <see cref="RefreshAsync"/> to populate it.</summary>
    public MonitorRegistry(IMonitorEnumerator enumerator, TimeProvider timeProvider, DdcWorkerOptions workerOptions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(workerOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _enumerator = enumerator;
        _timeProvider = timeProvider;
        _workerOptions = workerOptions;
        _logger = logger.ForContext<MonitorRegistry>();
    }

    /// <summary>The monitors found by the last refresh.</summary>
    public IReadOnlyList<DdcWorker> Monitors => Volatile.Read(ref _monitors);

    /// <summary>
    /// Releases all current monitor handles and enumerates monitors again.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await DisposeMonitorsAsync().ConfigureAwait(false);

            // Enumeration blocks briefly on Win32 calls; keep it off the caller's (possibly UI) thread.
            var controllers = await Task.Run(_enumerator.Enumerate, cancellationToken).ConfigureAwait(false);

            foreach (var duplicate in controllers.GroupBy(c => c.Identity.Key).Where(g => g.Count() > 1))
            {
                _logger.Warning("{Count} monitors share the identity {MonitorId}; per-monitor settings will collide", duplicate.Count(), duplicate.Key);
            }

            Volatile.Write(ref _monitors, [.. controllers.Select(c => new DdcWorker(c, _timeProvider, _workerOptions, _logger))]);
            _logger.Information("Found {Count} monitor(s)", controllers.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _refreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await DisposeMonitorsAsync().ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task DisposeMonitorsAsync()
    {
        var old = Volatile.Read(ref _monitors);
        Volatile.Write(ref _monitors, []);
        foreach (var worker in old)
        {
            await worker.DisposeAsync().ConfigureAwait(false);
        }
    }
}
