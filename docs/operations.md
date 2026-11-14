# Drift

## Bygga
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
Resultat: `Build\TextSync-<version>.exe` (cirka 165 kB), till exempel `Build\TextSync-2.4.exe`. Versionen läses ur `Program.Version` i `src\Program.cs`. Bygget avbryts med ett svenskt felmeddelande om versionen inte hittas, eller om `AssemblyVersion` och `AssemblyFileVersion` inte stämmer med den. Äldre versioner i `Build\` ligger kvar och tas aldrig bort av bygget. Inga beroenden utöver .NET Framework 4.8, som finns i Windows 10/11.

## Testa
```
powershell -ExecutionPolicy Bypass -File build.ps1 -Test
```
Bygger programmet, bygger alla testprogram i `Build\test\` och kör `Tests` med `%TEMP%\TextSyncTests` som arbetsmapp. Varje testprogram byggs av alla filer i `src\` plus testfilen, och `/main:TextSync.<Testnamn>` väljer testets `Main`. `Tests` körs inifrån PowerShell, eftersom Smart App Control ofta blockerar nybyggda exe-filer en stund.

`Tests.cs` avgör själv om testerna gick igenom:
- Sista raden är `RESULTAT: alla … kontroller gick igenom.` eller `RESULTAT: N av M kontroller MISSLYCKADES.`
- Varje misslyckad kontroll skriver en rad som börjar med `FEL:` direkt under raden den gäller. `Tests` returnerar då 1, och `build.ps1 -Test` avslutas med ett fel.
- Alla rader för **Smart** och **Advanced** måste ha `100,0% <300ms`. **Simple** bedöms inte i scenarierna med klipp (C, F, G), eftersom den bara gör linjär synk.
- Saknas mkvmerge hoppas testet av "Lägg in i MKV" över. Det står då på en rad som börjar med `OBS:` före resultatet.
- En ny kontroll läggs till med `Check("vad som ska gälla", villkor)` i `Tests.cs`.

Felsökningsprogrammen i `Build\test\` körs för hand mot riktiga filer, till exempel:
- `Diag.exe <film.mkv> <undertext.srt> <från s> <till s> <spårnr>`: visar rader utan referens (`NO-REF`).
- Övriga program och deras argument står i [structure.md](structure.md) och överst i varje testfil.

Testerna (`Tests.cs`) täcker:
- synk i 7 scenarier (A–G) × 3 metoder (Smart, Simple, Advanced)
- SRT-specialfall och teckenkodningar
- fördelning av filer vid dra-och-släpp
- sparande med säkerhetskopia
- MKV-läsning, också avkortade och trasiga filer
- inbäddning med riktig mkvmerge (hoppas över om MKVToolNix saknas)

**Gränssnittstest:** skicka aldrig tangenttryckningar eller klick blint. Kontrollera först att Text Sync-fönstret ligger överst. Om programmet blockeras hamnar annars tangenterna i det fönster som ligger överst. Ett Delete hamnade en gång i Utforskaren och flyttade en film till Papperskorgen.

## Smart App Control på byggdatorn
Smart App Control är påslaget på utvecklingsdatorn och blockerar ofta varje *nybyggd* exe-fil i flera minuter, ibland längre. Det gäller alltså även din egen dator. För att testa direkt kan programmet köras inifrån PowerShell (byt versionen mot den du har byggt):
```
powershell -STA -Command "$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes('Build\TextSync-2.4.exe')); $a.GetType('TextSync.Program').GetMethod('Main',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@(,[string[]]@()))"
```

## Dela ut
Kopiera den senaste `Build\TextSync-<version>.exe` till USB. Har mottagaren en äldre version (till exempel `TextSync.exe` eller `TextSync-2.0.exe`), be hen ta bort den, så att det inte ligger två versioner bredvid varandra. En genväg eller "Öppna med" som pekar på den gamla filen behöver göras om, men inställningarna följer med eftersom de ligger i `%LOCALAPPDATA%\TextSync`. Ingen hjälpfil följer med (se beslutsloggen 2026-11-09), så berätta om Windows-varningen när du lämnar över programmet: den visas innan programmet har startat, och då går det inte att nå hjälpen. Programmet är osignerat (se beslutsloggen):
- Från USB visas normalt ingen SmartScreen-varning.
- Har mottagaren *Smart App Control* påslaget kan Windows blockera programmet helt. Då finns ingen gratis lösning.

## Release på GitHub
Projektet ligger på https://github.com/ValpenMrT/Text-Sync. Varje push till `main` startar `.github/workflows/release.yml` på en Windows-dator hos GitHub:
1. `build.ps1 -Test` bygger programmet och kör testerna. Misslyckas en kontroll släpps inget.
2. Versionen läses ur `Program.Version`. Finns taggen `v<version>` inte redan skapas den, och `TextSync-<version>.exe` läggs upp som en release med namnet "Text Sync <version>". Texten i releasen är en rad om Windows-varningen och sedan commit-meddelandet.

Att tänka på:
- Pushas flera commits på en gång släpps bara den senaste versionen.
- Finns taggen redan släpps inget. Versionen ökar i varje commit, så det händer bara när ett jobb körs om.
- Hur det gick syns under **Actions** på GitHub. Från datorn: `git ls-remote --tags origin` visar taggen när releasen är klar (ett par minuter).
- Testet av "Lägg in i MKV" hoppas över hos GitHub, eftersom mkvmerge inte finns där.
- En exe-fil som laddas ner från webben märks av Windows som hämtad från internet. Då visas "Windows skyddade datorn" (Mer information → Kör ändå) oftare än från USB, och Smart App Control kan blockera filen helt.

## Loggar och felsökning
- Logg: `%LOCALAPPDATA%\TextSync\log.txt`, max ca 1 MB, och den äldre sparas som `log.txt.old`. Den innehåller filsökvägar, vald metod, resultat och fel. Den lämnar aldrig datorn.
- Användaren hittar loggmappen via **Hjälp** → "Öppna mappen med loggfilen".
- Vid oväntade fel visas ett svenskt felmeddelande som pekar på loggfilen.

## Säkerhetskopior
Från 2.4.17 skriver programmet aldrig över en fil, så inga kopior behövs. Äldre versioner lade originalet i `Text Sync original\` bredvid filen när det skrevs över. De mapparna tas aldrig bort automatiskt, och Hel säsong hoppar fortfarande över dem.
