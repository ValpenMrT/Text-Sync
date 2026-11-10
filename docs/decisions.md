# Beslutslogg

Nyast först.

## 2026-11-11 – 2.4.5: Knappen Lägg in i MKV… i fönstret Hel säsong
- **Område:** GUI
- **Önskemål (ägaren):** "Jag ser ingen knapp för Lägg till i MKV för en hel säsong. Kan du lägga till denna funktionen." I 2.4.3 fanns funktionen bara som en kryssruta ovanför listan, och den hittade ägaren inte.
- **Beslut:**
  - **Knappen Lägg in i MKV…** (Alt+L) står längst ner i fönstret, mellan Synka alla och Återställ…, som i huvudfönstret: först synka, sedan lägga in. Den gäller alla avsnitt som har en sparad undertext bredvid filmen, också de som står som Redan synkad, och använder SRT-filen som ligger på disken.
  - **En ruta frågar först:** hur många avsnitt det gäller, hur mycket plats kopiorna behöver och hur mycket som är ledigt, och att Återställ… tar bort dem. Knapparna är "Skapa kopiorna" och "Avbryt".
  - **Avsnitt som redan har en kopia hoppas över**, och rutan säger hur många.
  - **Avbryt** fungerar som under synken (samma knapp, två steg). En halvfärdig kopia tas bort.
  - **Beskedet på raden** får tillägget "· MKV skapad" eller "· ingen MKV", med skälet under listan.
  - **Kryssrutan finns kvar**, med ny text: "Lägg in i MKV direkt vid synken (kopia av filmen)". Den är för den som vill ha allt gjort i ett steg.
- **Teknik:** `MuxAll`, `HaveMkvMerge` och `GetRun` i `SeasonDialog.cs`, `SeasonSync.MuxSaved`.
- **Test:** 208 av 208 kontroller (3 nya, med riktig mkvmerge: kopian skapas av den sparade undertexten, skrivs inte över, tas bort av Återställ). Provat i programmet (fönstret i samma process, 200 %, mörkt läge): synk av en påhittad säsong, klick på knappen, frågan, tre kopior skapade, och Återställ… som tog bort kopiorna och undertexterna (6 filer).
- **Ej provat:** riktiga filmer på flera GB, Avbryt medan en kopia skapas, för lite plats, och avsnitt som står som Redan synkad.
- **Product-check:** Claude granskade själv (en knapp till en funktion som redan fanns och var testad).
- **Nivå:** LIVE

## 2026-11-11 – 2.4.4: Tio omgångar räcker för återställning av Hel säsong
- **Område:** filer
- **Fråga:** *De tio senaste omgångarna går att återställa, och äldre tas bort automatiskt. En omgång är en hel säsong (en mapp) med alla sina avsnitt. Räcker tio?* **Svar (ägaren):** "Ja det räcker."
- **Beslut:** ingen ändring i programmet. Gränsen står kvar på tio omgångar (`SeasonUndo.Prune(9)` före en ny omgång i `SeasonDialog.cs`), och omgångar utan något kvar att återställa räknas inte.
- **Nivå:** LIVE

## 2026-11-11 – 2.4.3: Hel säsong – ägarens svar: välja om osäkra sparas, välja ljudmetod, MKV-kopior
- **Område:** GUI / filer
- **Frågor och svar** (frågorna stod i README efter 2.4.2):
  - *Osäkra avsnitt sparas inte. Ska de sparas ändå?* **Svar:** "Låt mig välja."
  - *Film utan textspår synkas mot ljud A, och A+B bara om Whisper redan finns och ett NVIDIA-kort är valt. Rätt?* **Svar:** "Ja men informera användaren och ge alternativ att ladda ner det som behövs."
  - *Räcker det att de tio senaste säsongssynkarna går att återställa?* **Svar:** en motfråga, "Menar du hela säsonger eller episoder?" Hela säsonger. Besvarad i posten ovanför (2.4.4).
  - *Står knappen Hel säsong… på rätt plats?* **Svar:** ja.
  - *Behövs Lägg in i MKV… för en hel säsong?* **Svar:** "Ja det behövs. Både SRT och mkv."
- **Beslut:**
  - **Kryssrutan "Spara även osäkra avsnitt"** ovanför listan, av från början, sparas till nästa gång (`seasonunsure`). Med kryss sparas avsnittet med beskedet "✖ Osäkert – sparad ändå". Kryssar man i rutan efter en kö hamnar de osäkra avsnitten i kön igen.
  - **Fråga före kön när avsnitt saknar textspår** och A+B inte går att använda direkt: rutan säger hur många avsnitt det gäller och har knapparna "Ladda ner och använd A+B" (eller "Använd A+B"), "Använd A" och "Avbryt". Den säger också om A+B blir långsamt utan NVIDIA-kort. Själva nedladdningen frågar som förut, med storleken.
  - **Kryssrutan "Lägg också in undertexten i en kopia av filmen (MKV)"**, av från början (`seasonmkv`). Varje avsnitt får då SRT-filen och `Filmnamn (Text Sync).mkv` bredvid filmen, samma namn och spårinställningar som Lägg in i MKV… i huvudfönstret. Originalfilmen ändras aldrig.
  - **En MKV-kopia som redan finns skrivs aldrig över.** En kopia på flera GB kan inte sparas undan för återställning, så raden säger att den finns och att man tar bort den själv.
  - **Plats:** före kön jämförs filmernas sammanlagda storlek med ledigt utrymme, och varje avsnitt kontrolleras igen. Får kopian inte plats får avsnittet bara SRT-filen, med beskedet Kontrollera.
  - **Återställ… tar bort MKV-kopiorna.** De känns igen på storlek och ändringstid (SHA-256 av en film tar en halv minut). En kopia som har ändrats rörs inte. Bekräftelserutan säger hur många kopior som tas bort.
- **Teknik:** `SeasonOptions.SaveUnsure` och `MakeMkv`, `SeasonSync.Mux` och `MkvCopyPath`, `SeasonUndo.RegisterCreated`, `ConfirmDialog.Choose` (två val och Avbryt).
- **Test:** 205 av 205 kontroller (8 nya), bland annat en riktig MKV-kopia med mkvmerge: skapad med undertexten inlagd, originalet orört, inte överskriven andra gången, borttagen av Återställ. Fönstret provkört i samma process med de nya kryssrutorna (skärmbild, 200 %, mörkt läge).
- **Ej provat:** rutan med de tre knapparna (bara koden är läst), nedladdning efter valet A+B, MKV-kopior av riktiga filmer på flera GB, Avbryt medan en kopia skapas, och varningen om för lite plats.
- **Product-check:** Claude granskade själv (ägarens svar på ställda frågor, båda valen är av från början så att 2.4.2 beter sig som förut).
- **Nivå:** LIVE

## 2026-11-11 – 2.4.2: Hel säsong – synka alla avsnitt i kö, med återställning
- **Område:** GUI / filer
- **Önskemål (ägaren):** "göra så jag kan synka en hel säsong av en serie istället för att behöva göra det avsnitt för avsnitt. Gör så det går att återställa om något inte blir bra." Punkt 5 i `docs/next.md`.
- **Beslut:**
  - **Eget fönster, Hel säsong…** (knapp bredvid Inställningar…, Alt+H). Det öppnas också när man drar in en mapp eller minst två filmer i huvudfönstret. Huvudfönstret är orört i övrigt.
  - **Parning efter avsnittsnummer** i filnamnen (S04E01, 4x01), eller efter att undertexten heter som filmen. Undermappar tas med (tre nivåer, t.ex. `Subs`). Listan visar ett avsnitt per rad och går att rätta: Byt undertext…, Hoppa över.
  - **Vilken undertext som synkas:** den med språkkoden i huvudfönstret, annars en utan språkkod. En text på ett annat språk blir aldrig mål. Den vanliga texten går före text för hörselskadade och tvingad text.
  - **Referens:** filmens engelska textspår (samma val som huvudfönstret). Saknas textspår, eller har det bara tvingad text, används filmens ljud: A, eller A+B om Whisper redan finns och ett NVIDIA-kort är valt. Ljudet kan väljas för alla avsnitt i listan Referens. Verktygen hämtas en gång före kön.
  - **Sparas** som `Filmnamn.sv.srt` bredvid filmen, med samma teckenkodning som Spara SRT…. Metod, bildfrekvensfel, text för hörselskadade och formatering följer huvudfönstret.
  - **Besked per avsnitt:** Bra – sparad, Kontrollera – sparad, Osäkert – inte sparad, Misslyckades. Varningen för fel avsnitt eller olika längd blir Kontrollera i stället för en ruta som stoppar kön. Förklaringen står under listan när raden är markerad.
  - **Osäkra avsnitt sparas inte.** En dålig text bredvid filmen plockas upp automatiskt av spelaren, och det är värre än ingen text.
  - **Avbryt i två steg:** första klicket stannar efter avsnittet som pågår, andra klicket (Avbryt nu) avbryter direkt. Det som är sparat ligger kvar, och Synka resten fortsätter.
  - **Återställ…** byter tillbaka varje undertext mot filen som fanns före och tar bort undertexter som inte fanns före. En bekräftelseruta säger dag, mapp och antal. Återställ avsnittet gör samma sak för en rad.
  - **Återställningen fungerar efter omstart.** Varje omgång (en mapp) har en egen mapp i `%LOCALAPPDATA%\TextSync\undo` med kopior av det som skrevs över och en lista med SHA-256 för det som sparades. De tio senaste omgångarna sparas.
  - **En fil som användaren har ändrat efter synken rörs aldrig** vid återställning (annan SHA-256), och tas aldrig bort. Programmet säger vilka filer det gäller. Filmer rörs aldrig.
  - **Redan synkade avsnitt känns igen** när mappen läggs till igen (samma SHA-256 som i listan): de står som Redan synkad och synkas inte om, och den osynkade filen väljs som undertext i stället för programmets egen.
  - **Öppna i huvudfönstret** läser in avsnittets film och den osynkade undertexten och synkar, så att raderna går att granska och spela upp.
- **Arbetssätt:** ägaren bad om hjälp av subagenter. En byggde parningen och återställningen med tester i ett eget arbetsträd, medan huvudsessionen byggde kön och fönstret. Två granskade sedan oberoende: koden (dataförlust, trådar) och upplevelsen (sju scenarier som en icke-teknisk användare).
- **Rättat efter granskningarna:** redan synkade filer känns igen (förut skulle "Synka alla" ha synkat om allt dagen efter); Språkkod "swe" blir "sv" och sätts tillbaka efter Öppna i huvudfönstret; en omgång per mapp, och Töm listan börjar en ny (förut kunde Återställ… ångra två säsonger under ett namn); en fil som ändrats för hand mellan två synkar får en egen kopia; omgångar utan sparade filer tränger inte undan äldre; text för hörselskadade väljs inte före den vanliga texten; listan kan inte bytas ut medan kön går; dubbelklick på Avbryt blir inte Avbryt nu; frågan om Whisper på processorn kommer före nedladdningen; förklaringen visas under listan och inte bara i tipsrutan; raden säger före synken att en fil skrivs över.
- **Teknik:** `src/Season.cs` (`SeasonPlan`, `SeasonUndo`), `src/SeasonSync.cs` (`ChooseRef`, `SyncOne`), `src/SeasonDialog.cs`. I `MainForm`: `OpenSeason`, `LooksLikeSeason`, `PickTextTrack`, `PrepareGpu`, och `EnsureTools` tar emot vilket fönster som frågar.
- **Test:** 197 av 197 kontroller (67 nya): parning, `Collect`, återställning, och hela kön mot tre påhittade avsnitt med MKV-textspår (förskjutning, fel bildfrekvens, fel text → Osäkert, avbrott, Ta bort formatering, återställning). Provat i programmet (fönstret i samma process, 200 % skalning, mörkt läge, skärmbilder): parad lista, kön, besked, bekräftelserutan, återställt, och samma mapp tillagd igen (Redan synkad).
- **Ej provat:** en riktig säsong (bara testfilmens enda avsnitt finns på datorn), synk mot ljud i kön, nedladdning av verktyg från det här fönstret, riktiga klick och dra-och-släpp av en mapp, Öppna i huvudfönstret, Byt undertext…, Avbryt under en riktig kö, ljust läge, högkontrast, 100–150 % skalning.
- **Product-check:** två oberoende granskningar, inget blockerande kvar. Frågor till ägaren står i README.md under Obesvarade frågor. Kvarstående fynd står i `docs/next.md`.
- **Nivå:** LIVE

