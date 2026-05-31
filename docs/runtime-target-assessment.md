# Runtime Target Assessment

Det här dokumentet sammanfattar beslutet om projektet ska ligga kvar på `net10.0-windows` eller flyttas till en annan target.

## Nuvarande läge

- Appen är en Windows-specifik WPF-klient och bygger idag på `net10.0-windows`.
- Enligt Microsofts supportdokumentation är .NET 10 en LTS-release med support till november 2028.
- .NET 8 är också LTS, men bara till november 2026.

## Bedömning

Rekommendationen är att behålla `net10.0-windows` tills ett separat distributions- eller kompatibilitetskrav motiverar något annat.

Skälen är enkla:

- Appen är en distribuerad desktopklient, och Microsoft rekommenderar LTS-spår när stabilitet och längre supportfönster är viktigare än täta hopp mellan releases.
- Projektet ligger redan på ett LTS-target. En flytt bort från `net10.0-windows` skulle därför inte vara en övergång till LTS, utan en downgrade till en äldre LTS.
- Det längre supportfönstret för .NET 10 minskar trycket att planera nästa framework-migrering i närtid.
- Självpublicering som self-contained minskar dessutom slutanvändarens beroende av exakt installerad .NET-runtime.

## När en downgrade till `net8.0-windows` ändå kan vara rimlig

Överväg bara en downgrade om minst ett av följande är sant:

- Byggmiljön eller release-pipelinen måste köras på en verktygskedja som är låst till .NET 8.
- Distributionen behöver samsas med annan intern mjukvara som ännu inte är validerad för .NET 10.
- Det finns ett konkret supportkrav från användarmiljöer där .NET 10 SDK eller relaterade verktyg inte kan införas.

Om inget av detta gäller finns det ingen tydlig teknisk vinst i att lämna .NET 10 just nu.

## Praktisk release-rekommendation

- Behåll `net10.0-windows` i koden.
- Fortsätt patcha till senaste .NET 10 servicing update.
- Behandla ett eventuellt target-byte som ett separat releasebeslut med egen verifiering av publish, signing, tray, overlay, inputsimulering och startup-beteende.

## Källgrund

Bedömningen bygger på Microsofts aktuella dokumentation för .NET release tracks och supportfönster, där .NET 10 anges som LTS till november 2028 och där klientappar för distribution uttryckligen pekas mot LTS-spåret när lång stabilitet prioriteras.