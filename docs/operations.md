# Drift

## Bygga
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
Resultat: `TextSync.exe` (~80 kB). Inga beroenden utöver .NET Framework 4.8, som finns i Windows 10/11.

## Testa
Testprogrammen kompileras med samma `csc.exe` (se `build.ps1`) med `/target:exe` och källorna `src/Engine.cs src/Mkv.cs src/Log.cs` plus `test/Tests.cs` eller `test/Diag.cs`. Om Smart App Control blockerar det nybyggda testprogrammet kan det köras inifrån PowerShell via `[Reflection.Assembly]::Load(...)`.

- `Tests.exe <tempmapp>`: syntetiska scenarier. Alla rader för Smart ska ha `100 % <300ms`.
- `Diag.exe <film.mkv> <undertext.srt> <från s> <till s> <spårnr>`: riktiga filer. Visar rader utan referens (`NO-REF`).

## Smart App Control på byggdatorn
Smart App Control är påslaget på utvecklingsdatorn och blockerar ofta varje *nybyggd* `TextSync.exe` i flera minuter, ibland längre. Det gäller alltså även din egen dator. För att testa direkt kan programmet köras inifrån PowerShell:
```
powershell -STA -Command "$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes('TextSync.exe')); $a.GetType('TextSync.Program').GetMethod('Main',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@(,[string[]]@()))"
```

## Dela ut
Kopiera `TextSync.exe` till USB. Programmet är osignerat (se beslutsloggen):
- Från USB visas normalt ingen SmartScreen-varning.
- Har mottagaren *Smart App Control* påslaget kan Windows blockera programmet helt. Då finns ingen gratis lösning.

## Loggar och felsökning
- Logg: `%LOCALAPPDATA%\TextSync\log.txt`, max ca 1 MB, och den äldre sparas som `log.txt.old`. Den innehåller filsökvägar, vald metod, resultat och fel. Den lämnar aldrig datorn.
- Användaren hittar loggmappen via **Hjälp** → "Öppna mappen med loggfilen".
- Vid oväntade fel visas ett svenskt felmeddelande som pekar på loggfilen.

## Säkerhetskopior
När originalet skrivs över hamnar en kopia i `Text Sync original\` bredvid filen. Den tas aldrig bort automatiskt.
