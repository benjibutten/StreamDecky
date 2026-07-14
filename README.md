# StreamDecky

StreamDecky is a Stream Deck-inspired desktop app for Windows with a full-screen overlay, an editable button grid, sticky notes, quick clipboard text, and a global hotkey.

It is designed for fast in-game or in-app actions where the overlay appears above other windows and can send keystrokes or text back to the previously active window.

## Requirements

- Windows 10 or Windows 11
- `.NET 10 SDK` for local development
- A writable `%LOCALAPPDATA%\StreamDecky` folder for profiles, backups, and logs
- A stable install path if you plan to use `Start with Windows`

Published releases are self-contained `win-x64` builds, so end users do not need a separate .NET runtime installation.

## Highlights

### Overlay and focus

- Full-screen topmost overlay (`OverlayWindow`)
- Close with `Esc` or the close button
- Attempts to regain focus if the overlay loses it unexpectedly
- Restores focus to the previous foreground window before executing actions

### Single instance and startup

- Runs as a single-instance app
- A second launch activates the existing window instead of starting a second process
- Supports `--minimized` to start hidden in the tray
- `Start with Windows` writes `"<path to exe>" --minimized` to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`

### Tray and main window

- Creates a tray icon with `Show` and `Exit`
- Closing the main window hides it to the tray instead of exiting the process
- Loads the tray icon from the executable icon with a Windows application icon fallback

### Button actions

- `TextInput`
  - Clipboard paste mode or simulated typing
  - Optional `Enter` after text input
- `KeyPress`
  - SendKeys-style key strings with modifiers and special keys
- `MultiAction`
  - Ordered action steps: `KeyPress`, `TextInput`, and `Delay`
- `LayoutNavigation`
  - Jumps directly to another page or virtual layout

### Layouts, notes, and profiles

- Regular pages with previous/next navigation
- Virtual layouts for hidden menus and direct navigation targets
- Shared layout size (`Rows` and `Columns`) across layouts in a profile
- Page-independent sticky notes stored in dedicated note pages
- Multiple profiles with separate layouts, notes, quick text, and overlay/input settings
- Import/export for the active profile from Settings

### Quick clipboard text

- Profile-scoped quick text items and categories
- Overlay search/filter support
- Shared multi-step action pipeline for clipboard rows
- Session-only inline edits in the overlay without autosaving temporary changes

### MicMixer music widget

- Optional overlay widget (`Music` in the overlay top bar) that remote-controls MicMixer's built-in music player
- Transport controls, seek, music/monitor volume, and a volume-link toggle synced with MicMixer
- Delayed start with a configurable countdown that plays the track marked in the list, plus single-track mode
- Library browsing with search, per-folder filter chips, click-to-mark and play/enqueue per track, and a reorderable queue
- Compact mode hides the queue and library, keeping only the player controls
- Draggable, resizable from both bottom corners, and persisted per profile; reconnects automatically when MicMixer is not running

### Gamepad support

- Optional per-profile XInput support
- Recordable gamepad combo for toggling the overlay
- D-pad and left stick navigation in the overlay
- `A` activates the selected button, `LB` and `RB` switch pages

## Keyboard shortcuts

In the main window, when a text field is not focused:

- `Delete`: clear the selected button
- `Ctrl+C`: copy the selected button configuration
- `Ctrl+V`: paste onto the selected button

In the overlay:

- `Esc`: close the overlay

## Installation

1. Download the latest release zip.
2. Extract it to a stable folder, for example `%LOCALAPPDATA%\Programs\StreamDecky` or another folder you control.
3. Run `StreamDecky.exe`.
4. If you want background startup, enable `Start with Windows` from Settings after the app is running from its final location.

Keeping the same install path helps Windows preserve tray icon preferences and keeps the startup registry entry valid.

## Data storage and recovery

- Primary store: `%LOCALAPPDATA%\StreamDecky\profiles.json`
- One-version-back backup: `%LOCALAPPDATA%\StreamDecky\profiles.backup.json`
- Legacy import source: `%LOCALAPPDATA%\StreamDecky\profile.json`
- Diagnostics log: `%LOCALAPPDATA%\StreamDecky\logs\streamdecky.log`

Profile data is autosaved after real data mutations and can also be saved through explicit save flows.

The profile format uses explicit schema versions and versioned migrations. If StreamDecky opens data created by a newer app version, it will load that data without migration but block saving to avoid overwriting fields from a newer schema.

Recovery guidance, startup troubleshooting, and import/export recovery flows are documented in [docs/recovery-and-troubleshooting.md](docs/recovery-and-troubleshooting.md).

## Runtime target

The project currently stays on `net10.0-windows`.

The reasoning and release criteria for any future target change are documented in [docs/runtime-target-assessment.md](docs/runtime-target-assessment.md).

## Architecture overview

```text
StreamDecky/
|-- Models/
|   |-- DeckProfile, DeckProfileStore, DeckPage, NotePage, StickyNote
|   |-- ButtonConfig, ActionStep
|   `-- enums (ActionType, ActionStepType, TextMode, ButtonShape, ProfileSchemaVersion)
|-- ViewModels/
|   |-- MainViewModel (+ partials for profiles, layouts, sticky notes, quick text, and save flow)
|   |-- ButtonViewModel
|   `-- StickyNoteViewModel
|-- Views/
|   |-- OverlayWindow
|   `-- SettingsWindow
|-- Services/
|   |-- ProfileService
|   |-- ProfileSchemaMigrator
|   |-- TextInputActionService
|   |-- MultiActionService
|   |-- OverlayWindowController
|   |-- HotkeyRegistrationController
|   `-- StartupRegistrySyncService
|-- Helpers/
|   |-- OverlayInterop
|   |-- InputSimulator
|   |-- OverlayImageCache
|   `-- Converters
`-- MainWindow.xaml (+ code-behind)
```

### MicMixer integration

`Integrations/MicMixer` contains a reconnecting, same-user named-pipe client for
MicMixer's built-in music player. It provides typed state, all configured library
folders, folder management, transport, queue, volume, delayed-start, single-track,
source-mode, and download operations. The client does not start MicMixer and has no
UI or profile coupling.

`ViewModels/MusicWidgetViewModel` owns an `IMicMixerClient` for the overlay music
widget. The client is created lazily and `Start()` is only called once the widget is
visible in an open overlay, so sessions that never show the widget pay no pipe cost.
Widget visibility, compact mode, position, and size are persisted per profile.

No extra service or companion process is required. The matching server runs inside
`MicMixer.exe` and the connection remains offline until MicMixer is running for the
same Windows user.

## Run locally

Requirement: Windows with the `.NET 10 SDK` installed.

```powershell
dotnet restore StreamDecky.slnx
dotnet run --project StreamDecky/StreamDecky.csproj
```

If WPF design-time build or C# Dev Kit locks generated files such as `App.g.cs` and triggers `MC1000`-style failures, use the safer sequence below instead:

```powershell
dotnet build StreamDecky/StreamDecky.csproj -c Debug --disable-build-servers /nr:false
dotnet run --project StreamDecky/StreamDecky.csproj --no-build
```

To run the full automated test suite locally:

```powershell
dotnet test StreamDecky.Tests/StreamDecky.Tests.csproj -c Debug --disable-build-servers /nr:false
```

## Publishing

Example self-contained single-file publish for `win-x64`:

```powershell
dotnet publish StreamDecky/StreamDecky.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=false \
  -p:PublishReadyToRun=true
```

Single-file icon behavior:

- The executable embeds the app icon via `ApplicationIcon`.
- The taskbar and window use the executable icon directly.
- The tray icon is loaded from the executable icon at runtime.
- No separate `.ico` file needs to ship next to the executable for normal runtime icon behavior.

Release pipeline notes:

- `release.yml` injects `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` during publish.
- `pr-build.yml` and `release.yml` both run the automated test project before shipping artifacts.
- The local `.csproj` version is a fallback and can be overridden by CI.
- The release workflow supports optional code signing when the required PFX secrets are configured.
- Without code signing, Windows SmartScreen may still require manual approval.

## Known limitations

- Desktop apps cannot intercept system-level flows such as `Ctrl+Alt+Del`, Secure Desktop, or some UAC transitions.
- Some games or applications with aggressive anti-cheat or low-level input filtering may ignore simulated input.
- The overlay is primarily designed around the primary display.

## Security and privileges

- The app runs as `asInvoker`; administrator rights are not required by default.
- `Start with Windows` writes to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.
- Profiles, backups, and logs stay under `%LOCALAPPDATA%\StreamDecky`.

## License

See `LICENSE`.
