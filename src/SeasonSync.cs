// Text Sync - synkar ett avsnitt i kön för en hel säsong, utan gränssnitt (parningen och återställningen finns i Season.cs)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TextSync
{
    // Vad som används som referens för varje avsnitt
    public enum SeasonRefMode { Auto = 0, Speech = 1, Combined = 2 }   // Auto = filmens textspår, annars filmens ljud

    public class SeasonOptions
    {
        public string Lang = "sv";
        public SyncMode Mode = SyncMode.Smart;
        public bool DetectFramerate = true, RemoveHi, StripFormatting;
        public SeasonRefMode Ref = SeasonRefMode.Auto;
        public bool WhisperOk;   // Whisper finns på datorn och ett NVIDIA-kort är valt (då används A+B i Auto när filmen saknar text)
    }

    // Done = redan synkad av en tidigare säsongssynk (filen bredvid filmen är den som programmet sparade då)
    public enum EpisodeState { Waiting, Working, Good, Check, Unsure, Failed, Restored, Done }

    public class EpisodeOutcome
    {
        public EpisodeState State = EpisodeState.Waiting;
        public string Text = "";      // beskedet i listan
        public string Detail = "";    // mer i tipsrutan
        public UndoEntry Entry;       // filen som sparades (null = inget sparat)
        public UndoRun Run;           // omgången som Entry hör till
        public bool Saved { get { return Entry != null && !Entry.Restored; } }
    }

    // Referensen för en film: ett textspår, eller ljudet med en metod
    public class RefChoice
    {
        public MkvTrack Track;        // null = ljudet
        public AudioMode Mode;
        public int AudioIndex;
        public bool Audio { get { return Track == null; } }
    }

    public static class SeasonSync
    {
        // Samma val som huvudfönstret gör när en film läses in: engelskt textspår om det finns, annars ljudet.
        // Ett spår med bara tvingad text räcker inte som referens, så då används ljudet.
        public static RefChoice ChooseRef(string film, SeasonRefMode mode, bool whisperOk)
        {
            var c = new RefChoice { Mode = mode == SeasonRefMode.Combined || (mode == SeasonRefMode.Auto && whisperOk) ? AudioMode.Combined : AudioMode.Speech };
            List<Audio.AudioStream> streams;
            if (MainForm.IsMkv(film))
            {
                List<MkvTrack> tracks;
                using (var f = new MkvFile(film)) tracks = f.Tracks;   // stängs direkt så att filmen inte låses
                var text = tracks.Where(t => t.IsSubtitle && t.IsText).ToList();
                if (mode == SeasonRefMode.Auto && text.Count > 0)
                {
                    var pick = text[MainForm.PickTextTrack(text)];
                    if (!pick.Forced) c.Track = pick;
                }
                streams = tracks.Where(t => t.Type == 2).Select(t => new Audio.AudioStream
                { Language = t.Language, Name = t.Name, Default = t.Default, Commentary = Audio.LooksLikeCommentary(t.Name) }).ToList();
            }
            else streams = Audio.ListAudioStreams(film);
            if (streams.Count > 0) c.AudioIndex = Audio.PickAudio(streams);
            return c;
        }

        // Synkar ett avsnitt och sparar resultatet via SeasonUndo (så att det går att återställa).
        // Ett osäkert resultat sparas inte. Fel kastas som undantag och visas av den som anropar.
        // getRun = ger omgången som den sparade filen ska höra till (anropas bara när något sparas)
        public static EpisodeOutcome SyncOne(SeasonItem it, SeasonOptions o, Func<UndoRun> getRun, Action<int, string> progress, CancellationToken ct)
        {
            var notes = new List<string>();
            progress(0, "Läser undertexten…");
            var tgt = Markup.FixAll(Srt.Load(it.TargetPath));
            if (tgt.Count == 0) throw new InvalidDataException("Hittade inga repliker i undertexten – är det en SRT-fil?");
            if (o.RemoveHi)
            {
                // tas bort före synken, som i huvudfönstret
                int ch, rm;
                var cleaned = Sdh.Apply(tgt, out ch, out rm);
                if (cleaned.Count >= 5) tgt = cleaned;
                else notes.Add("Text för hörselskadade togs inte bort, eftersom nästan allt i texten ser ut som sådan.");
            }

            List<Cue> refc = null;
            bool refIsText = true, truncated = false;
            string how;
            if (!it.RefIsVideo)
            {
                refc = Markup.FixAll(Srt.Load(it.RefPath));
                if (refc.Count < 5) throw new InvalidDataException("Referensen innehåller nästan inga repliker (" + refc.Count + ").");
                how = "undertexten " + Path.GetFileName(it.RefPath);
            }
            else
            {
                var choice = ChooseRef(it.RefPath, o.Ref, o.WhisperOk);
                how = "";
                if (choice.Track != null)
                {
                    using (var f = new MkvFile(it.RefPath))
                    {
                        var t = f.Tracks.First(x => x.Number == choice.Track.Number);
                        refc = Markup.FixAll(f.ReadSubtitles(t, p => progress(p * 40 / 100, "Läser filmens textspår… " + p + " %"), () => ct.IsCancellationRequested));
                        truncated = f.Truncated;
                    }
                    ct.ThrowIfCancellationRequested();
                    how = "filmens textspår (" + choice.Track + ")";
                    if (refc.Count < 5) refc = null;   // nästan tomt spår: använd ljudet i stället
                }
                if (refc == null)
                {
                    var ar = Audio.GetReference(it.RefPath, new AudioRefOption { Mode = choice.Mode, AudioIndex = choice.AudioIndex },
                        (p, s) => { ct.ThrowIfCancellationRequested(); progress(p * 80 / 100, s); }, ct);
                    refc = ar.Cues; refIsText = false;
                    how = "filmens ljud (" + (choice.Mode == AudioMode.Speech ? "A – taldetektering" : "A+B – med Whisper") + ")";
                    if (refc.Count < 5) throw new InvalidDataException("Det gick nästan inte att hitta något tal i filmens ljud.");
                }
            }

            // samma varningar som huvudfönstret visar i en ruta: här blir de en anmärkning, och avsnittet får beskedet Kontrollera
            string wrongEp = EpisodeId.Mismatch(it.RefPath, it.TargetPath, "Texten", it.RefIsVideo ? "filmen" : "referensen");
            string wrongLen = refIsText && !truncated ? EpisodeId.LengthMismatch(refc, tgt, it.RefIsVideo ? "filmens text" : "referensen") : null;
            if (wrongEp != null) notes.Add(wrongEp);
            if (wrongLen != null) notes.Add(wrongLen);
            if (truncated) notes.Add("Filmen verkar vara ofullständig – bara en del av referensen kunde läsas.");

            int basePct = refIsText ? 40 : 80;
            var r = SyncEngine.Sync(refc, tgt, new SyncOptions { Mode = o.Mode, DetectFramerate = o.DetectFramerate },
                (p, s) => { ct.ThrowIfCancellationRequested(); progress(basePct + p * (98 - basePct) / 100, s); });
            ct.ThrowIfCancellationRequested();
            var output = r.Output;
            int stripped;
            if (o.StripFormatting) output = Markup.StripAll(output, out stripped);
            var q = Quality.Assess(refc, output, r, refIsText);
            notes.AddRange(r.Notes);
            Log.Info("Säsong " + it.Title + ": ref=" + it.RefPath + " (" + how + "), mål=" + it.TargetPath + ", " + r.Method + ", " + q.LogText());

            var res = new EpisodeOutcome();
            var detail = new List<string>();
            if (q.Verdict == Verdict.Unsure || output.Count(c => c.End > 0) == 0)
            {
                res.State = EpisodeState.Unsure;
                res.Text = "✖ Osäkert – inte sparad";
                detail.Add("Texten verkar inte passa " + (it.RefIsVideo ? "filmen" : "referensen") + ". Den kan höra till ett annat avsnitt eller en annan version. Ingenting har sparats.");
                detail.Add("Byt undertext och synka igen, eller öppna avsnittet i huvudfönstret för att se resultatet.");
            }
            else
            {
                string outPath = it.OutputPath(o.Lang);
                if (outPath == null || MainForm.SamePath(outPath, it.RefPath)) throw new InvalidDataException("Det går inte att avgöra var undertexten ska sparas.");
                // skrivs en fil över läggs en kopia i mappen "Text Sync original", som när man sparar över originalet i huvudfönstret.
                // En kopia som redan finns där skrivs aldrig över. (Kopian för Återställ… sparas dessutom alltid av SeasonUndo.)
                if (File.Exists(outPath))
                {
                    string bdir = Path.Combine(Path.GetDirectoryName(outPath), MainForm.BackupFolder);
                    string backup = Path.Combine(bdir, Path.GetFileName(outPath));
                    Directory.CreateDirectory(bdir);
                    if (!File.Exists(backup)) File.Copy(outPath, backup);
                }
                int cp, replaced;
                byte[] bytes = Srt.Encode(Srt.ToSrt(output), Srt.SaveFormat, out cp, out replaced);
                res.Run = getRun();   // omgången skapas först när något ska sparas
                res.Entry = SeasonUndo.Write(res.Run, outPath, bytes, it.Title);
                bool good = q.Verdict == Verdict.Good && wrongEp == null && wrongLen == null && !truncated;
                res.State = good ? EpisodeState.Good : EpisodeState.Check;
                res.Text = good ? "✔ Bra – sparad" : "⚠ Kontrollera – sparad";
                detail.Add("Sparad: " + outPath);
                if (!good)
                    detail.Add(q.Sparse ? "Referensen har för lite text för att synken ska kunna bedömas. Öppna avsnittet i huvudfönstret och kontrollera några rader."
                        : "Synken är inte helt säker. Öppna avsnittet i huvudfönstret för att se de svaga ställena." + (refIsText ? "" : " Filmens ljud A+B i listan Referens är säkrare än A."));
                Log.Info("Säsong " + it.Title + ": sparad " + outPath + (res.Entry.Kept != null ? " (den gamla filen finns i " + res.Entry.Kept + ")" : ""));
            }
            detail.AddRange(notes);
            detail.Add("Referens: " + how + ".");
            detail.Add(r.Segments.Count > 1 ? "Flyttad olika mycket i " + r.Segments.Count + " delar." : "Förskjutning " + MainForm.FmtOffset(r.OffsetMs) + ".");
            detail.Add(MainForm.Fmt(Math.Floor(q.Matched * 1000) / 10, "0.#") + " % av raderna " + (refIsText ? "har referenstext samtidigt." : "ligger på tal i ljudet."));
            res.Detail = string.Join("\n", detail);
            progress(100, "Klar");
            return res;
        }
    }
}
