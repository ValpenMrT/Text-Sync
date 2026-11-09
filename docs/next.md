# Nästa fas (planerad 2026-11-09)

Önskemål och kända begränsningar som ska tas upp härnäst.

## Önskemål från ägaren
1. ~~**Engelsk SRT som mellansteg.**~~ **Klart i 1.7.** Se beslutsloggen 2026-11-09, "Separat SRT på originalspråket + Whisper-kontroll".
2. **Översättning engelska → svenska** när svensk text saknas helt. Undersök alternativ för lokal översättning på datorn (helst utan internet och utan kostnad), kvalitet, hastighet på RTX A2000 och vad vännerna utan grafikkort behöver.
3. ~~**Versionsnummer i exe-filens namn.**~~ **Klart i 2.1.** Se beslutsloggen 2026-11-09, "Versionsnummer i filnamnet och mappen Build". Planen var:
   - `build.ps1` läser versionen ur `Program.Version` i `src/Program.cs`, så att den bara anges på ett ställe. Bygget avbryts med ett svenskt felmeddelande om versionen inte hittas.
   - Kontrollera samtidigt att `AssemblyVersion` och `AssemblyFileVersion` i `Program.cs` stämmer med `Program.Version`, och avbryt bygget om de inte gör det.
   - Kontrollera att inget i programmet beror på filnamnet. Inställningar, verktyg och logg ligger i `%LOCALAPPDATA%\TextSync`, och ikonen hämtas via `Application.ExecutablePath`, så ett nytt namn bör inte påverka något.
