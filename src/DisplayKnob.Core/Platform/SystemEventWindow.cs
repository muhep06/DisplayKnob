using System.Runtime.InteropServices;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DisplayKnob.Core.Platform;

/// <summary>
/// A hidden top-level window on its own message-loop thread that turns Win32 notifications into events:
/// display changes, monitor arrival/removal, resume and display power-on, and raw mouse wheel input.
/// </summary>
/// <remarks>
/// It must be a real top-level window: message-only windows don't receive broadcasts such as
/// <c>WM_DISPLAYCHANGE</c> and <c>WM_POWERBROADCAST</c>. It is never shown.
/// Raw input (with <c>RIDEV_INPUTSINK</c>) observes the wheel without intercepting it, so, unlike a
/// low-level mouse hook, it can never slow down or block the user's mouse.
/// </remarks>
internal sealed unsafe class SystemEventWindow : ISystemEventSource, IMouseWheelSource, IDisposable
{
    private const string ClassName = "DisplayKnob.SystemEvents";

    // GUID_CONSOLE_DISPLAY_STATE data: 0 = off, 1 = on, 2 = dimmed.
    private const uint DisplayOff = 0;
    private const uint DisplayOn = 1;

    private readonly ILogger _logger;
    private readonly WNDPROC _wndProc; // Kept in a field so the GC can't collect the delegate while Windows holds it.
    private readonly Thread _thread;
    private HWND _hwnd;
    private uint? _displayState;

    public SystemEventWindow(ILogger logger)
    {
        _logger = logger.ForContext<SystemEventWindow>();
        _wndProc = WndProc;

        using var started = new ManualResetEventSlim(false);
        _thread = new Thread(() => Run(started))
        {
            Name = "DisplayKnob system events",
            IsBackground = true,
        };
        _thread.Start();
        started.Wait();
    }

    public event EventHandler<SystemEventArgs>? SystemEventOccurred;

    public event EventHandler<MouseWheelEventArgs>? WheelScrolled;

