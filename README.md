# StreamDecky

StreamDecky är en Stream Deck-liknande desktopapp för Windows med fullskärms-overlay, redigerbar knappgrid, sticky notes och global hotkey.

Appen är byggd för snabba in-game/in-app actions där overlayen visas ovanpå andra fönster och kan skicka tangenttryckningar/text till tidigare aktivt fönster.

## Teknik

- Plattform: WPF
- Runtime: .NET 10 (`net10.0-windows`)
- Mönster: MVVM med CommunityToolkit.Mvvm
- Inputsimulering: Win32 `SendInput` (scan-code-baserat)
- Persistens: JSON i `%LOCALAPPDATA%\StreamDecky\profile.json`

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

### Inställningar

- Overlay-bakgrundsfärg
- Overlay-bakgrundsbild
- Button size / spacing / overlay opacity
- Grid layout (rows/columns, globalt för profilen)
- Hotkey recording för overlay toggle
- Start with Windows (HKCU Run)
- Natural typing (experimentell)
- Export layout till JSON

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
- Export i Settings skriver hela profilen till valfri JSON-fil

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

Projektet inkluderar `StreamDecky.ico` som publish-content och markerar den med `ExcludeFromSingleFile=true`, så ikonen kan ligga separat bredvid exe även vid single-file-publish.

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
