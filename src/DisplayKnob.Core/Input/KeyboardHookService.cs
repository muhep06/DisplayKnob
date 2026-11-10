using System.ComponentModel;
using System.Runtime.InteropServices;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DisplayKnob.Core.Input;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) for the media volume keys, on its own message-loop thread.
/// </summary>
/// <remarks>
/// Windows silently removes a low-level hook whose callback exceeds <c>LowLevelHooksTimeout</c>.
/// The callback therefore only asks the handler whether to swallow the key; the handler must return
/// immediately (no I/O, no locks, no logging) and do the real work elsewhere.
/// </remarks>
internal sealed class KeyboardHookService : IDisposable
{
    private const uint ReinstallMessage = PInvoke.WM_APP + 1;
    private const uint InjectVolumeKeyMessage = PInvoke.WM_APP + 2;
    private const uint MaskModifierMessage = PInvoke.WM_APP + 3;

    // A virtual-key code Windows leaves unassigned. Tapping it while Alt or Win is held tells the shell the
    // modifier was "used", so releasing it afterwards doesn't open a menu or the Start menu (AutoHotkey does the same).
    private const VIRTUAL_KEY MaskKey = (VIRTUAL_KEY)0xE8;

    private readonly ILogger _logger;
    private readonly HOOKPROC _hookProc; // Kept in a field so the GC can't collect the delegate while Windows holds it.
    private readonly bool[] _swallowedDown = new bool[3]; // Indexed by VolumeKey; only touched on the hook thread.
    private readonly ushort[] _hotkeyDownKey = new ushort[3]; // Indexed by VolumeKey: the virtual key whose shortcut press was swallowed, 0 = none.
    private HotkeyBindings? _bindings; // Replaced from other threads, read with Volatile.
    private bool _modifierMasked; // Hook thread only: a mask tap was already sent for the Alt/Win currently held.
    private Func<VolumeKeyPress, bool>? _handler;
    private Thread? _thread;
    private uint _threadId;

    public KeyboardHookService(ILogger logger)
    {
        _logger = logger.ForContext<KeyboardHookService>();
        _hookProc = HookProc;
    }

    /// <summary>
    /// Installs the hook. <paramref name="handler"/> runs on the hook thread for every volume key press
    /// and returns <see langword="true"/> to swallow the key (Windows then never sees it).
    /// </summary>
    /// <exception cref="Win32Exception">The hook could not be installed.</exception>
    public void Start(Func<VolumeKeyPress, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_thread is not null)
        {
            throw new InvalidOperationException("The keyboard hook is already running.");
        }

        _handler = handler;

        using var started = new ManualResetEventSlim(false);
        Exception? startError = null;
        _thread = new Thread(() => Run(started, e => startError = e))
        {
            Name = "DisplayKnob keyboard hook",
            IsBackground = true,
        };
        _thread.Start();
        started.Wait();

        if (startError is not null)
        {
            _thread = null;
            throw startError;
        }