4. ~~**Byggfilerna i `Build/`.**~~ **Klart i 2.1**, samma beslut. Planen var: `build.ps1` lägger exe-filen i `Build\` i stället för i projektroten.
   - `Build/` läggs in i `.gitignore`. Den gamla `TextSync.exe` i roten tas bort.
   - Uppdatera alla ställen som nämner `TextSync.exe` eller `test\bin\`: `build.ps1` (kommentarer och utskrift), `README.md` (bygga och dela ut), `docs/operations.md` (bygga, köra inifrån PowerShell, dela ut), `docs/structure.md` och `CLAUDE.md`. Snabbstarten och beslutsloggen kan säga "Text Sync-programmet" i stället för ett filnamn med version.
   - För in beslutet i `docs/decisions.md` och höj versionen.
   - Frågor 9–11 i README.md ("Obesvarade frågor") besvaras innan arbetet börjar.
5. **Synka en hel säsong på en gång** (önskemål 2026-11-09). Man drar in en mapp, eller flera filmer och SRT-filer. Programmet parar ihop dem efter avsnittsnummer i namnet (S04E01, 4x01 m.m.), visar parningen så att man kan rätta den, synkar dem i kö och sparar varje fil bredvid sitt avsnitt.
   - Visa en sammanfattning per avsnitt efteråt: klart, osäkert (se punkt 7) eller misslyckat med en svensk förklaring.
   - Avbryt ska stoppa kön efter det avsnitt som pågår och behålla det som redan är klart.
6. **Granska i filmen** (önskemål 2026-11-09). Man klickar på en rad i listan och spelar upp filmen några sekunder runt repliken, med den synkade texten ovanpå. Det kontrollerar att texten stämmer med det som sägs, inte bara att tiderna har ändrats.
   - Undersök ffplay (från samma ffmpeg-källa som i dag, fast URL + SHA-256) mot att öppna en vanlig spelare (VLC/mpv) med tid och textfil. ffplay kräver ingen installation hos vännerna.
   - Texten ska visas från osparade ändringar (tillfällig SRT), så att man kan granska före sparandet.
7. **Säg hur säker synken är** (önskemål 2026-11-09). Efter synken visas ett tydligt besked: grönt "Bra", gult "Kontrollera de markerade ställena" eller rött "Osäkert". Gult och rött har knappar för att hoppa till föregående och nästa svaga ställe i listan.
   - Bygg på det motorn redan räknar ut (hur väl varje mätpunkt passar, hopp mellan avsnitt). Gränserna sätts mot facit i testerna, så att "Bra" verkligen betyder bra.
   - Inga siffror i huvudbeskedet. Siffrorna kan finnas i loggen.
8. **Läsbarhet** (önskemål 2026-11-09). Programmet hittar rader som visas för kort tid, går för fort att läsa (tecken per sekund), överlappar varandra eller är för långa (riktvärde cirka 42 tecken per rad), och föreslår rättningar. Förslagen granskas innan de används, som förslagen om att dela och slå ihop rader, och kan ångras.
9. **Spara så att spelaren hittar texten** (önskemål 2026-11-09). När referensen är en film sparas texten redan som `Filmnamn.sv.srt` bredvid filmen (`DefaultSrtPath` i `MainForm.cs`). Det som återstår:
   - När referensen är en SRT föreslås i dag `Namn.synkad.srt`, som spelare inte hittar. Finns en film med samma avsnitt i mappen föreslås `Filmnamn.sv.srt` i stället.
10. **Ta bort text för hörselskadade, som en inställning** (önskemål 2026-11-09). En kryssruta, till exempel "Ta bort text för hörselskadade ([MUSIK], (skrattar), talarnamn)", bredvid "Upptäck bildfrekvensfel". Valet sparas med `Tools.SetSetting` i `settings.txt`, så att det gäller nästa gång också.
    - Det som tas bort: hela rader som bara är ljudbeskrivning (`[…]`, `(…)`, ♪/♫), ljudbeskrivning inuti en rad, och talarnamn i versaler först på raden (`JOHN:`, `- MARIA:`). Rader som blir tomma tas bort, och dialogstreck rättas när bara en talare blir kvar.
    - Gäller både texten som sparas och texten som synkas, eftersom ljudbeskrivningar inte motsvarar tal och kan störa ljudsynken.
    - Visa hur många rader som ändrades eller togs bort, och gör ändringen ångringsbar som annan redigering.
    - Tester: en `Check(...)` per mönster ovan, plus kontroller av att vanlig dialog med parenteser eller versaler (t.ex. "FBI" eller "Han (min bror) kom hem") inte förstörs.
    - Fråga 10 i README.md (om rutan är ikryssad från början) besvaras innan arbetet börjar.
11. **Varna för fel avsnitt** (önskemål 2026-11-09). Programmet varnar innan det synkar om texten verkar höra till ett annat avsnitt eller en annan version av filmen. Ett exempel är S04E02.srt mot S04E01.mkv. Varningen stoppar inte synken: rutan har knapparna "Synka ändå" och "Avbryt".
    - Kontroller: avsnittsnummer i filnamnen skiljer sig (samma tolkning som i punkt 5), textens längd skiljer sig mycket från filmens längd, eller synken hittar ingen tydlig matchning alls (den globala sökningen har ingen tydlig topp).
    - Meddelandet ska vara på svenska och säga vad som inte stämmer, t.ex. "Texten verkar höra till avsnitt 2, men filmen är avsnitt 1."
    - I kön för en hel säsong (punkt 5) blir varningen ett "osäkert"-besked för avsnittet i stället för en ruta som stoppar kön.
    - Gränserna sätts mot testerna och testfilmen, så att varningen inte visas för vanliga filmer med förtexter eller eftertexter som saknar text.

## Kvar från product-check 2026-11-09 (bör åtgärdas)
Blockerande fynd är rättade: inget undantag kastas i processernas utdatahändelser, och felmeddelandena är begripliga och på svenska (nätverk, Smart App Control, ffmpeg, Whisper).
- **Välja ljudspår:** ljudsynk använder alltid standardljudspåret. Filmer med kommentarspår eller dubbning som standard behöver ett val.
- **Nedladdning efter valt grafikkort:** i dag hämtas CUDA-paketet och turbo-modellen (cirka 1,3 GB) så fort `nvcuda.dll` finns, även om man har valt processorn eller kortet är för gammalt. Processorpaketet hämtas också i onödan på NVIDIA-datorer.
- **Statusrad när grafikkortet inte fungerar** och Whisper börjar om på processorn. I dag hoppar förloppet då tillbaka från cirka 50 % till 0. Varningen före en lång körning på processorn finns sedan 1.7.
- **Taldetekteringen** kan inte avbrytas medan den räknar. Den tar nu bara några sekunder.
- **nvidia-smi läses på gränssnittets tråd** och kan frysa fönstret om drivrutinen hänger sig.
- **Whisper-kontrollen** rättar ibland enstaka rader som inte syns i rapporten.
- **Ingen kontroll av diskutrymme** före nedladdningen på cirka 1,3–2 GB, och ingen hjälptext om hur man frigör `%LOCALAPPDATA%\TextSync\tools`.
- **ffmpeg och Whisper** lägger till engelsk teknisk text i felmeddelanden, under rubriken "Teknisk information".
- **Sökvägar med tecken utanför västeuropeiska** i användarnamnet kan få whisper-cli att misslyckas. Det är inte verifierat.

## Frågor till ägaren
Obesvarade frågor står i README.md under "Obesvarade frågor (tas upp vid nästa körning)". Flytta svaren hit eller till beslutsloggen när de är besvarade.

## Kända begränsningar
- **Mörkt läge:** meddelanderutor (MessageBox, t.ex. Hjälp), kryssrutans ruta och pilarna i talrutan för extra justering förblir ljusa. Windows ritar dem själv.
- **Förslag om att dela och slå ihop rader** jämför bara tider och längd, inte innehåll. En delning kan därför ibland hamna vid fel radbrytning. Därför är det förslag som granskas, inte en automatisk ändring. Tabellen räknas om helt vid varje uppdatering av listan, även vid varje klick i extra justering: cirka 10 ms för ett avsnitt, uppskattningsvis 100 ms för en långfilm. Räkna bara celler nära diagonalen om det känns trögt.
- **Korta klipp med ljud som referens** (t.ex. 10 minuter) kan hitta fel startpunkt, eftersom det pratas nästan hela tiden och den globala sökningen då blir tvetydig. Hela filmer och avsnitt fungerar (mediafel 0,18 s i test).
- **B (Whisper ensam)** ger grova tider (mediafel 0,47 s men bara 50 % inom 0,5 s). Använd A+B.
- **Whisper-kontrollen** kan bara upptäcka fel i textspåret som är större än ungefär 1 s, eftersom Whispers ordtider är för brusiga för mindre fel. Mindre fel finjusterar den vanliga synken.
- **Smart App Control** blockerar ibland nybyggda program och DLL-filer en stund (se docs/operations.md).
