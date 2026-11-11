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
5. ~~**Synka en hel säsong på en gång.**~~ **Klart i 2.4.2**, se beslutsloggen 2026-11-11. Planen var (önskemål 2026-11-09): Man drar in en mapp, eller flera filmer och SRT-filer. Programmet parar ihop dem efter avsnittsnummer i namnet (S04E01, 4x01 m.m.), visar parningen så att man kan rätta den, synkar dem i kö och sparar varje fil bredvid sitt avsnitt.
   - Visa en sammanfattning per avsnitt efteråt: klart, osäkert (se punkt 7) eller misslyckat med en svensk förklaring.
   - Avbryt ska stoppa kön efter det avsnitt som pågår och behålla det som redan är klart.
6. ~~**Granska i filmen.**~~ **Klart 2026-11-10**, se beslutsloggen. Planen var: Man klickar på en rad i listan och spelar upp filmen några sekunder runt repliken, med den synkade texten ovanpå. Det kontrollerar att texten stämmer med det som sägs, inte bara att tiderna har ändrats.
   - Undersök ffplay (från samma ffmpeg-källa som i dag, fast URL + SHA-256) mot att öppna en vanlig spelare (VLC/mpv) med tid och textfil. ffplay kräver ingen installation hos vännerna.
   - Texten ska visas från osparade ändringar (tillfällig SRT), så att man kan granska före sparandet.
7. ~~**Säg hur säker synken är.**~~ **Klart 2026-11-10**, se beslutsloggen. Planen var: Efter synken visas ett tydligt besked: grönt "Bra", gult "Kontrollera de markerade ställena" eller rött "Osäkert". Gult och rött har knappar för att hoppa till föregående och nästa svaga ställe i listan.
   - Bygg på det motorn redan räknar ut (hur väl varje mätpunkt passar, hopp mellan avsnitt). Gränserna sätts mot facit i testerna, så att "Bra" verkligen betyder bra.
   - Inga siffror i huvudbeskedet. Siffrorna kan finnas i loggen.
8. **Läsbarhet** (önskemål 2026-11-09). Programmet hittar rader som visas för kort tid, går för fort att läsa (tecken per sekund), överlappar varandra eller är för långa (riktvärde cirka 42 tecken per rad), och föreslår rättningar. Förslagen granskas innan de används, som förslagen om att dela och slå ihop rader, och kan ångras.
9. **Spara så att spelaren hittar texten** (önskemål 2026-11-09). När referensen är en film sparas texten redan som `Filmnamn.sv.srt` bredvid filmen (`DefaultSrtPath` i `MainForm.cs`). Det som återstår:
   - När referensen är en SRT föreslås i dag `Namn.synkad.srt`, som spelare inte hittar. Finns en film med samma avsnitt i mappen föreslås `Filmnamn.sv.srt` i stället.
10. ~~**Ta bort text för hörselskadade, som en inställning.**~~ **Klart 2026-11-10**, se beslutsloggen. Planen var: En kryssruta, till exempel "Ta bort text för hörselskadade ([MUSIK], (skrattar), talarnamn)", bredvid "Upptäck bildfrekvensfel". Valet sparas med `Tools.SetSetting` i `settings.txt`, så att det gäller nästa gång också.
    - Det som tas bort: hela rader som bara är ljudbeskrivning (`[…]`, `(…)`, ♪/♫), ljudbeskrivning inuti en rad, och talarnamn i versaler först på raden (`JOHN:`, `- MARIA:`). Rader som blir tomma tas bort, och dialogstreck rättas när bara en talare blir kvar.
    - Gäller både texten som sparas och texten som synkas, eftersom ljudbeskrivningar inte motsvarar tal och kan störa ljudsynken.
    - Visa hur många rader som ändrades eller togs bort, och gör ändringen ångringsbar som annan redigering.
    - Tester: en `Check(...)` per mönster ovan, plus kontroller av att vanlig dialog med parenteser eller versaler (t.ex. "FBI" eller "Han (min bror) kom hem") inte förstörs.
    - Rutan är inte ikryssad från början (ägarens svar 2026-11-10).
11. ~~**Varna för fel avsnitt.**~~ **Klart 2026-11-10**, se beslutsloggen. Planen var: Programmet varnar innan det synkar om texten verkar höra till ett annat avsnitt eller en annan version av filmen. Ett exempel är S04E02.srt mot S04E01.mkv. Varningen stoppar inte synken: rutan har knapparna "Synka ändå" och "Avbryt".
    - Kontroller: avsnittsnummer i filnamnen skiljer sig (samma tolkning som i punkt 5), textens längd skiljer sig mycket från filmens längd, eller synken hittar ingen tydlig matchning alls (den globala sökningen har ingen tydlig topp).
    - Meddelandet ska vara på svenska och säga vad som inte stämmer, t.ex. "Texten verkar höra till avsnitt 2, men filmen är avsnitt 1."
    - I kön för en hel säsong (punkt 5) blir varningen ett "osäkert"-besked för avsnittet i stället för en ruta som stoppar kön.
    - Gränserna sätts mot testerna och testfilmen, så att varningen inte visas för vanliga filmer med förtexter eller eftertexter som saknar text.

