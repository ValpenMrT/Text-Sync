# Text Sync – instruktioner för Claude

## Språk
- Skriv allt på svenska: svar, kommentarer i koden, texter i gränssnittet, felmeddelanden, dokumentation och commit-meddelanden.
- Namn i koden (klasser, metoder, variabler) får vara engelska, som i den befintliga koden.
- Felsökningsutdata från testprogrammen i `test/` ska också vara på svenska när de ändras.

## Projektet
- C# WinForms, byggs med `csc.exe` som finns i Windows (.NET Framework 4.8) via `build.ps1`. Inget Visual Studio, ingen NuGet.
- Användarna är ägaren och vänner: icke-tekniska Windows 11-användare. Programmet delas via USB (bara `TextSync.exe`, ingen hjälpfil), är osignerat och MIT-licensierat. Gränssnittet är bara på svenska.
- Nästa arbete börjar med att gå igenom **"Obesvarade frågor"** i `README.md` med ägaren, och sedan `docs/next.md` (önskemål och kvarstående fynd). Stryk en fråga ur README när den är besvarad och för in svaret i `docs/decisions.md`.

## Arbetssätt
- Committa efter varje ändring, när bygget och testerna går igenom (`build.ps1 -Test`). Committa bara det du själv har ändrat: en annan session kan arbeta i samma filer samtidigt (stadga då bara dina delar, t.ex. med `git apply --cached`).
- Kör skillen `product-check` före varje commit. Före push behövs den inte.
- Beslut förs in i `docs/decisions.md`, nyast först.
- Användarhjälpen finns i `docs/faq.md`, i snabbstarten i `README.md` och i `ShowHelp()` i `src/MainForm.cs`. Tabellen i `docs/structure.md` visar vad som ska stå var.
- Python finns inte på datorn. Ändra filer med Edit eller PowerShell.

## Testa
- `powershell -ExecutionPolicy Bypass -File build.ps1 -Test` bygger testprogrammen i `test\bin\` och kör `Tests`. Testerna avgör själva om de gick igenom: sista raden börjar med `RESULTAT:`, och kommandot misslyckas om någon kontroll gör det (se `docs/operations.md`). Lägg till ett `Check(...)` för varje ny kontroll i `test/Tests.cs`.
- Smart App Control är påslaget och blockerar ofta nybyggda exe-filer en stund. Kör dem inifrån PowerShell med `[Reflection.Assembly]::Load(...)`. Smart App Control får inte stängas av: det gäller hela Windows och går inte att slå på igen.
- Var testfilmen ligger står i `CLAUDE.local.md`, som inte checkas in.
- Whisper och annat GPU-arbete körs på NVIDIA RTX A2000, aldrig på Intel Iris Xe.

## Gränssnittstester
- Kontrollera att Text Sync-fönstret ligger överst (GetForegroundWindow mot programmets fönster) direkt före varje tangenttryckning eller klick. Avbryt om det inte gör det. Skicka aldrig Delete, Ctrl+Z eller Enter blint. 2026-11-08 blockerade Smart App Control programmet, ett {DEL} hamnade i Utforskaren och 13,9 GB-filmen flyttades till Papperskorgen.
- Kör hellre formuläret inifrån samma process: `Application.Run` styrt av en WinForms-`Timer`. Utan meddelandeslinga läses `Label.Text` tillbaka som tom.
