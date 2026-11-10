# Text Sync

Text Sync får en undertext som inte går i takt med filmen, till exempel en svensk SRT, att gå i takt. Programmet jämför den med en undertext på ett annat språk som redan går i takt, eller med filmens ljud.

## Snabbstart

### Om Windows blockerar programmet
- Står det "Windows skyddade datorn": klicka **Mer information** och sedan **Kör ändå**.
- Står det att "Smart App Control" blockerade programmet: då går det tyvärr inte att köra på den datorn, eftersom programmet inte är signerat.

### Så här gör du
1. Starta Text Sync-programmet och dra in filmen och den svenska SRT-filen i fönstret.
2. Klicka **Synka**. Står det **✔ Bra** ovanför listan gick det bra. Titta ändå på några rader: den engelska texten bredvid ska betyda samma sak som den svenska.
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
- Ljudsynk och Whisper behöver ffmpeg, whisper.cpp och en språkmodell. Programmet frågar och laddar ner dem första gången, och kontrollerar varje fil mot en SHA-256-summa. De hamnar i `%LOCALAPPDATA%\TextSync\tools`, cirka 1,4 GB med NVIDIA-grafikkort och 0,6 GB med processorn (det som hämtas följer valet i listan Grafikkort). Loggen och cachen ligger också i `%LOCALAPPDATA%\TextSync`.

### Testa
```
powershell -ExecutionPolicy Bypass -File build.ps1 -Test
```
Kommandot bygger testprogrammen i `Build\test\` och kör de syntetiska testerna. Testerna avgör själva om de gick igenom. Sista raden ska vara `RESULTAT: alla … kontroller gick igenom.` Misslyckas en kontroll skrivs en rad som börjar med `FEL:`, och kommandot avslutas med ett fel. Mer om detta, och om felsökningsprogrammen för riktiga filmer, finns i [docs/operations.md](docs/operations.md).

### Dela ut
Kopiera den senaste `TextSync-<version>.exe` från `Build\` till ett USB-minne. Versionen i filnamnet visar vilken version som ligger på minnet. Ingen hjälpfil följer med, så berätta om Windows-varningen (se snabbstarten ovan) när du lämnar över programmet. Varningen visas innan programmet har startat, och då går det inte att nå hjälpen i programmet.

Programmet är inte signerat (se [docs/decisions.md](docs/decisions.md)). Versionen står i filnamnet och i fönstrets rubrik, och sätts bara i `Program.Version` i [src/Program.cs](src/Program.cs) (plus de två `Assembly…Version`-raderna, som bygget kontrollerar).

### Obesvarade frågor (tas upp vid nästa körning)
Från genomgången av projektstrukturen 2026-11-09:

1. Ska en `.gitattributes` läggas till för enhetliga radslut? `src/Mkv.cs` har blandade radslut. Förslag: ja. (Ägaren svarar senare, 2026-11-10.)

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
