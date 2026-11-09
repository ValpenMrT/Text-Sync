# Struktur

```
build.ps1          Bygger TextSync.exe med csc.exe från .NET Framework (finns i Windows). Skapar ikonen första gången.
src/
  Program.cs       Start, globala felhanterare (visar svenskt felmeddelande och skriver loggen)
  MainForm.cs      Hela gränssnittet: inläsning, synk, förhandsvisning, redigering med ångra, sparande, MKV-inbäddning, tidslinje
  CueDialog.cs     Dialog för att ändra eller lägga till en rad (start, slut, text)
  Audio.cs         Ljudreferens: ffmpeg → 16 kHz mono WAV (mittkanal), taldetektering (A), Whisper (B), kombination (A+B), cache
  Tools.cs         Hittar/laddar ner ffmpeg, whisper.cpp, språkmodell och Silero (fasta URL:er + SHA-256), grafikkort, inställningar
  TextCheck.cs     Kontroll av textspår mot Whisper ord för ord (ankare, utjämning, rättning); separat SRT på originalspråket synkas först grovt mot ljudet
  Engine.cs        SRT läs/skriv och synkmotorn
  Mkv.cs           Minimal Matroska-läsare: spårlista och textundertexter (SRT/ASS/WebVTT, även zlib-komprimerade)
  Log.cs           Loggfil i %LOCALAPPDATA%\TextSync\log.txt
  TextSync.ico     Programikon
test/
  Tests.cs         Tester: synk i 6 scenarier × 3 metoder, SRT/teckenkodningar, dra-och-släpp-fördelning,
                   spara med säkerhetskopia, MKV-läsning (egen MKV-skrivare, avkortade filer), mkvmerge-inbäddning
  Diag.cs          Felsökning mot riktiga filer: skriver ut avsnitt, träffsäkerhet och rader utan referens
  AudioDiag.cs     Jämför ljudsynk A, B och A+B mot facit (synk mot filmens undertextspår)
  ToolsDl.cs       Hämtar ljudverktygen med programmets egen nedladdningskod
  TextCheckDiag.cs Testar Whisper-kontrollen: orört spår och med flit förskjutet spår
  VadTune.cs       Justerar taldetekteringens gräns mot facit
  OrigSrtDiag.cs   Separat engelsk SRT (förstörd med flit) → ljud → svensk synk, mot facit
docs/              Beslut, drift, FAQ, nästa fas (next.md)
```

## Synkmotorn (Engine.cs) i korthet

1. **Global sökning:** korskorrelation (FFT) av "tal pågår"-signaler för varje bildfrekvenskandidat. Bästa förskjutning och skala väljs.
2. **Lokala mätpunkter:** var 10:e sekund (±30 s fönster) beräknas hur väl varje förskjutning inom ±20 s passar. Måttet är 50 % överlapp och 50 % träff på repliksstarter. Starterna gör träffen skarp i tät dialog.
3. **Linjär drift** anpassas robust till mätpunkterna.
4. **Styckvis synk (Viterbi):** hittar avsnitt med olika förskjutning, alltså klipp och reklampauser. Vid varje hopp prövas alla repliker ±25 s runt gränsen, och en enda brytpunkt väljs.
5. **Smart**-läget väljer styckvis synk bara när den är klart bättre än linjär.
