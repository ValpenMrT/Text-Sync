# Nästa fas (planerad 2026-11-09)

Önskemål och kända begränsningar som ska tas upp härnäst.

## Önskemål från ägaren
1. ~~**Engelsk SRT som mellansteg.**~~ **Klart i 1.7.** Se beslutsloggen 2026-11-09, "Separat SRT på originalspråket + Whisper-kontroll".
2. **Översättning engelska → svenska** när svensk text saknas helt. Undersök alternativ för lokal översättning på datorn (helst utan internet och utan kostnad), kvalitet, hastighet på RTX A2000 och vad vännerna utan grafikkort behöver.

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
