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
    private readonly SolidColorBrush _normalBackground = new(Colors.SteelBlue);
    private readonly SolidColorBrush _warningBackground = new(Colors.DarkOrange);
    private VolumeChangedEventArgs? _lastLevel; // UI thread only.
    private Guid? _iconId;

    public TrayIconHost(MonitorProbe probe, VolumeController volume, AudioMonitorMapper mapper, ILogger logger)
    {
        _probe = probe;
        _volume = volume;
        _mapper = mapper;
        _logger = logger.ForContext<TrayIconHost>();
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("TrayIconHost must be created on the UI thread.");

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Settings",
            Command = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty)),
        });
        menu.Items.Add(new MenuFlyoutSeparator());
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
            Background = _normalBackground,
        };

        _icon = new TaskbarIcon
        {
            ToolTipText = IdleToolTip,
            // Native Win32 popup menu: works without any XAML window.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            IconSource = _iconSource,
            LeftClickCommand = new RelayCommand(() => FlyoutRequested?.Invoke(this, EventArgs.Empty)),
            NoLeftClickDelay = true, // We don't use double-click; don't wait to rule it out.
        };

        _volume.VolumeChanged += OnVolumeChanged;
        _volume.TargetRespondingChanged += OnStatusChanged;
        _mapper.TargetChanged += OnStatusChanged;
    }

    public event EventHandler? ExitRequested;

    public event EventHandler? SettingsRequested;

    /// <summary>The icon was left-clicked: show or hide the flyout.</summary>
    public event EventHandler? FlyoutRequested;

    /// <summary>The shell's id for the icon (set once shown). Readable from any thread.</summary>
    public Guid? IconId => _iconId;

    public void Show()
    {
        _icon.ForceCreate(enablesEfficiencyMode: false);
        _iconId = _icon.Id; // TaskbarIcon is a DependencyObject; cache the id for other threads.
    }

    public void Dispose()
    {
        _volume.VolumeChanged -= OnVolumeChanged;
        _volume.TargetRespondingChanged -= OnStatusChanged;
        _mapper.TargetChanged -= OnStatusChanged;
        _icon.Dispose();
    }

    // These events arrive on background threads.
    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e) =>
        _dispatcher.TryEnqueue(() =>
        {
            _lastLevel = e;
            Render();
        });

    private void OnStatusChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Render);

    private void Render()
    {
        var target = _mapper.Current;
        if (target is null)
        {
            SetIcon(IdleText, IdleToolTip, warning: false);
        }
        else if (!_volume.IsTargetResponding)
        {
            SetIcon("!", $"{target.Monitor.DisplayName} doesn't answer DDC. The volume keys control Windows for now.", warning: true);
        }
        else if (_lastLevel is { } last && last.MonitorName == target.Monitor.DisplayName)
        {
            var text = last.Muted ? "×" : last.Level.ToString(CultureInfo.InvariantCulture);
            var toolTip = last.Muted
                ? $"{last.MonitorName}: muted"
                : $"{last.MonitorName}: {last.Level.ToString(CultureInfo.CurrentCulture)}%";
            SetIcon(text, toolTip, warning: false);
        }
        else
        {
            SetIcon(IdleText, $"{IdleToolTip}: {target.Monitor.DisplayName}", warning: false);
        }
    }

    private void SetIcon(string text, string toolTip, bool warning)
    {
        _iconSource.Text = text;
        _iconSource.FontSize = text.Length >= 3 ? 26 : 36;
        _iconSource.Background = warning ? _warningBackground : _normalBackground;
        _icon.ToolTipText = toolTip;
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
