# Struktur

```
build.ps1          Bygger TextSync.exe med csc.exe från .NET Framework (finns i Windows). Skapar ikonen första gången.
src/
  Program.cs       Start, globala felhanterare (visar svenskt felmeddelande och skriver loggen)
  MainForm.cs      Hela gränssnittet: inläsning, synk, förhandsvisning, redigering med ångra, sparande, MKV-inbäddning, tidslinje
  CueDialog.cs     Dialog för att ändra eller lägga till en rad (start, slut, text)
  Engine.cs        SRT läs/skriv och synkmotorn
  Mkv.cs           Minimal Matroska-läsare: spårlista och textundertexter (SRT/ASS/WebVTT, även zlib-komprimerade)
  Log.cs           Loggfil i %LOCALAPPDATA%\TextSync\log.txt
  TextSync.ico     Programikon
test/
  Tests.cs         Syntetiska tester: synk i 6 scenarier × 3 metoder, SRT-läsning, MKV-läsning (egen MKV-skrivare)
  Diag.cs          Felsökning mot riktiga filer: skriver ut avsnitt, träffsäkerhet och rader utan referens
docs/              Beslut, drift, FAQ
```

## Synkmotorn (Engine.cs) i korthet

1. **Global sökning:** korskorrelation (FFT) av "tal pågår"-signaler för varje bildfrekvenskandidat. Bästa förskjutning och skala väljs.
2. **Lokala mätpunkter:** var 10:e sekund (±30 s fönster) beräknas hur väl varje förskjutning inom ±20 s passar. Måttet är 50 % överlapp och 50 % träff på repliksstarter. Starterna gör träffen skarp i tät dialog.
3. **Linjär drift** anpassas robust till mätpunkterna.
4. **Styckvis synk (Viterbi):** hittar avsnitt med olika förskjutning, alltså klipp och reklampauser. Vid varje hopp prövas alla repliker ±25 s runt gränsen, och en enda brytpunkt väljs.
5. **Smart**-läget väljer styckvis synk bara när den är klart bättre än linjär.
