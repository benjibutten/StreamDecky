# Remaining Improvements

Det här dokumentet listar sådant som fortfarande är öppet efter detta pass, samt markerar vilka tidigare riskområden som nu är adresserade.

## Fortfarande öppet

- Eventuell ändring av target framework ska fortfarande tas som ett separat releasebeslut, men nu med dokumenterad rekommendation att ligga kvar på `net10.0-windows` tills ett konkret kompatibilitetskrav dyker upp.
- UI-automation eller bredare end-to-end-manualtest runt fullskärmsappar, fokusåtertagning och anti-cheat-miljöer återstår fortfarande om projektet ska härdas för fler verkliga targetmiljöer.

## Adresserat i detta pass

- Best-effort-diagnostik till lokal loggfil.
- Loggning av tidigare tysta fel i profilhantering, autosave och bakgrundsactions.
- Säkrare atomiska filskrivningar för profil- och backupfiler.
- Skyddad registry-synk för `Start with Windows`.
- Explicit schema-versionering och versionerad profil/store-migrering.
- Användarsynlig sparstatus för osparade ändringar, pågående save och save-fel.
- Uppdelning av `MainViewModel` i mindre partials för profiler, layouts, sticky notes, quick text och save-livscykel.
- Tester för overlay-livscykel, hotkey-registrering, startup-registry-synk, import/export-roundtrip och action-exekvering.
- Fördjupad dokumentation för recovery-scenarier, användarflöden, felsökning och runtime target-bedömning.