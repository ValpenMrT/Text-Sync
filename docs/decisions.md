# Beslutslogg

Nyast först.

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