## Kvar efter betydelsejämförelsen 2.4.6 (2026-11-11)
Ägaren provar först, eftersom verktygen inte får köras i Claudes miljö:
1. Kryssa i **Jämför också vad raderna betyder** i Inställningar och synka Blacklist S04E01 mot det engelska spåret. Svara ja på nedladdningen.
2. Titta på rutan **Betydelse:** i resultatet och på raderna runt 03:30 och 07:18, där synken tidigare hade korta klipp.
3. Skicka `log.txt`. Raden "Betydelsejämförelse:" visar antal par, gränserna, nivåerna och tiden. Med den går gränserna att justera.

Det som inte är gjort:
- **Inte mätt mot facit.** Ett mätprogram i `test/` (som `OrigSrtDiag`) som förstör det engelska spåret med flit och jämför med och utan jämförelsen.
- **Bara processorn.** Paketet för NVIDIA (cirka 700 MB till) väntar tills tiden på processorn är känd. Tar det flera minuter per avsnitt är det värt att lägga till.
- **Ingen varning om att det tar tid** första gången, som den för Whisper på processorn. Skriv in en uppskattning i Inställningar när tiden är mätt.
- **Frågan om nedladdning nämner inte Smart App Control.** llama-server är inte signerat, så 440 MB kan hämtas och sedan blockeras. Felet förklaras då i resultatet. Förslag: kryssa ur rutan automatiskt efter första blockeringen, som `gpuwhisperfailed` gör för Whisper.
- **Raderna före första och efter sista säkra paret** får samma flytt som kanten, även om de ligger långt bort i tid. Titta på det i första riktiga loggen.
- **Frågor till ägaren från granskningen:** (1) Ska jämförelsen gälla Hel säsong redan nu, där resultatet sparas utan att någon ser det, eller bara huvudfönstret tills den är provad? Nu gäller den båda. (2) Räcker 10 s som längsta flytt? Synkmotorns finjustering har 6 s.
- **Flyttade rader märks inte i listan.** Kolumnen Flyttad visar hoppet, men inget märke säger att det var betydelsejämförelsen.
- **Beskedet** (Bra / Kontrollera / Osäkert) använder inte parningen. Få par är ett tecken på fel avsnitt och kunde ge Osäkert.
- **Förslagen om att dela och slå ihop rader** jämför fortfarande bara tider och längd. Parningen efter betydelse kunde göra dem säkrare.
- **Mot ljud:** Whispers text kunde paras med den svenska på samma sätt (Ljud B och A+B).
- **Cachen rensas aldrig.** Varje text tar några MB i `%LOCALAPPDATA%\TextSync\cache`.
- **Övriga idéer från samma samtal:** mäta på fler filmer, rätta den fasta avvikelsen på cirka 0,24 s mot ljud, Silero-taldetektering i Ljud A, flera referenser som röstar, scenklipp med ffmpeg, exakt ordtid mot ljudet (wav2vec2).

## Beslutat 2026-11-10, inte byggt än
- **Rekommendera Ljud A utan NVIDIA-grafikkort:** en kort förklaring bredvid valet när datorn saknar NVIDIA-kort.
- **Markera rättningar från Whisper-kontrollen i listan**, så att man kan granska dem.
- **Testutdata på svenska** ("offset", "cuts", "Route" m.fl.): översätts efter hand när testfilerna ändras.

