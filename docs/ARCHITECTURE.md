# Architecture

## What DisplayKnob is

An open-source display management hub for Windows, shipped as a tray app.

The first feature (v0.1): media volume keys control the **monitor's hardware volume over DDC/CI** (VCP 0x62) instead of Windows' software volume. The motivating case is a KVM shared between a Mac and a Windows PC. On the Mac, the volume keys already drive the monitor's DDC volume. On Windows, audio is attenuated in software, so the monitor stays at whatever level the Mac left it.

Later versions add brightness and input switching, display presets, virtual displays, and PiP. See [ROADMAP.md](ROADMAP.md).

## Constraints

- **No custom kernel drivers in v0.x.** A virtual audio driver was rejected: it needs Microsoft attestation signing, users can't be asked to enable test-signing mode, and anti-cheat software blocks test mode. Volume keys are intercepted with a low-level keyboard hook behind the `IVolumeSource` abstraction, so a driver-based source can be added later.
- Windows 10 22H2+ / Windows 11, x64 (ARM64 nice-to-have).
- Runs **without admin rights**.
- MIT license.

## Tech stack

| Area | Choice |
|---|---|
| Runtime | .NET 10 (LTS), C# latest, nullable enabled |
| UI | WinUI 3 (Windows App SDK), unpackaged, self-contained |
| MVVM | CommunityToolkit.Mvvm (App only) |
| DI | Microsoft.Extensions.DependencyInjection, composed at App startup |
| Tray | H.NotifyIcon.WinUI |
| Win32 interop | Microsoft.Windows.CsWin32 (`NativeMethods.txt`) |
| Core Audio | NAudio (MMDeviceEnumerator, AudioEndpointVolume, IMMNotificationClient) |
| Logging | Serilog → rolling file in `%LocalAppData%\DisplayKnob\logs` |
| Settings | JSON in `%AppData%\DisplayKnob\settings.json` (System.Text.Json source generation) |
| Installer / updates | Velopack + GitHub Releases |
| Tests | xUnit v3 (Microsoft Testing Platform) + fakes |

## Solution layout

```
DisplayKnob.slnx
global.json               # pins the .NET SDK version (local + CI)
Directory.Build.props     # shared build settings
Directory.Packages.props  # central package management: all versions live here
.github/workflows/        # CI
src/
  DisplayKnob.Core/        # no UI references. All hardware/OS logic lives here.
    Ddc/                  # IMonitorController, DxvaMonitorController, VcpCodes, per-monitor DDC worker
    Displays/             # monitor enumeration, stable IDs, hotplug watcher
    Audio/                # AudioEndpointWatcher, endpoint ↔ monitor mapping
    Input/                # KeyboardHookService (dedicated thread + message loop)
    Volume/               # IVolumeSource, HookVolumeSource, VolumeController (target selection, swallow decision, stepping, mute)
    Settings/
  DisplayKnob.App/         # WinUI 3, MVVM
    Tray/                 # tray icon, menu, flyout
    Osd/                  # OSD window
    Views/                # settings window and pages
    ViewModels/
    Assets/
tests/
  DisplayKnob.Core.Tests/
    Fakes/                # FakeMonitorController etc.
docs/
```

`DisplayKnob.Core` stays free of WinUI types so it is testable and could back a CLI later.

## Engineering rules (the known traps)

1. **Hook callbacks must return immediately.** Windows silently removes a low-level hook whose callback exceeds `LowLevelHooksTimeout`. The callback only decides whether to swallow or pass the key and enqueues an event. It never touches DDC, never logs synchronously, and never allocates heavily. Keyboard shortcuts (custom hotkeys) follow the same rule: the callback only reads the modifier state, matches, and enqueues. Anything that sends input (the Windows volume fallback, the Alt/Win "mask" tap that keeps the Start menu from opening) is posted to the hook thread's message loop and runs after the callback has returned.
2. **DDC is slow (~50 ms per SetVCPFeature) and flaky.** All DDC I/O runs on one dedicated worker per physical monitor. Rapid key repeats are coalesced so only the latest target value gets written. Retry with backoff (2–3 attempts) and treat failures as normal.
3. **Physical monitor handles are not stable.** Re-enumerate on `WM_DISPLAYCHANGE`, device-arrival notifications, and resume from sleep. A KVM switch can invalidate handles. Always `DestroyPhysicalMonitors` old handles.
4. **Stable monitor identity** = monitor device path from `DisplayConfigGetDeviceInfo` (DISPLAYCONFIG_TARGET_DEVICE_NAME) plus EDID manufacturer/product/serial. Never the enumeration index.
5. **Only swallow volume keys when it makes sense:** the default render endpoint is mapped to a DDC-capable monitor that has "DDC volume" enabled in settings. Otherwise the key passes through untouched.
6. **Audio endpoint ↔ monitor mapping:** auto-match the endpoint FriendlyName against the monitor's EDID name; the user can override it in settings.
7. On first use after a (re)connect, **read** the current VCP 0x62 value with GetVCPFeatureAndVCPFeatureReply before stepping, because another machine (e.g. the Mac) may have changed it.
8. **Mute:** try VCP 0x8D. If the monitor doesn't support it, emulate mute by remembering the volume and setting 0. Mute is independent of volume (0x62 keeps its value while muted). MCCS says 1 = muted, 2 = unmuted, but some monitors report **0** for unmuted (seen on Samsung S34C65xU), so treat any value other than 1 as unmuted, and verify a write by reading it back.
9. **Known limitation (accepted for now):** when an elevated window has focus, the hook may not receive keys (UIPI). The real fix, `uiAccess`, needs a signed build installed under Program Files, which the per-user Velopack install can't provide, so it would be a separate optional package. A `RegisterHotKey` fallback is an untested idea.

## References

- [emoacht/Monitorian](https://github.com/emoacht/Monitorian) (C#, MIT): DDC/CI and monitor enumeration on Windows.
- [MonitorControl](https://github.com/MonitorControl/MonitorControl) (macOS): UX behavior for keyboard-driven monitor control.
