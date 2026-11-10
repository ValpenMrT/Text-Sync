# Struktur

```
build.ps1          Bygger Build/TextSync-<version>.exe (version ur Program.Version) med csc.exe från .NET Framework (finns i Windows). Skapar ikonen första gången.
                   -Test bygger också testprogrammen i Build/test/ och kör Tests (misslyckas om en kontroll gör det).
README.md          Snabbstart, sedan bygga/testa/dela ut för utvecklare. Delas inte ut på USB.
CLAUDE.md          Instruktioner för Claude: allt på svenska, var dokumentationen finns
CLAUDE.local.md    Sökvägar som bara gäller den här datorn (testfilmen). Inte i git.
src/
  Program.cs       Start, globala felhanterare (visar svenskt felmeddelande och skriver loggen)
  MainForm.cs      Gränssnittet (partial class, delad på flera filer): fält, fönstrets uppbyggnad, tema, dra-och-släpp, knappar, hjälp
  MainForm.Load.cs   Inläsning av referens (film eller SRT), originaltext och undertexten som ska synkas
  MainForm.Edit.cs   Redigera rader med ångra, förslag om radindelning, hoppa till nästa röda rad
  MainForm.Audio.cs  Listorna Ljudspår och Grafikkort, nedladdning av verktyg, spara igenkänd text
  MainForm.Sync.cs   Synken (DoSync), resultatrutan och förhandsvisningen i listan
  MainForm.Save.cs   Spara SRT med säkerhetskopia, lägga in i MKV
  MainForm.Play.cs   Spela upp filmen runt en rad med ffplay (▶ i listan), med de synkade raderna ovanpå
  Timeline.cs      Tidslinjen under listan (TimelinePanel)
  SettingsDialog.cs Dialogen Inställningar: utseende, bildfrekvensfel, text för hörselskadade, formatering
  CueDialog.cs     Dialog för att ändra eller lägga till en rad (start, slut, text)
  Align.cs         Förslag om att slå ihop (2↔1) eller dela (1↔2) rader efter referensens radindelning (dynamisk programmering på tider)
  Theme.cs         Ljust/mörkt läge: paletter, färgbyte på kontrollerna, mörka Windows-delar (rullister, rubriker, titelrad)
  Audio.cs         Ljudreferens: ffmpeg → 16 kHz mono WAV (mittkanal), taldetektering (A), Whisper (B), kombination (A+B), cache
  Tools.cs         Hittar/laddar ner ffmpeg, whisper.cpp, språkmodell och Silero (fasta URL:er + SHA-256), grafikkort, inställningar
  TextCheck.cs     Kontroll av textspår mot Whisper ord för ord (ankare, utjämning, rättning); separat SRT på originalspråket synkas först grovt mot ljudet
  Engine.cs        SRT läs/skriv och synkmotorn
  Sdh.cs           Tar bort text för hörselskadade: [MUSIK], (skrattar), ♪ och talarnamn i versaler
  Markup.cs        Kodade tecken (&amp; → &) och taggar (<i>, <b>, <u>, <s>, <font>): avkodas och lagas alltid vid inläsningen, tas bort som ett val.
                   Också koder från webbsidor (<br>, <p>, <span>), ASS (\N, {\i1}) och MicroDVD ({y:i})
  Quality.cs       Besked om hur säker synken är (Bra / Kontrollera / Osäkert, svaga ställen) och avsnittsnummer i filnamn (varning för fel avsnitt)
  ConfirmDialog.cs Varningsruta med egna knapptexter ("Synka ändå" / "Avbryt")
  Mkv.cs           Minimal Matroska-läsare: spårlista och textundertexter (SRT/ASS/WebVTT, även zlib-komprimerade)
  Log.cs           Loggfil i %LOCALAPPDATA%\TextSync\log.txt
  TextSync.ico     Programikon
test/
  Tests.cs         Tester: synk i 7 scenarier × 3 metoder (G = korta klipp), tid före synk, SRT/teckenkodningar, dra-och-släpp-fördelning,
                   spara med säkerhetskopia, MKV-läsning (egen MKV-skrivare, avkortade filer), mkvmerge-inbäddning,
                   matchning av rader (dela, slå ihop, radlängd, mitt i mening, gles referens),
                   text för hörselskadade, kodade tecken och taggar
  Diag.cs          Felsökning mot riktiga filer: skriver ut avsnitt, träffsäkerhet och rader utan referens
  AudioDiag.cs     Jämför ljudsynk A, B och A+B mot facit (synk mot filmens undertextspår)
  ToolsDl.cs       Hämtar ljudverktygen med programmets egen nedladdningskod
  TextCheckDiag.cs Testar Whisper-kontrollen: orört spår och med flit förskjutet spår
  VadTune.cs       Justerar taldetekteringens gräns mot facit
  QualityDiag.cs   Mäter beskedet mot en riktig film: rätt text, texten baklänges (som ett annat avsnitt) och ett missat klipp
  OrigSrtDiag.cs   Separat engelsk SRT (förstörd med flit) → ljud → svensk synk, mot facit
  bin/             Byggda testprogram (skapas av build.ps1 -Test, inte i git)
docs/              Beslut, drift, FAQ, nästa fas (next.md)
```

## Användarhjälp finns på tre ställen

| Var | Omfång |
|---|---|
| `docs/faq.md` | Fullständig hjälp. Här beskrivs allt först. |
| `README.md`, avsnittet "Snabbstart" | Kort snabbstart: Windows-varningen, de tre stegen, ljudsynk i en mening och "klicka Hjälp". Den följer inte med på USB, utan är det ägaren berättar vid överlämningen. |
| `ShowHelp()` i `MainForm.cs` | Kort hjälp i programmet och länk till loggmappen. |

När en funktion ändras eller läggs till som användaren märker: uppdatera faq.md, och kontrollera att snabbstarten i README.md och `ShowHelp()` inte säger emot den. Lägg inte till detaljer i snabbstarten, så hålls den aktuell.

## Synkmotorn (Engine.cs) i korthet

1. **Global sökning:** korskorrelation (FFT) av "tal pågår"-signaler för varje bildfrekvenskandidat. Bästa förskjutning och skala väljs.
2. **Lokala mätpunkter:** var 10:e sekund (±30 s fönster) beräknas hur väl varje förskjutning inom ±20 s passar. Måttet är 50 % överlapp och 50 % träff på repliksstarter. Starterna gör träffen skarp i tät dialog.
3. **Linjär drift** anpassas robust till mätpunkterna.
4. **Styckvis synk (Viterbi):** hittar avsnitt med olika förskjutning, alltså klipp och reklampauser. Vid varje hopp prövas alla repliker ±25 s runt gränsen, och en enda brytpunkt väljs.
5. **Finjustering per replik:** en andra Viterbi över raderna (±6 s) hittar korta klipp (15–60 s) som mätpunkterna missar. Byte kräver flera rader som är överens, och raderna får inte byta ordning.
6. **Smart**-läget väljer styckvis synk bara när den är klart bättre än linjär.