## 2026-11-10 – 2.4.1: val av hur SRT-filen sparas, Lägg in i MKV blir Avbryt, versionsnummer i varje commit
- **Område:** GUI / filformat
- **Önskemål (ägaren):** en inställning för hur SRT-filen sparas, så att tecken utanför ASCII (å, ä, ö med flera) visas rätt i olika spelare. Lägg in i MKV… ska bli en avbrytknapp när man trycker på den. Versionsnumret ska öka i varje commit.
- **Frågor och svar:**
  - *Kan man blanda formaten för att öka kompatibiliteten?* Nej, en fil har bara en teckenkodning. UTF-8 med BOM är det som fungerar på flest ställen: nya spelare struntar i BOM, och äldre TV-apparater behöver den för att förstå att filen är UTF-8. Programmet sparade redan så, och det är fortfarande förvalt.
  - *Vilka val ska finnas?* **Svar:** tre.
  - *Hur väljs teckentabellen för mycket gamla spelare?* **Svar:** automatiskt efter texten.
  - *Hur ska versionsnumret öka?* **Svar:** ett tredje led per commit (2.4.1, 2.4.2 …). 2.5 sparas till större ändringar som ägaren säger till om.
  - *Behövs fler teckenkodningar i listan i ruta 2?* Nej, inte för nordiska och europeiska texter. Automatisk klarar UTF-8, UTF-16 och gissar mellan västeuropeisk, centraleuropeisk, kyrillisk och grekisk. Det som saknas är hebreiska, arabiska och östasiatiska tabeller, och de läggs till om någon behöver dem.
  - *Upptäck bildfrekvensfel ikryssad som standard?* Den är redan det (`fps` saknas i settings.txt tills man ändrar den, och då gäller ikryssad). Ingen ändring behövdes.
