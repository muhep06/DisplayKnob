using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Volume;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace DisplayKnob.App.Tray;

/// <summary>
/// Owns the notification-area icon and its context menu.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private const string IdleText = "DK";
    private const string IdleToolTip = "DisplayKnob";

    private readonly TaskbarIcon _icon;
    private readonly GeneratedIconSource _iconSource;
    private readonly MonitorProbe _probe;
    private readonly VolumeController _volume;
    private readonly AudioMonitorMapper _mapper;
    private readonly DispatcherQueue _dispatcher;
    private readonly ILogger _logger;

    public TrayIconHost(MonitorProbe probe, VolumeController volume, AudioMonitorMapper mapper, ILogger logger)
    {
        _probe = probe;
        _volume = volume;
        _mapper = mapper;
        _logger = logger.ForContext<TrayIconHost>();
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("TrayIconHost must be created on the UI thread.");

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Probe monitors", Command = new AsyncRelayCommand(ProbeAsync) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Open log folder", Command = new RelayCommand(OpenLogFolder) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Exit",
            Command = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty)),
        });

        // Placeholder until a real icon asset exists. While a monitor is the volume target it shows the level.
        _iconSource = new GeneratedIconSource
        {
            Text = IdleText,
            FontSize = 36,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Colors.SteelBlue),
        };

        _icon = new TaskbarIcon
        {
            ToolTipText = IdleToolTip,
            // Native Win32 popup menu: works without any XAML window.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            IconSource = _iconSource,
        };

        _volume.VolumeChanged += OnVolumeChanged;
        _mapper.TargetChanged += OnTargetChanged;
    }

    public event EventHandler? ExitRequested;

    public void Show() => _icon.ForceCreate(enablesEfficiencyMode: false);

    public void Dispose()
    {
        _volume.VolumeChanged -= OnVolumeChanged;
        _mapper.TargetChanged -= OnTargetChanged;
        _icon.Dispose();
    }

    // Both events arrive on background threads.
    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e) =>
        _dispatcher.TryEnqueue(() => ShowLevel(e.MonitorName, e.Level, e.Muted));

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        if (_mapper.Current is null)
        {
            _dispatcher.TryEnqueue(ShowIdle);
        }
    }

    private void ShowLevel(string monitorName, int level, bool muted)
    {
        _iconSource.Text = muted ? "×" : level.ToString(CultureInfo.InvariantCulture);
        _iconSource.FontSize = level >= 100 && !muted ? 26 : 36;
        _icon.ToolTipText = muted
            ? $"{monitorName}: muted"
            : $"{monitorName}: {level.ToString(CultureInfo.CurrentCulture)}%";
    }

    private void ShowIdle()
    {
        _iconSource.Text = IdleText;
        _iconSource.FontSize = 36;
        _icon.ToolTipText = IdleToolTip;
    }

    private async Task ProbeAsync()
    {
        try
        {
            await _probe.RunAsync();
            _icon.ShowNotification("DisplayKnob", "Monitor probe finished. Results are in the log.");
        }
        catch (Exception ex)
        {
            // Probe is diagnostic; a bug in it must not take the app down.
            _logger.Error(ex, "Monitor probe failed");
            _icon.ShowNotification("DisplayKnob", "Monitor probe failed. See the log.");
        }
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LoggingSetup.LogDirectory);
        Process.Start(new ProcessStartInfo { FileName = LoggingSetup.LogDirectory, UseShellExecute = true })?.Dispose();
    }
}
