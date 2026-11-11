# Troubleshooting

## The volume keys don't change the monitor

1. **Enable DDC/CI in the monitor's on-screen menu.** It is often under "System", "Setup" or "Other", and some monitors ship with it off.
2. **Make the monitor the Windows audio output** (HDMI or DisplayPort audio). DisplayKnob controls the monitor that is playing the sound; with headphones or speakers selected, the keys behave normally.
3. **Check the monitor's switch in Settings.** Under *Monitors*, "Control volume" must be on for that monitor, and *Audio outputs* shows which monitor each output plays through.
4. **Run "Probe monitors"** from the tray menu, then open the log folder. The log lists each monitor with its capabilities and its current brightness, volume and mute values. If a monitor isn't listed, it doesn't answer DDC/CI.

## The tray icon is orange with an exclamation mark

The monitor stopped answering DDC. The volume keys control Windows until it answers again, and DisplayKnob checks again on its own and on the next key press. If it keeps happening, try another cable or port, and avoid dock or adapter chains in between.

## It works directly, but not through my KVM

Switch the KVM to this PC and wait a few seconds. Some KVMs and docks don't pass DDC/CI at all. Connecting the monitor directly tells you whether the KVM is the cause. DisplayKnob re-reads the monitor after a KVM switch, so changes made from the other computer show up.

## No monitor is found

Some docks, adapters and DisplayLink-style outputs don't support DDC/CI. Check that DDC/CI is on in the monitor's menu, then use "Probe monitors".

## The volume jumps by a different amount than expected

- Change the *Step size* in Settings. Hold Shift or Alt with a volume key for 1%.
- Many monitors only use part of the 0–100 range. The per-monitor *Maximum volume* squeezes the scale below a limit, and *Volume curve* "Logarithmic" takes finer steps at low volume. Try it if the lowest levels are too loud or the top ones do nothing.
- If another computer or the monitor's own buttons left the volume above your limit, the next volume key press brings it back down to the limit.

## Brightness or volume stops short of the value I dragged to

Some monitors silently ignore a DDC command that comes too soon after the previous one. DisplayKnob keeps a short pause between commands, reads the value back once you stop, and if the last write was lost it sends it again and lengthens the pause for that monitor. What it learned is saved per monitor as `commandSpacingMs` in `settings.json` (delete the entry to learn again). If it still happens, attach the log: it lists every DDC command with its duration.

## The keyboard shortcuts don't work

- Use the **left** Alt key. The right one is AltGr on many keyboards and never triggers a shortcut, so it can't swallow the characters you type with it.
- Settings shows a warning when another application also registered a shortcut. DisplayKnob receives the keys first, so that application's shortcut stops working while DisplayKnob runs. Pick another preset or record your own.
- Without a DDC monitor as the audio output, the shortcuts change the Windows volume instead.

## The volume keys don't work while an administrator window has focus

Windows doesn't deliver keys from elevated windows to normal programs (UIPI). It is a known limitation for now: click a normal window first. Fixing it properly needs a signed build installed under Program Files (see the [roadmap](ROADMAP.md)).

## Start with Windows

Turn it on in Settings, or click the notification DisplayKnob shows the first time it runs. It appears under *Task Manager → Startup apps*. If you disable it there, Settings shows it as off, and switching it on in Settings enables it again.

## Windows SmartScreen warns about the installer

The installer isn't code-signed yet, so SmartScreen may warn you. Choose *More info*, then *Run anyway*. Code signing is on the [roadmap](ROADMAP.md).

## Where are the files?

| What | Where |
|---|---|
| Log | `%LocalAppData%\DisplayKnob\logs` (tray menu: *Open log folder*) |
| Settings | `%AppData%\DisplayKnob\settings.json` |
| Installed app | `%LocalAppData%\DisplayKnob\current` |

Uninstalling removes the app, its log folder and the "start with Windows" entry. Your settings file is kept.

## Reporting a bug

Open an issue and include:

- Your monitor model(s) and how they are connected (direct, KVM, dock).
- The log from the *Probe monitors* run, with the DisplayKnob version (tray menu or Settings).
- What you pressed and what happened instead.

Real DDC/CI behavior varies a lot between monitors, so the probe log is the most useful thing you can attach.
