using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using DisplayKnob.Core.Displays;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace DisplayKnob.App.Tray;

/// <summary>
/// Owns the notification-area icon and its context menu.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MonitorProbe _probe;
    private readonly ILogger _logger;

    public TrayIconHost(MonitorProbe probe, ILogger logger)
    {
        _probe = probe;
        _logger = logger.ForContext<TrayIconHost>();

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Probe monitors", Command = new AsyncRelayCommand(ProbeAsync) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Open log folder", Command = new RelayCommand(OpenLogFolder) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Exit",
            Command = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty)),
        });

        _icon = new TaskbarIcon
        {
            ToolTipText = "DisplayKnob",
            // Native Win32 popup menu: works without any XAML window.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            // Placeholder until a real icon asset exists.
            IconSource = new GeneratedIconSource
            {
                Text = "DK",
                FontSize = 36,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Colors.SteelBlue),
            },
        };
    }

    public event EventHandler? ExitRequested;

    public void Show() => _icon.ForceCreate(enablesEfficiencyMode: false);

    public void Dispose() => _icon.Dispose();

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
