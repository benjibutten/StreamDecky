# Contributing

## Build and test

You need Windows and the `.NET 10 SDK`.

```powershell
dotnet restore StreamDecky.slnx
dotnet run --project src/StreamDecky/StreamDecky.csproj
```

To run the full automated test suite:

```powershell
dotnet test tests/StreamDecky.Tests/StreamDecky.Tests.csproj -c Debug --disable-build-servers /nr:false
```

If the WPF design-time build or C# Dev Kit locks generated files such as
`App.g.cs` and the build fails with `MC1000`, see
[the build lock workaround](docs/recovery-and-troubleshooting.md#developer-troubleshooting-wpf-build-lock-or-mc1000).

## Architecture

```text
StreamDecky/
|-- Models/
|   |-- DeckProfile, DeckProfileStore, DeckPage, NotePage, StickyNote
|   |-- ButtonConfig, ActionStep
|   |-- FormTemplate, FormField, FormFieldOption, FormCounter, FormSubmission, FormDataStore
|   |-- AppSettings
|   `-- enums (ActionType, ActionStepType, TextMode, ButtonShape, FormFieldType, ProfileSchemaVersion)
|-- ViewModels/
|   |-- MainViewModel (+ partials for profiles, layouts, sticky notes, quick text, text helper, and save flow)
|   |-- ButtonViewModel
|   |-- TextHelperWidgetViewModel
|   `-- StickyNoteViewModel
|-- Views/
|   |-- OverlayWindow
|   `-- SettingsWindow
|-- Services/
|   |-- ProfileService
|   |-- ProfileSchemaMigrator
|   |-- FormRenderService
|   |-- FormDataService
|   |-- AppSettingsService
|   |-- DeepSeekSpellCheckService
|   |-- QuickAnswerService
|   |-- BraveSearchService
|   |-- TextInputActionService
|   |-- MultiActionService
|   |-- OverlayWindowController
|   |-- HotkeyRegistrationController
|   `-- StartupRegistrySyncService
|-- Helpers/
|   |-- OverlayInterop
|   |-- InputSimulator
|   |-- OverlayImageCache
|   |-- DataProtection
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

## Publishing

A self-contained single-file publish for `win-x64`:

```powershell
dotnet publish src/StreamDecky/StreamDecky.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=false \
  -p:EnableCompressionInSingleFile=false \
  -p:PublishReadyToRun=true
```

The executable embeds the app icon via `ApplicationIcon`. The taskbar and window
use it directly, and the tray icon is loaded from it at runtime, so no separate
`.ico` file needs to ship next to the executable.

## Release notes

[RELEASE_NOTES.md](RELEASE_NOTES.md) holds the short, hand-written notes. Update
it together with user-visible changes under `src/`. The release workflow adds the
version, commit, archive hash, and verified signing status.

## Branches and releases

- Pushes to `main` only trigger `release.yml` when `src/**` changes.
- `release.yml` restores the complete solution and runs the full test suite before
  publishing anything.
- It injects `Version`, `AssemblyVersion`, `FileVersion`, and
  `InformationalVersion` during publish. The local `.csproj` version is a
  fallback and can be overridden by CI.
- The release workflow signs and verifies the executable when both required PFX
  secrets are configured, and fails if only one secret is present.
- The release tag points to the exact commit that was checked out and built.
- `build.yml` is a manual candidate build for any selected branch. It runs the
  full restore, test, publish, and packaging flow and uploads ZIP plus SHA-256
  without creating a tag or public release.

Feature branches can be combined on the optional `next` branch when a set of
changes should be tested together before `next` is merged to `main`. Small fixes
can go directly through a feature branch to `main`. Only `main` publishes a
release. Without a trusted signature, Windows SmartScreen may require manual
approval; even a newly signed build can initially lack reputation.

## Runtime target

The project stays on `net10.0-windows`.
[docs/runtime-target-assessment.md](docs/runtime-target-assessment.md) has the
reasoning and the release criteria for any future target change.
