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
- [x] `IMonitorController` (Get/Set VCP, capabilities string, Id, DisplayName)
- [x] `DxvaMonitorController` over dxva2 (GetPhysicalMonitorsFromHMONITOR, GetVCPFeatureAndVCPFeatureReply, SetVCPFeature, capabilities)
- [x] `MonitorRegistry`: enumerate, build stable IDs (device path + EDID), dispose handles
- [x] Per-monitor DDC worker with coalescing queue + retry
- [x] Tray menu → "Probe monitors": logs each monitor's ID, name, capabilities, current 0x10/0x62/0x8D values

**Manual test:** run the probe with the monitor connected directly and through a KVM; compare the logs.

### M2. Audio endpoint awareness
- [x] `AudioEndpointWatcher`: default render endpoint, changes (IMMNotificationClient)
- [x] Endpoint ↔ monitor auto-match by name; persisted override
- [x] Optional setting: pin the Windows endpoint volume of a mapped monitor endpoint to 100%
- [x] Target follows the default render endpoint (whichever monitor is playing audio gets controlled)

### M3. Keyboard hook → DDC volume
- [x] `KeyboardHookService` on a dedicated thread with a message loop (WH_KEYBOARD_LL)
- [x] Swallow VK_VOLUME_UP/DOWN/MUTE only when the default endpoint maps to a DDC-volume monitor (see [ARCHITECTURE.md](ARCHITECTURE.md), rule 5)
- [x] `HookVolumeSource : IVolumeSource`. Step size setting (default 2), read-before-first-step sync
- [x] Mute via 0x8D with emulation fallback
- [x] Shift/Alt + volume key = fine step (1), mirroring macOS
- [x] Per-monitor usable range (min/max): the 0–100 user scale maps onto the monitor's useful VCP range
- [x] Mouse wheel over the tray icon steps the volume (same pipeline as the keys)

**Manual test:** with HDMI/DP audio as the default device, the keys change the monitor volume. With headphones selected, the keys behave normally. Holding the key down is smooth with no lag.

### M4. OSD and tray feedback
- [x] Borderless, topmost, no-activate, click-through WinUI window, bottom-center of the target monitor
- [x] Shows icon + level bar + monitor name, fades after ~1.5 s, respects light/dark theme
- [x] Never steals focus (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW)
- [x] Tray icon reflects the current level and mute state

### M5. Robustness
- [x] Hotplug / KVM switch / sleep-resume re-enumeration
- [x] Pick up external volume changes (another machine, monitor buttons): re-read 0x62 after KVM switch / resume; optional light polling
- [x] Hook health check (re-installed after resume and every 10 minutes; Windows offers no way to ask)
- [x] Graceful degradation when DDC fails (fall back to pass-through, log once, tray badge)

### M6. Settings + tray UX
- [x] Settings window: monitors list, per-monitor "DDC volume" toggle, endpoint mapping, step size, volume polling interval, "Windows volume controls the monitor" (mirror, off by default), start with Windows (HKCU Run)
- [x] Tray flyout: per-monitor volume + brightness sliders

### M7. Release
- [x] Velopack packaging, GitHub Release workflow, auto-update check
- [x] README: features, the KVM/Mac motivation, the elevated-window limitation, DDC/CI troubleshooting
- [ ] winget manifest (later)

## v0.2 — Volume polish
Ships together with v0.3; it is not released on its own.

- [x] Custom hotkeys (Ctrl+Alt+↑/↓/M, Ctrl+Alt+Win, or recorded) for keyboards without media keys
- [x] Volume curve (linear / logarithmic feel)
- [x] Volume cap (max level) per monitor
- [x] Volume group: choose the monitors that step together (all to the same level), with a shortcut (Ctrl+Alt+G) to turn it on and off and a choice of one OSD or one per monitor
- Mirror the Windows volume UI to DDC: implemented behind `mirrorWindowsVolume` (off by default; the Windows UI then shows the 98% rest level). Needs a settings toggle (M6)

## v0.3 — Monitor groups
Replaces the single volume group with any number of named groups. A group is a set of monitors plus the features its members share. A monitor can be in several groups and several groups can be active at once, but for each monitor and feature at most one enabled group applies, so groups never fight over a value (the first listed wins; the settings window disables the conflicting checkbox and says why).

### M8. Groups, volume and mute
- [ ] Settings model: `MonitorGroup` (name, enabled, members, linked features), `LinkedFeature` flags (volume, mute, brightness, contrast, input source), conflict resolver
- [ ] Migrate the old single volume group (`VolumeGroupEnabled`, `InVolumeGroup`) to "Group 1"
- [ ] `VolumeController` follows the groups: keys move the target's peers for volume and mute
- [ ] Settings window: add, remove and rename groups, pick members and shared features
- [ ] OSD shows the group name; per-group on/off in the settings and the tray flyout (decide what Ctrl+Alt+G does with several groups)

### M9. Brightness and contrast
- [ ] Brightness and contrast hotkeys (e.g. Ctrl+Alt+PgUp/PgDn for brightness)
- [ ] Tray sliders and hotkeys move the group's peers on their own scales

### M10. Input source
- [ ] Input source switching (VCP 0x60). Source numbers differ per monitor (HDMI 1 on one, DP on another), so sharing it needs a per-monitor mapping; decide before building

## Later
- Per-monitor profiles
- Resolution / refresh / HDR presets (QueryDisplayConfig / SetDisplayConfig, advanced color)
- Virtual displays: manage the VirtualDrivers VDD (MIT, signed)
- PiP / screen mirror window (Windows.Graphics.Capture)
- Software dimming below the DDC minimum (gamma/overlay)
- Code signing (SignPath Foundation, free for open source) so SmartScreen stops warning about the installer
- Elevated (admin) windows: the volume keys don't reach us while one has focus (UIPI). Accepted for now. Try a `RegisterHotKey` fallback first; the real fix (`uiAccess`) needs a signed build in Program Files, so an optional separate package
- Optional "native mode" virtual audio driver behind `IVolumeSource` (needs signing)
