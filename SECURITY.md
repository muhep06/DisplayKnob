# Security policy

## Supported versions

Security fixes go into the latest release. Please update before reporting, in case the problem is already fixed.

## Reporting a vulnerability

**Please don't open a public issue for a security problem.** Use GitHub's private reporting instead:

1. Go to the repository's **Security** tab.
2. Choose **Report a vulnerability**.
3. Describe the problem, how to reproduce it, and which version you tested.

You'll get an answer in that private thread. DisplayKnob is maintained by volunteers, so please allow some time. We'll tell you when a fix is released and, if you like, credit you for the report.

## What DisplayKnob does, so you can judge a report

DisplayKnob installs a system-wide low-level keyboard hook, which is why it deserves a careful look:

- **The hook sees every key event, but only acts on the volume keys and the shortcuts you configured.** It doesn't record, log or send any other key.
- **It runs as your user, without administrator rights**, and installs per user.
- **Network access is limited to checking GitHub Releases for updates** (and downloading an update when you choose to install it). There is no telemetry and no account.
- **Settings and logs stay on your computer** (`%AppData%\DisplayKnob` and `%LocalAppData%\DisplayKnob\logs`).
- **It talks to monitors over DDC/CI** (volume, mute, brightness), and to Windows' audio endpoints.

## Known limitations

- The installer is not code-signed yet, so Windows SmartScreen may warn when you run it. Code signing is on the [roadmap](docs/ROADMAP.md).
- Windows doesn't deliver keys from elevated (administrator) windows to normal programs, so the volume keys may not work while one has focus. A signed `uiAccess` build is planned.
