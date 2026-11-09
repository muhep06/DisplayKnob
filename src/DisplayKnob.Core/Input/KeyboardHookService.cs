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
    private readonly ILogger _logger;
    private readonly HOOKPROC _hookProc; // Kept in a field so the GC can't collect the delegate while Windows holds it.
    private readonly bool[] _swallowedDown = new bool[3]; // Indexed by VolumeKey; only touched on the hook thread.
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
            while (PInvoke.GetMessage(out _, HWND.Null, 0, 0) > 0)
            {
            }
        }
        finally
        {
            PInvoke.UnhookWindowsHookEx(hook);
        }
    }

    // Runs on the hook thread for every key event system-wide. Must be fast.
    private unsafe LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && _handler is { } handler)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam.Value;
            if (TryMapKey((VIRTUAL_KEY)info->vkCode, out var key))
            {
                var message = (uint)wParam.Value;
                var isDown = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
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
