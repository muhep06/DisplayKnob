using System.Runtime.InteropServices;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace DisplayKnob.Core.Input;

/// <summary>
/// <see cref="IHotkeyConflictChecker"/> that tries to register the shortcut itself: the system refuses
/// a combination another application already holds.
/// </summary>
internal sealed class HotkeyConflictChecker : IHotkeyConflictChecker
{
    // Any id works: the registration lives for a few microseconds and is tied to this calling thread.
    private const int ProbeId = 0x444B; // "DK"

    private readonly ILogger _logger;

    public HotkeyConflictChecker(ILogger logger)
    {
        _logger = logger.ForContext<HotkeyConflictChecker>();
    }

    /// <inheritdoc/>
    public bool IsTakenByAnotherApp(Hotkey hotkey)
    {
        // RegisterHotKey with a null window binds to the calling thread; unregister on the same thread right away.
        var modifiers = ToNative(hotkey.Modifiers) | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (PInvoke.RegisterHotKey(HWND.Null, ProbeId, modifiers, hotkey.VirtualKey))
        {
            PInvoke.UnregisterHotKey(HWND.Null, ProbeId);
            return false;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == (int)WIN32_ERROR.ERROR_HOTKEY_ALREADY_REGISTERED)
        {
            return true;
        }

        // Some other failure: say nothing to the user rather than a false warning, but keep a trace.
        _logger.Debug("RegisterHotKey probe for {Hotkey} failed with {Error}", hotkey, error);
        return false;
    }

    private static HOT_KEY_MODIFIERS ToNative(HotkeyModifiers modifiers)
    {
        var native = default(HOT_KEY_MODIFIERS);
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            native |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            native |= HOT_KEY_MODIFIERS.MOD_ALT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            native |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            native |= HOT_KEY_MODIFIERS.MOD_WIN;
        }

        return native;
    }
}