    public void Dispose()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // WM_CLOSE → DestroyWindow → WM_DESTROY → PostQuitMessage ends the loop on the window thread.
        PInvoke.PostMessage(_hwnd, PInvoke.WM_CLOSE, default, default);
        _thread.Join(TimeSpan.FromSeconds(2));
        _hwnd = HWND.Null;
    }

    private void Run(ManualResetEventSlim started)
    {
        var instance = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        HDEVNOTIFY deviceNotification = default;
        HPOWERNOTIFY powerNotification = default;

        fixed (char* className = ClassName)
        {
            try
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(), // Managed struct (holds the delegate), so no sizeof.
                    lpfnWndProc = _wndProc,
                    hInstance = instance,
                    lpszClassName = className,
                };
                if (PInvoke.RegisterClassEx(in windowClass) == 0)
                {
                    _logger.Error("RegisterClassEx failed ({Error}); display changes and the tray wheel won't be detected", Marshal.GetLastPInvokeError());
                    return;
                }

                _hwnd = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                    className,
                    className,
                    WINDOW_STYLE.WS_OVERLAPPED,
                    0,
                    0,
                    0,
                    0,
                    HWND.Null,
                    HMENU.Null,
                    instance,
                    null);
                if (_hwnd.IsNull)
                {
                    _logger.Error("CreateWindowEx failed ({Error}); display changes and the tray wheel won't be detected", Marshal.GetLastPInvokeError());
                    return;
                }

                deviceNotification = RegisterMonitorNotifications();
                powerNotification = RegisterDisplayPowerNotifications();
                RegisterWheelInput();
            }
            finally
            {
                started.Set();
            }

            try
            {
                while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0) > 0)
                {
                    PInvoke.TranslateMessage(in msg);
                    PInvoke.DispatchMessage(in msg);
                }
            }
            finally
            {
                if (!deviceNotification.IsNull)
                {
                    PInvoke.UnregisterDeviceNotification(deviceNotification);
                }

                if (!powerNotification.IsNull)
                {
                    PInvoke.UnregisterPowerSettingNotification(powerNotification);
                }

                PInvoke.UnregisterClass(className, instance);
            }
        }
    }

    private HDEVNOTIFY RegisterMonitorNotifications()
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE_W
        {
            dbcc_size = (uint)sizeof(DEV_BROADCAST_DEVICEINTERFACE_W),
            dbcc_devicetype = (uint)DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = PInvoke.GUID_DEVINTERFACE_MONITOR,
        };
        var handle = PInvoke.RegisterDeviceNotification(new HANDLE(_hwnd.Value), &filter, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (handle.IsNull)
        {
            _logger.Warning("RegisterDeviceNotification failed ({Error}); relying on WM_DISPLAYCHANGE only", Marshal.GetLastPInvokeError());
        }

        return handle;
    }

    // Modern standby often skips the classic resume messages; the display turning back on is the reliable signal.
    private HPOWERNOTIFY RegisterDisplayPowerNotifications()
    {
        var setting = PInvoke.GUID_CONSOLE_DISPLAY_STATE;
        var handle = PInvoke.RegisterPowerSettingNotification(new HANDLE(_hwnd.Value), &setting, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (handle.IsNull)
        {
            _logger.Warning("RegisterPowerSettingNotification failed ({Error}); display power-on won't be detected", Marshal.GetLastPInvokeError());
        }

        return handle;
    }

    private void RegisterWheelInput()
    {
        // Generic desktop page (0x01), mouse usage (0x02). INPUTSINK: deliver even when we're not in the foreground.
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            hwndTarget = _hwnd,
        };
        if (!PInvoke.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
        {
            _logger.Warning("RegisterRawInputDevices failed ({Error}); the tray wheel won't work", Marshal.GetLastPInvokeError());
        }
    }

    // Runs on the window thread. An exception escaping into native code would kill the process.
    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case PInvoke.WM_INPUT:
                    OnRawInput(lParam);
                    break; // DefWindowProc must still run for WM_INPUT to free the input.

                case PInvoke.WM_DISPLAYCHANGE:
                    Raise(SystemEventKind.DisplaysChanged);
                    break;

                case PInvoke.WM_DEVICECHANGE when wParam.Value is PInvoke.DBT_DEVNODES_CHANGED or PInvoke.DBT_DEVICEARRIVAL or PInvoke.DBT_DEVICEREMOVECOMPLETE:
                    Raise(SystemEventKind.DevicesChanged);
                    break;

                case PInvoke.WM_POWERBROADCAST:
                    OnPowerBroadcast(wParam, lParam);
                    return (LRESULT)1; // TRUE: we don't object to anything.

                case PInvoke.WM_CLOSE:
                    PInvoke.DestroyWindow(hwnd);
                    return (LRESULT)0;

                case PInvoke.WM_DESTROY:
                    PInvoke.PostQuitMessage(0);
                    return (LRESULT)0;
            }
        }
        catch (Exception ex)
        {
            // A bug in a subscriber; keep the window (and the app) alive.
            _logger.Error(ex, "Handling window message 0x{Message:X4} failed", message);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnPowerBroadcast(WPARAM wParam, LPARAM lParam)
    {
        if (wParam.Value == PInvoke.PBT_APMRESUMEAUTOMATIC)
        {
            Raise(SystemEventKind.Resumed);
            return;
        }

        if (wParam.Value != PInvoke.PBT_POWERSETTINGCHANGE)
        {
            return;
        }

        var setting = (POWERBROADCAST_SETTING*)lParam.Value;
        if (setting->PowerSetting != PInvoke.GUID_CONSOLE_DISPLAY_STATE || setting->DataLength < sizeof(uint))
        {
            return;
        }

        var state = *(uint*)&setting->Data;
        var previous = _displayState;
        _displayState = state;

        // Registration immediately reports the current state; only an off → on transition counts.
        if (previous == DisplayOff && state == DisplayOn)
        {
            Raise(SystemEventKind.Resumed);
        }
    }

    private void OnRawInput(LPARAM lParam)
    {
        RAWINPUT input;
        var size = (uint)sizeof(RAWINPUT);
        var read = PInvoke.GetRawInputData(new HRAWINPUT(lParam.Value), RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, &input, &size, (uint)sizeof(RAWINPUTHEADER));
        if (read == unchecked((uint)-1) || input.header.dwType != (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEMOUSE)
        {
            return;
        }

        var buttons = input.data.mouse.Anonymous.Anonymous;
        if ((buttons.usButtonFlags & PInvoke.RI_MOUSE_WHEEL) == 0 || WheelScrolled is not { } handler)
        {
            return;
        }

        if (PInvoke.GetCursorPos(out var point))
        {
            handler(this, new MouseWheelEventArgs((short)buttons.usButtonData, point.X, point.Y));
        }
    }

    private void Raise(SystemEventKind kind)
    {
        _logger.Debug("System event: {Kind}", kind);
        SystemEventOccurred?.Invoke(this, new SystemEventArgs(kind));
    }
}
