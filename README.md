# StreamDecky

StreamDecky är en Stream Deck-liknande desktopapp för Windows med fullskärms-overlay, redigerbar knappgrid, sticky notes och global hotkey.

Appen är byggd för snabba in-game/in-app actions där overlayen visas ovanpå andra fönster och kan skicka tangenttryckningar/text till tidigare aktivt fönster.

## Teknik

- Plattform: WPF
- Runtime: .NET 10 (`net10.0-windows`)
- Mönster: MVVM med CommunityToolkit.Mvvm
- Inputsimulering: Win32 `SendInput` (scan-code-baserat)
- Persistens: JSON i `%LOCALAPPDATA%\StreamDecky\profiles.json` (migrerar automatiskt från legacy `profile.json`)

## Nuvarande funktionalitet

### Overlay och fokus

- Fullskärms-overlay (`OverlayWindow`) med topmost-beteende
- Stäng med `Esc` eller stängknapp
- Overlay försöker återta fokus om det tappas
- Vid action klickas overlay ner och fokus återställs aggressivt till tidigare foreground-fönster innan action körs

### System tray och huvudfönster

- Appen skapar en tray-ikon med meny:
  - `Show`
  - `Exit`
- Huvudfönstrets stängning minimerar till tray (hide) i stället för att avsluta appen
- Tray-ikon laddas robust:
  - Primärt från `StreamDecky.ico` i runtime-mappen
  - Fallback till ikonen inbäddad i exe-filen
  - Sista fallback: Windows standard-applikationsikon

### Actions per knapp

- `TextInput`
  - Textläge: clipboard-paste eller simulerad typing
  - Valfritt Enter efter text
- `KeyPress`
  - SendKeys-liknande strängformat med modifierare/specialtangenter
- `MultiAction`
  - Sekvens av steps:
    - KeyPress
    - TextInput
    - Delay
- `LayoutNavigation`
  - Hoppar till target-layout (page eller virtual layout)

### Layoutsystem

- Vanliga pages med pager (föregående/nästa)
- Virtual layouts som kan nås via navigation targets
- Gemensam layoutstorlek (`Rows`/`Columns`) för alla pages/layouts i profil
- Lägg till, ta bort, döp om pages/layouts
- Layoutselector (`Go To Layout`) i editorn

### Knappeditor

- Titel, ikontext, bild, färger, corner radius, shape
- Shapes: none, heart, star, diamond, hexagon
- Högerklicksmeny + kortkommandon för copy/paste av knappkonfiguration
- Dubbeklick på `LayoutNavigation`-knapp i editorn följer target direkt

### Sticky notes

- Notes är page-oberoende och ligger i separata note pages (`NotePages`)
- Overlay kan:
  - visa note pages
  - dra notes
  - ändra färg
  - minimera/maximera
  - ta bort note
  - inline-redigera titel
- Main window kan hantera note pages (lägg till/ta bort/navigera)

### Profiler

- Flera profiler med helt separata:
  - pages/virtual layouts/knappar
  - notes areas/sticky notes
  - overlay- och inputinställningar
- Aktiv profil byts direkt i editorn via profillistan
- Snabbhantering i editorn: add, duplicate, rename och remove
- Import/export av profiler görs i Settings
- Legacy data i `profile.json` läses in och blir automatiskt din första standardprofil

### Inställningar

- Overlay-bakgrundsfärg
- Overlay-bakgrundsbild
- Button size / spacing / overlay opacity
- Grid layout (rows/columns, globalt för profilen)
- Hotkey recording för overlay toggle
- Start with Windows (HKCU Run)
- Natural typing (experimentell)
- Import/export av profiler till/från JSON

## Kortkommandon

I huvudfönstret (när textfält inte är fokuserat):

- `Delete`: töm vald knapp
- `Ctrl+C`: kopiera vald knapp
- `Ctrl+V`: klistra in på vald knapp

I overlay:

- `Esc`: stäng overlay

## Data och profiler

- Profil sparas automatiskt (debounce) samt vid explicit save-flöden
- Auto-save är riktad till faktiska datamutationer
- Primär lagring: `%LOCALAPPDATA%\StreamDecky\profiles.json`
- Legacy-läsning: `%LOCALAPPDATA%\StreamDecky\profile.json` (migreras in som standardprofil)
- Backup (1 version bakåt): `%LOCALAPPDATA%\StreamDecky\profiles.backup.json`
- Import/export i Settings gäller aktiv profil och lägger importerad profil som separat profil

## Arkitekturöversikt

```text
StreamDecky/
├── Models/
│   ├── DeckProfile, DeckPage, NotePage, StickyNote
│   ├── ButtonConfig, ActionStep
│   └── enums (ActionType, ActionStepType, TextMode, ButtonShape)
├── ViewModels/
│   ├── MainViewModel
│   ├── ButtonViewModel
│   └── StickyNoteViewModel
├── Views/
│   ├── OverlayWindow
│   └── SettingsWindow
├── Services/
│   ├── ProfileService
│   ├── TextInputActionService
│   └── MultiActionService
├── Helpers/
│   ├── OverlayInterop
│   ├── InputSimulator
│   ├── OverlayImageCache
│   └── Converters
└── MainWindow.xaml (+ code-behind)
```

## Köra lokalt

Krav: .NET 10 SDK på Windows.

```powershell
dotnet restore StreamDecky/StreamDecky.csproj
dotnet run --project StreamDecky/StreamDecky.csproj
```

## Publicering

Exempel (self-contained single-file, win-x64):

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

Ikonhantering vid single-file:

- Exe-filen har inbäddad appikon via `ApplicationIcon`.
- Taskbar/fönster använder exe-ikonen direkt (ingen separat runtime-override av Window.Icon).
- Tray-ikon hämtas från exe-ikonen vid runtime.
- Ingen separat `.ico` behöver publiceras bredvid exe för att ikonerna ska visas i vanliga app-sammanhang.

Versionsinfo och signering i release-pipeline:

- `release.yml` sätter `Version`, `AssemblyVersion`, `FileVersion` och `InformationalVersion` vid publish.
- Lokal build har fallback-version i `.csproj` som kan överskridas av pipeline.
- Optional code-signing steg finns i pipeline om secrets för PFX-certifikat anges.
- Utan kodsignering kan Windows SmartScreen fortfarande kräva manuellt godkännande vid uppdateringar.

## Kända begränsningar

- Systemnivåfunktioner som `Ctrl+Alt+Del`, Secure Desktop och vissa UAC-scenarier kan inte blockeras av en vanlig desktopapp
- Vissa spel/appar med stark anti-cheat eller låg nivå-inputfilter kan fortfarande ignorera simulerad input
- Overlayen är primärt designad kring huvudskärmsbeteende

## Säkerhet och privilegier

- `app.manifest` körs som `asInvoker` (ingen admin krävs)
- Start with Windows skrivs till:
  - `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`

## Licens

Se `LICENSE`.
