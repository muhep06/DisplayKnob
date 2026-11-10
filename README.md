# DisplayKnob

> Work in progress. Not ready for use yet.

An open-source display management hub for Windows, living in the system tray.

## Why

If you share a monitor between a Mac and a Windows PC through a KVM, the Mac's volume keys change the **monitor's own volume** over DDC/CI, while Windows only attenuates audio in software. DisplayKnob makes the Windows volume keys drive the monitor's hardware volume too, so both machines agree.

## Features

- Media volume keys control monitor volume over DDC/CI (VCP 0x62)
- Mute via VCP 0x8D, with emulation when the monitor doesn't support it
- Fine steps (1%) with Shift or Alt; mouse wheel over the tray icon
- On-screen display that never steals focus
- Follows the default audio output: the keys control whichever monitor is playing audio, and pass through untouched when audio goes elsewhere (e.g. headphones)
- Tray flyout with volume and brightness sliders per monitor
- Settings window: per-monitor volume control, audio output to monitor mapping, step size, polling for changes made on the monitor or another computer, start with Windows
- Survives hotplug, KVM switches and sleep/resume
- Updates itself from GitHub Releases
- No drivers, no admin rights

See [docs/ROADMAP.md](docs/ROADMAP.md) for what is next.

## Requirements

- Windows 10 22H2+ or Windows 11, x64
- A monitor with DDC/CI enabled (see below)
- The monitor must be the Windows audio output (HDMI or DisplayPort audio) for the volume keys to control it

## Install

Download `DisplayKnob-win-Setup.exe` from the [latest release](../../releases/latest) and run it. It installs for the current user and needs no admin rights. DisplayKnob then lives in the notification area; click its icon for the sliders, right-click for the menu.

The installer is not code-signed yet, so Windows SmartScreen may warn you. Choose "More info" then "Run anyway".

## Building

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
```

Run the app from `src/DisplayKnob.App`. Logs go to `%LocalAppData%\DisplayKnob\logs`.

## Troubleshooting DDC/CI

- **Enable DDC/CI in the monitor's on-screen menu.** It is often under "System", "Setup" or "Other", and some monitors ship with it off.
- **Use the tray menu's "Probe monitors"**, then open the log folder. The log lists each monitor with its capabilities and its current brightness, volume and mute values.
- **The tray icon shows `!`:** the monitor stopped answering DDC. DisplayKnob falls back to normal Windows volume until it answers again.
- **Nothing happens on a KVM:** switch the KVM to this PC and wait a few seconds. Some KVMs and docks don't pass DDC/CI at all; connecting the monitor directly tells you whether the KVM is the cause.
- **No monitor found:** some docks, adapters and DisplayLink-style outputs don't support DDC/CI.
- **Volume jumps by a different amount than expected:** change the step size in Settings. Many monitors only use part of the 0-100 range.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Known limitations

- When an elevated (admin) window has focus, the volume keys may not be intercepted (UIPI). Planned fix: a signed `uiAccess` build.
- Real DDC/CI behavior varies by monitor. If yours misbehaves, open an issue with the probe log.

## License

[MIT](LICENSE)
