using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DisplayHub.App.Tray;

/// <summary>
/// Owns the notification-area icon and its context menu.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIconHost()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Exit",
            Command = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty)),
        });

        _icon = new TaskbarIcon
        {
            ToolTipText = "DisplayHub",
            // Native Win32 popup menu: works without any XAML window.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            // Placeholder until a real icon asset exists.
            IconSource = new GeneratedIconSource
            {
                Text = "DH",
                FontSize = 36,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Colors.SteelBlue),
            },
        };
    }

    public event EventHandler? ExitRequested;

    public void Show() => _icon.ForceCreate(enablesEfficiencyMode: false);

    public void Dispose() => _icon.Dispose();
}