- **Beslut:**
  - **Inställningar → Spara → "SRT-filen sparas för":** De flesta spelare (UTF-8 med BOM, förvalt), UTF-8 utan BOM, eller Mycket gamla spelare (Windows-teckentabell). Sparas i settings.txt (`srtformat` 0, 1 eller 2). Ett annat val än det förvalda står på raden under Metod, så att det inte glöms.
  - **Mycket gamla spelare:** programmet väljer den tabell som saknar minst tecken i texten, i ordningen 1252, 1250, 1251, 1253, 1254, 1257. Tecken som saknas byts mot liknande (… mot ..., typografiska citattecken mot ", tankstreck mot -, ♪ mot #), annars mot ?. Efter Spara SRT… säger statusraden vilken tabell som valdes och hur många tecken som byttes. Texten i programmet ändras inte, bara filen.
  - **Gäller** Spara SRT…, Spara synkad originaltext… och Spara igenkänd text…. **Gäller inte** Lägg in i MKV… (MKV kräver UTF-8) och programmets egna tillfälliga filer.
  - **Lägg in i MKV… är Avbryt** medan MKV-filen skapas, med samma skydd mot dubbelklick som Synka. Synka-knappen är då avstängd. Vid synk och nedladdning är det som förut Synka som är Avbryt.
- **Teknik:** `Srt.Encode`, `Srt.SaveFormat` och `Srt.CodepageName` i `Engine.cs`, `WriteSafely` och `LastSaveNote` i `MainForm.Save.cs`, `busyBtn` i `MainForm.cs`, listan i `SettingsDialog.cs`.
- **Test:** 130 av 130 kontroller (9 nya: BOM och inte BOM, svensk text i 1252, rysk i 1251, polsk i 1250, utbytta tecken, a + ring, och hela vägen via inställningen). Provat i programmet (formuläret i samma process, 200 % skalning): Lägg in i MKV… som Avbryt och Avbryter…, knapparna tillbaka efteråt, och dialogen Inställningar med det nya valet.
- **Ej provat:** en riktig gammal spelare, ett riktigt MKV-arbete som avbryts, och klick i dialogen (valet sattes av testet).
- **Product-check:** Claude granskade själv (en ny inställning med förvalt värde som förut). Kan vänta: statusraden efter Spara synkad originaltext… och Spara igenkänd text… säger inte vilken teckentabell som valdes (det står i loggen).
- **Nivå:** LIVE

## 2026-11-10 – Två oberoende granskningar av ombyggnaden: orsaken till skalningsfelet och rättningar
- **Område:** GUI
- **Bakgrund:** ägaren bad om hjälp av subagenter. En granskade layoutkoden och en granskade ändringen som en icke-teknisk användare. Ingen hittade något blockerande.
- **Orsaken till dubbelskalningen är hittad och rättad.** Skalningen (`AutoScaleDimensions`, `AutoScaleMode`) slogs på överst i konstruktorn. Då skalades varje delträd direkt när det lades till, och en kontroll som fick ny höjd däremellan skalades en gång till. Därför fick rubriken i ruta 1 (lång text, ny höjd) marginalen 24 i stället för 12, men inte ruta 2. Skalningen slås nu på sist i konstruktorn, så att allt skalas exakt en gång. Följd vid 200 %: beskedet under filnamnet i ruta 1, raden med Synka och rutorna längst ner har nu samma luft som vid 100 % (de var för stora förut). Knappen Inställningar… har fått fast bredd, eftersom den med AutoSize blev dubbelt så stor.
- **Rättat efter granskningen av koden:**
  - Avbryt kunde fastna som "Avbryter…" under hela Lägg in i MKV, om man klickade innan mkvmerge hade startat. Processen stoppas nu även då.
  - Högerklick och mittenklick i ruta 1 och 2 öppnade Välj fil. Nu gör bara vänsterklick det.
  - Rutan kunde lysa kvar efter att Välj fil stängdes. Den släcks nu.
- **Rättat efter granskningen av upplevelsen:**
  - En egen siffra visar när text för hörselskadade eller formatering har tagits bort ("Text för hörselskadade borttagen"), med hela beskedet i tipsrutan. Förut stod det bara under Detaljer.
  - "8 avsnitt" heter "Flyttad olika mycket i 8 delar", så att det inte läses som avsnitt av en tv-serie.
  - Procentsiffran har en tipsruta: vad som är normalt, och att de röda raderna bör granskas under 95 %.
  - Hjälpen (`ShowHelp`) nämner Detaljer och Avbryt.
  - "Fält 1/2" heter "ruta 1/2" i felmeddelanden och FAQ.
  - Spara SRT… har Alt+P. Förut delade den Alt+S med Synka.
  - Skärmläsare: Bläddra… och filnamnet säger vilken ruta det gäller och vilken fil som är vald, med hela sökvägen.
- **Test:** 121 av 121 kontroller. Skärmbilder med formuläret i samma process, i 200 % (ägarens maximerade fönster, 3840 × 2259) och i 100 % (1280 × 900): efter synk, Detaljer öppet, knappen som Avbryt.
- **Ej provat:** 125 % och 150 %, högkontrast, riktiga klick och dra-och-släpp, Avbryt under ett riktigt MKV-arbete, och de nya tipsrutorna.
- **Kvar, se `docs/next.md`:** frågor till ägaren (fråga innan osparade ändringar kastas, Detaljer öppet första gången, var Avbryt sitter vid Lägg in i MKV) och mindre fynd.
- **Nivå:** LIVE

## 2026-11-10 – Rättat: ruta 1 och 2 fyllde hela fönstret vid 200 % skalning
- **Område:** GUI
- **Fel (ägaren):** "när man startar programmet så tar val av fil upp hela rutan". På ägarens skärm (3840 × 2400, 200 % skalning, fönstret maximerat) blev ruta 1 och 2 nästan 5 000 bildpunkter höga, så att resten av fönstret hamnade utanför. Felet kom med ombyggnaden samma dag och syntes inte vid 100 % skalning, som skärmbilderna då togs i.
- **Orsak:** rubriken och beskedet i rutorna var etiketter med AutoSize i nästlade tabeller. Vid skalningen mättes de som om de vore några bildpunkter breda, och raderna behöll den höjden. Resultatrutan hade samma fel i mindre skala (tomrum under siffrorna).
- **Beslut:** etiketter som ska bryta text får sin höjd uträknad av programmet efter den bredd de faktiskt har (`FitLabelHeight`), på samma sätt som raderna med knappar och siffror (`FitFlowHeight`). När en höjd ändras görs layouten om strax efteråt (`RelayoutLater`), och en gång när fönstret visas (`RelayoutAll`).
- **Också rättat:** rubriken i ruta 1 fick dubbelt så stor marginal under sig som ruta 2 vid 200 %, så att filnamnen hamnade på olika höjd. Varför marginalen skalades två gånger i just ruta 1 är inte utrett. Luften under rubriken räknas nu ut efter typsnittets höjd i stället.
- **Test:** 121 av 121 kontroller. Provat med formuläret i samma process, nu med samma skalning som det riktiga programmet (`SetProcessDPIAware`) och ägarens sparade fönster (maximerat, 3840 × 2259): tomt fönster, efter synk, Detaljer öppet. Rutorna är lika höga och listan får resten av fönstret.
- **Kvar att veta:** i det minsta fönstret (940 × 600 vid 100 %, alltså hela skärmen på en bärbar dator med 1920 × 1200 och 200 % skalning) syns nästan ingenting av listan efter en synk: rubrik, rutor, resultat, tidslinje och spara-rad tar hela höjden. Det var så även före ombyggnaden. Förslag i `docs/next.md`.
- **Ej provat:** 125 % och 150 % skalning, och att flytta fönstret mellan skärmar med olika skalning.
- **Lärdom för test:** skärmbilder av fönstret ska tas med `SetProcessDPIAware` anropat först, annars körs testet i 100 % oavsett skärm.
- **Product-check:** två oberoende granskningar (layoutkoden och användarens upplevelse) startades samtidigt. Deras fynd står i posten ovanför.
- **Nivå:** LIVE

## 2026-11-10 – Kompakt resultatruta, Synka blir Avbryt, ruta 1 och 2 bredvid varandra
- **Område:** GUI
- **Önskemål (ägaren):** resultatrutan var svårläst och tog plats från listan ("minska ner detta och göra mer plats till själva srt överskådningen"), Synka-knappen ska bli en avbrytknapp när man trycker på den, steg 1 och 2 ska stå bredvid varandra, och man ska kunna klicka var som helst i rutan för att välja fil. En mockup gjordes först och godkändes ("designen såg bra ut. implementera den").
- **Beslut:**
  - **Resultatrutan är två rader i stället för upp till nio.** Rad 1: beskedet (Bra / Kontrollera / Osäkert), en mening, länkarna Föregående/Nästa ställe och knappen **Detaljer**. Rad 2: siffrorna, med samma färger som raderna i listan: procent med referenstext samtidigt (med en liten stapel), röda rader, förslag, rader som bara finns i referensen, rättade rader och förskjutning eller antal avsnitt. I ett smalt fönster hamnar meningen på en egen rad.
  - **Varningar står alltid kvar** under siffrorna (till exempel att MKV-filen är ofullständig). De göms inte under Detaljer.
  - **Detaljer** (Alt+D) fäller ut resten i tre kolumner: hur synken gjordes (metod, förskjutning eller avsnitten), vad färgerna och märkena i listan betyder (bara de som finns just nu) och hur man redigerar i listan. Programmet minns om Detaljer var öppet (`details` i settings.txt). Från början är det stängt.
  - **Synka-knappen är Avbryt medan programmet arbetar** (synk, nedladdning, lägga in i MKV): samma knapp på samma plats, grå med en stoppruta, Alt+A. Efter klicket står det **Avbryter…** och knappen går inte att klicka förrän arbetet har stannat. Den separata Avbryt-knappen är borta.
  - **Skydd mot dubbelklick:** klick på knappen de första 0,7 sekunderna efter starten räknas inte, så att ett dubbelklick på Synka inte avbryter synken direkt.
  - **Ruta 1 och 2 står bredvid varandra** och delar bredden lika. Listorna (Referens, Grafikkort, Ljudspår, Teckenkodning) står under varandra i rutan och fyller dess bredd. Den öppnade listan är så bred som sin längsta rad.
  - **Hela övre delen av rutan väljer fil** (rubrik, filnamn, beskedet under): den lyser upp under muspekaren och öppnar Välj fil vid klick. Bläddra… finns kvar, för tangentbordet. En fil som släpps på en ruta hamnar i just den rutan.
  - **Rutan visar filens namn, inte hela sökvägen.** Sökvägen står i tipsrutan när muspekaren hålls över namnet. Textfältet för sökvägen är borta, så en sökväg kan inte längre skrivas eller klistras in. Dra in filen eller välj den i stället.
- **Rättat på vägen:** raden med Metod och Synka reserverade tomrum under sig (60–95 bildpunkter), eftersom Windows mätte den som om varje del stod på en egen rad. Det är borta, och listan får den platsen också (`FitFlowHeight`).
- **Teknik:** `MakeCard`, `ShowFile`, `MakeChip`, `SetDetails`, `LayoutResult` och `ShowSyncButton` i `MainForm.cs`, `ShowResult` i `MainForm.Sync.cs`. `refBox`, `tgtBox`, `cancelBtn`, `resultLabel` och `audioRow` finns inte längre. Etiketten Grafikkort visas nu framför listan (den skapades förut men lades aldrig in i fönstret).
- **Test:** 121 av 121 kontroller (inga nya, ändringen är bara gränssnitt). Provat i programmet (formuläret kört i samma process, två små SRT-filer, skärmbilder): tomt fönster, efter synk med Bra och med Kontrollera, Detaljer öppet och stängt, knappen som Avbryt och Avbryter…, i 940 × 760, 1280 × 900 och 1500 × 900, mörkt och ljust läge. Listan fick ungefär 105 bildpunkter mer höjd i 1280 × 900 jämfört med första försöket, och mer än så mot den gamla rutan.
- **Ej provat:** riktiga klick och muspekaren över rutorna (bara koden är läst), att släppa en fil på en ruta, Avbryt under en riktig lång synk (lägena sattes direkt), film med textspår, ljudspår och grafikkort i ruta 1, högkontrast och 150 % skalning.
- **Kvar att veta:** i det minsta fönstret (940 × 600) med Detaljer öppet får listan nästan ingen plats. Stäng Detaljer eller gör fönstret större.
- **Product-check:** Claude granskade själv (befintligt innehåll flyttat, inga nya funktioner). Rättat: dubbelklick på Synka avbröt direkt, meningen bröts på fyra rader i ett smalt fönster, tomrum under raden med Synka. Val som Claude gjorde utan svar från ägaren: Detaljer minns sitt läge, och redigeringstipsen står under Detaljer.
- **Nivå:** LIVE

## 2026-11-10 – Fönstrets storlek och plats sparas
- **Område:** GUI
- **Önskemål (ägaren):** "Kom ihåg fönsterstorlek, och om den var maximerad sist."
- **Beslut:** när programmet stängs sparas fönstrets plats, storlek och om det var maximerat (`window` i settings.txt, som `x,y,bredd,höjd,maximerat`). Nästa start öppnas fönstret likadant. Var fönstret maximerat sparas storleken det hade före, så att Återställ ger den tillbaka.
- **Skydd:** fönstret läggs alltid helt inom en skärm som finns. Är den sparade skärmen borta (t.ex. en bortkopplad extern skärm) öppnas fönstret mitt på skärmen där muspekaren är. Det blir aldrig större än skärmen. Första gången, eller om inställningen är trasig, öppnas det som förut (1080 × 1000, mitt på skärmen).
- **Teknik:** `Tools.FormatWindow`, `Tools.ParseWindow` och `Tools.FitWindow`, som används i `MainForm.OnLoad` och `OnFormClosing`.
- **Test:** 121 av 121 kontroller (7 nya). Provat i programmet (formuläret kört i samma process): storleken 1300 × 850 på plats 100,80 kom tillbaka, maximerat kom tillbaka med rätt storlek för Återställ, och en plats utanför skärmen flyttades in. settings.txt återställdes efteråt.
- **Ej provat:** två skärmar på riktigt, och byte av skalning (t.ex. 100 % → 150 %) mellan två starter. Storleken sparas i bildpunkter, så fönstret blir då lika stort på skärmen men rymmer mindre.
- **Product-check:** Claude granskade själv (liten ändring). Rättat: en plats utanför alla skärmar hamnade i skärmens hörn, nu mitt på skärmen.
- **Nivå:** LIVE

## 2026-11-10 – .gitattributes för enhetliga radslut
- **Område:** projektstruktur
- **Fråga (från README, "Obesvarade frågor"):** ska en `.gitattributes` läggas till för enhetliga radslut? **Svar (ägaren):** ja ("skapa en sån fil").
- **Beslut:** `.gitattributes` i projektets rot. Textfiler (`.cs`, `.ps1`, `.md`, `LICENSE`, `.gitignore`) lagras med LF i git och har Windows-radslut (CRLF) i arbetsmappen, oavsett hur git är inställt på datorn. `.ico`, `.exe` och `.png` är binära och rörs aldrig.
- **Varför:** `src/Mkv.cs` hade blandade radslut, git varnade vid varje commit, och en ändring på en rad kunde visas som att hela filen var ändrad.
- **Följd:** inget i programmet ändras. I git låg alla filer redan med LF, så commiten innehåller bara den nya filen och dokumentationen. Arbetsmappens filer hämtades ut på nytt efter commiten, så att alla har CRLF.
- **Test:** 114 av 114 kontroller efter att `src/Mkv.cs` fått enhetliga radslut.
- **Nivå:** LIVE

## 2026-11-10 – Inställningar i en egen ruta
- **Område:** GUI
- **Fråga (från README, "Obesvarade frågor"):** ska inställningarna flyttas till en egen ruta? Raden under fält 2 hade tre kryssrutor och bröts på två rader. **Svar (ägaren):** ja ("implementera alla dina förslag").
- **Beslut:**
  - Knappen **Inställningar…** längst upp till höger (Alt+I) öppnar en ruta med **Utseende**, **Upptäck bildfrekvensfel**, **Ta bort text för hörselskadade** och **Ta bort kursiv och annan formatering**, var och en med en rad förklaring. **OK** sparar och gäller direkt, **Avbryt** ändrar ingenting.
  - **Metod** och **Synka** står kvar i huvudfönstret, eftersom de hör till varje synk.
  - **Under Metod står vilka inställningar som avviker från det vanliga**, till exempel "Inställningar: text för hörselskadade tas bort · bildfrekvensfel upptäcks inte". Raden syns inte när allt är som vanligt. Så glöms en inställning inte bort i rutan.
  - **Allt fungerar som förut** när ett val ändras: text för hörselskadade rensas direkt efter en synk och går att ångra med Ctrl+Z, och formateringen tas bort eller kommer tillbaka direkt.
  - **"Upptäck bildfrekvensfel" sparas nu** till nästa gång (`fps` i settings.txt). Förut var den alltid ikryssad vid start.
  - **Beskedet under fält 2 är kortare** ("Rättat: kodade tecken på 14 rader, formatering på 3 rader. 2 rader utan text borttagna.") och bryts på två rader i ett smalt fönster. Det längre beskedet hamnade under Teckenkodning.
- **Teknik:** kryssrutorna `fpsCheck`, `hiCheck` och `plainCheck` finns kvar i `MainForm` som bärare av valen och deras händelser, men visas inte. `SettingsDialog` läser och sätter dem.
- **Test:** 114 av 114 kontroller. Provat i programmet: rutan öppnad, formatering ikryssad och bildfrekvensfel urkryssad, OK gav direkt "Formatering borttagen på 74 rader", raden under Metod och rätt värden i settings.txt. Öppnad igen visade rutan de sparade valen, och Avbryt ändrade ingenting. Skärmbilder i mörkt läge.
- **Ej provat:** ljust läge och högkontrast (där Utseende inte visas), byte av utseende från rutan, och 150 % skalning.
- **Product-check:** Claude granskade själv (befintliga val flyttade, inga nya). Rättat under arbetet: beskedet under fält 2 krockade med Teckenkodning. Kan vänta: "Föreslå radindelning som referensen" och Visa står kvar ovanför listan, eftersom de hör till listan.
- **Nivå:** LIVE

## 2026-11-10 – Koder från ASS, MicroDVD och webbsidor
- **Område:** text
- **Önskemål (ägaren):** "implementera alla dina förslag", efter listan över taggar och koder som programmet inte rörde (se "Kända gränser" i posten nedan, som den här posten ersätter).
- **Beslut:** alltid vid inläsningen, på samma sätt som övrig lagning (raden märks ✎ och räknas som "trasig formatering"):
  - **`\N`** (radbrytning i ASS) blir en riktig radbrytning.
  - **`{\i1}…{\i0}`, `{\b1}`, `{\u1}`, `{\s1}`** (ASS) görs om till `<i>`, `<b>`, `<u>`, `<s>`. Andra koder i samma klammer står kvar (`{\an8\i1}` → `{\an8}<i>`). Det som inte stängs stängs sist i raden.
  - **`<p>` och `<div>`** tas bort, och där de slutar blir det en ny rad. **`<span>`, `<sup>`, `<sub>`, `<small>`** och **länkar (`<a href=…>`)** tas bort, och texten står kvar.
  - Taggarna från webbsidor räknas bara med små bokstäver, och attribut måste se ut som `namn=värde`. "Kör in på \<P\>", "\<small talk\>" och ett ensamt `<a>` är vanlig text.
- **Med rutan "Ta bort kursiv och annan formatering":** koder från MicroDVD tas också bort (`{y:i}`, `{c:$0000ff}`, `{f:…}`, `{s:…}`, `{P:…}`, högst 40 tecken). Utan rutan står de kvar.
- **Kända gränser:** `\h` (hårt blanksteg i ASS) rörs inte. `<span style="font-style:italic">` blir vanlig text och inte kursiv. Reklamtexten i en länk står kvar (bara taggen tas bort).
- **Test:** 114 av 114 kontroller (4 nya). Inte provat i programmet mot en riktig fil med sådana koder.
- **Product-check:** Claude granskade själv (tillägg till lagningen i posten nedan, samma regler om vanlig text inom vinkelparenteser och om mönster som inte får bli långsamma). Inget blockerande.
- **Nivå:** LIVE

## 2026-11-10 – 2.4: rader utan text tas bort, rättade rader märks, \<br\> blir radbrytning
- **Område:** text / GUI
- **Önskemål (ägaren, svar på det som stod kvar efter posten nedan):** ta bort rader som bara bestod av formatering. Markera vilka rader som har rättats. `<br>` brukar betyda ny rad. Version 2.4.
- **Beslut:**
  - **Rader utan text tas bort.** Blir ingen text kvar när en rad rättas vid inläsningen (den bestod bara av formatering, `&nbsp;` eller `<br>`) tas raden bort, och raden under fält 2 säger hur många ("3 rader utan text borttagna."). En rad som blir tom senare, till exempel `{\an8}` ensam med rutan "Ta bort kursiv och annan formatering" ikryssad, sparas inte (`Srt.ToSrt` hoppar över rader utan text). Det gäller alla filer som programmet sparar.
  - **Rättade rader märks med ✎** i kolumnen # i listan. Verktygstipset på raden visar vad som stod i filen. Resultatrutan säger hur många rader som rättades, och **Visa → Bara rättade rader** visar bara dem. Märkningen följer raden genom synken (`Cue.Raw`). Den försvinner om radens text ändras för hand eller om raden slås ihop eller delas.
  - **`<br>`, `<br/>` och `<BR />` blir en riktig radbrytning**, alltid vid inläsningen. Ingen spelare tolkar `<br>` i en SRT.
  - **`<strike>` och `<del>`** görs om till `<s>`, på samma sätt som `<em>` och `<strong>`.
  - **Version 2.4.**
- **Claudes tolkning:** "markera" tolkades som en märkning i listan plus ett sätt att se bara de raderna och vad som stod före. Rader som togs bort helt visas inte, eftersom de inte hade någon text.
- **Kända gränser:** `<br>` och `<strike>` räknas i beskedet som "trasig formatering". Andra taggar från webbsidor (`<p>`, `<span>`, `<a>`, `<sup>`) och koder från andra undertextformat (`\N` som radbrytning, `{y:i}` från MicroDVD, `{\i1}` från ASS) rörs inte.
- **Test:** 110 av 110 kontroller (4 nya). Provat i programmet med Blacklist-texten med inlagda fel: 37 rader utan text togs bort vid inläsningen, 148 rader märktes med ✎ och visades ensamma med Bara rättade rader, verktygstipset visade originaltexten, och den sparade filen hade 330 block utan `<br>` eller `<strike>`.
- **Product-check:** Claude granskade själv (mindre tillägg till posten nedan). Inget blockerande.
- **Nivå:** LIVE

## 2026-11-10 – Kodade tecken avkodas alltid, taggar lagas, formatering kan tas bort
- **Område:** text / GUI
- **Önskemål (ägaren):** vissa SRT-filer har `&amp;` och liknande HTML-kodade tecken, och taggar som `<i>` visas som text i en del spelare. Avkoda tecknen alltid. Behåll `<i>` som standard och laga trasiga taggar. Ta bort taggar som ett val, för den som tittar på en TV som visar dem som text. Ta hänsyn till fler taggar än kursiv, till exempel fetstil. Gärna som en inställning.
- **Bakgrund:** SRT har ingen standard och är ren text, så kodade tecken hör aldrig hemma där. De kommer från slarviga konverteringar från webbformat (WebVTT, TTML, HTML). Taggarna `<i>`, `<b>`, `<u>`, `<font>` är en inofficiell del av formatet som de flesta datorspelare tolkar men många TV-apparater inte.
- **Beslut:**
  - **Alltid, vid inläsningen** (texten som ska synkas, en SRT som referens, originaltexten och textspår ur MKV): kodade tecken blir riktiga tecken, och trasiga taggar lagas. Raden under fält 2 säger hur många rader som rättades i texten som ska synkas ("Rättat: kodade tecken (t.ex. &amp;amp;) på 14 rader, trasig formatering (t.ex. \<i\> utan avslut) på 3 rader."). Det går inte att stänga av. Text som skrivs i "Ändra rad" och rader från godkända förslag lagas på samma sätt.
  - **Tecken:** namngivna och numeriska koder som slutar med semikolon (`&amp;`, `&lt;`, `&quot;`, `&#39;`, `&#xE9;`, `&eacute;`, också tecken som emoji). `&nbsp;` blir ett vanligt blanksteg. Dubbelt kodade (`&amp;amp;`) avkodas helt. Okända koder, styrtecken och vanliga &-tecken ("AT&T", "Q&A") står kvar.
  - **Taggar som räknas:** bara rena taggar: `<i>`, `<b>`, `<u>`, `<s>` (utan attribut) och `<font …>` med attribut som `color=`. `<em>` och `<strong>` görs om till `<i>` och `<b>`. Allt annat mellan `<` och `>` är vanlig text: "\<I morgon åker vi.\>", "a < b > c", "I <3 you".
  - **Det som lagas:** stora bokstäver och blanksteg i taggen (`<I>`, `</ i>`); en tagg som inte stängs stängs sist i raden; en som stängs utan att vara öppnad öppnas först i raden (efter `{\an8}`); en stängning för mycket tas bort; fel ordning (`<i><b>x</i></b>`); dubbla och tomma taggar. Rester från WebVTT tas bort när det syns att det är WebVTT: `<c.yellow>…</c>` och `<v Anna>…</v>` (stängningen måste finnas) och tidskoder.
  - **Grundregel:** en text utan kodade tecken och med hela taggar lämnas exakt som den är.
  - **Rutan "Ta bort kursiv och annan formatering (för TV som visar <i> som text)"** står bredvid "Ta bort text för hörselskadade". Inte ikryssad från början, och valet sparas (`striptags` i settings.txt). Den tar bort alla taggarna ovan och koder från ASS-formatet som `{\an8}`.
  - **Rutan ändrar inte den synkade texten i minnet**, utan det som visas i listan, spelas upp och sparas (också "Spara synkad originaltext" och "Lägg in i MKV"). Därför kommer formateringen tillbaka när rutan kryssas ur, utan Ctrl+Z, och rutan fungerar likadant före och efter synken.
- **Claudes tolkning:** "trasiga taggar kan lagas" görs alltid och inte som ett val, eftersom en trasig tagg aldrig är avsiktlig. En tagg som stängs utan att vara öppnad tolkas som att formateringen gäller från radens början (vanligt när en rad har delats i två). `{\an8}` behålls när rutan inte är ikryssad, som förut.
- **Kända gränser:** en rad vars text ändras för hand, eller som slås ihop eller delas via ett förslag, medan rutan är ikryssad behåller inte formateringen när rutan kryssas ur (ändras bara tiden behålls den). Okända taggar som `<br>` rörs inte, inte heller av rutan. En rad som bara bestod av formatering blir tom och ligger kvar. Attributen i `<font>` rättas inte (bara taggens namn). En kursiv som börjar i en replik och slutar i nästa lagas replik för replik: den första stängs, och den andra öppnas från början.
- **Test:** 106 av 106 kontroller (18 nya). Provat i programmet med Blacklist-texten med inlagda fel på var tionde rad: 74 rader med kodade tecken och 74 med trasiga taggar rättades vid inläsningen, rutan tog bort formateringen på 185 rader, den sparade filen hade inga taggar eller koder kvar, och formateringen kom tillbaka när rutan kryssades ur. Synken gav beskedet Bra.
- **Ej provat:** en riktig SRT-fil med kodade tecken från en undertextsajt, och en TV som visar taggar som text. "Ändra rad" och godkända förslag med rutan ikryssad är inte provade i programmet.
- **Product-check (oberoende granskning):** inget blockerande kvar. Rättat: vanlig text inom vinkelparenteser som börjar med I, B, U, S, C eller V togs för taggar och försvann ("\<I morgon åker vi.\>", "a < b > c"); ett `<` följt av många blanksteg hängde programmet i 20 sekunder; delade rader och egna ändringar kunde ge halva taggar; "Ändra rad" med rutan ikryssad tog bort formateringen också när bara tiden ändrades; en stängning för mycket gav dubbla taggar; orimligt djupa taggar blåste upp texten; `&#10;&#10;` gav en tomrad mitt i en replik; emoji som tal och `&AMP;` avkodades inte; "trasiga taggar" i beskedet byttes mot "trasig formatering (t.ex. \<i\> utan avslut)". Kan vänta (docs/next.md): tomma rader, avvisade förslag som kommer tillbaka när rutan ändras, visa vilka rader som rättades. Frågan om en egen ruta för inställningar står i README.md.
- **Nivå:** LIVE

## 2026-11-10 – 2.3: större uppspelning, förslagen visas i listan, sök i texten
- **Område:** GUI
- **Önskemål (ägaren, efter att ha provat 2.2):** play-knappen fungerar, men uppspelningsfönstret ska vara större. Det ska synas tydligare hur det blir med "Föreslå radindelning som referensen". Man ska kunna söka i texten.
- **Beslut:**
  - **Uppspelningen** är tre fjärdedelar av skärmens bredd, minst 960 och högst 1920 bildpunkter (förut alltid 960). På en skärm som är 1920 bred blir det 1440.
  - **Förslagen visas i listan.** Under varje förslag står en eller två kursiva rader märkta **→ blir**, med ljusblå bakgrund, som visar tiderna och texten raden får om förslaget godkänns, och referensraden den då motsvarar. Förut syntes det bara i verktygstipset. Raderna är en förhandsvisning: de sparas inte, räknas inte i "Visar X av Y rader" och kan inte ändras. Högerklick på dem godkänner eller avvisar förslaget, och ▶ spelar stället.
  - **Sök** ovanför listan (Ctrl+F): letar i den synkade texten och i referensen, utan skillnad på stora och små bokstäver, bland raderna som listan visar. Första träffen markeras medan man skriver. Enter = nästa, Skift+Enter = föregående, Esc tömmer. Länkarna Föregående och Nästa gör samma sak. Bredvid står "Träff 2 av 23".
  - **Version 2.3**, eftersom 2.2 var öppen hos ägaren när den nya byggdes.
- **Claudes tolkning:** "visa tydligare hur det ser ut" tolkades som att resultatet av varje förslag ska synas direkt i listan. Sökningen flyttar markeringen till träffen. Den döljer inga rader.
- **Test:** 88 av 88 kontroller (1 ny, för fönstrets bredd). Provat i programmet mot Blacklist: 155 förslag gav raderna "→ blir" (skärmbild i mörkt läge), godkänn från en sådan rad gav 367 → 368 rader, sökning på "kirk" gav 23 träffar med hopp framåt och bakåt, och "xyzzy" gav "Inga träffar.".
- **Ej provat:** den större uppspelningen (ägaren provar), och hur lång tid klippet tar att förbereda i den storleken.
- **Product-check:** Claude granskade själv (tre mindre GUI-ändringar). Rättat under arbetet: sökrutan hamnade på en egen rad skild från etiketten, och pilen "↳" såg ut som en fyra i kursiv stil. Kan vänta: F3 hoppar till nästa röda rad och inte till nästa sökträff, och sökningen letar inte i rader som är dolda av valet i Visa (den säger det när inget hittas).
- **Nivå:** LIVE

## 2026-11-10 – Ta bort text för hörselskadade, som en inställning
- **Område:** GUI / text
- **Önskemål (ägaren):** en kryssruta som tar bort text för hörselskadade. Inte ikryssad från början, och valet sparas (`removehi` i settings.txt).
- **Beslut:**
  - Rutan **"Ta bort text för hörselskadade ([MUSIK], (skrattar), talarnamn)"** står bredvid "Upptäck bildfrekvensfel".
  - **Grundregel:** en rad som inte innehåller något av mönstren lämnas exakt som den är, också blanksteg och taggar.
  - **Det som tas bort:** allt inom [hakparenteser], även över flera rader; parenteser som är hela raden, står först på en rad, står sist efter en hel mening eller bara innehåller versaler (minst 6 bokstäver eller flera ord); rader som bara är noter (♪ ♫); talarnamn i versaler först på raden (`JOHN:`, `- MARIA:`, `MAN 2:`, `JOHN (skrattar):`). Rader som blir tomma tas bort, och replikstrecket tas bort när bara en talare blir kvar.
  - **Talarnamn** tas bara bort när samma namn står först på minst två rader i filen. Då är det ett namn och inte en rubrik ("USA: 3, Sverige: 2"). Vanliga ord före kolon (OBS, PS, OK, DAG, ÅR, KAPITEL, DEL m.fl.) tas aldrig bort.
  - **Det som står kvar:** parentes mitt i en mening, också när den börjar på rad två ("Jag träffade Anna / (min syster) i går"); korta förkortningar i parentes ("(WHO)"); versaler utan kolon ("FBI är här", "NEJ!"); klockslag; namn med små bokstäver; rader utan bokstäver ("?!", "…"); sångtexter mellan ♪; positionstaggar som `{\an8}`.
  - **Före synken** rensas texten som ska synkas, eftersom ljudbeskrivningar inte motsvarar tal och kan störa synken mot ljudet. Resultatrutan säger hur många rader som ändrades och togs bort, och att man kryssar ur rutan och synkar igen för att få tillbaka dem. Den rensningen går inte att ångra med Ctrl+Z.
  - **Efter synken** (rutan kryssas i när det finns ett resultat) rensas den synkade texten direkt, som en ändring som går att ångra med Ctrl+Z. Ångrar man kryssas rutan ur, så att rutan och texten stämmer.
  - Skulle färre än 5 rader bli kvar görs ingenting, och programmet säger att nästan allt ser ut som text för hörselskadade.
  - Originalfilen rörs inte förrän man sparar, och då sparas en kopia som vanligt.
- **Kända gränser:** talarnamn med små bokstäver ("John:") och namn som bara förekommer en gång tas inte bort. En text helt i versaler kan förlora början av en mening före ett kolon om samma början återkommer. Originaltexten (separat SRT på filmens språk) rensas inte.
- **Test:** 87 av 87 kontroller (11 nya, en per mönster plus kontroller av att vanlig dialog inte förstörs och att konstig indata inte hänger programmet). Provat i programmet med Blacklist-texten med påhittade `RED:`, `[MUSIK]` och ♪: 43 rader ändrade och 2 borttagna, Ctrl+Z gav tillbaka dem, och synken gav samma besked (Bra) med och utan rutan. **Inte provat mot en riktig text för hörselskadade.**
- **Product-check (oberoende granskning):** inget blockerande. Rättat: orörda rader ändras inte längre (blanksteg vid kursivtaggar försvann), förkortningar och rubriker före kolon står kvar, parentes först på rad två mitt i en mening står kvar, korta förkortningar i parentes står kvar, rader utan bokstäver står kvar, beskrivningar över flera rader och efter kursiv text tas bort, tydligt besked om hur man får tillbaka text som rensades före synken, rutan kryssas ur när rensningen ångras, skydd när nästan allt skulle tas bort, och ett reguljärt uttryck som kunde bli mycket långsamt. Kan vänta (docs/next.md): rensa originaltexten, visa vad som tas bort innan det sker.
- **Nivå:** LIVE
## 2026-11-10 – Spela upp filmen runt en rad (▶ i listan)
- **Område:** GUI
- **Önskemål (ägaren):** en liten play-knapp på varje textrad som spelar 5 sekunder före och 5 sekunder efter, så att man kan kontrollera att texten stämmer med det som sägs.
- **Beslut:**
  - En smal kolumn **▶** längst till vänster i listan, på varje rad (även de grå referensraderna). Klick, tangenten **P** eller högerklick → "Spela upp i filmen" spelar från 5 s före radens start till 5 s efter radens slut.
  - **ffmpeg klipper ut stället** till en liten tillfällig film (960 px bred, med texten inbränd), och **ffplay spelar den** i ett eget fönster som ligger överst och stänger sig själv när klippet är slut. Esc stänger det, också från Text Sync-fönstret. Båda verktygen följer med i ffmpeg-paketet som ljudsynken redan använder (fast URL + SHA-256), så vännerna behöver inte installera något. Saknas de frågar programmet först (cirka 110 MB). VLC och mpv valdes bort eftersom de kräver installation.
  - **Varför två steg:** att låta ffplay hoppa direkt i filmen (`-ss`, `-t`, `-autoexit`) prövades först på papperet. Granskningen visade att ffplay då läser till filens slut innan fönstret stängs (minuter på en film på 14 GB), att starten hamnar på närmaste nyckelbild och att texten kan hamna fel i format där tiderna inte börjar på 0 (.ts). Ett utklippt klipp börjar exakt och på tiden 0. Priset är några sekunders väntan ("Förbereder…" i statusraden).
  - **Texten ovanpå är de synkade raderna som de är just nu**, även osparade ändringar. De skrivs till en tillfällig SRT i `%TEMP%` med tider räknade från klippets början. Verktygen startas i den mappen och får bara filnamn, så att sökvägen inte behöver skrivas om för ffmpegs filter. Klippet och SRT-filen tas bort när uppspelningen är slut, och rester rensas vid nästa start.
  - **Ljudspåret** är det som är valt i listan Ljudspår.
  - **Filmen:** referensfilmen. Är referensen en SRT letar programmet bredvid filerna (samma avsnittsnummer, annars den enda filmen i mappen, annars samma namn som texten). Går det inte att avgöra får man välja filmen, en gång.
  - En ny uppspelning stoppar den förra. Uppspelningen stoppas när en ny fil läses in och när programmet stängs.
- **Teknik:** listan ritar nu alla celler själv (utom i högkontrast, och utom texten på en markerad rad i ljust läge), eftersom Windows annars inte anropar ritningen per cell. Play-knappen är en triangel i länkfärgen. Cellens text är "Spela rad N", så att en skärmläsare säger vad knappen gör.
- **Test:** 76 av 76 kontroller (8 nya: argumenten till ffmpeg och ffplay, vilka rader som följer med och deras tider, val av film). Listan kontrollerades på skärmbilder i ljust och mörkt läge.
- **Ej provat: själva uppspelningen.** ffmpeg och ffplay finns inte på utvecklingsdatorn, och nedladdade program får inte köras i Claudes miljö. Ägaren provar på sin dator: en rad mitt i en stor film, en rad nära början, valt ljudspår 2 och hur lång tid "Förbereder…" tar på en 4K-film.
- **Product-check (oberoende granskning):** ett blockerande fynd (fönstret stängdes inte i tid), löst med de två stegen ovan. Rättat också: klickytan följer med vid rullning (HitTest), skärmläsartext på play-knappen, fokusramen ritas igen, dubbelklick startar inte om uppspelningen, play-knappen ritas rätt på en markerad rad i ljust läge, länken Ångra kommer tillbaka efter nedladdningen, vanlig muspekare medan en synk pågår. Kan vänta (docs/next.md): uppspelningen blir kvar om Text Sync kraschar, ingen uppspelning av filer utan bild.
- **Nivå:** LIVE
## 2026-11-10 – Besked om hur säker synken är, och varning för fel avsnitt
- **Område:** GUI / synk
- **Önskemål (ägaren):** programmet ska säga hur säker synken är och varna för fel avsnitt.
- **Beslut:**
  - **Besked ovanför listan** efter synken: **✔ Bra** (grönt), **⚠ Kontrollera de markerade ställena** (gult) eller **✖ Osäkert** (rött). Beskedet har symbol och text, inte bara färg, och inga siffror. Siffrorna står i detaljerna under och i loggen. Det räknas om efter varje ändring i listan.
  - **Svaga ställen** är avsnitt på 2 minuter där färre än 45 % av raderna börjar inom 0,5 s från en referensrad. Raderna märks **⚠** i kolumnen #, och länkarna **Föregående ställe** och **Nästa ställe** hoppar mellan dem. Finns inga svaga ställen hoppar länkarna mellan de röda raderna.
  - **Varning före synken**, med knapparna **Synka ändå** och **Avbryt** (Avbryt är förvald): när filnamnen har olika avsnittsnummer (S04E01, s4e1, 4x01, "Säsong 4 Avsnitt 1"), eller när texterna slutar mer än 10 minuter och 30 % från varandra. Texter med färre än 50 rader bedöms inte på längd. Mot ljud finns ingen längdkontroll före synken, men beskedet Osäkert säger samma sak efteråt.
- **Gränser (mätta):** Bra kräver att minst 95 % av raderna har referens samtidigt och att minst 65 % börjar inom 0,5 s, utan svaga ställen och utan anmärkningar från motorn. Osäkert är under 45 % och under 90 %, eller motorns poäng under 0,35. Mätt på Blacklist S04E01: rätt text 99,5 % / 83 % / poäng 0,92, samma text baklänges i tiden (beter sig som ett annat avsnitt) 79 % / 35 % / 0,62. Syntetiska tester: rätt 99 % / 93 %, annan film 79 % / 30 %.
- **Mot ljud** (taldetektering) börjar raderna inte lika exakt samtidigt. Där bedöms bara överlappet (svagt ställe = under 60 % av raderna ligger på tal). **Det är inte mätt mot en riktig film**, eftersom ljudverktygen inte får köras i Claudes miljö.
- **Gles referens** (färre än hälften så många rader som texten, t.ex. bara tvingad text): beskedet blir Kontrollera med förklaringen att referensen har för lite text för att bedöma synken.
- **Product-check (oberoende granskning):** inget blockerande. Rättat: synken efter dra-och-släpp startas först när släppet är klart (annars kunde en varningsruta låsa Utforskaren), ingen längdvarning för en ofullständig MKV, egen förklaring för gles referens och för synk mot ljud, förklaring även när beskedet är Kontrollera utan markerade ställen, F4 och Skift+F4 hoppar mellan ställena, högst en varningsruta per synk, filnamnen står i varningsrutan. Kan vänta (docs/next.md): fler skrivsätt för avsnittsnummer, dubbelavsnitt, längdkontroll av originaltexten.
- **Test:** 68 av 68 kontroller (13 nya). Gränssnittet provades inifrån samma process mot Blacklist: rätt text gav Bra, en kopia med S04E02 i namnet gav varningsrutan (Avbryt lämnade allt orört), och texten baklänges gav Osäkert med 9 ställen att hoppa mellan.
- **Kvar:** gränserna bygger på en film och syntetiska data. Ett riktigt fel avsnitt av samma serie har inte provats.
- **Nivå:** LIVE

## 2026-11-10 – MainForm.cs uppdelad i delar
- **Område:** Struktur
- **Beslut (ägaren, se svaren på frågorna samma dag):** `MainForm.cs` (cirka 2 000 rader) är uppdelad i `MainForm.cs`, `MainForm.Load.cs`, `MainForm.Edit.cs`, `MainForm.Audio.cs`, `MainForm.Sync.cs` och `MainForm.Save.cs` (partial class), och tidslinjen ligger i `Timeline.cs`. Ingen kod är ändrad, bara flyttad. `docs/structure.md` visar vad som ligger var.
- **Test:** 55 av 55 kontroller, och gränssnittet startar och läser in testfilmen.
- **Product-check:** Claude granskade själv (ren flytt av kod, inget ändrat för användaren).
- **Nivå:** LIVE

## 2026-11-10 – Svar på de obesvarade frågorna
- **Område:** GUI / ljudsynk / struktur
- **Beslut (ägaren godkände Claudes förslag):**
  - **Smart App Control:** ljudsynken döljs inte. Det svenska felmeddelandet räcker.
  - **Nedladdning:** NVIDIA-användare får ingen extra fråga om ett mindre paket. Den som vill ha det väljer processorn i listan Grafikkort.
  - **Ljud A rekommenderas** när datorn saknar NVIDIA-grafikkort, med en kort förklaring bredvid valet. (Inte byggt än, se docs/next.md.)
  - **Ingen knapp för att ta bort verktygen.** Hjälptexten räcker.
  - **Rättningar från Whisper-kontrollen markeras i listan**, så att man kan granska dem. (Inte byggt än.)
  - **Testutdata översätts till svenska** efter hand, när testfilerna ändras.
  - **`MainForm.cs` delas upp** i delar (partial class), med tidslinjen i en egen fil, innan nästa större funktion.
  - **"Ta bort text för hörselskadade" är inte ikryssad från början.** Valet sparas till nästa gång.
- **Kvar:** frågan om `.gitattributes`. Ägaren svarar senare.
- **Nivå:** LIVE

## 2026-11-09 – Rättningar från product-check: ljudspår, grafikkort, nedladdning, felmeddelanden
- **Område:** GUI / ljudsynk / drift
- **Beslut (ägarens svar):** listan **Ljudspår** visas bara när filmen har mer än ett ljudspår och referensen använder ljudet (Ljud A/B/A+B eller Whisper-kontroll). Engelska väljs först, annars standardspåret, men aldrig ett kommentarspår eller syntolkning om det finns något annat. Valet gäller alla val i listan Referens. För MKV läses spåren ur filen, och för andra format med ffprobe i bakgrunden.
- **Claudes val (övriga punkter från granskningen):**
  - **Nedladdning efter valt grafikkort:** grafikkortet ger CUDA-versionen och turbo, processorn ger processorversionen och small. Finns redan en modell används den. Processorpaketet hämtas inte på NVIDIA när grafikkortet är valt, eftersom CUDA-versionen kan köras med `-ng`. Har CUDA-versionen misslyckats en gång sparas det (`gpuwhisperfailed` i settings.txt), och då hämtas processorpaketet (21 MB) när man väljer processorn. Felmeddelandet säger då hur man gör.
  - **Grafikkort:** nvidia-smi körs en gång i bakgrunden vid start, med högst 5 s. Listan visar "letar…" tills sökningen är klar, och Synka väntar på den. Kort under compute capability 5.0 och drivrutiner före 527 (CUDA 12.4) visas inte. Orsaken skrivs i loggen.
  - **Omstart på processorn:** statusraden säger "Grafikkortet fungerade inte – Whisper börjar om på processorn (tar längre tid)…" innan förloppet går tillbaka.
  - **Taldetekteringen** kan avbrytas.
  - **Whisper-kontrollen** rättar inte längre ensamma rader. Rapporten stämmer därmed alltid med det som ändrades. En ensam avvikande rad är oftast brus i Whispers ordtider.
  - **Diskutrymme** kontrolleras före nedladdningen: 2,5 × zip-filernas storlek + övriga filer + 100 MB. Hjälp-rutan och FAQ säger var verktygen ligger och hur man tar bort dem.
  - **Felmeddelanden** innehåller inte längre ffmpegs och Whispers engelska text. Den skrivs i loggen, och meddelandet hänvisar dit.
  - **Sökvägar till whisper-cli** med tecken utanför Windows teckentabell (t.ex. kyrilliska i användarnamnet) skrivs om till korta 8.3-namn.
- **Test:** 55 av 55 kontroller (9 nya: nvidia-smi-tolkning, förval av ljudspår, att rapporten från Whisper-kontrollen stämmer i 20 scenarier, korta sökvägar, diskutrymme). Kontrollen av Whisper-rapporten misslyckades i 6 av 20 scenarier utan rättningen. Gränssnittet provades inifrån samma process mot Blacklist S04E01 (ett ljudspår, listan dold), och med tre påhittade spår (engelska förvalt, bytet slår igenom på valen). RTX A2000 hittas med den nya kontrollen.
- **Ej provat:** whisper-cli med en sökväg med kyrilliska tecken (nedladdade program får inte köras i Claudes miljö), omstart på processorn och fullt diskutrymme på en riktig dator.
- **Product-check (oberoende granskning):** inget blockerande. Rättat: reserv på processorn när CUDA-versionen inte fungerar (Bör), förklaringen visas före nollan (Bör), ljudspåren i MP4 läses efter att ffmpeg har laddats ner (Bör), kontroll av stängt fönster, nvidia-smis svar i loggen när inga kort hittas och ett eget lås för sökningen. Kan vänta (docs/next.md): ffprobes svar under en synk, turbo efter byte till grafikkort, diskutrymme i `%TEMP%`.
- **Nivå:** LIVE

## 2026-11-09 – 2.1: versionsnummer i filnamnet och mappen Build
- **Område:** Bygge / distribution
- **Önskemål (ägaren):** det ska synas vilken version som ligger på ett USB-minne, och det som byggs ska hamna i en egen mapp.
- **Beslut (ägarens svar på frågorna 9–11):**
  - Programmet heter `TextSync-<version>.exe`, med **bindestreck** (t.ex. `TextSync-2.1.exe`), så att sökvägen inte behöver citattecken i kommandon.
  - Det byggs i **`Build\`**, och testprogrammen i **`Build\test\`**, så att allt som byggs hamnar i samma mapp. `Build/` står i `.gitignore`. Den gamla `TextSync.exe` i roten och `test\bin\` är borttagna.
  - **Äldre versioner ligger kvar** i `Build\`, så att en tidigare version går att dela ut igen.
- **Teknik:** `build.ps1` läser versionen ur `Program.Version` i `src/Program.cs`. Bygget avbryts med ett svenskt felmeddelande om versionen inte hittas, eller om `AssemblyVersion`/`AssemblyFileVersion` inte är samma version med fyra delar (2.1 → 2.1.0.0). Inget i programmet beror på filnamnet: inställningar, verktyg och logg ligger i `%LOCALAPPDATA%\TextSync`, och ikonen hämtas via `Application.ExecutablePath`.
- **Test:** bygget ger `Build\TextSync-2.1.exe` och testprogrammen i `Build\test\`, 46 av 46 kontroller. Med `AssemblyFileVersion` satt till 2.0.0.0 avbröts bygget med meddelandet "AssemblyFileVersion i src\Program.cs ska vara "2.1.0.0" …".
- **Product-check:** Claude granskade själv (liten ändring i bygget). Inga blockerande fynd. Bör: en vän som får en ny version med nytt filnamn kan få två versioner bredvid varandra, och en genväg eller "Öppna med" pekar då på den gamla. Därför står det i docs/operations.md under "Dela ut" att den gamla ska tas bort.
- **Nivå:** LIVE

## 2026-11-09 – Synligt när muspekaren är över rader och knappar
- **Område:** GUI
- **Önskemål (ägaren):** man ska se när man håller muspekaren över saker.
- **Beslut:** raden under muspekaren i listan får en ljus bakgrund (ljust läge 229,241,251, mörkt 52,52,52), och radens egen färg (röd, blå, grå) behålls. De platta knapparna får en tydlig färg när muspekaren är över dem och när de trycks ned, i båda lägena. Listan ritas därför av programmet (OwnerDraw) i båda lägena utom högkontrast, men bara rubrikerna i mörkt läge, den markerade raden i mörkt läge och raden under muspekaren. Allt annat ritas av Windows. Rullistor, kryssrutor, menyer och länkar visade redan detta.
- **Product-check:** Claude granskade själv (liten GUI-ändring). Listraderna kontrollerades på skärmbilder i båda lägena. Knapparnas färg sätts med WinForms `FlatAppearance.MouseOverBackColor` men gick inte att visa på en skärmbild utan riktig mus.
- **Nivå:** LIVE

## 2026-11-09 – Mörkt läge: vita kolumnrubriker och tydlig markerad rad
- **Område:** GUI
- **Problem (ägaren):** kolumnrubrikerna i listan var grå i mörkt läge. Windows mörka tema för rubriker (`DarkMode_ItemsView`) ritar texten grå, och färgen går inte att ändra.
- **Beslut:** i mörkt läge ritar programmet själv rubrikerna (vit text, `ListView.OwnerDraw`). Det gjorde den markerade raden grå på grå, så den ritas också själv, med blå bakgrund (0,95,170) och vit text. Övriga rader, ljust läge och högkontrast ritas av Windows som förut.
- **Product-check:** Claude granskade själv (liten GUI-ändring). Kontrast på den markerade raden cirka 6,5:1.
- **Nivå:** LIVE

## 2026-11-09 – Commit efter varje ändring, product-check före varje commit
- **Område:** Arbetssätt
- **Beslut (ägaren):** Claude committar efter varje ändring, när bygget och testerna går igenom. Product-check körs före varje commit, men inte före push. Bara egna ändringar committas, eftersom en annan session kan arbeta i samma filer samtidigt.
- **Nivå:** LIVE

## 2026-11-09 – 2.0: filter i listan, mörkt läge, förslag om att dela och slå ihop rader
- **Område:** GUI / synk
- **Önskemål (ägaren):** dölja grå rader, visa bara röda rader, mörkt läge, och matcha de svenska raderna mot de engelska (två svenska rader kan slås ihop till en, och en kan delas i två).
- **Beslut (ägarens val i intervjun):**
  - **Visa** ovanför listan: Alla rader, Dölj grå rader, Bara röda rader, Bara förslag. Sparas i `settings.txt` (`view`). Bredvid står "Visar X av Y rader.". I Bara förslag visas också de grå referensrader som ingår i ett förslag.
  - **Utseende** längst upp till höger: Som Windows (förvalt, läser `AppsUseLightTheme` och följer ändringar direkt), Ljust, Mörkt. Sparas (`theme`). Högkontrast går alltid före, och då döljs valet.
  - **Matchning ändrar den sparade filen** men ges som **förslag att granska**: blå rader, verktygstips, högerklick Godkänn/Avvisa, länken "Godkänn alla förslag (N)". Godkänn är ett steg för Ctrl+Z. Avvisade förslag visas inte igen förrän nästa synk.
  - Nya rader får **referensradens tider**, men går aldrig in i grannraderna.
  - Bara **2↔1 och 1↔2**. Sammanslagning som blir längre än **2 rader à 42 tecken** föreslås inte.
  - Delning bara vid **radbrytning eller efter . ? !**, aldrig vid ett godtyckligt mellanslag.
- **Claudes val (efter test på Blacklist S04E01):**
  - Mitt i en mening (radbrytning utan . ? ! före) delas bara om referensens första rad också slutar mitt i en mening ("...", komma eller utan punkt). Utan regeln blev 49 av 164 delningar mitt i en mening, bland annat "…för att få ett / normalt liv…" mot en engelsk rad som slutade med punkt. Med regeln: 155 delningar, och de 38 som delar mitt i en mening följer engelskan.
  - Minsta tidsöverlapp 60 %. 70 % prövades, men de 15 förslag som då försvann var nästan alla riktiga.
  - Inga förslag om referensen har färre än hälften så många textrader som den synkade texten. Mot spåret med bara tvingad text (38 rader) blev förslagen annars fel. Inga förslag mot ljud (A, B, A+B), eftersom "(tal)" och Whisper-fraser inte är en radindelning.
  - Ensamt replikstreck tas bort när en dialograd delas ("- Hem." blir "Hem."). Visar referensen två personer ("- … / - …") får en sammanslagning replikstreck.
- **Teknik:** `Align.cs`, dynamisk programmering över (svensk rad, engelsk rad) med stegen hoppa över, 1↔1, 2↔1 och 1↔2. Poäng = antal rader × tidsöverlapp (gemensam tid / hela spannet). 2↔1 och 1↔2 kostar 0,5. Cirka 10 ms för ett avsnitt (367 × 577 rader). `Theme.cs`: två paletter, färgbyte på alla kontroller (gammal palettfärg → ny), `SetWindowTheme` (DarkMode_Explorer, DarkMode_CFD, DarkMode_ItemsView) och `DwmSetWindowAttribute` 20 för titelraden.
- **Test:** 46 av 46 kontroller (11 nya för matchningen). Gränssnittet provades inifrån samma process mot Blacklist: alla fyra visningslägena, ljust → mörkt → ljust, Godkänn alla (367 → 522 rader) och Ctrl+Z (367), dialogrutan i mörkt läge.
- **Kvar:** meddelanderutor (MessageBox), kryssrutans ruta och pilarna i talrutan förblir ljusa i mörkt läge.
- **Product-check (oberoende granskning + ägaren):**
  - Ägaren förstod inte vad en blå rad var. Därför är **förslagen avstängda från början** ("Föreslå radindelning som referensen", sparas som `propose`), och kolumnen **#** visar **Dela?** eller **Ihop?**, så att förslagen inte bara märks med färg.
  - "Godkänn alla" frågar inte först, eftersom Ctrl+Z räcker. Att spara med förslag som inte är godkända frågar inte heller, eftersom förslagen är frivilliga.
  - F3 och "Nej" vid låg matchning byter till Alla rader när den röda raden är dold. Tidigare hände ingenting.
  - En tom lista säger varför den är tom ("Inga röda rader …", "Förslag är avstängda …").
  - Rättat: tiden "Före synk" för andra delen efter en delning, nyckeln för avvisade förslag (nu med radens tid, så att två likadana rader inte avvisas tillsammans) och resultattexten i Bara förslag.
  - Kan vänta (docs/next.md): tabellen för matchningen räknas om helt vid varje uppdatering. Det tar cirka 10 ms för ett avsnitt och uppskattningsvis 100 ms för en långfilm.
- **Nivå:** LIVE

## 2026-11-09 – Bara exe-filen på USB, testerna avgör själva, lokala sökvägar utanför git
- **Område:** Distribution / test / struktur
- **Beslut (product-check före commit):**
  - **Bara `TextSync.exe` delas ut på USB.** Ingen hjälpfil följer med. Ägaren berättar om Windows-varningen vid överlämningen. Snabbstarten står kvar överst i README.md.
  - **`Tests.cs` avgör själv om testerna gick igenom.** Varje kontroll går via `Check(...)`. En misslyckad kontroll skriver `FEL: …`, sista raden är `RESULTAT: …`, och `Tests` returnerar 1 så att `build.ps1 -Test` avslutas med ett fel. Simple bedöms inte i scenarierna med klipp (C, F, G). Första körningen: 35 av 35 kontroller gick igenom.
  - **Sökvägen till testfilmen ligger i `CLAUDE.local.md`**, som står i `.gitignore`. Resten av `CLAUDE.md` checkas in.
- **Varför:** Windows 11 har inget program kopplat till `.md`-filer, så en vän som dubbelklickar på README.md får rutan "Välj en app" och ser sedan rå markdown och utvecklardelen. Testerna returnerade alltid 0, så ett synkfel syntes bara om någon läste cirka 40 rader utdata. Sökvägen innehåller användarnamnet och ska inte följa med om förrådet publiceras.
- **Invändning:** Claude rekommenderade att `build.ps1` skapar en `LÄS MIG.txt` ur snabbstarten, eftersom en vän som möter Windows-varningen annars inte har något skrivet att gå efter. Ägaren valde bara exe-filen.
- **Nivå:** LIVE

## 2026-11-09 – README och LÄS MIG sammanslagna, testbygge i build.ps1, allt på svenska
- **Område:** Struktur / dokumentation
- **Beslut:** LÄS MIG.txt är borttagen. README.md börjar nu med en snabbstart för användare och har sedan ett avsnitt för utvecklare. `build.ps1 -Test` bygger testprogrammen i `test\bin\` och kör `Tests`. Det finns en `CLAUDE.md` med regeln att kommentarer, texter och dokumentation ska vara på svenska. (Först delades README.md ut på USB tillsammans med exe-filen. Det ändrades samma dag, se posten ovan.)
- **Varför:** LÄS MIG.txt hade hunnit bli inaktuell, eftersom hjälpen fanns på tre ställen. Instruktionen för testbygget i operations.md gick inte att bygga med.
- **Nivå:** LIVE

## 2026-11-09 – 1.9: listan visar alla rader i båda texterna
- **Område:** GUI
- **Problem (ägaren):** Listan visade inte alla rader i den engelska texten. Den hade en rad per svensk replik, med bara den engelska rad som överlappade mest bredvid. När en svensk rad täckte två engelska syntes bara den ena, och engelska rader utan svensk rad samtidigt syntes inte alls.
- **Beslut:** Varje engelsk rad som inte redan står bredvid en svensk rad får en egen grå rad i tidsordning, med tomma svenska kolumner. Grå rader sparas aldrig och kan inte tas bort. Dubbelklick eller Enter på en grå rad öppnar "Lägg till rad" med den engelska radens tid. Resultatet visar antalet grå rader. Procenten och de röda raderna räknas som förut, bara på svenska rader. "(tal)"-avsnitt från taldetekteringen (Ljud A) visas inte som grå rader, eftersom de inte är text.
- **Teknik:** Listrader och synkade rader har egna index (`rowCue`/`cueRow`). Redigera, ta bort, F3 och markeringen efter en ändring går via dem.
- **Product-check:** Claude granskade själv (liten GUI-ändring). Det finns inget val för att dölja de grå raderna, eftersom ägaren ville se alla rader. Det kan läggas till om listan blir för lång.
- **Nivå:** LIVE

## 2026-11-09 – 1.8: finjustering per replik + tidskolumner i listan
- **Område:** Synk / GUI
- **Problem (ägaren):** Blacklist S04E01 mot engelska spåret: runt 03:45 kom den svenska texten lite för tidigt. Felsökningen visade två korta klipp som synken missade. Rad 24–40 (03:30–04:33) skulle ha +8,2 s men fick +7,2 s, alltså cirka 1 s för tidigt. Rad 67–71 (07:18–07:38) skulle ha +5,4 s men låg upp till 4,6 s för tidigt och i fel ordning. Mätpunkterna i den styckvisa synken ligger var 10:e s och ser ±30 s, så avsnitt kortare än ungefär en minut suddas ut.
- **Beslut:** Efter den styckvisa synken körs en finjustering per replik (Viterbi över raderna i tidsordning, ±6 s, steg 50 ms). Varje rad får poäng för repliksstart och överlapp mot referensen. Byte av förskjutning kostar lika mycket som en rads hela poäng, så det krävs flera rader i rad som är överens. Små steg är inte tillåtna, eftersom de bara följde slumpvariationer (medianfelet steg från 27 till 72 ms i testerna). Raderna får inte hamna i fel ordning eller överlappa mer än i originalet. Finjusteringen kan slås av med `SyncOptions.RefineCues` för tester.
- **Test:** De syntetiska testerna A–F ger exakt samma resultat som förut. Nytt test G (+1 s i 60 s, ett avsnitt på 20 s före nästa hopp) gav 98,5 % inom 0,3 s före och 100 % efter. På Blacklist hittades klippen 03:30, 04:32, 07:18 och 07:45, och Whispers ordtider bekräftar 07:30. Separat engelsk SRT (OrigSrtDiag, samma facit): utan finjustering 85–94 % inom 0,3 s och 5 rader över 1 s, med finjustering 90–100 % och 0 rader över 1 s. Mot ljud A och A+B blev 90 %-felet 383 → 283 ms och 338 → 272 ms. Antalet rader över 1 s är oförändrat: ljudsynken missar fortfarande klippet vid 07:18. B ensam var redan fel (12 s globalt) och påverkas inte.
- **GUI:** Listan visar nu **Före synk** (tiden i den osynkade filen), **Synkad start/slut** och **Referenstid** (starttiden för referensraden som visas bredvid). Raden får sin tid före synk från synkmotorn (`Cue.OrigStart`) och behåller den när man ändrar den. En tillagd rad visar "— (ny rad)". Kolumnen **Flyttad** visar hur mycket raden flyttades efter omräkningen av bildfrekvensen, alltså samma tal som "Avsnitt" i resultatet. Ett klipp syns som ett hopp i kolumnen. Textkolumnerna fyller bredden, så att listan inte får en vågrät rullist.
- **Product-check:** Ägaren valde kolumnen Flyttad. Att färga rader där synkad start skiljer sig från referenstiden valdes bort, eftersom svenska ofta slår ihop två engelska repliker och det skulle ge falsklarm. Fönstret startar nu 1000 px högt i stället för 800 (begränsat till skärmen), eftersom listan bara visade cirka 3 rader.
- **Nivå:** LIVE

## 2026-11-09 – Varning innan Whisper körs på processorn
- **Område:** GUI
- **Beslut:** Är processorn vald och Whisper-resultatet för filmen inte redan sparat i cachen, frågar programmet innan det startar: det tar ungefär lika lång tid som filmen. Det gäller Ljud B, A+B, Whisper-kontrollen och originaltexten.
- **Varför:** Vänner utan NVIDIA-kort skulle annars vänta 1–2 timmar och bara se en procentsiffra. Ägaren valde varningen i product-check.
- **Nivå:** LIVE

## 2026-11-09 – Product-check av 1.7 (originaltext)
- **Område:** GUI / synk
- **Beslut:** Följande rättades efter en oberoende granskning:
  - Ligger filmen redan i fält 1 och man drar in två SRT-filer, behålls filmen och den andra SRT-filen blir originaltext.
  - Programmet säger till när fler än två SRT-filer dras in, och när originaltexten inte kunde användas.
  - Den sparade originaltexten får språkkod från filnamnet, annars från det språk Whisper hörde, annars en.
  - Steget med originaltexten körs alltid i Smart-läge med bildfrekvensdetektering, oavsett valen för den svenska synken.
  - Väljer man samma fil som i fält 2 får man ett tydligt fel.
  - Info-raden följer valet i listan, och länken byter namn till "Byt …".
  - De rättade raderna sorteras.
- **Ägarens val:** Den synkade engelska texten räknas inte som osparad när fönstret stängs, eftersom cachen gör en ny körning snabb.
- **Nivå:** LIVE

## 2026-11-09 – Separat SRT på originalspråket + Whisper-kontroll
- **Område:** Synk / GUI
- **Beslut:** Har filmen inget eget engelskt textspår kan man lägga till en separat SRT på filmens originalspråk. Det görs med länken "Lägg till undertext på filmens originalspråk (SRT)…", eller genom att dra in filmen och två SRT-filer. Kedjan:
  1. Den engelska SRT-filen synkas grovt mot ljudet, med taldetektering (A) som tider och Whispers fraser som rensning, alltså samma idé som A+B. Det hanterar förskjutning, bildfrekvens och klipp.
  2. Whisper-kontrollen rättar ord för ord det som fortfarande avviker.
  3. Den svenska texten synkas mot den rättade engelska.

  Den rättade engelska texten kan sparas med "Spara synkad originaltext…" (Film.en.srt).
- **Varför:** Ägarens önskemål nr 1 i docs/next.md. Ord för ord-kontrollen söker bara ±60 s, så en SRT med fel bildfrekvens (cirka 5 min drift på 2 timmar) måste först synkas grovt.
- **Test (Blacklist S04E01, engelska spåret förstört med flit, test/OrigSrtDiag.cs):** +7 s, 25→23,976 fps −3 s, klipp vid 20:00 och 10–15 min +3 s gav 88–100 % av de engelska raderna inom 0,3 s. Den svenska texten hamnade inom 0,3 s på 95–100 % av raderna, med 0 rader över 1 s. Direkt svensk synk mot ljudet (A+B) gav 94 % och 8 rader över 1 s. Med bara Whispers fraser som tider blev det sämre: 0,29 s fast avvikelse, och klippfallet blev fel.
- **Kvar:** En fast avvikelse på cirka 0,24 s mot facit, som vid all synk mot ljudet. Ljudet läses ur filmen två gånger (taldetektering och Whisper), men båda resultaten sparas i cachen.
- **Nivå:** LIVE

## 2026-11-09 – Textspår + Whisper-kontroll
- **Område:** Synk
- **Beslut:** Ett eget val i referenslistan, "Spår N + Whisper-kontroll", för varje textspår som inte är tvingat. Whisper körs i ordläge. Varje engelsk rads första ord letas upp i det Whisper hörde (±60 s), och varje träff blir en ankarpunkt. Där minst 6 av de 9 närmaste ankarpunkterna är överens och avviker mer än 1 s från den typiska nivån rättas textspåret innan den svenska texten synkas mot det.
- **Varför:** Ägarens idé. Textspåret kan självt gå ur takt i vissa filmer, och engelska mot engelska går att jämföra ord för ord.
- **Test:** Orört spår gav inga rättningar. Med 10:00–15:00 förskjutet +3 s rättades 42 rader, inga rader utanför ändrades, och svensk synk gick från 40 till 0 rader med mer än 1 s fel.
- **Nivå:** LIVE

## 2026-11-09 – A+B: A ger tiderna, B rensar
- **Område:** Synk
- **Beslut:** I A+B ger taldetekteringen (A) tiderna. Talavsnitt utan några ord från Whisper (musik, effekter) tas bort, och A:s avsnitt får Whispers text i förhandsvisningen. Whisper använder Silero-taldetektering.
- **Varför:** Mätt mot facit på Blacklist S04E01:
  - A: 0,18 s mediafel, 97 % inom 0,5 s.
  - B ensam: 0,47 s mediafel, 50 % inom 0,5 s.
  - Första versionen av A+B, där Whisper gav tiderna: 5,4 s mediafel.
  - Nuvarande A+B: 0,18 s mediafel, 98 % inom 0,5 s, och rätt bildfrekvens.
- **Nivå:** LIVE

## 2026-11-09 – Valbart grafikkort, CUDA 12.4
- **Område:** Verktyg / GUI
- **Beslut:** Grafikkortet för Whisper väljs i referensraden. Där visas NVIDIA-kort, eller processorn. Valet sparas i `settings.txt`. Intel Iris Xe visas inte, eftersom Whisper-versionen bara stöder CUDA (NVIDIA). GPU-paketet bytte från CUDA 11.8 till 12.4, eftersom 11.8 saknade cuBLAS och därför tyst föll tillbaka på processorn. Hela avsnittet tar ungefär 1 minut på RTX A2000, mot 1,5–2 timmar på processorn med turbo-modellen.
- **Varför:** Ägaren ville köra på RTX A2000 och kunna välja kort.
- **Nivå:** LIVE

## 2026-11-09 – Taldetekteringens gräns justerad mot facit
- **Område:** Synk
- **Beslut:** Gränsen börjar på 18 dB över bakgrunden och skärps tills tal täcker högst cirka 50 % av tiden.
- **Varför:** Den första gränsen, 9 dB, hittade tal nästan överallt och gav ungefär 55 s fel.
- **Nivå:** LIVE

## 2026-11-09 – Drift som motsvarar en bildfrekvens byts till exakt bildfrekvens
- **Område:** Synk
- **Beslut:** Om den uppmätta driften motsvarar en känd omräkning, till exempel 4,3 % = 25 → 23,976, används den exakta bildfrekvensen och analysen görs om.
- **Nivå:** LIVE

## 2026-11-08 – Synk mot filmens ljud (A, B, A+B)
- **Område:** Synk / GUI / verktyg
- **Beslut:** Referenslistan har tre val för ljud. Det fungerar för alla filmer, även MP4 och AVI, och kräver ingen textundertext.
  - **A – taldetektering:** ffmpeg läser ut ljudet, bara mittkanalen vid 5.1. Talet hittas via energin i talets frekvensband jämfört med bakgrundsnivån.
  - **B – Whisper:** taligenkänning med whisper.cpp. Den igenkända texten blir referens.
  - **A+B:** se 2026-11-09, där kombinationen ändrades efter mätning.
  - Resultaten sparas i `%LOCALAPPDATA%\TextSync\cache`, så att en ny synk mot samma film går direkt.
  - Whisper-texten kan sparas som en egen undertext med "Spara igenkänd text…".
  - Förval när filmen saknar textundertext: A+B om Whisper redan finns, annars A.
- **Varför:** Ägaren vill kunna synka filmer som saknar undertexter. Hen valde A och B som egna val, och dessutom en kombination.
- **Nivå:** LIVE

## 2026-11-08 – Verktyg laddas ner vid behov, med fasta kontrollsummor
- **Område:** Drift / säkerhet
- **Beslut:** ffmpeg 9.0.2 (Gyan essentials), whisper.cpp b5454 (för processorn, plus CUDA-versionen om det finns ett NVIDIA-kort) och språkmodellen hämtas första gången de behövs. Modellen är large-v3-turbo-q5_0 med NVIDIA-kort och small utan. Programmet frågar först och visar storleken. Varje fil kontrolleras mot en inbyggd SHA-256-summa, och filer som inte stämmer kastas. Allt hamnar i `%LOCALAPPDATA%\TextSync\tools`. ffmpeg som redan är installerat, i PATH eller via winget, används direkt.
- **Varför:** Vännerna får bara exe-filen via USB. Ägaren valde "laddar ner vid behov", och kontrollsummorna skyddar mot manipulerade nedladdningar.
- **Nivå:** LIVE

## 2026-11-08 – Smart App Control stängs inte av
- **Område:** Drift
- **Beslut:** Smart App Control förblir påslaget. Nybyggda versioner testas genom att programmet laddas inifrån PowerShell.
- **Varför:** Ägaren bad om att stänga av det för projektet. Det går inte per mapp eller projekt, utan bara för hela Windows, och det går inte att slå på igen utan ominstallation. Claude ändrar inte säkerhetsinställningar för hela datorn. Ägaren har fått veta var inställningen finns om hen själv vill ändra den.
- **Nivå:** LIVE

## 2026-11-08 – Projektet flyttat
- **Område:** Struktur
- **Beslut:** Projektet ligger i `%USERPROFILE%\Documents\TextSync`.
- **Nivå:** LIVE

## 2026-11-08 – Fler teckenkodningar
- **Område:** Inläsning
- **Beslut:** Text Sync gissar automatiskt mellan UTF-8, västeuropeisk, centraleuropeisk (polska, tjeckiska…) och kyrillisk kodning. Det finns också ett manuellt val, "Teckenkodning", i kort 2. Det återställs till Automatisk när man byter fil.
- **Varför:** Undertexter på polska, ryska, grekiska med flera blev fel tecken.
- **Nivå:** LIVE

## 2026-11-08 – Tillgänglighet: tangentbord och högkontrast
- **Område:** GUI
- **Beslut:**
  - Tidslinjen går att nå med Tab. Pilarna flyttar, PageUp och PageDown flyttar en hel skärm, + och − zoomar och Home går till början.
  - Tidslinjen visar fokus och har en beskrivning för skärmläsare.
  - I Windows högkontrastläge används systemets färger och vanliga knappar.
- **Varför:** Det var kvar som "kan vänta" efter den första granskningen.
- **Nivå:** LIVE

## 2026-11-08 – Kvalitetsmått: andel rader med referenstext, 95 %
- **Område:** GUI / synk
- **Beslut:** Resultatet visas som "Rader med referenstext samtidigt: X av Y (Z %)". Under 95 % frågar Spara och Lägg in i MKV "spara ändå?". Svarar man Nej hoppar listan till första röda raden. F3 går till nästa röda rad.
- **Varför:** Ägaren vill att alla rader ska matcha. Det gamla överlappsmåttet blev 86–92 % även när synken var helt rätt, så en gräns på 95 % hade gett frågan vid varje sparning.
- **Invändning:** Claude föreslog först 75 % på överlappsmåttet. Ägaren ville ha 95 % och att alla rader ska matcha. Det löstes med det nya radmåttet, där en rätt synk ger 98–99,5 %.
- **Nivå:** LIVE

## 2026-11-08 – Rader kan ändras, läggas till och tas bort
- **Område:** GUI / data
- **Beslut:** Dubbelklick eller Enter ändrar text och tid i en dialog, Insert lägger till en rad och Delete tar bort. Allt kan ångras i flera steg med Ctrl+Z eller länken "Ångra", och det finns också i högerklicksmenyn. Ändringarna gäller bara den synkade texten. Originalfilen ändras inte förrän man sparar, och då sparas en säkerhetskopia.
- **Varför:** Ägaren vill kunna ta bort textarnamn, rätta fel och lägga till rader.
- **Invändning:** Claude rekommenderade att bara kunna ta bort, eftersom det ger ett enklare gränssnitt. Ägaren valde full redigering. Därför ligger den i högerklicksmenyn och på kortkommandon, så att huvudvyn inte blir rörigare.
- **Nivå:** LIVE

## 2026-11-08 – Filnamn för VLC/Plex/Kodi
- **Område:** Spara
- **Beslut:** Förvalt namn är `Film.sv.srt`. Det behövs inget val för TV-spelare som kräver `Film.srt`.
- **Varför:** Vännerna tittar med VLC, Plex eller Kodi på dator.
- **Nivå:** LIVE

## 2026-11-08 – Saknat språk på MKV-spår betyder engelska
- **Område:** MKV
- **Beslut:** Har ett spår inget språkelement tolkas det som engelska, enligt Matroska-standarden. Språken visas med svenska namn i spårlistan.
- **Varför:** Spåren visades som "und" medan spelaren visade "English", eftersom mkvmerge utelämnar språket för engelska spår. Det påverkade också vilket spår som valdes automatiskt.
- **Nivå:** LIVE

## 2026-11-08 – Versionsnummer synligt
- **Område:** GUI
- **Beslut:** Versionen visas i fönstrets rubrik, i namnlisten och i alla dialogrutor, till exempel "Text Sync 1.3".
- **Varför:** Vännerna får nya versioner via USB och behöver kunna säga vilken de har när de rapporterar fel.
- **Nivå:** LIVE

## 2026-11-08 – Byggutdata hålls utanför git
- **Område:** Struktur
- **Beslut:** `TextSync.exe` och testprogram byggs med `build.ps1` och checkas inte in (`.gitignore`). Ikonen (`src/TextSync.ico`) checkas in.
- **Varför:** Källkod i git, binärer delas separat via USB.
- **Nivå:** LIVE

## 2026-11-08 – Ingen kodsignering; distribution via USB till vänner
- **Område:** Drift / distribution
- **Beslut:** TextSync.exe är osignerad och delas via USB. Filer från USB får ingen nedladdningsmärkning, så SmartScreen-varningen syns oftast inte.
- **Varför:** Signering kostar pengar (Azure Trusted Signing ≈ 10 USD/mån).
- **Invändning:** Claude rekommenderade signering. Datorer med *Smart App Control* påslaget kan blockera osignerade program helt, och det går inte att klicka sig förbi. Ägaren valde att inte betala. Det står i FAQ:n.
- **Nivå:** LIVE

## 2026-11-08 – MIT-licens
- **Område:** Juridik
- **Beslut:** MIT-licens (`LICENSE`).
- **Nivå:** LIVE

## 2026-11-08 – Användare och nivå
- **Område:** Produkt
- **Beslut:** Ägaren och vänner, icke-tekniska Windows 11-användare. Nivå **LIVE**.
- **Varför:** Programmet ska delas vidare. Ägaren valde först "publiceras öppet" och preciserade sedan till USB till vänner.
- **Nivå:** LIVE

## 2026-11-08 – Gränssnittet på svenska
- **Område:** GUI / språk
- **Beslut:** Bara svenska. Inga språkfiler.
- **Varför:** Användarna är svenska och synkar svenska undertexter.
- **Invändning:** Claude påpekade att en öppen publicering når icke-svensktalande. Det är inte längre aktuellt eftersom programmet delas till vänner.
- **Nivå:** LIVE

## 2026-11-08 – Originalet säkerhetskopieras vid överskrivning
- **Område:** GUI / data
- **Beslut:** Sparar man den synkade texten över originalfilen kopieras originalet först till undermappen `Text Sync original\`, med samma filnamn. En befintlig kopia skrivs aldrig över. Allt sparas via en temporär fil.
- **Varför:** En överskrivning går inte att ångra. Standardnamnet (`Film.sv.srt`) är ofta samma som originalets.
- **Avvikelse:** Ägaren godkände först `namn.original.srt` bredvid filmen. Claude lade kopian i en undermapp i stället, eftersom VLC, Plex och Kodi annars visar `Film.sv.original.srt` som ett extra undertextspår.
- **Nivå:** LIVE

## 2026-11-08 – Inbäddad undertext blir standardspår
- **Område:** GUI / MKV
- **Beslut:** "Lägg in i MKV" sätter den nya undertexten som standardspår. Övriga undertextspår behålls men är inte standard. Originalfilmen ändras aldrig, eftersom en ny fil skapas via `.part` och byter namn när den är klar.
- **Varför:** Texten ska visas direkt när filmen startar.
- **Nivå:** LIVE
