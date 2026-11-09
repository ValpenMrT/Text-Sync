# Text Sync

Text Sync får en undertext som inte går i takt med filmen, till exempel en svensk SRT, att gå i takt. Programmet jämför den med en undertext på ett annat språk som redan går i takt, eller med filmens ljud.

## Snabbstart

### Om Windows blockerar programmet
- Står det "Windows skyddade datorn": klicka **Mer information** och sedan **Kör ändå**.
- Står det att "Smart App Control" blockerade programmet: då går det tyvärr inte att köra på den datorn, eftersom programmet inte är signerat.

### Så här gör du
1. Starta Text Sync-programmet och dra in filmen och den svenska SRT-filen i fönstret.
2. Klicka **Synka** och titta på några rader i listan. Den engelska texten bredvid ska betyda samma sak som den svenska.
3. Klicka **Spara SRT…**. Filen sparas som `Filmnamn.sv.srt` bredvid filmen, så hittar VLC, Plex och Kodi den automatiskt.

Har filmen ingen engelsk text kan Text Sync synka mot filmens ljud i stället. Första gången frågar programmet om det får hämta gratisverktyg som behövs för det. Utan NVIDIA-grafikkort tar det längre tid.

Originalfilen ändras aldrig utan att en kopia sparas.

**Mer hjälp:** klicka **Hjälp** i programmet.
**Problem?** Klicka **Hjälp**, öppna loggmappen och skicka `log.txt` till den som gav dig programmet.

---

## För utvecklare

### Vad programmet gör
Referensen kan vara ett textspår i en MKV-fil, en separat SRT eller filmens ljud. Ljudet analyseras med taldetektering, Whisper eller båda kombinerat. Ett textspår, eller en separat SRT på filmens originalspråk, kan också **kontrolleras ord för ord med Whisper** innan den svenska texten synkas mot det. Whisper körs på NVIDIA-grafikkort om det finns, annars på processorn.

Programmet jämför *när* det pratas och *när* repliker börjar, inte vad som sägs. Därför fungerar olika språk. Det hanterar förskjutning, fel bildfrekvens (25 ↔ 23,976 fps m.fl.), drift och klipp/reklampauser.

### Bygga
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
Det skapar `Build\TextSync-<version>.exe`, till exempel `Build\TextSync-2.1.exe`. Versionen läses ur `Program.Version` i [src/Program.cs](src/Program.cs), och bygget avbryts om `AssemblyVersion` och `AssemblyFileVersion` inte stämmer med den. Äldre versioner ligger kvar i `Build\`. Kräver bara Windows, med .NET Framework 4.8 som redan finns i Windows 10/11.

- **Lägg in i MKV…** kräver [MKVToolNix](https://mkvtoolnix.download/). Det behövs bara för den knappen.
- Ljudsynk och Whisper behöver ffmpeg, whisper.cpp och en språkmodell. Programmet frågar och laddar ner dem första gången, och kontrollerar varje fil mot en SHA-256-summa. De hamnar i `%LOCALAPPDATA%\TextSync\tools`, cirka 1,3–2 GB med NVIDIA-kort. Loggen och cachen ligger också i `%LOCALAPPDATA%\TextSync`.

### Testa
```
powershell -ExecutionPolicy Bypass -File build.ps1 -Test
```
Kommandot bygger testprogrammen i `Build\test\` och kör de syntetiska testerna. Testerna avgör själva om de gick igenom. Sista raden ska vara `RESULTAT: alla … kontroller gick igenom.` Misslyckas en kontroll skrivs en rad som börjar med `FEL:`, och kommandot avslutas med ett fel. Mer om detta, och om felsökningsprogrammen för riktiga filmer, finns i [docs/operations.md](docs/operations.md).

### Dela ut
Kopiera den senaste `TextSync-<version>.exe` från `Build\` till ett USB-minne. Versionen i filnamnet visar vilken version som ligger på minnet. Ingen hjälpfil följer med, så berätta om Windows-varningen (se snabbstarten ovan) när du lämnar över programmet. Varningen visas innan programmet har startat, och då går det inte att nå hjälpen i programmet.

Programmet är inte signerat (se [docs/decisions.md](docs/decisions.md)). Versionen står i filnamnet och i fönstrets rubrik, och sätts bara i `Program.Version` i [src/Program.cs](src/Program.cs) (plus de två `Assembly…Version`-raderna, som bygget kontrollerar).

### Obesvarade frågor (tas upp vid nästa körning)
Från granskningen 2026-11-09 (product-check):
1. Vänner med Smart App Control: ska ljudsynken döljas, eller räcker det svenska felmeddelandet som finns nu?
2. Är cirka 1,3 GB nedladdning okej för NVIDIA-användare, eller ska man kunna välja ett mindre paket (processor + small)?
3. Behövs val av ljudspår? (Filmer med kommentarspår eller dubbning som standard.)
4. Ska programmet rekommendera Ljud A när det inte finns något NVIDIA-grafikkort?
5. Behövs en knapp eller hjälptext för att ta bort nedladdade verktyg i `%LOCALAPPDATA%\TextSync\tools`?
6. Ska rättningar från Whisper-kontrollen markeras i listan så att man kan granska dem?

Från genomgången av projektstrukturen 2026-11-09:

7. Ska testutdata översättas till svenska ("offset", "cuts", "Route" m.fl.), enligt regeln i CLAUDE.md?
8. Ska en `.gitattributes` läggas till för enhetliga radslut? `src/Mkv.cs` har blandade radslut.
9. Ska `MainForm.cs` (cirka 1 900 rader) delas upp, med tidslinjen i en egen fil och resten i delar (partial class)?

Från planen för att ta bort text för hörselskadade 2026-11-09 (se docs/next.md, punkt 10):

10. Ska inställningen "Ta bort text för hörselskadade" vara ikryssad från början? Förslag: inte ikryssad, så att ingen text försvinner utan att man har valt det. Valet sparas till nästa gång.

### Dokumentation

| Fil | Innehåll |
|---|---|
| [docs/faq.md](docs/faq.md) | Fullständig hjälp till användare |
| [docs/decisions.md](docs/decisions.md) | Beslutslogg |
| [docs/structure.md](docs/structure.md) | Var koden ligger och varför |
| [docs/operations.md](docs/operations.md) | Bygga, testa, dela ut, loggar, felsökning |
| [docs/next.md](docs/next.md) | Nästa fas och kända begränsningar |
| [CLAUDE.md](CLAUDE.md) | Instruktioner för Claude (språk, dokumentation) |

### Licens
MIT, se [LICENSE](LICENSE).