## Kvar från granskningarna av Hel säsong 2026-11-11 (kan vänta)
- **Inte provat mot en riktig säsong**, mot ljud i kön eller med riktiga klick. Se beslutsloggen 2026-11-11.
- **Två serier i samma mapp:** parningen tittar bara på säsong och avsnitt, inte serienamnet. Fel text blir oftast Osäkert och sparas inte. Förslag: jämför namnet före SxxEyy, och visa serienamnet i kolumnen Avsnitt.
- **Undertexter i en mapp per avsnitt** (`Subs\Show.S04E01\3_Swedish.srt`) paras inte, eftersom numret står i mappnamnet och "Swedish" inte är en känd språkkod.
- **Avsnittstitlar som slutar på ett språkord** ("…Say.No.srt") tolkas som språkkod (`GuessLang`) och blir aldrig mål.
- **Två rader med samma utfil** (t.ex. `X.mkv` och `X.mp4`) varnas inte för. Den andra skriver över den första (den första finns i återställningen).
- **Textspår med nästan ingen text** gör att kön går över till ljudet utan att verktygen har kontrollerats först. Saknas ffmpeg blir avsnittet Misslyckades.
- **Återställ… visar ingen lista över omgångarna.** Den tar den senaste, och säger efteråt om en äldre finns. Återställ avsnittet finns bara medan programmet är öppet.
- **Efter Öppna i huvudfönstret och Spara SRT…** står raden kvar som Kontrollera, och den filen räknas sedan som ändrad av användaren (återställs inte).
- **Ingen länk Visa i mappen** i fönstret. Sökvägen står under listan när raden är markerad.
- **Byter man utseende** (ljust/mörkt) medan listan är fylld töms listan nästa gång fönstret öppnas. Det som är sparat går fortfarande att återställa.
- **Kopian i `Text Sync original`** kan vara en tidigare synkad fil, om samma avsnitt synkas om efter att kopian i den mappen har tagits bort.
- **Nya filer har radslut LF** (`Season.cs`, `SeasonSync.cs`, `SeasonDialog.cs`), övriga CRLF. Kompilatorn bryr sig inte.

## Kvar från product-check 2026-11-10 (kodade tecken och taggar, kan vänta)
- **Inte provat mot en riktig SRT-fil** med kodade tecken från en undertextsajt, eller på en TV som visar taggar som text.
- **Avvisade förslag kommer tillbaka** när rutan "Ta bort kursiv och annan formatering" kryssas i eller ur, eftersom förslagets nyckel (`AlignProposal.Key`) innehåller radens text.
- **Inte provat mot riktiga filer** med koder från ASS (`\N`, `{\i1}`), MicroDVD (`{y:i}`) eller webbsidor (`<p>`, `<span>`, `<a>`).
- **Text för hörselskadade** (`src/Sdh.cs`) räknar fortfarande allt mellan `<` och `>` som en tagg.

## Kvar från product-check 2026-11-10 (text för hörselskadade, kan vänta)
- **Inte provat mot en riktig text för hörselskadade** (svensk eller engelsk). Prova en och se att rätt saker tas bort.
- **Originaltexten** (separat SRT på filmens språk) rensas inte, så "Spara synkad originaltext" behåller `[MUSIC]`.
- **Ingen förhandsvisning** av vad som tas bort före synken. Efter synken syns ändringen i listan och kan ångras.
- **Namn med små bokstäver** ("John:") och `#` som musiktecken tas inte bort.

## Kvar från product-check 2026-11-10 (uppspelning, kan vänta)
- **Uppspelningen är inte provad med riktiga verktyg.** Se beslutsloggen 2026-11-10 för vad ägaren ska prova.
- **Kraschar Text Sync** medan ett klipp spelas blir ffplay-fönstret kvar överst tills klippet är slut (högst cirka 20 s) eller man trycker Esc.
- **Filer utan bild** (t.ex. `.mka`) går inte att spela upp. Felmeddelandet säger att filmen kan sakna bild.
- **Smart App Control** kan blockera ffmpeg och ffplay hos vännerna. Då visas en förklaring, och uppspelningen går inte att använda där.
- **Tangenten P** står i hjälpen och i högerklicksmenyn, men inte bredvid knappen.

## Kvar från product-check 2026-11-10 (besked och varning för fel avsnitt, kan vänta)
- **Beskedet mot ljud är inte mätt mot en riktig film.** Ett fel avsnitt ger där troligen Kontrollera eller Bra, inte Osäkert. Texten vid Bra säger därför att beskedet är mindre säkert mot ljud.
- **Ett riktigt grannavsnitt** (t.ex. S04E02 mot S04E01) har inte provats. Gränserna bygger på Blacklist S04E01 med texten baklänges och på syntetiska data.
- **Avsnittsnummer som inte tolkas** (ingen varning, inget falsklarm): "S04 - E01", "Season 4, Episode 1", "E05", "Avsnitt 5", "401". Dubbelavsnitt (`S04E01E02` mot `S04E02.srt`) ger falsklarm.
- **Originaltexten** (separat SRT på filmens språk) kontrolleras på filnamn men inte på längd.
- **Motorns poäng och anmärkningar räknas inte om** när man redigerar rader. Beskedet kan därför inte bli Bra efter en rättning om motorn lämnade en anmärkning.
- **Kolumnen #** kan bli för smal för "⚠ Ihop? 1234".

