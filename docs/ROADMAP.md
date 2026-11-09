# Roadmap

## v0.1 — "Volume keys control the monitor"

### M0. Scaffold
- [x] `DisplayKnob.slnx` with Core, App (WinUI 3 unpackaged, self-contained), Core.Tests
- [x] `Directory.Build.props`, central package management, `global.json`
- [x] CsWin32 + `NativeMethods.txt`; NAudio; Serilog; H.NotifyIcon.WinUI
- [x] GitHub Actions: build + test on windows-latest
- [x] README, LICENSE (MIT), CONTRIBUTING, `.editorconfig`

**Done when:** `dotnet build` and `dotnet test` pass and the app starts with only a tray icon (no main window).

### M1. DDC layer + probe
- [ ] `IMonitorController` (Get/Set VCP, capabilities string, Id, DisplayName)
- [ ] `DxvaMonitorController` over dxva2 (GetPhysicalMonitorsFromHMONITOR, GetVCPFeatureAndVCPFeatureReply, SetVCPFeature, capabilities)
- [ ] `MonitorRegistry`: enumerate, build stable IDs (device path + EDID), dispose handles
- [ ] Per-monitor DDC worker with coalescing queue + retry
- [ ] Tray menu → "Probe monitors": logs each monitor's ID, name, capabilities, current 0x10/0x62/0x8D values

**Manual test:** run the probe with the monitor connected directly and through a KVM; compare the logs.

### M2. Audio endpoint awareness
- [ ] `AudioEndpointWatcher`: default render endpoint, changes (IMMNotificationClient)
- [ ] Endpoint ↔ monitor auto-match by name; persisted override
- [ ] Optional setting: pin the Windows endpoint volume of a mapped monitor endpoint to 100%
- [ ] Target follows the default render endpoint (whichever monitor is playing audio gets controlled)

### M3. Keyboard hook → DDC volume
- [ ] `KeyboardHookService` on a dedicated thread with a message loop (WH_KEYBOARD_LL)
- [ ] Swallow VK_VOLUME_UP/DOWN/MUTE only when the default endpoint maps to a DDC-volume monitor (see [ARCHITECTURE.md](ARCHITECTURE.md), rule 5)
- [ ] `HookVolumeSource : IVolumeSource`. Step size setting (default 2), read-before-first-step sync
- [ ] Mute via 0x8D with emulation fallback
- [ ] Shift/Alt + volume key = fine step (1), mirroring macOS
- [ ] Per-monitor usable range (min/max): the 0–100 user scale maps onto the monitor's useful VCP range
- [ ] Mouse wheel over the tray icon steps the volume (same pipeline as the keys)

**Manual test:** with HDMI/DP audio as the default device, the keys change the monitor volume. With headphones selected, the keys behave normally. Holding the key down is smooth with no lag.

### M4. OSD and tray feedback
- [ ] Borderless, topmost, no-activate, click-through WinUI window, bottom-center of the target monitor
- [ ] Shows icon + level bar + monitor name, fades after ~1.5 s, respects light/dark theme
- [ ] Never steals focus (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW)
- [ ] Tray icon reflects the current level and mute state

### M5. Robustness
- [ ] Hotplug / KVM switch / sleep-resume re-enumeration
- [ ] Pick up external volume changes (another machine, monitor buttons): re-read 0x62 after KVM switch / resume; optional light polling
- [ ] Hook health check (re-install if removed)
- [ ] Graceful degradation when DDC fails (fall back to pass-through, log once, tray badge)

### M6. Settings + tray UX
- [ ] Settings window: monitors list, per-monitor "DDC volume" toggle, endpoint mapping, step size, start with Windows (HKCU Run)
- [ ] Tray flyout: per-monitor volume + brightness sliders

### M7. Release
- [ ] Velopack packaging, GitHub Release workflow, auto-update check
- [ ] README: features, the KVM/Mac motivation, the elevated-window limitation, DDC/CI troubleshooting
- [ ] winget manifest (later)

## v0.2 — Volume polish
- Custom hotkeys (e.g. Ctrl+Alt+↑/↓) for keyboards without media keys
- Volume curve (linear / logarithmic feel)
- Volume cap (max level) per monitor
- "All monitors" mode: step every DDC-volume monitor together
- Experimental: mirror the Windows volume slider to DDC while pinning the endpoint at 100% (watch for feedback loops and flyout flicker)

## v0.3+
- Brightness/contrast hotkeys, input source switching (VCP 0x60), per-monitor profiles
- Resolution / refresh / HDR presets (QueryDisplayConfig / SetDisplayConfig, advanced color)
- Virtual displays: manage the VirtualDrivers VDD (MIT, signed)
- PiP / screen mirror window (Windows.Graphics.Capture)
- Software dimming below the DDC minimum (gamma/overlay)
- uiAccess build, code signing (SignPath Foundation)
- Optional "native mode" virtual audio driver behind `IVolumeSource` (needs signing)
