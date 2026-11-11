using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using DisplayKnob.App.Updates;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Volume;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DisplayKnob.App.Tray;

/// <summary>
/// Owns the notification-area icon and its context menu.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private const string IdleToolTip = "DisplayKnob";
    private const string CheckForUpdatesText = "Check for updates";

    private readonly TaskbarIcon _icon;
    private readonly KnobIconSet _icons = new(GetNotificationIconSize());
    private readonly MonitorProbe _probe;
    private readonly VolumeController _volume;
    private readonly AudioMonitorMapper _mapper;
    private readonly UpdateService _updates;
    private readonly MenuFlyoutItem? _updateItem;
    private readonly DispatcherQueue _dispatcher;
    private readonly ILogger _logger;
    private VolumeChangedEventArgs? _lastLevel; // UI thread only.
    private string? _lastNotificationTag; // UI thread only.
    private Guid? _iconId;

    public TrayIconHost(MonitorProbe probe, VolumeController volume, AudioMonitorMapper mapper, UpdateService updates, ILogger logger)
    {
        _probe = probe;
        _volume = volume;
        _mapper = mapper;
        _updates = updates;
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
        if (_updates.IsInstalled)
        {
            // Dev builds and plain bin folders can't update, so they don't get the item.
            _updateItem = new MenuFlyoutItem { Text = CheckForUpdatesText, Command = new AsyncRelayCommand(OnUpdateClickedAsync) };
            menu.Items.Add(_updateItem);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Exit",
            Command = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty)),
        });

        // The knob's pointer turns with the volume of the monitor the keys control.
        _icon = new TaskbarIcon
        {
            ToolTipText = IdleToolTip,
            // Native Win32 popup menu: works without any XAML window.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            Icon = _icons.Idle,
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

    /// <summary>
    /// The user clicked the most recent notification; the argument is the tag it was shown with (see <see cref="Notify"/>).
    /// Windows only reports a click on the last notification, so a click on an older one can't be told apart.
    /// </summary>
    public event EventHandler<string?>? NotificationClicked;

    /// <summary>
    /// Shows a notification from the tray icon. <paramref name="tag"/> comes back with <see cref="NotificationClicked"/>,
    /// so a notification that asks something can tell its own click from another notification's.
    /// </summary>
    public void Notify(string message, string? tag = null)
    {
        _lastNotificationTag = tag;
        _icon.ShowNotification("DisplayKnob", message);
    }

    /// <summary>The shell's id for the icon (set once shown). Readable from any thread.</summary>
    public Guid? IconId => _iconId;

    public void Show()
    {
        _icon.ForceCreate(enablesEfficiencyMode: false);
        _iconId = _icon.Id; // TaskbarIcon is a DependencyObject; cache the id for other threads.
        _icon.TrayIcon.MessageWindow.MouseEventReceived += OnTrayMouseEvent;
    }

    // A click on a notification arrives as a mouse event of the icon's message window.
    private void OnTrayMouseEvent(object? sender, MessageWindow.MouseEventReceivedEventArgs e)
    {
        if (e.MouseEvent == MouseEvent.BalloonToolTipClicked)
        {
            _dispatcher.TryEnqueue(() => NotificationClicked?.Invoke(this, _lastNotificationTag));
        }
    }

    public void Dispose()
    {
        _volume.VolumeChanged -= OnVolumeChanged;
        _volume.TargetRespondingChanged -= OnStatusChanged;
        _mapper.TargetChanged -= OnStatusChanged;
        _icon.Dispose();
    }

    // These events arrive on background threads.
    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        // The other monitors of the volume group report too; the icon shows the one the audio plays through.
        if (e.MonitorKey is not null && _mapper.Current is { } target && e.MonitorKey != target.Monitor.Identity.Key)
        {
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            _lastLevel = e;
            Render();
        });
    }

    private void OnStatusChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Render);

    private void Render()
    {
        var target = _mapper.Current;
        if (target is null)
        {
            SetIcon(_icons.Idle, IdleToolTip);
        }
        else if (!_volume.IsTargetResponding)
        {
            SetIcon(_icons.Get(KnobIconSet.IdleLevel, KnobIconMode.Warning), $"{target.Monitor.DisplayName} doesn't answer DDC. The volume keys control Windows for now.");
        }
        else if (_lastLevel is { } last && last.MonitorName == target.Monitor.DisplayName)
        {
            var toolTip = last.Muted
                ? $"{last.MonitorName}: muted"
                : $"{last.MonitorName}: {last.Level.ToString(CultureInfo.CurrentCulture)}%";
            SetIcon(_icons.Get(last.Level, last.Muted ? KnobIconMode.Muted : KnobIconMode.Normal), toolTip);
        }
        else
        {
            SetIcon(_icons.Idle, $"{IdleToolTip}: {target.Monitor.DisplayName}");
        }
    }

    private void SetIcon(System.Drawing.Icon icon, string toolTip)
    {
        _icon.Icon = icon;
        _icon.ToolTipText = toolTip;
    }

    // The shell asks for a specific small-icon size (16 px at 100% scale, more on larger scales).
    private static int GetNotificationIconSize() =>
        Math.Max(16, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON));

    /// <summary>Looks for a new version without bothering the user unless there is one. Call once at startup.</summary>
    public async Task CheckForUpdatesInBackgroundAsync()
    {
        if (await _updates.CheckAsync())
        {
            AnnounceUpdate();
        }
    }

    private async Task OnUpdateClickedAsync()
    {
        if (_updates.PendingVersion is null)
        {
            if (await _updates.CheckAsync())
            {
                AnnounceUpdate();
            }
            else
            {
                Notify("You're up to date.");
            }

            return;
        }

        Notify("Downloading the update. DisplayKnob will restart.");
        if (!await _updates.ApplyPendingAsync())
        {
            Notify("The update failed. See the log.");
        }
    }

    // The menu item turns into "install"; installing restarts the app, so it is the user's call.
    private void AnnounceUpdate()
    {
        var text = $"Install update {_updates.PendingVersion} and restart";
        if (_updateItem is not null)
        {
            _updateItem.Text = text;
        }

        Notify($"Version {_updates.PendingVersion} is available. Use the tray menu to install it.");
    }

    private async Task ProbeAsync()
    {
        try
        {
            await _probe.RunAsync();
            Notify("Monitor probe finished. Results are in the log.");
        }
        catch (Exception ex)
        {
            // Probe is diagnostic; a bug in it must not take the app down.
            _logger.Error(ex, "Monitor probe failed");
            Notify("Monitor probe failed. See the log.");
        }
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LoggingSetup.LogDirectory);
        Process.Start(new ProcessStartInfo { FileName = LoggingSetup.LogDirectory, UseShellExecute = true })?.Dispose();
    }
}