## Kvar från product-check 2026-11-09
Alla punkter är åtgärdade i 2.1 (se beslutsloggen 2026-11-09, "Rättningar från product-check"). Det som återstår:
- **Sökvägar med tecken utanför västeuropeiska:** skyddet (korta 8.3-namn till whisper-cli) är testat i `Tests`, men whisper-cli har inte körts med en sådan sökväg, eftersom nedladdade program inte får köras i Claudes miljö. Prova en gång med en mapp som heter till exempel `Жора` och en riktig film. Är korta namn avstängda på disken fungerar skyddet inte, och då står det i loggen.
- **Listan Ljudspår för MP4 och andra format än MKV** läses med ffprobe: direkt om ffmpeg finns, annars strax efter att ffmpeg har laddats ner. Kommer ffprobes svar medan en synk pågår visas listan inte för den filen förrän den läses in igen (kan vänta).
- **Turbo-modellen erbjuds inte** den som först körde Whisper på processorn (small) och sedan väljer grafikkort. Small används då även på grafikkortet (kan vänta, ägaren bör välja).
- **Diskutrymmet för den tillfälliga ljudfilen** (cirka 230 MB för 2 timmar film i `%TEMP%`) kontrolleras inte. Fullt utrymme ger felmeddelandet om att filen inte kunde läsas eller skrivas (kan vänta).
- **Ordningen på ljudspåren** i MKV antas vara samma som ffmpegs `0:a:n`. Det stämmer enligt Matroska och ffmpeg, men är inte provat på en riktig film med flera ljudspår.

## Kvar från ombyggnaden av gränssnittet 2026-11-10 (kan vänta)
- **Litet fönster:** i det minsta fönstret (940 × 600 vid 100 %, till exempel hela skärmen på en bärbar dator med 1920 × 1200 och 200 % skalning) syns nästan ingenting av listan efter en synk. Förslag: göm tidslinjen när höjden inte räcker, eller låt hela fönstret rulla.
- **Skalning:** 125 % och 150 % är inte provade efter ombyggnaden.
- **Frågor till ägaren från granskningen:** (1) Ska programmet fråga innan en ny fil eller en ny synk kastar ändringar som är gjorda för hand? Klickytan för att välja fil är stor nu. (2) Ska Detaljer vara öppet första gången?
- **Tomt läge:** Synka är grå utan förklaring när en fil saknas. Förslag: "Välj en referens och en undertext först." i statusraden.
- **Fel fil i en ruta** står kvar som vald, med röd text under. Förslag: visa "Ingen fil vald" eller behåll den förra filen.
- **Maximerat läge glöms** om fönstret stängs medan det är minimerat. Storleken sparas i bildpunkter och följer inte en ändrad skalning.
- **Meningen efter beskedet:** `LayoutResult` räknar med fasta bildpunkter (20, 300 och marginalerna 4 och 14), så vid 200 % kan meningen brytas på smala rader i stället för att flyttas ned, i ett fönster nära gränsen.
- **Dialogerna** (`SettingsDialog`, `CueDialog`, `ConfirmDialog`) slår på skalningen överst i konstruktorn, som huvudfönstret gjorde. De är enklare och har inte visat fel, men kan rättas på samma sätt.
- **`CreateGraphics()` i konstruktorn** skapar fönsterhandtaget mitt i bygget bara för att läsa skalningen. `DeviceDpi / 96f` räcker.
- **Test som saknas:** ett `Check` som bygger fönstret med faktor 2 och jämför marginalerna i ruta 1 och 2 hade fångat skalningsfelet.

## Frågor till ägaren
Obesvarade frågor står i README.md under "Obesvarade frågor (tas upp vid nästa körning)". Flytta svaren hit eller till beslutsloggen när de är besvarade.

## Kända begränsningar
- **Mörkt läge:** meddelanderutor (MessageBox, t.ex. Hjälp), kryssrutans ruta och pilarna i talrutan för extra justering förblir ljusa. Windows ritar dem själv.
- **Förslag om att dela och slå ihop rader** jämför bara tider och längd, inte innehåll. En delning kan därför ibland hamna vid fel radbrytning. Därför är det förslag som granskas, inte en automatisk ändring. Tabellen räknas om helt vid varje uppdatering av listan, även vid varje klick i extra justering: cirka 10 ms för ett avsnitt, uppskattningsvis 100 ms för en långfilm. Räkna bara celler nära diagonalen om det känns trögt.
- **Korta klipp med ljud som referens** (t.ex. 10 minuter) kan hitta fel startpunkt, eftersom det pratas nästan hela tiden och den globala sökningen då blir tvetydig. Hela filmer och avsnitt fungerar (mediafel 0,18 s i test).
- **B (Whisper ensam)** ger grova tider (mediafel 0,47 s men bara 50 % inom 0,5 s). Använd A+B.
- **Whisper-kontrollen** kan bara upptäcka fel i textspåret som är större än ungefär 1 s, eftersom Whispers ordtider är för brusiga för mindre fel. Mindre fel finjusterar den vanliga synken.
- **Smart App Control** blockerar ibland nybyggda program och DLL-filer en stund (se docs/operations.md).
