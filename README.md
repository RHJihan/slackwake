<div align="center">

# SlackWake

**A Windows tray app that throws a fullscreen, multi-monitor alert when Slack pings you — but only while you're away from your desk.**

When you're at the keyboard, Slack's own notifications do the job and SlackWake stays out of the way. When you've stepped away, the next Slack message lights up every monitor with an unmissable overlay (optionally with sound and a strobing flash) so you don't miss the things you walked away from.

[Features](#features) · [Architecture](#architecture) · [Getting started](#getting-started) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting)

</div>

---

## Table of contents

- [Overview](#overview)
- [Features](#features)
- [Screenshots](#screenshots)
- [How it works](#how-it-works)
- [Architecture](#architecture)
- [Technology stack](#technology-stack)
- [Getting started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Build and run](#build-and-run)
  - [Slack & Windows setup](#slack--windows-setup)
- [Configuration](#configuration)
  - [Settings file](#settings-file)
  - [Setting reference](#setting-reference)
- [Usage](#usage)
- [Development](#development)
- [Build and deployment](#build-and-deployment)
- [Project structure](#project-structure)
- [Security and privacy](#security-and-privacy)
- [Troubleshooting](#troubleshooting)
- [Assumptions, limitations, and known issues](#assumptions-limitations-and-known-issues)
- [Contributing](#contributing)
- [License](#license)

---

## Overview

SlackWake solves a narrow but real problem: **you stepped away from your computer, and a Slack message arrived that you needed to see.** Slack's normal notifications are easy to miss from across the room — a small toast in the corner, maybe a sound you weren't listening for.

SlackWake watches the system idle timer. Once you've been away longer than a configurable timeout, it *arms* itself. The next Slack notification then triggers a fullscreen overlay on **every connected monitor**, optionally accompanied by a looping sound and a color strobe. Dismiss it with any key, a click, or `Esc`, and SlackWake disarms again the moment you start using the computer.

Crucially, **SlackWake never talks to Slack's servers.** It does not use the Slack API, require a token, or need a custom Slack app or workspace-admin approval. Instead it subscribes to the same Windows notification stream you already see in Action Center (via the `UserNotificationListener` WinRT API) and filters it down to notifications whose source app is Slack. If Slack chose to toast it, SlackWake can react to it — and nothing else.

---

## Features

- **Idle-gated alerts** — Overlays only fire after you've been away from the keyboard/mouse for a configurable idle timeout (default 5 minutes). While you're active, SlackWake stays silent and lets Slack's own notifications handle things.
- **Fullscreen multi-monitor overlay** — A topmost, fullscreen alert appears on every connected display simultaneously, with the message's channel/sender and body text. Correctly positioned per-monitor even across mixed-DPI setups.
- **Dismiss any way you like** — `Esc`, a mouse click, or any keystroke closes the overlay on all monitors at once.
- **Sound alerts** — Optionally play a sound when the overlay appears. Choose the built-in system sound or **any `.wav` from `C:\Windows\Media`**, preview it inline, and even **preview-on-hover** while browsing the dropdown (like the iOS/Slack/Discord sound pickers).
- **Looping sound** — Keep the alert sound replaying until you dismiss the overlay (with a short gap between plays).
- **Configurable alert delay** — Wait a few seconds after the overlay appears before sound and flash kick in (default 5s), so a glance is enough to dismiss it without the full sensory assault.
- **Visual flash** — Optionally strobe the overlay between two configurable colors to make it impossible to ignore from across the room. Overlay text automatically picks black or white per the [WCAG](https://www.w3.org/TR/WCAG21/) contrast formula so it stays legible against whatever colors you choose. Off by default; flash speed is bounded to stay clear of photosensitive-seizure territory.
- **Auto-stop with per-channel control** — A *maximum duration* time-boxes continuous alerts so they self-stop even if the overlay is never dismissed. Toggle independently whether the cap ends the **looping sound**, the **visual flash**, or both — leave one on to keep, say, a silent flash going while the noise cuts out.
- **Keyword muting** — Silently drop pings whose sender, channel, or text matches your keyword list (case-insensitive substring). Comma- or newline-separated; wrap a phrase in double quotes to match it verbatim. Useful for muting noisy bots, channels, or topics while away.
- **System-tray native** — Lives in the notification area with an at-a-glance status icon: green = armed, white = idle/paused, slashed = disabled. Left-click opens settings; right-click for the menu.
- **Start with Windows** — Optional per-user auto-launch at sign-in (silent, into the tray).
- **Test overlay** — A one-click button to preview your overlay with current sound/flash settings, bypassing the idle gate.
- **Fluent design** — Native Windows 11 Fluent 2 look with Mica backdrop, light/dark theme that tracks your OS setting live, and rounded corners.
- **Zero cloud, zero account** — No Slack token, no API calls, no telemetry. Settings stay on your machine.

---

## How it works

1. **`IdleMonitorService`** polls the Win32 `GetLastInputInfo` API once per second to determine how long it's been since any keyboard/mouse input system-wide. When that exceeds your idle timeout, SlackWake becomes *active (armed)*.
2. **`SlackMonitorService`** requests notification-listener access once, then polls `UserNotificationListener.GetNotificationsAsync()` every ~1.5 seconds. New toasts whose source app matches "Slack" are parsed into a `SlackEvent` (sender/channel + body).
3. **`MainViewModel`** is the gate. A Slack event only becomes an overlay if monitoring is enabled **and** you're currently idle **and** the message doesn't match a mute keyword. The gate is evaluated on the UI thread to avoid races with a concurrent disable.
4. **`App.ShowOverlay`** opens one fullscreen `OverlayWindow` per monitor, plays the sound on the primary overlay only, and strobes the flash on all of them. Closing any overlay tears down the whole set.

> **Why polling, not the `NotificationChanged` event?** For unpackaged Win32/WPF apps the event-based path reports access as `Allowed` but frequently never fires. `GetNotificationsAsync` reads the same data and works reliably; the ~1.5s latency is invisible for an "I walked away" alert.

---

**Design notes:**

- **MVVM, no framework.** A tiny hand-rolled `ObservableObject` + `RelayCommand` keeps view-models testable without pulling in a MVVM library.
- **No DI container.** `App.OnStartup` is the composition root — for an app this small, manual wiring is clearer than a container.
- **Threading.** Idle ticks arrive on the WPF dispatcher (`DispatcherTimer`). Slack events arrive on thread-pool threads and are marshaled to the UI thread *before* the enabled/idle gate is checked, so a disable that lands mid-poll can't leak an overlay.
- **Settings are synchronous and best-effort.** Each property change writes the JSON file immediately (the file is tiny). Read/write failures fall back to defaults rather than crashing — settings are advisory, not load-bearing.
- **Multi-monitor & DPI.** Each overlay is `Show()`n first (so its HWND exists), then moved with Win32 `MoveWindow` in *physical pixels*, sidestepping WPF's DIP scaling so windows land exactly on the right monitor even with mixed DPI.

---

## Technology stack

| Layer | Choice |
| --- | --- |
| Language / runtime | C# 12, .NET 8 (`net8.0-windows10.0.19041.0`) |
| UI framework | WPF (with WinForms interop for the tray icon, color dialog, and screen enumeration) |
| Design system | [WPF-UI](https://github.com/lepoco/wpfui) `4.3.0` — Fluent 2 / WinUI styling (Mica, `FluentWindow`, `ToggleSwitch`, `Card`, `InfoBar`, Fluent System Icons) |
| Pattern | MVVM (no third-party MVVM library) |
| Slack integration | `Windows.UI.Notifications.Management.UserNotificationListener` (WinRT) — no Slack API |
| Persistence | `System.Text.Json` → per-user JSON file |
| Native interop | Win32 P/Invoke (`GetLastInputInfo`, `MoveWindow`, `SetForegroundWindow`, `DestroyIcon`), GDI+ for in-memory tray icons, HKCU registry for startup |
| Build | .NET SDK / MSBuild (`dotnet` CLI or Visual Studio) |

The **only** NuGet dependency is `WPF-UI`, and it is purely presentational — no app logic depends on it.

---

## Getting started

### Prerequisites

- **Windows 10 version 1903 (build 18362) or newer**, or Windows 11. The `UserNotificationListener` API SlackWake relies on is unavailable on older builds. _(The project's `SupportedOSPlatformVersion` is 1809, but notification listening specifically needs 1903+.)_
- **[.NET 8 SDK](https://dotnet.microsoft.com/download)** to build, or just the **.NET 8 Desktop Runtime** to run a framework-dependent build.
- The **Slack desktop client**, signed in, with OS notifications enabled (see [Slack & Windows setup](#slack--windows-setup)).

### Build and run

From the repository root:

```powershell
# Restore + build
dotnet build .\SlackWake.sln -c Release

# Run
dotnet run --project .\SlackWake\SlackWake.csproj
```

Or open `SlackWake.sln` in **Visual Studio 2022 (17.0+)** and press <kbd>F5</kbd>.

On first launch the settings window appears and SlackWake adds a tray icon. The first time monitoring is enabled, Windows prompts you to grant notification-listener access.

### Slack & Windows setup

SlackWake doesn't register anything on Slack's side — there's no app to create and no token to manage. You only need to make sure the toasts actually reach Windows:

1. **Run the Slack desktop client** and sign into your workspace(s).
2. **Let Slack use Windows notifications.** Slack → *Preferences* → *Notifications* → set the notification display style to **"Use Windows notifications"** rather than Slack's in-app banner. (Recent Slack versions default to this.)
3. **Allow Windows to deliver toasts.** Windows *Settings* → *System* → *Notifications* → ensure notifications are on and Slack isn't blocked.
4. **Grant SlackWake notification access.** Enabling monitoring calls `UserNotificationListener.RequestAccessAsync()`, which prompts you. If you deny it by accident, re-enable under *Settings* → *Privacy &amp; security* → *Notifications* → *"Let apps access your notifications."*

> **DND wins.** SlackWake only sees what Slack chooses to toast. If Slack is muted, in Do-Not-Disturb mode, or a channel is set to "Nothing," there's nothing to listen for — which is usually exactly what you want.

---

## Configuration

All configuration is done through the settings window — there are no command-line flags except the internal `--minimized` hint added to the auto-start entry. There are **no environment variables**; everything persists to a single JSON file.

### Settings file

```
%AppData%\SlackWake\settings.json
```

A diagnostic log is written alongside it at `%AppData%\SlackWake\debug.log` (append-only, capped at ~256 KB). Example settings file with all keys at their defaults:

```json
{
  "Enabled": true,
  "IdleTimeoutSeconds": 300,
  "StartWithWindows": false,
  "StartMinimized": false,
  "SoundEnabled": true,
  "SoundDelaySeconds": 5,
  "SoundLoop": false,
  "SoundFilePath": "",
  "FlashEnabled": false,
  "FlashIntervalMs": 500,
  "FlashColorA": "#000000",
  "FlashColorB": "#FFFFFF",
  "AlertAutoStopEnabled": false,
  "AlertMaxDurationSeconds": 60,
  "AlertAutoStopIncludesSound": true,
  "AlertAutoStopIncludesVisual": true,
  "KeywordFilterEnabled": false,
  "KeywordFilterText": ""
}
```

Hand-editing the file while the app is running is safe — the next save from the UI overwrites it. A corrupt or unreadable file resets to defaults rather than crashing.

### Setting reference

| Key | Type | Default | Range / notes |
| --- | --- | --- | --- |
| `Enabled` | bool | `true` | Master switch. When off, nothing is monitored. |
| `IdleTimeoutSeconds` | int | `300` | Idle time before alerts arm. Clamped to **10–7200s** (slider exposes 10–1800). |
| `StartWithWindows` | bool | `false` | Adds/removes a per-user `HKCU\...\Run` entry (launches silently into the tray). |
| `StartMinimized` | bool | `false` | Skip showing the settings window on launch. |
| `SoundEnabled` | bool | `true` | Play a sound when the overlay appears. |
| `SoundDelaySeconds` | int | `5` | Delay after the overlay appears before sound **and** flash start. Clamped **0–120s** (0 = immediate). |
| `SoundLoop` | bool | `false` | Replay the sound until dismissed (with a 0.5s gap between plays). |
| `SoundFilePath` | string | `""` | Path to a `.wav`. Empty = system *Exclamation* sound. UI lists `C:\Windows\Media`, but any readable `.wav` path works. |
| `FlashEnabled` | bool | `false` | Strobe the overlay between two colors. |
| `FlashIntervalMs` | int | `500` | Half-cycle (one color→other) in ms. Clamped **100–2000ms** — the floor keeps flashing below seizure-risk frequency. |
| `FlashColorA` | string | `#000000` | First flash color (hex). |
| `FlashColorB` | string | `#FFFFFF` | Second flash color (hex). Overlay text contrast is derived automatically. |
| `AlertAutoStopEnabled` | bool | `false` | Master switch: auto-stop continuous alerts after a cap. |
| `AlertMaxDurationSeconds` | int | `60` | How long continuous alerts run when auto-stop is on. Clamped **5–600s**. |
| `AlertAutoStopIncludesSound` | bool | `true` | When auto-stop is on, also silence the **looping sound** at the cap. Off = sound runs until dismissed. |
| `AlertAutoStopIncludesVisual` | bool | `true` | When auto-stop is on, also stop the **visual flash** at the cap. Off = flash runs until dismissed. |
| `KeywordFilterEnabled` | bool | `false` | Mute pings matching a keyword. |
| `KeywordFilterText` | string | `""` | Comma- or newline-separated keywords; case-insensitive substring match against sender + channel + text. Wrap a phrase in `"double quotes"` to match it verbatim (including commas). |

---

## Usage

- **Open settings** — Left-click the tray icon, or right-click → *Open settings*. Closing the window hides it back to the tray; it doesn't exit the app.
- **Exit** — Right-click the tray icon → *Exit*.
- **Read the status at a glance** — The tray icon and the status pill agree:
  - 🟢 **Green bell** = *Active (armed)* — you're idle and the next Slack ping will fire an overlay.
  - ⚪ **White bell** = *Idle (paused)* — monitoring is on, but you're at the keyboard, so alerts are suppressed.
  - 🚫 **Slashed bell** = *Disabled* — monitoring is off.
- **Dismiss an alert** — Press `Esc`, click anywhere, or type any key. All monitors clear together and SlackWake re-arms once you've stepped away again.
- **Preview before you trust it** — Click **"Test overlay with current settings"** to see exactly what an alert will look and sound like, without waiting to go idle. Use the **▶** button next to the sound dropdown to audition a sound, or just hover entries in the dropdown to hear them.

---

## Development

```powershell
# Build (Debug)
dotnet build .\SlackWake.sln

# Run with a live debugger from VS, or:
dotnet run --project .\SlackWake\SlackWake.csproj

# Run, simulating an auto-start (skips showing the settings window)
dotnet run --project .\SlackWake\SlackWake.csproj -- --minimized
```

**Debugging the listener:** Tail `%AppData%\SlackWake\debug.log` while testing — `SlackMonitorService`, the keyword filter, and the hover-preview behavior all log their decisions there (which app a toast came from, whether it was filtered, etc.).

**Code layout conventions:**

- Services are plain classes with C# events; the view-model subscribes and re-publishes bindable state.
- UI-only concerns (numeric-only text boxes, multi-line placeholders, hover-preview) are implemented as **attached behaviors** in `Helpers/` rather than code-behind, so the XAML stays declarative.
- The brand bell mark is defined three times for three render targets and intentionally kept visually identical: as WPF vector geometry (settings header), GDI+ path (tray icons in `TrayIconFactory`), and a generator script (`Assets/generate-app-icon.ps1` → `app.ico`).

---

## Build and deployment

SlackWake is a single desktop executable; there is no server, installer pipeline, or cloud component.

**Framework-dependent single file** (smallest output, requires the .NET 8 Desktop Runtime on the target machine):

```powershell
dotnet publish .\SlackWake\SlackWake.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true
```

**Self-contained single file** (no runtime install needed, larger output):

```powershell
dotnet publish .\SlackWake\SlackWake.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true
```

Output lands in:

```
SlackWake\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
```

**Deployment** is simply copying `SlackWake.exe` to the target machine and running it. To auto-start at sign-in, either toggle *Start with Windows* in the app (writes a per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry pointing at the exe with `--minimized`) or place a shortcut in the user's Startup folder. No elevation/admin rights are required at any point.

---

## Project structure

```
SlackWake/
├── SlackWake.sln
├── README.md
├── .gitignore
└── SlackWake/
    ├── SlackWake.csproj              # net8.0-windows, WPF + WinForms, WPF-UI dep
    ├── app.manifest                  # asInvoker (no elevation), PerMonitorV2 DPI
    ├── App.xaml / App.xaml.cs         # Composition root: services, tray icon, overlay spawner
    ├── Assets/
    │   ├── app.ico                    # App / taskbar / window icon
    │   └── generate-app-icon.ps1      # Regenerates app.ico from the bell geometry
    ├── Helpers/
    │   ├── NativeMethods.cs           # Win32 P/Invoke (idle, MoveWindow, foreground, icons)
    │   ├── ColorUtil.cs               # Hex parsing + WCAG contrast for flash colors
    │   ├── Log.cs                     # Capped append-only debug log
    │   ├── TrayIconFactory.cs         # Renders the 3 tray-icon states in-memory (GDI+)
    │   ├── NumericTextBoxBehavior.cs  # Digits-only text box paired with a slider
    │   ├── Placeholder.cs             # Top-aligned placeholder for multi-line text boxes
    │   └── SoundHoverPreviewBehavior.cs # Hover/arrow-to-preview for the sound dropdown
    ├── Models/
    │   └── AppSettings.cs             # Persisted settings POCO
    ├── Services/
    │   ├── SettingsService.cs         # JSON load/save (best-effort)
    │   ├── IdleMonitorService.cs      # 1 Hz idle-time poller
    │   ├── SlackMonitorService.cs     # Windows toast listener (Slack-only, polled)
    │   ├── SoundLibrary.cs            # Enumerate + one-shot play of WAVs
    │   ├── PreviewPlayer.cs           # Stateful preview transport (play/stop/IsPlaying)
    │   ├── LoopingSoundPlayer.cs      # Gapped looping playback w/ optional cap
    │   └── StartupService.cs          # HKCU Run-key toggle
    ├── ViewModels/
    │   ├── ObservableObject.cs        # Minimal INotifyPropertyChanged base
    │   ├── RelayCommand.cs            # ICommand helpers (parameterless + generic)
    │   └── MainViewModel.cs           # Glue: settings ⇄ services ⇄ UI
    └── Views/
        ├── MainWindow.xaml(.cs)       # Fluent settings window (hides to tray on close)
        └── OverlayWindow.xaml(.cs)    # Fullscreen topmost alert (sound + flash)
```

---

## Security and privacy

SlackWake is designed to be privacy-preserving by construction:

- **No network access.** It does not connect to Slack's API or any other server. It reads notifications that Windows has *already delivered* to your machine.
- **No credentials.** No Slack token, OAuth flow, or login. Nothing to leak.
- **No telemetry.** Nothing is sent anywhere. The only data written is your local settings file and a local diagnostic log.
- **Runs as the invoking user, unelevated.** The manifest requests `asInvoker` — no admin rights, ever. The startup entry is per-user (`HKCU`), not machine-wide.
- **Scoped to Slack.** The notification listener technically receives toasts from *all* apps (this is the user-level notification stream), but SlackWake filters to Slack and ignores everything else. Toast contents are used only to populate the overlay and are not persisted.

**Things to be aware of:**

- The diagnostic log (`%AppData%\SlackWake\debug.log`) records notification metadata and, for diagnostic purposes, the **title and body text** of Slack toasts it processes. If you share this file, treat it as potentially sensitive. It's capped and rotates, but consider clearing it before sharing.
- Because SlackWake reads the OS notification stream, it inherently has the technical ability to see non-Slack notifications; it is written to ignore them, but auditors should be aware of the capability the `UserNotificationListener` permission grants.

---

## Troubleshooting

| Symptom | Likely cause / fix |
| --- | --- |
| Status says *"Notification access …"* and no overlays ever fire | Listener access wasn't granted. *Settings → Privacy &amp; security → Notifications → Let apps access your notifications.* Then toggle *Enable monitoring* off/on. |
| Overlays never appear even when idle | (1) Slack isn't using Windows notifications — set *Slack → Preferences → Notifications → Use Windows notifications.* (2) Slack is in DND / channel muted — nothing to listen for. (3) You aren't actually idle yet (check the idle timeout). |
| Overlay appears but `Esc`/keys don't dismiss it | Usually transient focus loss. Click the overlay once, then keys work. SlackWake force-foregrounds the window, but click is always reliable. |
| Sound doesn't play | Check *Sound alert* is on and a valid sound is selected. Empty/missing paths fall back to the system Exclamation sound; if your selected `.wav` was deleted, re-pick one. Only the **primary** monitor's overlay plays sound (by design). |
| Flash doesn't show | Flash is **off by default** — enable *Visual flash*. Note sound and flash both wait out the *Alert delay*. |
| Keyword mute isn't working | Ensure *Mute by keyword* is on. Matching is substring + case-insensitive across sender, channel, and message text. For a phrase containing a comma, wrap it in `"double quotes"`. |
| "Start with Windows" doesn't stick | Locked-down group policy can block `HKCU\...\Run` writes; the toggle fails silently in that case. Add a Startup-folder shortcut manually as a fallback. |
| It worked, then stopped after an OS upgrade | Re-grant notification access — major Windows updates occasionally reset the permission. |
| Need more detail | Read `%AppData%\SlackWake\debug.log`. |

---

## Assumptions, limitations, and known issues

- **You only get what Slack toasts.** SlackWake's content is whatever Slack put in the Windows notification — typically channel/sender as the title and a body snippet. It can't resolve message context Slack didn't include (it also doesn't need to resolve user IDs — Slack already does that before toasting).
- **DND / muted = nothing.** If Slack is closed, in Do-Not-Disturb, or a channel is muted/set to "Nothing," there's no toast and therefore no overlay. This is intentional.
- **One alert at a time.** Only one overlay *set* is open at once. Additional Slack messages that arrive while an overlay is up are dropped — you're already looking at the screen.
- **Latency.** Detection runs on a ~1.5s poll, so an overlay can lag a ping by a second or two. Negligible for the "I walked away" use case.
- **`UserNotificationListener` requires Windows 10 1903+ (build 18362).** Older builds can't listen for notifications.
- **Listener sees all apps.** The OS notification stream isn't Slack-scoped; SlackWake filters by app name/AUMID containing "slack." An app that impersonates Slack's display name could theoretically be matched (low risk for a local-only desktop tool).
- **Custom (non-`C:\Windows\Media`) sounds** can be used by hand-editing `SoundFilePath`, but the UI only enumerates the Windows Media folder.
- **No automated tests** ship with the project at present.

---

## Contributing

Contributions are welcome! This is a small, focused codebase that's easy to get into.

1. **Fork** the repository and create a feature branch off `main`.
2. **Build and run locally** (`dotnet run --project .\SlackWake\SlackWake.csproj`) and verify your change manually — there's currently no test suite, so describe how you tested in the PR.
3. **Match the existing style:** MVVM with the hand-rolled `ObservableObject`/`RelayCommand`; UI-only logic as attached behaviors in `Helpers/`; no new dependencies unless clearly justified (the project deliberately ships with only `WPF-UI`).
4. **Keep `AppSettings` stable** — adding optional properties is safe, but renaming/removing them resets existing users' settings. Document new settings in this README's [setting reference](#setting-reference).
5. **Open a pull request** with a clear description of the change and rationale.

Good first contributions: real screenshots under `docs/`, additional sound-source folders, configurable overlay text, or unit tests around the keyword parser and contrast math.

---

## License

SlackWake is released under the **GNU General Public License v3.0**. See the [`LICENSE`](LICENSE) file for the full text.

```text
SPDX-License-Identifier: GPL-3.0-only
```

GPL-3.0 is a strong copyleft license: you are free to use, study, share, and modify SlackWake, but **any distributed derivative must also be released under GPL-3.0 with source available, and must preserve attribution.** It cannot be incorporated into closed-source/proprietary software.

```text
SlackWake — fullscreen Slack alerts while you're away from your desk.
Copyright (C) 2026 Md. Rifat Hasan Jihan

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
```
