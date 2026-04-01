# StreamDecky

En enkel Stream Deck-klon för Windows med fullskärms-overlay.

## Teknikval

**WPF (.NET 9)** valdes över WinUI 3 av följande skäl:
- WPF har mogen och stabil Win32-interop för topmost-fönster och input-hantering
- WinUI 3 har kända begränsningar med topmost-overlay-scenarier
- WPF:s databinding och MVVM-stöd med CommunityToolkit.Mvvm ger ren arkitektur
- Enklare kontroll över fönsterbeteende (WindowStyle, AllowsTransparency, etc.)

## Arkitektur

```
StreamDecky/
├── Models/          # Data: ButtonConfig, DeckPage, DeckProfile, ActionType, TextMode
├── ViewModels/      # MVVM: MainViewModel, ButtonViewModel
├── Views/           # OverlayWindow (fullskärm)
├── Services/        # ProfileService (JSON-persistens), TextInputActionService
├── Helpers/         # Win32 interop (OverlayInterop), WPF-konverterare
├── MainWindow.xaml  # Huvudfönster med deck-grid + editor
└── App.xaml         # Applikationsstart
```

**Mönster**: MVVM med CommunityToolkit.Mvvm (source generators för RelayCommand, ObservableProperty).

## Hur overlayen fungerar

1. **Öppning**: Användaren klickar "Open Overlay" → nytt `OverlayWindow` skapas
2. **Fullskärm**: `WindowState="Maximized"`, `WindowStyle="None"`, `Topmost="True"`
3. **Input-blockering**: Overlayen täcker hela skärmen med `AllowsTransparency` och blockerar musklick till underliggande fönster
4. **Win32-förstärkning**: `OverlayInterop.MakeTopmost()` använder `SetWindowPos(HWND_TOPMOST)` + `SetForegroundWindow` för garanterat topmost
5. **Fokusåterfångning**: `OnDeactivated` re-aktiverar overlayen om den förlorar fokus (t.ex. vid Alt+Tab)
6. **Stängning**: Esc-tangent eller stängknapp → overlay stängs, fokus återgår till Windows

### Kända begränsningar

- **Ctrl+Alt+Del**: Systemkombinationen kan inte fångas utan kernel-nivå hooks. Windows visar alltid sin säkerhetsskärm.
- **Win-tangenten**: Kan öppna startmenyn kortvarigt, men overlayen re-fångar fokus automatiskt.
- **Secure Desktop**: Applikationen kan inte blockera UAC-prompter eller liknande systemdialoger.
- **Multi-monitor**: Overlayen täcker primär skärm. Fullständigt multi-monitor-stöd kräver ytterligare arbete.

## Implementerad funktionalitet (v1)

### Text Input Action
- **Title**: Visningsnamn på knappen i decken
- **Text**: Innehåll som skrivs/klistras in
- **Press Enter after message**: Valfritt Enter-tryck efter texten
- **Text Mode**:
  - *Paste from Clipboard*: Placerar texten i clipboard och klistrar in (Ctrl+V)
  - *Simulate typing*: Simulerar tangenttryckningar tecken för tecken via SendKeys

### Anpassning
- Bakgrundsfärg på overlay
- Knappfärg (per knapp)
- Textfärg (per knapp)
- Ikon/emoji (per knapp)
- Hörnradie (per knapp)
- Knappstorlek och spacing (globalt)

### Editor
- Klicka på en knapp i griden för att redigera den
- Visuell markering (lila ram) visar vald knapp
- Alla ändringar reflekteras direkt i förhandsvisningen
- Spara-knapp persisterar profilen till `%LOCALAPPDATA%\StreamDecky\profile.json`

## Hur man kör

```bash
# Kräver .NET 9 SDK
dotnet run --project StreamDecky\StreamDecky.csproj
```

## Vad som är verifierat

- [x] Projektet kompilerar utan fel
- [x] Appen startar och visar huvudfönster med deck-grid och editor
- [x] Overlay öppnas i fullskärm vid klick på "Open Overlay"
- [x] Overlay stängs med Esc eller stängknapp
- [x] Text Input-action stöder båda lägena (Paste, Simulate typing)
- [x] Knappanpassning (färg, text, ikon, rundning) reflekteras i UI
- [x] Profil sparas/laddas med JSON

## Ej implementerat (planerade actions)

- Skicka tangentkombination/hotkey
- Starta program
- Öppna fil/mapp/URL
- Växla sida i decken
- Multi-monitor overlay
- Globalt hotkey för att toggla overlay

## Nästa steg

1. Implementera Hotkey-action (SendKeys med modifierare)
2. Implementera Launch Program-action (Process.Start)
3. Implementera Open File/Folder/URL-action
4. Implementera sidbyte i decken
5. Globalt tangentbordsgenväg för att aktivera overlay (RegisterHotKey)
6. Multi-monitor-stöd
