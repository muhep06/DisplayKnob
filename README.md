# DisplayHub

> Work in progress. Not ready for use yet.

An open-source display management hub for Windows, living in the system tray.

## Why

If you share a monitor between a Mac and a Windows PC through a KVM, the Mac's volume keys change the **monitor's own volume** over DDC/CI, while Windows only attenuates audio in software. DisplayHub makes the Windows volume keys drive the monitor's hardware volume too, so both machines agree.

## Features (v0.1, planned)

- Media volume keys control monitor volume over DDC/CI (VCP 0x62)
- Mute via VCP 0x8D, with emulation when unsupported
- Fine steps with Shift/Alt, mouse wheel over the tray icon
- On-screen display that never steals focus
- Keys pass through untouched when audio goes elsewhere (e.g. headphones)
- No drivers, no admin rights

See [docs/ROADMAP.md](docs/ROADMAP.md) for the roadmap.

## Requirements

- Windows 10 22H2+ or Windows 11, x64
- A monitor with DDC/CI enabled (usually in the monitor's OSD menu)

## Building

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Known limitations

- When an elevated (admin) window has focus, the volume keys may not be intercepted (UIPI). Planned fix: a signed `uiAccess` build.

## License

[MIT](LICENSE)
