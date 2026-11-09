using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace DisplayKnob.App.Tray;

/// <summary>Where the notification-area icon is on screen.</summary>
internal static class TrayIconLocator
{
    /// <summary>
    /// Gets the icon's rectangle in physical screen pixels. Fails (e.g. E_FAIL) while the icon is hidden in
    /// the overflow area.
    /// </summary>
    public static unsafe bool TryGetRect(Guid iconId, out RECT rect)
    {
        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
            guidItem = iconId,
        };
        return PInvoke.Shell_NotifyIconGetRect(in identifier, out rect).Succeeded;
    }
}