        _logger.Information("Keyboard hook installed");
    }

    /// <summary>
    /// Sets the keyboard shortcuts to listen for, or <see langword="null"/> for none. Safe to call from any thread
    /// while the hook runs. A shortcut press goes to the same handler as the volume keys; the shortcut is always
    /// swallowed, and when the handler declines it the Windows volume is changed instead.
    /// </summary>
    public void SetHotkeys(HotkeyBindings? bindings) => Volatile.Write(ref _bindings, bindings);

    /// <summary>
    /// Replaces the hook with a fresh one. Windows silently removes a low-level hook whose callback once took
    /// too long (e.g. while the system was busy or resuming) and never tells us, so this is done periodically.
    /// </summary>
    public void Reinstall()
    {
        if (_thread is not null)
        {
            PInvoke.PostThreadMessage(_threadId, ReinstallMessage, default, default);
        }
    }

    public void Dispose()
    {
        if (_thread is null)
        {
            return;
        }

        // WM_QUIT ends GetMessage; the thread then unhooks itself.
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _logger.Information("Keyboard hook removed");
    }

    private void Run(ManualResetEventSlim started, Action<Exception> reportError)
    {
        _threadId = PInvoke.GetCurrentThreadId();

        // For low-level hooks the module handle only needs to be any module of this process.
        var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc, PInvoke.GetModuleHandle((PCWSTR)null), 0);
        if (hook.IsNull)
        {
            reportError(new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowsHookEx(WH_KEYBOARD_LL) failed"));
            started.Set();
            return;
        }

        started.Set();
        try
        {
            // Low-level hook callbacks are delivered through this thread's message loop.
            while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0) > 0)
            {
                switch (msg.message)
                {
                    case ReinstallMessage:
                        hook = Rehook(hook);
                        break;
                    case InjectVolumeKeyMessage:
                        TapKey(ToWindowsVolumeKey((VolumeKey)(int)msg.wParam.Value));
                        break;
                    case MaskModifierMessage:
                        TapKey(MaskKey);
                        break;
                }
            }
        }
        finally
        {
            PInvoke.UnhookWindowsHookEx(hook);
        }
    }

    // Runs on the hook thread. No key event can slip between the two calls: hook callbacks are delivered through
    // this same thread's message loop, which is busy here.
    private HHOOK Rehook(HHOOK current)
    {
        var fresh = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc, PInvoke.GetModuleHandle((PCWSTR)null), 0);
        if (fresh.IsNull)
        {
            _logger.Warning("Re-installing the keyboard hook failed ({Error}); keeping the current one", Marshal.GetLastPInvokeError());
            return current;
        }

        PInvoke.UnhookWindowsHookEx(current);
        _logger.Debug("Keyboard hook re-installed");
        return fresh;
    }

    // Runs on the hook thread for every key event system-wide. Must be fast.
    private unsafe LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && _handler is { } handler)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam.Value;
            var message = (uint)wParam.Value;
            var isDown = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
            if (!TryMapKey((VIRTUAL_KEY)info->vkCode, out var key))
            {
                if (HandleHotkey((ushort)info->vkCode, isDown, handler))
                {
                    return (LRESULT)1;
                }
            }
            else
            {
                bool swallow;
                if (isDown)
                {
                    swallow = handler(new VolumeKeyPress(key, IsFineModifierDown()));
                    _swallowedDown[(int)key] = swallow;
                }
                else
                {
                    // Swallow the key-up exactly when its key-down was swallowed, so Windows sees matched pairs.
                    swallow = _swallowedDown[(int)key];
                    _swallowedDown[(int)key] = false;
                }

                if (swallow)
                {
                    return (LRESULT)1;
                }
            }
        }

        return PInvoke.CallNextHookEx(default, code, wParam, lParam);
    }

    // Runs on the hook thread for every key that isn't a media volume key. Returns true to swallow the event.
    // Like the rest of the callback: no locks, no logging, no allocation; injection is left to the message loop.
    private bool HandleHotkey(ushort vk, bool isDown, Func<VolumeKeyPress, bool> handler)
    {
        if (!isDown)
        {
            if (vk is (ushort)VIRTUAL_KEY.VK_LMENU or (ushort)VIRTUAL_KEY.VK_RMENU or (ushort)VIRTUAL_KEY.VK_LWIN or (ushort)VIRTUAL_KEY.VK_RWIN)
            {
                _modifierMasked = false;
            }

            // Swallow the key-up exactly when its key-down was swallowed, even if the modifiers were released first.
            for (var i = 0; i < _hotkeyDownKey.Length; i++)
            {
                if (_hotkeyDownKey[i] == vk)
                {
                    _hotkeyDownKey[i] = 0;
                    return true;
                }
            }

            return false;
        }

        if (Volatile.Read(ref _bindings) is not { } bindings || !bindings.UsesKey(vk))
        {
            return false;
        }

        var held = ReadModifiers(out var altGr);
        if (!HotkeyMatcher.TryMatch(bindings, vk, held, altGr, out var key, out var fine))
        {
            return false;
        }

        _hotkeyDownKey[(int)key] = vk;
        if (!handler(new VolumeKeyPress(key, fine)))
        {
            // No monitor to control: do what the media key would have done.
            PInvoke.PostThreadMessage(_threadId, InjectVolumeKeyMessage, (WPARAM)(nuint)key, default);
        }

        if (!_modifierMasked && (held & (HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0)
        {
            _modifierMasked = true;
            PInvoke.PostThreadMessage(_threadId, MaskModifierMessage, default, default);
        }

        return true;
    }

    // Right Alt counts as AltGr: on layouts that have it, Windows reports AltGr as Ctrl+right Alt and it types
    // characters, so it must never trigger a shortcut. Shortcuts therefore need the left Alt key.
    private static HotkeyModifiers ReadModifiers(out bool altGr)
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VIRTUAL_KEY.VK_CONTROL))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(VIRTUAL_KEY.VK_MENU))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VIRTUAL_KEY.VK_SHIFT))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(VIRTUAL_KEY.VK_LWIN) || IsDown(VIRTUAL_KEY.VK_RWIN))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        altGr = IsDown(VIRTUAL_KEY.VK_RMENU);
        return modifiers;
    }

    // High bit set = key currently down.
    private static bool IsDown(VIRTUAL_KEY vk) => PInvoke.GetAsyncKeyState((int)vk) < 0;

    private static VIRTUAL_KEY ToWindowsVolumeKey(VolumeKey key) => key switch
    {
        VolumeKey.Up => VIRTUAL_KEY.VK_VOLUME_UP,
        VolumeKey.Down => VIRTUAL_KEY.VK_VOLUME_DOWN,
        _ => VIRTUAL_KEY.VK_VOLUME_MUTE,
    };

    // Presses and releases a key. Runs on the hook thread's message loop, never inside the hook callback.
    private void TapKey(VIRTUAL_KEY vk)
    {
        Span<INPUT> inputs = [KeyInput(vk, 0), KeyInput(vk, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP)];
        if (PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            // Blocked, e.g. by UIPI while an elevated window has focus. The shortcut then just does nothing.
            _logger.Debug("SendInput for key {Key:X2} failed ({Error})", (int)vk, Marshal.GetLastPInvokeError());
        }
    }

    private static INPUT KeyInput(VIRTUAL_KEY vk, KEYBD_EVENT_FLAGS flags) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } },
    };

    private static bool TryMapKey(VIRTUAL_KEY vk, out VolumeKey key)
    {
        switch (vk)
        {
            case VIRTUAL_KEY.VK_VOLUME_UP:
                key = VolumeKey.Up;
                return true;
            case VIRTUAL_KEY.VK_VOLUME_DOWN:
                key = VolumeKey.Down;
                return true;
            case VIRTUAL_KEY.VK_VOLUME_MUTE:
                key = VolumeKey.Mute;
                return true;
            default:
                key = default;
                return false;
        }
    }

    // High bit set = key currently down.
    private static bool IsFineModifierDown() =>
        PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0 || PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_MENU) < 0;
}
