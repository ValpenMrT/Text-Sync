# Text Sync – vanliga frågor

**Windows säger att programmet är blockerat eller farligt.**
Text Sync är inte signerat med ett köpt certifikat.
- Står det *"Windows skyddade datorn"*: klicka **Mer information** och sedan **Kör ändå**.
- Står det att *Smart App Control* blockerade programmet: då går det tyvärr inte att köra på den datorn. Stäng inte av Smart App Control för Text Sync skull, för det går inte att slå på igen utan att installera om Windows.

**Hur gör jag?**
1. Dra in filmen (MKV) och den svenska SRT-filen i fönstret. Det går också att använda **Bläddra…**.
2. Klicka **Synka**.
3. Titta på några rader i listan. Den engelska texten bredvid ska betyda samma sak, och **Synkad start** ska ligga nära **Referenstid**. **Före synk** visar tiden i din osynkade fil, och **Flyttad** hur mycket raden flyttades. Ett plötsligt hopp i Flyttad betyder ett klipp i filmen. Kontrollera gärna raderna där.
4. Klicka **Spara SRT…**. Filen sparas som `Filmnamn.sv.srt` bredvid filmen, och då hittar VLC, Plex och Kodi den automatiskt.

**Vad betyder röda rader och procenten?**
Röda rader har ingen engelsk text samtidigt. Det är ofta helt rätt, till exempel ett utrop eller textarens namn. Procenten visar hur många rader som *har* engelsk text samtidigt, och en lyckad synk brukar ge 98–100 %. Under 95 % frågar programmet innan det sparar. Tryck **F3** för att hoppa till nästa röda rad. Är många rader röda i följd har synken gått fel. Prova då metoden **Avancerad**, eller välj ett annat engelskt spår.

**Vad betyder grå rader?**
Listan visar alla rader i både den svenska och den engelska texten. En grå rad är en engelsk rad som inte står bredvid någon svensk rad, till exempel när en svensk rad täcker två engelska. Grå rader sparas aldrig. Dubbelklicka på en grå rad om du vill lägga till en svensk rad på samma tid.

**Kan jag dölja de grå raderna, eller bara se de röda?**
Ja, välj i **Visa** ovanför listan: **Alla rader**, **Dölj grå rader**, **Bara röda rader** eller **Bara förslag**. Bredvid står hur många rader som visas, eller varför listan är tom. Valet sparas till nästa gång. Det påverkar bara listan, inte filen du sparar. Trycker du **F3** när de röda raderna är dolda, byter listan till **Alla rader**.

**Kan programmet dela eller slå ihop svenska rader så att de blir som de engelska?**
Ja. Kryssa i **Föreslå radindelning som referensen** ovanför listan. Rutan är avstängd från början, och valet sparas till nästa gång. Programmet föreslår då ändringar, men ändrar ingenting förrän du godkänner. Förslagsraderna har blå text, och i kolumnen **#** står **Dela?** eller **Ihop?**:
- **Dela:** en svensk rad som täcker två engelska rader delas i två, till exempel "- Vart då? / - Hem." blir "Vart då?" och "Hem.".
- **Slå ihop:** två svenska rader som tillsammans motsvarar en engelsk rad blir en.

De nya raderna får den engelska radens tider. Håll muspekaren över en blå rad för att se förslaget. Högerklicka (eller tryck **Skift+F10**) för att **godkänna** eller **avvisa** det, eller klicka **Godkänn alla förslag** ovanför listan. Förslag som du inte godkänner påverkar inte filen du sparar. **Ctrl+Z** ångrar, även om du godkände alla på en gång.

Programmet föreslår bara det som går att göra säkert:
- En rad delas bara vid en radbrytning eller efter . ? !. Mitt i en mening delas den bara om den engelska raden också slutar mitt i en mening.
- Två rader slås inte ihop om texten då blir längre än två rader à 42 tecken.
- Förslag görs bara mot engelsk text (textspår eller SRT), inte mot ljudet, och inte mot ett spår med bara tvingad text.

