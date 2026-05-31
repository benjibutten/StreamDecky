# Recovery, Användarflöden och Felsökning

Det här dokumentet samlar de vanligaste operativa scenarierna runt profiler, autosave, overlay, autostart och lokal utveckling.

## Viktiga sökvägar

- Primär profilfil: `%LOCALAPPDATA%\StreamDecky\profiles.json`
- Backup: `%LOCALAPPDATA%\StreamDecky\profiles.backup.json`
- Legacy-profil: `%LOCALAPPDATA%\StreamDecky\profile.json`
- Loggfil: `%LOCALAPPDATA%\StreamDecky\logs\streamdecky.log`

## Användarflöde: skapa en ny profil

1. Välj aktuell profil i huvudfönstrets profilväljare.
2. Lägg till en ny profil och ge den ett tydligt namn.
3. Justera grid-storlek, overlay-beteende, hotkey och gamepad-inställningar i Settings.
4. Lägg till pages eller virtual layouts beroende på om du behöver pager-navigering eller direktnavigation.
5. Fyll knappar, sticky notes och quick text för den aktiva profilen.
6. Bekräfta att sparstatus går från osparade ändringar till sparad.

## Användarflöde: exportera och importera en profil

1. Öppna Settings för den profil som ska exporteras.
2. Exportera profilen till en JSON-fil.
3. På målmaskinen eller i en annan installation: öppna Settings och välj Import.
4. Den importerade profilen läggs in som en separat profil i store-filen.
5. Byt till den importerade profilen och verifiera layout, sticky notes och quick text innan du tar bort originalet.

## Recovery: sparfel eller fast osparat läge

Symptom:

- Sparstatus visar att ändringar inte kunde sparas.
- Sparstatus återgår inte till sparad efter att du slutat redigera.

Åtgärd:

1. Läs feltexten i sparstatusens tooltip i huvudfönstret.
2. Kontrollera loggfilen i `%LOCALAPPDATA%\StreamDecky\logs\streamdecky.log`.
3. Verifiera att `%LOCALAPPDATA%\StreamDecky` går att skriva till och att filen inte hålls låst av annan process eller backup-synk.
4. Starta om appen och bekräfta om senaste lyckade save finns kvar.
5. Om senaste ändringen saknas, återställ manuellt från `profiles.backup.json`.

## Recovery: återställ från backup efter korrupt profilfil

Symptom:

- Appen startar med tom standardprofil eller faller tillbaka till legacy-data.
- JSON-filen går inte att läsa eller är uppenbart korrupt.

Åtgärd:

1. Stäng appen helt från tray-menyn för att undvika nya writes.
2. Ta en kopia av den nuvarande `profiles.json` för forensik innan du ändrar något.
3. Byt namn på `profiles.backup.json` till `profiles.json`.
4. Starta appen och kontrollera att profiler, pages och notes ser rimliga ut.
5. Exportera kritiska profiler separat när appen väl öppnat korrekt igen.

## Recovery: autostart eller tray-start fungerar inte

Symptom:

- `Start with Windows` är aktiverat men appen startar inte efter inloggning.
- Appen verkar starta men inget huvudfönster syns.

Åtgärd:

1. Kontrollera att registry-värdet finns i `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.
2. Verifiera att kommandot pekar på aktuell exe och innehåller `--minimized`.
3. Om appen publicerats till ny plats, öppna appen en gång och toggla `Start with Windows` av/på så att registry-raden skrivs om.
4. Kontrollera system tray om appen redan kör gömd.
5. Läs loggfilen om registry-synken eller uppstarten verkar ha misslyckats.

## Recovery: overlay eller hotkey svarar inte

Symptom:

- Global hotkey öppnar inte overlay.
- Overlay öppnas ibland men förlorar fokus eller beter sig inkonsekvent.

Åtgärd:

1. Öppna Settings och spela in hotkey igen för att tvinga omregistrering.
2. Undvik kombinationer som redan används globalt av Windows, drivrutiner eller andra overlayappar.
3. Bekräfta att appen fortfarande körs i tray.
4. Testa att öppna overlay från huvudfönstret för att skilja hotkey-problem från overlay-problem.
5. Om problemet bara uppstår i ett specifikt spel eller program kan låg nivå-inputfilter, exclusive fullscreen eller anti-cheat vara orsaken.

## Recovery: import lyckas men profilen ser fel ut

Symptom:

- Sidor, sticky notes eller quick text ser tomma eller omordnade ut efter import.

Åtgärd:

1. Kontrollera att du faktiskt bytt till den importerade profilen i profilväljaren.
2. Exportera profilen igen direkt efter import och verifiera att schema-version finns i JSON.
3. Jämför importerad fil med originalet för att se om problemet uppstod före eller efter import.
4. Om originalfilen är legacy-formaterad, låt appen importera och migrera den och exportera sedan om i nytt format.

## Utvecklarfelsökning: WPF build-lock eller MC1000

Symptom:

- Build fallerar sporadiskt mot genererade filer som `App.g.cs`.

Åtgärd:

1. Kör `dotnet build StreamDecky/StreamDecky.csproj -c Debug --disable-build-servers /nr:false`.
2. Kör därefter `dotnet run --project StreamDecky/StreamDecky.csproj --no-build`.
3. Om problemet återkommer, stäng design-time hosts i editorn och kontrollera att antivirus inte aggressivt indexerar `bin` eller `obj`.

## När loggen räcker och när backup ska användas

- Använd loggen först när UI visar save-fel, overlay-problem eller registry-problem men data fortfarande finns kvar.
- Använd backup när profilfilen inte längre går att läsa eller när senaste fungerande save behöver återställas snabbt.
- Exportera en fungerande profil efter återställning om du vill ha en extra manuell checkpoint.