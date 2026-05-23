# SlackWake

A minimal Windows tray app that throws a fullscreen alert on every monitor when
a Slack message arrives **while you're away from your desk**. If you're already
active, Slack's normal notifications do the job and SlackWake stays quiet.

- C# + .NET 8 + WPF, MVVM
- No third-party packages — just the BCL, WPF, WinForms (for the tray icon), and WinRT
- Slack integration via the **Windows notification listener** (`UserNotificationListener`)
  — no custom Slack app, no token, no admin approval. Reads the Slack desktop
  client's native Windows toasts.
- Per-user, per-machine: settings live under `%AppData%\SlackWake\settings.json`

---

## 1. Project structure

```
SlackWake/
├── SlackWake.sln
├── README.md
└── SlackWake/
    ├── SlackWake.csproj          # net8.0-windows, WPF + WinForms
    ├── app.manifest              # PerMonitorV2 DPI awareness
    ├── App.xaml / App.xaml.cs    # Composition root, tray icon, overlay spawner
    ├── Helpers/
    │   └── NativeMethods.cs      # GetLastInputInfo / LASTINPUTINFO P/Invoke
    ├── Models/
    │   └── AppSettings.cs        # Persisted POCO
    ├── Services/
    │   ├── SettingsService.cs    # JSON load/save
    │   ├── IdleMonitorService.cs # 1 Hz idle-time poller
    │   ├── SlackMonitorService.cs# Windows toast listener (UserNotificationListener)
    │   └── StartupService.cs     # HKCU Run-key toggle
    ├── ViewModels/
    │   ├── ObservableObject.cs   # MVVM base
    │   ├── RelayCommand.cs       # ICommand helper
    │   └── MainViewModel.cs      # Glue: settings <-> services <-> UI
    └── Views/
        ├── MainWindow.xaml(.cs)  # Settings window
        └── OverlayWindow.xaml(.cs) # Fullscreen topmost alert
```

---

## 2. Build and run

Prerequisites:

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

From the repo root:

```powershell
dotnet build .\SlackWake.sln -c Release
dotnet run --project .\SlackWake\SlackWake.csproj
```

Or open `SlackWake.sln` in Visual Studio 2022+ and press F5.

To produce a single self-contained exe:

```powershell
dotnet publish .\SlackWake\SlackWake.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The output ends up under `SlackWake\bin\Release\net8.0-windows\win-x64\publish\`.

---

## 3. Slack setup (no custom app required)

SlackWake doesn't talk to Slack's servers at all. It subscribes to the same
Windows toast notification stream you see in Action Center, filters for
notifications whose source app is Slack, and uses their title/body as the
overlay content. Your workspace's normal notification rules apply.

There's nothing to register on Slack's side. You only need to make sure:

1. **The Slack desktop client is running** and signed into your workspace(s).
2. **Slack is configured to use the OS notification system.** Open Slack →
   *Preferences* → *Notifications* → set *"Notification display style"* (Win10)
   or equivalent to **"Use Windows notifications"** rather than Slack's
   in-app banner. (On recent Slack versions this is the default.)
3. **Windows is allowed to deliver toasts to apps.** Settings → *Privacy &
   security* → *Notifications* → make sure *"Notifications"* is on and that
   Slack isn't in the per-app blocked list.
4. **SlackWake has notification access.** The first time you enable it, the
   app calls `UserNotificationListener.RequestAccessAsync()` which prompts
   Windows for permission. If you accidentally deny it, you can flip it back
   on under Settings → *Privacy & security* → *Notifications* → scroll to
   *"Let apps access your notifications"*.

That's it. Launch SlackWake, leave **Enable monitoring** checked, walk away
until the idle timer trips, and the next Slack ping will fire the overlay.

> **Note on scope:** SlackWake sees whatever Slack chooses to toast. If Slack
> is muted, in DND mode, or has a channel set to "Nothing" for notifications,
> there's nothing to listen for. That's usually what you want — DND should
> mean DND.

---

## 4. How the pieces talk to each other

```
   +--------------------+        idle ms        +----------------------+
   | IdleMonitorService | --------------------> |                      |
   +--------------------+                       |                      |
                                                |   MainViewModel      |
   +--------------------+   SlackEvent          |                      |
   | SlackMonitorService| --------------------> |  - tracks isIdle     |
   | (Windows toast     |                       |  - filters: only     |
   |  listener, Slack-  |                       |    fires when idle   |
   |  only)             |                       |                      |
   +--------------------+                       +----------+-----------+
                                                           |
                                                           v
                                            App.ShowOverlay (one window/monitor)
                                                           |
                                                           v
                                            +--------------------------------+
                                            |  OverlayWindow (fullscreen,    |
                                            |  topmost, dismissable by ESC / |
                                            |  click / any keypress)         |
                                            +--------------------------------+
```

- `SlackMonitorService` runs on a thread-pool task. UI marshaling happens once,
  inside `MainViewModel.OnSlackNotification`, via `Application.Current.Dispatcher`.
- Duplicate overlays are prevented by a single `_overlayOpen` flag in `App`.
  Closing any overlay in the set closes all of them and resets the flag.
- Settings persistence is synchronous on every property change — settings files
  are tiny and writes are rare.

---

## 5. Mock UI

**Settings window** (`MainWindow.xaml`):

```
+---------------------------------------------------+
|  SlackWake                                        |
|  Wake me up when Slack pings during idle time.    |
|                                                   |
|  [x] Enable monitoring                            |
|  [ ] Start with Windows                           |
|                                                   |
|  Idle timeout (seconds)                           |
|  [============o===========================]  300  |
|                                                   |
|  +---------------------------------------------+  |
|  | Setup                                       |  |
|  | SlackWake reads Slack's native Windows      |  |
|  | toasts. Make sure the Slack desktop app is  |  |
|  | running, and that Windows notifications are |  |
|  | allowed for SlackWake.                      |  |
|  +---------------------------------------------+  |
|                                                   |
|  +---------------------------------------------+  |
|  | * Active - Watching Slack notifications     |  |
|  +---------------------------------------------+  |
+---------------------------------------------------+
```

**Fullscreen overlay** (`OverlayWindow.xaml`), shown once per monitor:

```
###################################################
#                                                 #
#                                                 #
#                                                 #
#         Slack notification received             #
#                                                 #
#               Channel: #eng-builds              #
#               From:    U01ABC2DEF               #
#                                                 #
#    +-----------------------------------------+  #
#    |  build is red on main — can someone     |  #
#    |  take a look?                           |  #
#    +-----------------------------------------+  #
#                                                 #
#       Press ESC, click, or type to dismiss      #
#                                                 #
###################################################
```

---

## 6. Settings file

`%AppData%\SlackWake\settings.json`:

```json
{
  "Enabled": true,
  "IdleTimeoutSeconds": 300,
  "StartWithWindows": false,
  "StartMinimized": false
}
```

Hand-editing while the app is running is fine — the next save from the UI
will overwrite it.

---

## 7. Known limitations / "future work" if you want to extend

- We only see what Slack puts in its toast — title (channel or sender) and
  body. Resolving Slack user IDs to display names is unnecessary here because
  Slack already does it before posting the toast.
- If Slack is closed, in DND, or has a channel muted, no toast = no overlay.
  That's intentional; DND should win.
- One alert set covers the latest message; if more arrive while the overlay is
  up, they're dropped (by design — the user is already looking at the screen).
- `UserNotificationListener` requires Windows 10 1903 (build 18362) or newer.
- The listener runs as the current user, so it sees the toasts that user sees —
  including non-Slack apps. We filter to Slack only.
