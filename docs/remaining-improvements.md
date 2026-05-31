# Remaining Improvements

Det här dokumentet listar sådant som medvetet inte implementerades i detta pass eftersom det antingen påverkar appens beteende, kräver större arkitekturarbete eller behöver mer riktade produktbeslut.

## Kvarstående större arbeten

- Dela upp `MainViewModel` i mindre moduler eller tjänster. Filen är fortfarande den största underhållsrisken och bör brytas ut i separata ansvar för profiler, layouts, sticky notes, quick text och autosave.
- Inför en tydlig schema-version för profilformatet. Nuvarande migrering bygger fortfarande på heuristik i modellerna i stället för en explicit versionerad migreringskedja.
- Lägg till användarsynlig återkoppling för sparfel och osparade ändringar. Det här passet loggar fel, men appen visar fortfarande inte status i UI.
- Utöka testytan till UI-nära och integrationsnära delar: overlay-livscykel, hotkey-registrering, registry-startup, import/export och action-exekvering.
- Bygg ut dokumentationen ytterligare med konkreta recovery-scenarier, användarflöden och felsökningsexempel. Grundläggande README-täckning för gamepad, `--minimized`, backup och build-quirks finns nu, men den kan fortfarande fördjupas.

## Implementerat i detta pass

- Best-effort-diagnostik till lokal loggfil.
- Loggning av tidigare tysta fel i profilhantering, autosave och bakgrundsactions.
- Säkrare atomiska filskrivningar för profil- och backupfiler.
- Skyddad registry-synk för `Start with Windows`.
- Regressionstester för profilpersistens och modellinitialisering.