Det är vanligt med många delningar men få sammanslagningar, eftersom svensk text ofta slår ihop två engelska repliker.

**Kan programmet vara mörkt?**
Ja. Välj **Utseende** längst upp till höger: **Som Windows** (förvalt), **Ljust** eller **Mörkt**. Som Windows följer inställningen *Välj appläge* under Inställningar → Anpassning → Färger och byter direkt när du ändrar den. Meddelanderutor (till exempel Hjälp) är alltid ljusa, eftersom Windows ritar dem. Med högkontrast i Windows används alltid Windows färger.

**Hur vet jag om synken blev bra?**
Efter synken står ett besked ovanför listan:
- **✔ Bra:** texten går i takt med referensen.
- **⚠ Kontrollera de markerade ställena:** på några ställen passar texten sämre. Raderna där är märkta ⚠ i listan. Klicka **Nästa ställe** och **Föregående ställe** för att hoppa mellan dem, eller tryck F4 och Skift+F4.
- **✖ Osäkert:** texten verkar inte passa. Den kan höra till ett annat avsnitt eller en annan version av filmen.

**Kan jag se och höra om texten stämmer med filmen?**
Ja. Klicka på **▶** längst till vänster på en rad i listan (eller markera raden och tryck **P**). Efter några sekunders förberedelse spelas filmen i ett eget fönster från 5 sekunder före raden till 5 sekunder efter, med den synkade texten ovanpå. Du ser texten som den är just nu, även det du inte har sparat. **Esc** stänger fönstret.
- Första gången frågar programmet om det får hämta gratisverktyget ffmpeg (cirka 110 MB), om det inte redan finns.
- Är referensen en SRT-fil letar programmet efter filmen i samma mapp. Hittar det ingen får du välja filmen.
- Blockerar Smart App Control verktyget går uppspelningen inte att använda på den datorn.

**Programmet varnar för att texten hör till ett annat avsnitt.**
Filnamnen pekar på olika avsnitt, till exempel S04E02 i textens namn och S04E01 i filmens, eller så är texterna mycket olika långa. Kontrollera att du har valt rätt filer. Är du säker klickar du **Synka ändå**.

**Kan jag ändra, lägga till eller ta bort rader?**
Ja, i listan:
- **Dubbelklick** eller **Enter** ändrar text och tid.
- **Insert** lägger till en rad.
- **Delete** tar bort de markerade raderna.
- **Ctrl+Z** ångrar, i flera steg.

Allt finns också i högerklicksmenyn. Ingenting ändras i filen förrän du sparar.

**Min film har ingen textundertext alls.**
Synka mot filmens **ljud** i stället. Välj i listan **Referens** under fält 1:
- **Ljud A – taldetektering:** snabbast, ungefär en minut. Den hittar var det pratas.
- **Ljud B – Whisper:** taligenkänning som hör vad som sägs. Den är säkrare men tar längre tid, särskilt utan NVIDIA-grafikkort.
- **Ljud A+B – kombinerat:** säkrast. Rekommenderas när verktygen finns.

Första gången laddar Text Sync ner de gratisverktyg som behövs. Programmet frågar först och visar hur stort det är. Vad som hämtas beror på valet i listan **Grafikkort**: cirka 1,4 GB med NVIDIA-grafikkort och cirka 0,6 GB med processorn. Medan det laddas ner behövs ungefär dubbelt så mycket ledigt utrymme. Finns det inte säger programmet till innan något hämtas. Efter en Whisper-synk kan du också spara den igenkända texten som en egen undertext med **Spara igenkänd text…**. Den är automatisk och kan innehålla fel.

**Kan Whisper kontrollera att filmens engelska textspår går i takt?**
Ja. Välj "Spår N … + Whisper-kontroll" i listan **Referens**. Whisper lyssnar ord för ord, och delar av textspåret som ligger mer än ungefär 1 s fel rättas innan din svenska text synkas. I resultatet står hur mycket som kontrollerades och vad som rättades. Det tar ungefär 1 minut per avsnitt med NVIDIA-grafikkort. Välj grafikkort i listan bredvid.

