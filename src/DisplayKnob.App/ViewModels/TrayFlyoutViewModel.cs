using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Volume;
using Microsoft.UI.Dispatching;
using Serilog;

namespace DisplayKnob.App.ViewModels;

/// <summary>The tray flyout: one card per monitor with brightness and volume sliders.</summary>
internal sealed partial class TrayFlyoutViewModel : ObservableObject, IDisposable
{
    private readonly MonitorRegistry _registry;
    private readonly MonitorLevelService _levels;
    private readonly VolumeController _volume;
    private readonly DispatcherQueue _dispatcher;
    private readonly ILogger _logger;

    public TrayFlyoutViewModel(MonitorRegistry registry, MonitorLevelService levels, VolumeController volume, ILogger logger)
    {
        _registry = registry;
        _levels = levels;
        _volume = volume;
        _logger = logger.ForContext<TrayFlyoutViewModel>();
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("TrayFlyoutViewModel must be created on the UI thread.");
        _volume.VolumeChanged += OnVolumeChanged;
    }

    public ObservableCollection<MonitorLevelsItemViewModel> Monitors { get; } = [];

    [ObservableProperty]
    public partial bool HasNoMonitors { get; set; }

    /// <summary>
    /// Rebuilds the monitor list synchronously (so the flyout can be sized), then reads the levels.
    /// </summary>
    public Task LoadAsync()
    {
        Monitors.Clear();
        foreach (var monitor in _registry.Monitors)
        {
            Monitors.Add(new MonitorLevelsItemViewModel(monitor, _levels));
        }

        HasNoMonitors = Monitors.Count == 0;
        return ReadAllAsync([.. Monitors]);
    }

    public void Dispose() => _volume.VolumeChanged -= OnVolumeChanged;

    private async Task ReadAllAsync(IReadOnlyList<MonitorLevelsItemViewModel> items)
    {
        try
        {
            // Each monitor has its own DDC worker, so they can be read in parallel.
            await Task.WhenAll(items.Select(i => i.LoadAsync()));
        }
        catch (ObjectDisposedException ex)
        {
            // Monitors were re-enumerated while reading; the next open shows the new set.
            _logger.Debug(ex, "Monitor went away while the flyout was reading it");
        }
        catch (OperationCanceledException ex)
        {
            _logger.Debug(ex, "Flyout read cancelled");
        }
    }

    // Raised on the volume controller's thread.
    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e) =>
        _dispatcher.TryEnqueue(() =>
        {
            foreach (var item in Monitors)
            {
                if (item.Key == e.MonitorKey && !item.IsLoading)
                {
                    item.ShowVolume(e.Level, e.Muted);
                }
            }
        });
}
