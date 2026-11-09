using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DisplayKnob.App.Interop;

/// <summary>
/// Turns a WinUI window into a frameless, rounded Windows 11 style popup (OSD, tray flyout): no title bar,
/// no border, no taskbar button, not in Alt+Tab.
/// </summary>
internal sealed class BorderlessWindowChrome : IDisposable
{
    private const uint DwmColorNone = 0xFFFFFFFE; // DWMWA_COLOR_NONE

    private readonly HWND _hwnd;
    private readonly SUBCLASSPROC _subclassProc; // Kept in a field so the GC can't collect it while Windows holds it.

    public BorderlessWindowChrome(Window window)
    {
        _hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        _subclassProc = SubclassProc;
        PInvoke.SetWindowSubclass(_hwnd, _subclassProc, 1, 0);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.IsShownInSwitchers = false;

        // Even without border and title bar the overlapped style keeps a frame; a plain popup has none.
        var style = (WINDOW_STYLE)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        style &= ~(WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME | WINDOW_STYLE.WS_BORDER | WINDOW_STYLE.WS_DLGFRAME | WINDOW_STYLE.WS_SYSMENU);
        style |= WINDOW_STYLE.WS_POPUP;
        PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (nint)style);

        // TOOLWINDOW: no taskbar button.
        AddExtendedStyle(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);
        ApplyFrameAttributes();
    }

    public HWND Handle => _hwnd;

    public void AddExtendedStyle(WINDOW_EX_STYLE add)
    {
        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)(exStyle | add));
    }

    /// <summary>Rounded corners and no DWM border. WinUI re-applies its own when the window is shown, so call it after showing.</summary>
    public unsafe void ApplyFrameAttributes()
    {
        // Borderless windows get square corners by default; ask DWM for the Windows 11 rounding.
        var corner = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        // DWM otherwise draws a bright 1px frame around the rounded window.
        var borderColor = DwmColorNone;
        PInvoke.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));
    }

    public void Dispose() => PInvoke.RemoveWindowSubclass(_hwnd, _subclassProc, 1);

    // Even with border and title bar removed, WinUI keeps reserving a thin non-client frame (painted white on
    // layered windows). Returning 0 for WM_NCCALCSIZE makes the client area cover the whole window.
    private LRESULT SubclassProc(HWND hWnd, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint refData) =>
        message == PInvoke.WM_NCCALCSIZE && wParam.Value != 0
            ? (LRESULT)0
            : PInvoke.DefSubclassProc(hWnd, message, wParam, lParam);
}
