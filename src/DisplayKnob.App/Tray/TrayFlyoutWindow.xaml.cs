using DisplayKnob.App.Interop;
using DisplayKnob.App.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DisplayKnob.App.Tray;

/// <summary>
/// The flyout shown when the tray icon is clicked. Frameless like the Windows 11 Quick Settings flyout,
/// and hidden as soon as it loses focus.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Window has no Dispose; the chrome is disposed in the Closed handler.")]
internal sealed partial class TrayFlyoutWindow : Window
{
    private readonly BorderlessWindowChrome _chrome;

    public TrayFlyoutWindow(TrayFlyoutViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Bindings.Update(); // Bindings normally initialize on first activation; we measure before that.

        _chrome = new BorderlessWindowChrome(this);
        Activated += OnActivated;
        Root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                Hide();
            }
        };
        Closed += (_, _) => _chrome.Dispose();
    }

    public event EventHandler? SettingsRequested;

    public TrayFlyoutViewModel ViewModel { get; }

    public bool IsShown { get; private set; }

    /// <summary>When the flyout last hid itself (Environment.TickCount64).</summary>
    public long HiddenAt { get; private set; }

    public HWND Handle => _chrome.Handle;

    /// <summary>Height the content needs at the given width (DIP).</summary>
    public double MeasureHeight(double width)
    {
        Root.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        return Root.DesiredSize.Height;
    }

    /// <summary>Moves (physical pixels), shows and focuses the flyout.</summary>
    public void ShowAt(int x, int y, int width, int height)
    {
        PInvoke.SetWindowPos(Handle, HWND.HWND_TOPMOST, x, y, width, height, SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        Activate();

        // The tray click gave us the right to take the foreground; without it the flyout can't close on deactivate.
        PInvoke.SetForegroundWindow(Handle);
        _chrome.ApplyFrameAttributes();
        IsShown = true;
    }

    public void Hide()
    {
        if (!IsShown)
        {
            return;
        }

        IsShown = false;
        HiddenAt = Environment.TickCount64;
        PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Hide();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }
}
