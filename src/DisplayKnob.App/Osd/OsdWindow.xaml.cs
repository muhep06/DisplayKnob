using DisplayKnob.App.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT;

namespace DisplayKnob.App.Osd;

/// <summary>
/// The volume OSD window: borderless, topmost, never activated, click-through, hidden from Alt+Tab.
/// Visibility is controlled through the layered-window alpha so it can fade.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Window has no Dispose; the controller is disposed in the Closed handler.")]
internal sealed partial class OsdWindow : Window
{
    private const uint DwmColorNone = 0xFFFFFFFE; // DWMWA_COLOR_NONE

    private readonly SUBCLASSPROC _subclassProc; // Kept in a field so the GC can't collect it while Windows holds it.
    private readonly DesktopAcrylicController _acrylic = new();
    private readonly SystemBackdropConfiguration _backdropConfig = new() { IsInputActive = true };

    public OsdWindow(OsdViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // x:Bind normally initializes when the root element loads, which only happens on activation.
        // This window is never activated, so initialize the bindings explicitly.
        Bindings.Update();

        Handle = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _subclassProc = SubclassProc;
        PInvoke.SetWindowSubclass(Handle, _subclassProc, 1, 0);
        ConfigureWindow();
        ConfigureBackdrop();
        Closed += (_, _) =>
        {
            PInvoke.RemoveWindowSubclass(Handle, _subclassProc, 1);
            _acrylic.Dispose();
        };
    }

    public OsdViewModel ViewModel { get; }

    public HWND Handle { get; }

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

        // WinUI re-applies its own frame attributes when the window is shown, so set ours afterwards.
        ApplyFrameAttributes();
    }

    public void Hide() => PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // Even without border and title bar the overlapped style keeps a frame; a plain popup has none.
        var style = (WINDOW_STYLE)PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        style &= ~(WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME | WINDOW_STYLE.WS_BORDER | WINDOW_STYLE.WS_DLGFRAME | WINDOW_STYLE.WS_SYSMENU);
        style |= WINDOW_STYLE.WS_POPUP;
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (nint)style);

        // NOACTIVATE: never takes focus. TOOLWINDOW: no taskbar button.
        // LAYERED + TRANSPARENT: mouse clicks fall through to whatever is underneath, and alpha enables fading.
        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)exStyle);
        SetAlpha(0);

        ApplyFrameAttributes();
    }

    private unsafe void ApplyFrameAttributes()
    {
        // Borderless windows get square corners by default; ask DWM for the Windows 11 rounding.
        var corner = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(Handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        // DWM otherwise draws a bright 1px frame around the rounded window.
        var borderColor = DwmColorNone;
        PInvoke.DwmSetWindowAttribute(Handle, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));
    }

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

    // Even with border and title bar removed, WinUI keeps reserving a thin non-client frame, which a layered
    // window paints white. Returning 0 for WM_NCCALCSIZE makes the client area cover the whole window.
    private LRESULT SubclassProc(HWND hWnd, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint refData) =>
        message == PInvoke.WM_NCCALCSIZE && wParam.Value != 0
            ? (LRESULT)0
            : PInvoke.DefSubclassProc(hWnd, message, wParam, lParam);

    private void UpdateBackdropTheme() =>
        _backdropConfig.Theme = Root.ActualTheme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
}
