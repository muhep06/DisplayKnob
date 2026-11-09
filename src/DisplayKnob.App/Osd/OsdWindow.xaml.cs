using DisplayKnob.App.Interop;
using DisplayKnob.App.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT;

namespace DisplayKnob.App.Osd;

/// <summary>
/// The volume OSD window: borderless, topmost, never activated, click-through, hidden from Alt+Tab.
/// Visibility is controlled through the layered-window alpha so it can fade.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Window has no Dispose; the chrome and controller are disposed in the Closed handler.")]
internal sealed partial class OsdWindow : Window
{
    private readonly BorderlessWindowChrome _chrome;
    private readonly DesktopAcrylicController _acrylic = new();
    private readonly SystemBackdropConfiguration _backdropConfig = new() { IsInputActive = true };

    public OsdWindow(OsdViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // x:Bind normally initializes when the root element loads, which only happens on activation.
        // This window is never activated, so initialize the bindings explicitly.
        Bindings.Update();

        _chrome = new BorderlessWindowChrome(this);

        // NOACTIVATE: never takes focus. LAYERED + TRANSPARENT: mouse clicks fall through, and alpha enables fading.
        _chrome.AddExtendedStyle(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT);
        SetAlpha(0);
        ConfigureBackdrop();

        Closed += (_, _) =>
        {
            _chrome.Dispose();
            _acrylic.Dispose();
        };
    }

    public OsdViewModel ViewModel { get; }

    private HWND Handle => _chrome.Handle;

    /// <summary>Sets the window opacity (0 = invisible, 255 = opaque).</summary>
    public void SetAlpha(byte alpha) =>
        PInvoke.SetLayeredWindowAttributes(Handle, default, alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

    /// <summary>Moves and resizes the window (physical pixels) and shows it on top without activating it.</summary>
    public void ShowAt(int x, int y, int width, int height)
    {
        PInvoke.SetWindowPos(
            Handle,
            HWND.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        _chrome.ApplyFrameAttributes();
    }

    public void Hide() => PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);

    // The XAML SystemBackdrop falls back to a solid color whenever the window is inactive, and this window is
    // never active. Driving the controller directly lets us keep the acrylic look.
    private void ConfigureBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        UpdateBackdropTheme();
        Root.ActualThemeChanged += (_, _) => UpdateBackdropTheme();
        _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        _acrylic.SetSystemBackdropConfiguration(_backdropConfig);
    }

    private void UpdateBackdropTheme() =>
        _backdropConfig.Theme = Root.ActualTheme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
}
