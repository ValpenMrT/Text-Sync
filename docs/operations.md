# Drift

## Bygga
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
Resultat: `TextSync.exe` (cirka 165 kB). Inga beroenden utöver .NET Framework 4.8, som finns i Windows 10/11.

## Testa
```
powershell -ExecutionPolicy Bypass -File build.ps1 -Test
```
Bygger `TextSync.exe`, bygger alla testprogram i `test\bin\` och kör `Tests` med `%TEMP%\TextSyncTests` som arbetsmapp. Varje testprogram byggs av alla filer i `src\` plus testfilen, och `/main:TextSync.<Testnamn>` väljer testets `Main`. `Tests` körs inifrån PowerShell, eftersom Smart App Control ofta blockerar nybyggda exe-filer en stund.

`Tests.cs` avgör själv om testerna gick igenom:
- Sista raden är `RESULTAT: alla … kontroller gick igenom.` eller `RESULTAT: N av M kontroller MISSLYCKADES.`
- Varje misslyckad kontroll skriver en rad som börjar med `FEL:` direkt under raden den gäller. `Tests` returnerar då 1, och `build.ps1 -Test` avslutas med ett fel.
- Alla rader för **Smart** och **Advanced** måste ha `100,0% <300ms`. **Simple** bedöms inte i scenarierna med klipp (C, F, G), eftersom den bara gör linjär synk.
- Saknas mkvmerge hoppas testet av "Lägg in i MKV" över. Det står då på en rad som börjar med `OBS:` före resultatet.
- En ny kontroll läggs till med `Check("vad som ska gälla", villkor)` i `Tests.cs`.

Felsökningsprogrammen i `test\bin\` körs för hand mot riktiga filer, till exempel:
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
Smart App Control är påslaget på utvecklingsdatorn och blockerar ofta varje *nybyggd* `TextSync.exe` i flera minuter, ibland längre. Det gäller alltså även din egen dator. För att testa direkt kan programmet köras inifrån PowerShell:
```
powershell -STA -Command "$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes('TextSync.exe')); $a.GetType('TextSync.Program').GetMethod('Main',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@(,[string[]]@()))"
```

## Dela ut
Kopiera `TextSync.exe` till USB. Ingen hjälpfil följer med (se beslutsloggen 2026-11-09), så berätta om Windows-varningen när du lämnar över programmet: den visas innan programmet har startat, och då går det inte att nå hjälpen. Programmet är osignerat (se beslutsloggen):
- Från USB visas normalt ingen SmartScreen-varning.
- Har mottagaren *Smart App Control* påslaget kan Windows blockera programmet helt. Då finns ingen gratis lösning.

## Loggar och felsökning
- Logg: `%LOCALAPPDATA%\TextSync\log.txt`, max ca 1 MB, och den äldre sparas som `log.txt.old`. Den innehåller filsökvägar, vald metod, resultat och fel. Den lämnar aldrig datorn.
- Användaren hittar loggmappen via **Hjälp** → "Öppna mappen med loggfilen".
- Vid oväntade fel visas ett svenskt felmeddelande som pekar på loggfilen.

## Säkerhetskopior
När originalet skrivs över hamnar en kopia i `Text Sync original\` bredvid filen. Den tas aldrig bort automatiskt.