**Filmen har ingen engelsk text, men jag har en engelsk SRT som inte går i takt. Kan den användas?**
Ja. Klicka **Lägg till undertext på filmens originalspråk (SRT)…** under fält 1, eller dra in filmen och båda SRT-filerna samtidigt. Den svenska texten hamnar i fält 2 och den engelska blir originaltext. Den engelska synkas först mot filmens ljud och kontrolleras ord för ord med Whisper. Sedan synkas den svenska mot den. Den synkade engelska texten kan du spara med **Spara synkad originaltext…**. SRT-filen måste vara på samma språk som filmens ljud.

**Filmen har flera ljudspår (till exempel dubbning eller kommentarspår).**
Då visas listan **Ljudspår** under fält 1 när du synkar mot ljudet eller använder Whisper-kontroll. Engelska väljs först, annars filmens standardspår, men aldrig ett kommentarspår eller syntolkning om det finns något annat. Välj spåret där skådespelarna pratar filmens originalspråk.

**Varför syns inte mitt grafikkort i listan Grafikkort?**
Whisper kan bara använda NVIDIA-grafikkort från ungefär 2014 och senare, med en drivrutin från december 2022 eller senare (version 527). Uppdatera drivrutinen från NVIDIA:s webbplats. Annars körs Whisper på processorn, vilket tar längre tid. Står det att grafikkortet inte fungerade under synken, börjar Whisper om på processorn automatiskt.

**Hur tar jag bort verktygen för ljudsynk?**
De ligger i mappen `tools` i `%LOCALAPPDATA%\TextSync` och tar cirka 0,6–1,4 GB. Stäng Text Sync, skriv `%LOCALAPPDATA%\TextSync` i adressfältet i Utforskaren och tryck Enter. Ta sedan bort mappen `tools`. Mappen `cache`, med sparade analyser av ljudet, kan också tas bort. Verktygen laddas ner igen nästa gång du synkar mot ljudet.

**Fungerar ljudsynk på korta klipp?**
Bäst på hela filmer och avsnitt. På korta klipp, som 10 minuter, kan synken hamna fel, eftersom det pratas nästan hela tiden. Kontrollera resultatet i listan.

**Skickas filmens ljud någonstans?**
Nej. Allt körs på din egen dator. Internet behövs bara första gången, för att hämta verktygen.

**Min film har bara bildbaserade undertexter (PGS/VobSub).**
De kan inte läsas, men ljudsynken ovan fungerar. Du kan också använda en engelsk SRT-fil för samma version av filmen som referens.

**Bokstäverna ser konstiga ut (t.ex. "Ã¥" i stället för "å", eller fel tecken på ryska och polska).**
Välj rätt **Teckenkodning** i kort 2, under fältet för undertexten. Västeuropeisk passar svenska och engelska, Centraleuropeisk passar polska och tjeckiska, och Kyrillisk passar ryska.

**Kan jag använda programmet utan mus?**
Ja. Tab flyttar mellan fälten och Alt + understruken bokstav trycker på en knapp, till exempel Alt+S för Synka. I tidslinjen flyttar piltangenterna och + och − zoomar. Programmet följer också Windows högkontrastläge.

**Allt är lite för tidigt eller för sent.**
Ändra **Extra justering**. 500 betyder en halv sekund senare och −500 en halv sekund tidigare.

**Jag skrev över min originalfil.**
Originalet finns kvar i mappen `Text Sync original` bredvid filen.

**"Lägg in i MKV" säger att MKVToolNix behövs.**
Installera gratisprogrammet MKVToolNix. Det behövs bara för den knappen. Det räcker oftast att spara SRT-filen.

**Något gick fel.**
Klicka **Hjälp** och öppna loggmappen. Skicka `log.txt` till den som gav dig programmet.
