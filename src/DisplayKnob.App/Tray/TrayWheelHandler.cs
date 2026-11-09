using DisplayKnob.Core.Platform;
using DisplayKnob.Core.Volume;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;

namespace DisplayKnob.App.Tray;

/// <summary>
/// Scrolling the mouse wheel over the tray icon steps the volume, through the same pipeline as the keys.
/// </summary>
/// <remarks>
/// The wheel is observed with raw input, not intercepted, so scrolling elsewhere is unaffected.
/// Holding Shift or Alt gives the fine step, like the keys.
/// </remarks>
internal sealed class TrayWheelHandler : IDisposable
{
    private const int WheelNotch = 120;
    private const long ResetAfterMs = 400; // A pause ends a scroll gesture; leftover partial notches are dropped.

    private readonly TrayIconHost _tray;
    private readonly IMouseWheelSource _wheel;
    private readonly IVolumeCommandSink _sink;
    private int _accumulated; // Only touched on the wheel source's thread.
    private long _lastWheel;

    public TrayWheelHandler(TrayIconHost tray, IMouseWheelSource wheel, IVolumeCommandSink sink)
    {
        _tray = tray;
        _wheel = wheel;
        _sink = sink;
        _wheel.WheelScrolled += OnWheel;
    }

    public void Dispose() => _wheel.WheelScrolled -= OnWheel;

    // Raised on the system-event window thread for every wheel movement anywhere.
    private void OnWheel(object? sender, MouseWheelEventArgs e)
    {
        if (_tray.IconId is not { } iconId || !IsOverIcon(iconId, e.X, e.Y))
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastWheel > ResetAfterMs || Math.Sign(_accumulated) != Math.Sign(e.Delta))
        {
            _accumulated = 0;
        }

        _lastWheel = now;
        _accumulated += e.Delta;

        // High-resolution wheels report fractions of a notch; step once per whole notch.
        var fine = IsFineModifierDown();
        while (Math.Abs(_accumulated) >= WheelNotch)
        {
            var up = _accumulated > 0;
            _accumulated -= up ? WheelNotch : -WheelNotch;
            _sink.TryPost(new VolumeCommand(up ? VolumeAction.Up : VolumeAction.Down, fine));
        }
    }

    private static unsafe bool IsOverIcon(Guid iconId, int x, int y)
    {
        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
            guidItem = iconId,
        };

        // Fails (e.g. E_FAIL) while the icon is hidden in the overflow area; then it simply isn't under the cursor.
        if (PInvoke.Shell_NotifyIconGetRect(in identifier, out RECT rect).Failed)
        {
            return false;
        }

        return x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
    }

    // High bit set = key currently down.
    private static bool IsFineModifierDown() =>
        PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0 || PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_MENU) < 0;
}
