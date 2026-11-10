# Beslutslogg

Nyast först.

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
