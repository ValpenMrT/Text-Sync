# Text Sync

Windows-program som synkar en undertext som inte går i takt (t.ex. svensk SRT) mot en undertext på ett annat språk som redan går i takt med filmen. Referensen kan vara ett textspår i en MKV-fil eller en separat SRT.

Programmet jämför *när* det pratas och *när* repliker börjar, inte vad som sägs. Därför fungerar olika språk. Det hanterar förskjutning, fel bildfrekvens (25 ↔ 23,976 fps m.fl.), drift och klipp/reklampauser.

## Komma igång

1. Bygg: `powershell -ExecutionPolicy Bypass -File build.ps1`. Det skapar `TextSync.exe`. Kräver bara Windows, med .NET Framework 4.8 som redan finns i Windows 10/11.
2. Starta `TextSync.exe`, dra in filmen (MKV) och den osynkade SRT-filen och klicka **Synka**. Granska resultatet i listan, där röda rader saknar referenstext och F3 hoppar till nästa röd rad. Där kan du också ändra, lägga till eller ta bort rader, med ångra.
3. **Spara SRT…** sparar `Filmnamn.sv.srt` bredvid filmen. **Lägg in i MKV…** skapar en ny MKV och kräver [MKVToolNix](https://mkvtoolnix.download/).

Användarhjälp finns i [docs/faq.md](docs/faq.md). Den finns också kort i programmet under länken **Hjälp**.

## Dokumentation

| Fil | Innehåll |
|---|---|
| [docs/decisions.md](docs/decisions.md) | Beslutslogg |
| [docs/structure.md](docs/structure.md) | Var koden ligger och varför |
| [docs/operations.md](docs/operations.md) | Bygga, dela ut, loggar, felsökning |
| [docs/faq.md](docs/faq.md) | Hjälp till användare |

## Licens

MIT, se [LICENSE](LICENSE).
