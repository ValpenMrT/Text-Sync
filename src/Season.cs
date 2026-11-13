// Text Sync - synka en hel säsong: para ihop filmer och undertexter, och återställ filerna efter en säsongssynk.
// Här finns bara logiken. Kön och fönstret byggs ovanpå.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TextSync
{
    // Ett avsnitt i säsongslistan: en film (eller en referenstext) och undertexten som ska synkas mot den
    public class SeasonItem
    {
        public EpisodeId Id;                 // null om filnamnet saknar avsnittsnummer
        public string RefPath;               // filmen, eller en referens-SRT (annat språk) när film saknas. null = saknas
        public bool RefIsVideo;
        public string TargetPath;            // undertexten som ska synkas. null = saknas
        public List<string> Candidates = new List<string>();   // alla SRT-filer som kan vara mål för avsnittet, TargetPath först
        public string Note;                  // svensk anmärkning till användaren, eller null
        public bool Include;                 // ska synkas (false när något saknas)

        // "S04E01", eller filnamnet utan ändelse när avsnittsnummer saknas
        public string Title
        {
            get
            {
                if (Id != null) return "S" + Id.Season.ToString("00", CultureInfo.InvariantCulture) + "E" + Id.Episode.ToString("00", CultureInfo.InvariantCulture);
                string p = RefPath ?? TargetPath;
                if (p == null) return "";
                try { return Path.GetFileNameWithoutExtension(p); } catch { return p; }
            }
        }

        // Var den synkade texten sparas. Film: bredvid filmen med filmens namn och språkkoden, så att spelare hittar den.
        // Referens-SRT: bredvid texten med ".synkad" i namnet. null om film/referens eller undertext saknas.
        public string OutputPath(string lang)
        {
            if (RefPath == null || TargetPath == null) return null;
            string o;
            if (RefIsVideo)
            {
                lang = (lang ?? "").Trim();
                if (lang.Length == 0) lang = "sv";
                o = Path.Combine(Path.GetDirectoryName(RefPath) ?? "", Path.GetFileNameWithoutExtension(RefPath) + "." + lang + ".srt");
            }
            else o = Path.Combine(Path.GetDirectoryName(TargetPath) ?? "", Path.GetFileNameWithoutExtension(TargetPath) + ".synkad.srt");
            // Regel 9: resultatet får aldrig bli samma fil som referensen
            while (MainForm.SamePath(o, RefPath))
                o = Path.Combine(Path.GetDirectoryName(o) ?? "", Path.GetFileNameWithoutExtension(o) + ".synkad.srt");
            return o;
        }

        // Där avsnittet faktiskt sparas: OutputPath om den är ledig, annars med ".synkad" i namnet (Film.synkad.sv.srt, Film.synkad2.sv.srt …).
        // Programmet skriver aldrig över en fil (ägarens svar 2026-11-14).
        public string FreeOutputPath(string lang)
        {
            string o = OutputPath(lang);
            return o == null ? null : MainForm.FreePath(o, "synkad");
        }

        // OutputPath och de namn med ".synkad" som finns på disken: filerna som en tidigare säsongssynk kan ha sparat för avsnittet
        public List<string> SavedCandidates(string lang)
        {
            var res = new List<string>();
            string o = OutputPath(lang);
            if (o == null) return res;
            try
            {
                if (File.Exists(o)) res.Add(o);
                for (int n = 1; n <= 50; n++)
                {
                    string p = MainForm.TaggedPath(o, "synkad", n);
                    if (File.Exists(p) && !res.Contains(p, StringComparer.OrdinalIgnoreCase)) res.Add(p);
                }
            }
            catch { }
            return res;
        }
    }

    public static class SeasonPlan
    {
        public const string NoteNoSubtitle = "Ingen undertext hittades för avsnittet.";
        public const string NoteSeveral = "Flera undertexter passar. Kontrollera att rätt fil är vald.";
        public const string NoteDuplicate = "Avsnittet finns två gånger bland filmerna.";
        const int MaxDepth = 3;                       // undermappar så här många nivåer ner tas med (t.ex. "Subs")
        const string OwnMovieSuffix = " (Text Sync)"; // programmets egna MKV-kopior heter "<film> (Text Sync).mkv"

        static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        // ------------------------------------------------------------ samla filer
        // Mappar görs om till filerna i dem (även undermappar, högst 3 nivåer ner). Filer tas med som de är.
        // Mappar som heter MainForm.BackupFolder hoppas över. Bara filmer och .srt tas med.
        // Fel på en mapp (behörighet m.m.) kastar inte: mappen hoppas över.
        public static List<string> Collect(IEnumerable<string> paths)
        {
            var res = new List<string>();
            var seen = new HashSet<string>(Ci);
            if (paths == null) return res;
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                try
                {
                    if (Directory.Exists(p)) AddDir(p, 0, res, seen);
                    else AddFile(p, res, seen);
                }
                catch (Exception ex) { Log.Error("Säsong: kunde inte läsa " + p, ex); }
            }
            return res;
        }

        static void AddFile(string f, List<string> res, HashSet<string> seen)
        {
            if (!IsSub(f) && !IsFilm(f)) return;
            string key;
            try { key = Path.GetFullPath(f); } catch { key = f; }
            if (seen.Add(key)) res.Add(f);
        }

        static void AddDir(string dir, int depth, List<string> res, HashSet<string> seen)
        {
            if (IsBackupName(dir)) return;
            try
            {
                var files = Directory.GetFiles(dir);
                Array.Sort(files, Ci);
                foreach (var f in files) AddFile(f, res, seen);
                if (depth >= MaxDepth) return;
                var subs = Directory.GetDirectories(dir);
                Array.Sort(subs, Ci);
                foreach (var s in subs) AddDir(s, depth + 1, res, seen);
            }
            catch (Exception ex) { Log.Info("Säsong: mappen hoppades över (" + ex.GetType().Name + "): " + dir); }
        }

        static bool IsBackupName(string dir)
        {
            try { return string.Equals(Path.GetFileName(dir.TrimEnd('\\', '/')), MainForm.BackupFolder, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ------------------------------------------------------------ para ihop
        // Parar ihop filmer och undertexter. wantLang = språkkoden som ska synkas (t.ex. "sv"; null/tomt = "sv").
        // unused = filer som inte användes, i samma ordning som de kom in. Programmets egna filer (regel 1) räknas inte dit.
        //
        // Regler:
        // 1. Hoppas över: filer i en mapp som heter "Text Sync original", undertexter vars namn innehåller ".whisper."
        //    (programmets egna igenkända texter) och ".synkad.srt"-filer som mål när en annan kandidat finns.
        //    Filmer vars namn slutar med " (Text Sync)" (programmets egna MKV-kopior) hoppas över om en annan film
        //    med samma avsnitt (eller samma namn utan tillägget) finns.
        // 2. Varje film blir en post. Två filmer med samma avsnitt blir var sin post, med en anmärkning om att avsnittet finns två gånger.
        // 3. En SRT hör till en film om (a) SRT:ns filnamn börjar med filmens namn utan ändelse följt av "." (skiftlägesokänsligt),
        //    eller annars (b) avsnittsnumret är samma. (a) går före (b). Passar (b) på flera filmer hör texten till alla.
        // 4. Mål bland ett avsnitts SRT-filer: först de med önskat språk. Finns ingen sådan: de utan känd språkkod.
        //    SRT-filer med ett annat känt språk (t.ex. .en.srt) blir aldrig mål. Flera lika bra: den i samma mapp som filmen,
        //    annars första i bokstavsordning, och en anmärkning. Candidates = alla som kan vara mål, den valda först.
        // 5. Avsnitt utan film men med minst två SRT, där en har önskat (eller okänt) språk och en annan har ett annat känt språk:
        //    den andra blir referens och den första synkas. Två SRT som båda saknar språkkod paras inte.
        // 6. Film utan undertext: posten finns med, utan mål och utan bock. En SRT utan film och utan referens-SRT hamnar i unused.
        // 7. Filer utan avsnittsnummer paras bara via regel 3a.
        // 8. Include = både referens och mål finns. Sortering: säsong, avsnitt, filnamn. Poster utan avsnittsnummer sist i bokstavsordning.
        // 9. OutputPath blir aldrig samma fil som RefPath (se SeasonItem.OutputPath).
        public static List<SeasonItem> Build(IEnumerable<string> files, string wantLang, out List<string> unused)
        {
            string want = MainForm.Norm((wantLang ?? "").Trim()) ?? "sv";
            var all = new List<string>();
            var seen = new HashSet<string>(Ci);
            if (files != null)
                foreach (var f in files)
                {
                    if (string.IsNullOrEmpty(f)) continue;
                    string key;
                    try { key = Path.GetFullPath(f); } catch { key = f; }
                    if (seen.Add(key)) all.Add(f);
                }

            // Regel 1: sortera bort programmets egna filer
            var own = new HashSet<string>(Ci);
            var filmPaths = new List<string>();
            var srts = new List<string>();
            foreach (var f in all)
            {
                if (InBackupFolder(f)) { own.Add(f); continue; }
                if (IsSub(f))
                {
                    if (FileName(f).IndexOf(".whisper.", StringComparison.OrdinalIgnoreCase) >= 0) own.Add(f);
                    else srts.Add(f);
                }
                else if (IsFilm(f)) filmPaths.Add(f);
            }
            var films = new List<Film>();
            foreach (var f in filmPaths)
            {
                if (IsOwnCopy(f) && filmPaths.Any(o => !IsOwnCopy(o) && SameMovie(o, f))) { own.Add(f); continue; }
                films.Add(new Film { Path = f, Name = NameNoExt(f), Id = EpisodeId.Parse(f) });
            }

            // Regel 3: vilken film hör varje SRT till?
            var loose = new List<string>();
            foreach (var s in srts)
            {
                string sn = FileName(s);
                Film best = null;
                foreach (var film in films)
                {
                    if (!sn.StartsWith(film.Name + ".", StringComparison.OrdinalIgnoreCase)) continue;
                    // flera filmer passar: det längsta namnet är det mest exakta, därefter filmen i samma mapp
                    if (best == null || film.Name.Length > best.Name.Length ||
                        (film.Name.Length == best.Name.Length && SameDir(film.Path, s) && !SameDir(best.Path, s))) best = film;
                }
                if (best != null) { best.Subs.Add(s); continue; }
                var id = EpisodeId.Parse(s);
                var same = id == null ? new List<Film>() : films.Where(f => f.Id != null && f.Id.Same(id)).ToList();
                if (same.Count == 0) { loose.Add(s); continue; }
                foreach (var film in same) film.Subs.Add(s);
            }

            var items = new List<SeasonItem>();
            // Regel 2, 4 och 6: en post per film
            foreach (var film in films)
            {
                var it = new SeasonItem { Id = film.Id, RefPath = film.Path, RefIsVideo = true };
                Pick(film.Subs, want, film, it, own);
                if (it.TargetPath == null) it.Note = NoteNoSubtitle;
                if (film.Id != null && films.Count(f => f.Id != null && f.Id.Same(film.Id)) > 1)
                    it.Note = it.Note == null ? NoteDuplicate : it.Note + " " + NoteDuplicate;
                items.Add(it);
            }

            // Regel 5: avsnitt utan film, där en text på ett annat språk får vara referens
            var groups = new List<List<string>>();
            foreach (var s in loose)
            {
                var id = EpisodeId.Parse(s);
                if (id == null) continue;   // regel 7
                var g = groups.FirstOrDefault(x => EpisodeId.Parse(x[0]).Same(id));
                if (g == null) { g = new List<string>(); groups.Add(g); }
                g.Add(s);
            }
            foreach (var g in groups)
            {
                var refs = g.Where(s => { string l = LangOf(s); return l != null && l != want; }).ToList();
                if (refs.Count == 0) continue;
                var it = new SeasonItem { Id = EpisodeId.Parse(g[0]), RefIsVideo = false };
                Pick(g, want, null, it, own);
                if (it.TargetPath == null) continue;
                string tgt = it.TargetPath;
                it.RefPath = refs.OrderBy(s => SameDir(s, tgt) ? 0 : 1).ThenBy(s => FileName(s), Ci).ThenBy(s => s, Ci).First();
                items.Add(it);
            }

            // Regel 8
            foreach (var it in items) it.Include = it.RefPath != null && it.TargetPath != null;
            items = items.OrderBy(i => i.Id == null ? 1 : 0)
                .ThenBy(i => i.Id != null ? i.Id.Season : 0).ThenBy(i => i.Id != null ? i.Id.Episode : 0)
                .ThenBy(i => FileName(i.RefPath ?? i.TargetPath ?? ""), Ci).ThenBy(i => i.RefPath ?? "", Ci).ToList();

            var used = new HashSet<string>(Ci);
            foreach (var it in items)
            {
                if (it.RefPath != null) used.Add(it.RefPath);
                foreach (var c in it.Candidates) used.Add(c);
            }
            unused = all.Where(f => !used.Contains(f) && !own.Contains(f)).ToList();
            return items;
        }

        class Film
        {
            public string Path, Name;
            public EpisodeId Id;
            public List<string> Subs = new List<string>();
        }

        // Regel 4: väljer mål bland avsnittets SRT-filer.
        // film = filmen (texter i samma mapp går före, därefter texter som heter som filmen), eller null.
        // own = hit läggs synkade texter som valdes bort, så att de inte räknas som överblivna.
        static void Pick(List<string> subs, string want, Film film, SeasonItem it, HashSet<string> own)
        {
            var ok = subs.Where(s => { string l = LangOf(s); return l == null || l == want; }).ToList();
            // regel 1: en redan synkad text (.synkad.srt) väljs aldrig om en annan kandidat finns
            if (ok.Any(s => !IsSynced(s)))
            {
                foreach (var s in ok.Where(s => IsSynced(s))) own.Add(s);
                ok = ok.Where(s => !IsSynced(s)).ToList();
            }
            if (ok.Count == 0) return;
            var pool = ok.Where(s => LangOf(s) == want).ToList();
            if (pool.Count == 0) pool = ok;
            Func<IEnumerable<string>, List<string>> sort = l =>
                l.OrderBy(s => film != null && SameDir(s, film.Path) ? 0 : 1)
                 .ThenBy(s => film != null && FileName(s).StartsWith(film.Name + ".", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                 // den vanliga texten går före text för hörselskadade och tvingad text (Film.sv.srt före Film.sv.sdh.srt)
                 .ThenBy(s => System.Text.RegularExpressions.Regex.IsMatch(NameNoExt(s), @"[._ -](forced|sdh|cc|hi)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 1 : 0)
                 .ThenBy(s => FileName(s), Ci).ThenBy(s => s, Ci).ToList();
            pool = sort(pool);
            it.TargetPath = pool[0];
            if (pool.Count > 1) it.Note = NoteSeveral;
            it.Candidates = pool;
            it.Candidates.AddRange(sort(ok.Where(s => !pool.Contains(s))));
        }

        // ------------------------------------------------------------ hjälpare
        static bool IsFilm(string f) { try { return MainForm.IsMkv(f) || Audio.IsVideo(f); } catch { return false; } }
        static bool IsSub(string f) { try { return MainForm.IsSrt(f); } catch { return false; } }
        static string FileName(string f) { try { return Path.GetFileName(f) ?? ""; } catch { return f ?? ""; } }
        static string NameNoExt(string f) { try { return Path.GetFileNameWithoutExtension(f) ?? ""; } catch { return f ?? ""; } }
        static string DirOf(string f) { try { return Path.GetDirectoryName(Path.GetFullPath(f)) ?? ""; } catch { return ""; } }
        static bool SameDir(string a, string b) { return string.Equals(DirOf(a), DirOf(b), StringComparison.OrdinalIgnoreCase); }
        // programmets egna synkade texter: Namn.synkad.srt, Namn.synkad2.srt, Film.synkad.sv.srt, Film.synkad3.sv.srt
        static bool IsSynced(string f) { return System.Text.RegularExpressions.Regex.IsMatch(FileName(f), @"\.synkad\d*(\.[A-Za-z]{2,3})?\.srt$", System.Text.RegularExpressions.RegexOptions.IgnoreCase); }

        // Språkkoden i filnamnet, normaliserad ("swe" → "sv"), eller null. ".synkad" räknas inte som en del av namnet.
        static string LangOf(string f)
        {
            string n = NameNoExt(f);
            n = System.Text.RegularExpressions.Regex.Replace(n, @"\.synkad\d*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            try { return MainForm.Norm(MainForm.GuessLang(n + ".srt")); } catch { return null; }
        }

        static bool InBackupFolder(string f)
        {
            try
            {
                string d = Path.GetDirectoryName(Path.GetFullPath(f));
                while (!string.IsNullOrEmpty(d))
                {
                    if (IsBackupName(d)) return true;
                    d = Path.GetDirectoryName(d);
                }
            }
            catch { }
            return false;
        }

        static bool IsOwnCopy(string film) { return NameNoExt(film).EndsWith(OwnMovieSuffix, StringComparison.OrdinalIgnoreCase); }

        // Är kopian "X (Text Sync)" samma film som other? Samma avsnitt, eller samma namn utan tillägget.
        static bool SameMovie(string other, string copy)
        {
            EpisodeId a = EpisodeId.Parse(other), b = EpisodeId.Parse(copy);
            if (a != null && a.Same(b)) return true;
            string n = NameNoExt(copy);
            n = n.Substring(0, n.Length - OwnMovieSuffix.Length).TrimEnd();
            return string.Equals(NameNoExt(other), n, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ================================================================ återställning
    public class UndoEntry
    {
        public string Path;      // filen som skrevs
        public string Kept;      // kopian av filen som fanns där före (i körningens mapp), null = filen fanns inte före
        public string Hash;      // SHA-256 (hex) av det programmet skrev, för att se om filen har ändrats sedan dess
        public string Label;     // t.ex. "S04E01"
        public bool Restored;    // redan återställd
    }

    public class UndoRun
    {
        public string Dir;       // körningens mapp
        public DateTime When;
        public string Title;     // visas för användaren
        public List<UndoEntry> Entries = new List<UndoEntry>();
        public int Pending { get { return Entries.Count(e => !e.Restored); } }   // antal poster som inte är återställda
    }

    // Restored = filen som fanns före är tillbaka. Removed = den nya filen togs bort (ingen fil fanns före).
    // Changed = filen har ändrats efter synken (eller ersatts) och rördes inte. Failed = fel, se Message.
    public enum RestoreOutcome { Restored, Removed, Changed, Failed }

    public class RestoreResult
    {
        public UndoEntry Entry;
        public RestoreOutcome Outcome;
        public string Message;   // svensk mening för användaren
    }

    // Varje säsongssynk ("körning") får en egen mapp under Root, med ett manifest och kopior av filerna som skrevs över.
    // Så går en hel säsongssynk att ångra, också efter omstart och ett avsnitt i taget.
    public static class SeasonUndo
    {
        public static string Root = System.IO.Path.Combine(Log.Dir, "undo");   // testerna sätter en egen mapp
        const string ManifestName = "manifest.txt";
        const string Header = "TextSync-undo 1";
        const string TmpSuffix = ".textsync-tmp";
        static readonly object Lock = new object();

        // Testerna stänger av loggningen, så att de inte skriver i användarens riktiga loggfil
        public static bool Logging = true;
        static void Info(string msg) { if (Logging) Log.Info(msg); }
        static void Err(string msg, Exception ex) { if (Logging) Log.Error(msg, ex); }

        // Skapar mappen <Root>\<yyyyMMdd-HHmmss>[-n] och ett tomt manifest. Kastar om mappen inte går att skapa.
        public static UndoRun Begin(string title)
        {
            lock (Lock)
            {
                string root = System.IO.Path.GetFullPath(Root);
                Directory.CreateDirectory(root);
                DateTime now = DateTime.Now;
                string stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string dir = System.IO.Path.Combine(root, stamp);
                for (int n = 2; Directory.Exists(dir) || File.Exists(dir); n++) dir = System.IO.Path.Combine(root, stamp + "-" + n);
                Directory.CreateDirectory(dir);
                var run = new UndoRun { Dir = dir, When = now };
                run.Title = string.IsNullOrEmpty(title) ? System.IO.Path.GetFileName(dir) : title;
                Save(run);
                return run;
            }
        }

        // Skriver bytes till path. Fanns filen kopieras den först till körningens mapp, i en egen undermapp per post
        // (0001\<samma filnamn>), så att filnamnet och språkkoden behålls.
        // Ordningen är: kopia, manifest, fil. Kraschar programmet mitt i stämmer manifestet ändå: antingen ligger originalet
        // kvar orört (Restore ser det och gör ingenting), eller så är den nya filen skriven och går att återställa.
        // Skrivs samma path två gånger i samma körning behålls den första postens Kept (det verkliga originalet) och bara Hash uppdateras.
        // Kastar om filen inte går att skriva. Då finns ingen post kvar för den.
        public static UndoEntry Write(UndoRun run, string path, byte[] bytes, string label)
        {
            if (run == null) throw new ArgumentNullException("run");
            if (bytes == null) throw new ArgumentNullException("bytes");
            lock (Lock)
            {
                string full = System.IO.Path.GetFullPath(path);
                string hash = HashBytes(bytes);
                UndoEntry e = run.Entries.LastOrDefault(x => !x.Restored && MainForm.SamePath(x.Path, full));
                // Posten återanvänds bara om filen på disken är den som programmet skrev. Har användaren ändrat filen sedan dess
                // får den en egen post med en egen kopia, så att ändringen inte skrivs över utan att sparas.
                if (e != null)
                {
                    bool same = false;
                    try { same = File.Exists(full) && string.Equals(HashBytes(File.ReadAllBytes(full)), e.Hash, StringComparison.OrdinalIgnoreCase); } catch { }
                    if (!same) e = null;
                }
                bool isNew = e == null;
                string oldHash = null, oldLabel = null, keptDir = null;
                try
                {
                    if (isNew)
                    {
                        e = new UndoEntry { Path = full, Hash = hash, Label = label };
                        if (File.Exists(full))
                        {
                            int n = run.Entries.Count + 1;
                            do { keptDir = System.IO.Path.Combine(run.Dir, n.ToString("0000", CultureInfo.InvariantCulture)); n++; }
                            while (Directory.Exists(keptDir) || File.Exists(keptDir));
                            Directory.CreateDirectory(keptDir);
                            string kept = System.IO.Path.Combine(keptDir, System.IO.Path.GetFileName(full));
                            File.Copy(full, kept, false);
                            File.SetAttributes(kept, FileAttributes.Normal);   // en skrivskyddad kopia skulle inte gå att rensa bort
                            e.Kept = kept;
                        }
                        run.Entries.Add(e);
                    }
                    else
                    {
                        oldHash = e.Hash; oldLabel = e.Label;
                        e.Hash = hash;
                        if (label != null) e.Label = label;
                    }
                    Save(run);
                    WriteFile(full, bytes);
                    return e;
                }
                catch
                {
                    // Ingenting skrevs: ta bort posten igen (eller sätt tillbaka den gamla), så att manifestet stämmer med disken
                    if (isNew)
                    {
                        if (e != null) run.Entries.Remove(e);
                        try { if (keptDir != null && Directory.Exists(keptDir)) Directory.Delete(keptDir, true); } catch { }
                    }
                    else { e.Hash = oldHash; e.Label = oldLabel; }
                    try { Save(run); } catch { }
                    throw;
                }
            }
        }

        // Alla körningar som går att läsa, nyast först. Trasiga manifest hoppas över och loggas.
        public static List<UndoRun> List()
        {
            var res = new List<UndoRun>();
            lock (Lock)
            {
                try
                {
                    foreach (var d in RunDirs())
                    {
                        try { res.Add(Load(d)); }
                        catch (Exception ex) { Err("Återställning: manifestet kunde inte läsas och hoppas över: " + d, ex); }
                    }
                }
                catch (Exception ex) { Err("Återställning: kunde inte läsa " + Root, ex); }
            }
            return res.OrderByDescending(r => r.When).ThenByDescending(r => DirKey(r.Dir), StringComparer.Ordinal).ToList();
        }

        // Nyaste körningen som har något kvar att återställa, annars null
        public static UndoRun Latest()
        {
            return List().FirstOrDefault(r => r.Pending > 0);
        }

        // Återställer en post. Säkerheten går först: en fil som användaren har ändrat efter synken tas aldrig bort och skrivs aldrig över.
        // - Filen finns och är den programmet skrev (samma SHA-256): originalet läggs tillbaka (Restored), eller filen tas bort
        //   om ingen fil fanns före (Removed).
        // - Filen finns men är en annan: den rörs inte (Changed). Undantag: den är redan identisk med originalet (Restored, inget görs).
        // - Filen finns inte: originalet läggs tillbaka (Restored), eller så finns inget att göra (Removed).
        // - Kopian av originalet saknas: Failed, och filen rörs inte.
        // - Fel (låst fil m.m.): Failed med en mening på svenska. Kastar aldrig.
        // En redan återställd post återställs inte igen.
        public static RestoreResult Restore(UndoRun run, UndoEntry e)
        {
            var r = new RestoreResult { Entry = e, Outcome = RestoreOutcome.Failed };
            if (run == null || e == null || !run.Entries.Contains(e))
            {
                r.Message = "Filen hör inte till den här synkningen och kunde inte återställas.";
                return r;
            }
            string name = e.Path;
            try { name = System.IO.Path.GetFileName(e.Path); } catch { }
            lock (Lock)
            {
                if (e.Restored)
                {
                    r.Outcome = e.Kept != null ? RestoreOutcome.Restored : RestoreOutcome.Removed;
                    r.Message = "Redan återställd: " + name;
                    return r;
                }
                try
                {
                    if (e.Kept != null && !File.Exists(e.Kept))
                    {
                        r.Message = "Kopian av originalet saknas, så filen kunde inte återställas och har inte rörts: " + name;
                        return r;
                    }
                    if (File.Exists(e.Path))
                    {
                        string now = IsStamp(e.Hash) ? FileStamp(e.Path) : HashFile(e.Path);
                        if (string.Equals(now, e.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            if (e.Kept != null)
                            {
                                PutBack(e.Kept, e.Path);
                                r.Outcome = RestoreOutcome.Restored; r.Message = "Återställd: " + name;
                            }
                            else
                            {
                                File.Delete(e.Path);
                                r.Outcome = RestoreOutcome.Removed; r.Message = "Borttagen: " + name;
                            }
                        }
                        else if (e.Kept != null && string.Equals(now, HashFile(e.Kept), StringComparison.OrdinalIgnoreCase))
                        {
                            r.Outcome = RestoreOutcome.Restored; r.Message = "Originalet låg redan på plats: " + name;
                        }
                        else
                        {
                            r.Outcome = RestoreOutcome.Changed;
                            r.Message = "Filen har ändrats efter synken och rördes inte: " + name;
                            return r;
                        }
                    }
                    else if (e.Kept != null)
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(e.Path));
                        PutBack(e.Kept, e.Path);
                        r.Outcome = RestoreOutcome.Restored; r.Message = "Återställd: " + name;
                    }
                    else
                    {
                        r.Outcome = RestoreOutcome.Removed; r.Message = "Filen fanns inte kvar: " + name;
                    }
                }
                catch (Exception ex)
                {
                    Err("Återställning: kunde inte återställa " + e.Path, ex);
                    r.Outcome = RestoreOutcome.Failed;
                    r.Message = Friendly(ex) + " (" + name + ")";
                    return r;
                }
                e.Restored = true;
                // Går manifestet inte att skriva är filen ändå återställd. Nästa försök ser det och gör ingenting.
                try { Save(run); }
                catch (Exception ex) { Err("Återställning: manifestet kunde inte skrivas: " + run.Dir, ex); }
                Info("Återställning: " + r.Message + " (" + e.Path + ")");
                return r;
            }
        }

        // Alla poster som inte redan är återställda, den senast skrivna först
        public static List<RestoreResult> RestoreAll(UndoRun run)
        {
            var res = new List<RestoreResult>();
            if (run == null) return res;
            List<UndoEntry> todo;
            lock (Lock) { todo = run.Entries.Where(e => !e.Restored).Reverse().ToList(); }
            foreach (var e in todo) res.Add(Restore(run, e));
            return res;
        }

        // Tar bort alla utom de keep nyaste körningarnas mappar. Bara mappar direkt under Root som innehåller ett manifest
        // tas bort, och inga sökvägar ur manifestet följs. Kastar inte.
        public static void Prune(int keep)
        {
            if (keep < 0) keep = 0;
            lock (Lock)
            {
                try
                {
                    var dirs = RunDirs().OrderByDescending(d => DirKey(d), StringComparer.Ordinal).ToList();
                    for (int i = keep; i < dirs.Count; i++)
                    {
                        try
                        {
                            if ((File.GetAttributes(dirs[i]) & FileAttributes.ReparsePoint) != 0) continue;   // en länk till en annan mapp rörs aldrig
                            Directory.Delete(dirs[i], true);
                        }
                        catch (Exception ex) { Err("Återställning: kunde inte rensa " + dirs[i], ex); }
                    }
                }
                catch (Exception ex) { Err("Återställning: kunde inte rensa i " + Root, ex); }
            }
        }

        // Tar bort körningar där ingenting finns kvar att återställa (tomma eller helt återställda), utom exceptDir.
        // De ska inte räknas bland dem som Prune behåller. Kastar inte.
        public static void PruneFinished(string exceptDir)
        {
            foreach (var u in List())
            {
                if (u.Pending > 0 || string.Equals(u.Dir, exceptDir, StringComparison.OrdinalIgnoreCase)) continue;
                lock (Lock)
                {
                    try
                    {
                        if (!RunDirs().Any(d => string.Equals(d, u.Dir, StringComparison.OrdinalIgnoreCase))) continue;   // bara mappar direkt under Root med manifest
                        if ((File.GetAttributes(u.Dir) & FileAttributes.ReparsePoint) != 0) continue;
                        Directory.Delete(u.Dir, true);
                    }
                    catch (Exception ex) { Err("Återställning: kunde inte rensa " + u.Dir, ex); }
                }
            }
        }

        // SHA-256 som hexsträng (små bokstäver)
        public static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }

        // En stor fil som programmet har skapat (en MKV-kopia på flera GB) känns igen på storlek och ändringstid i stället för SHA-256,
        // som skulle ta en halv minut per film. "size-<byte>-<ticks>".
        static bool IsStamp(string h) { return h != null && System.Text.RegularExpressions.Regex.IsMatch(h, @"^size-\d{1,19}-\d{1,19}$"); }
        static string FileStamp(string path)
        {
            var fi = new FileInfo(path);
            return "size-" + fi.Length.ToString(CultureInfo.InvariantCulture) + "-" + fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        // Lägger till en fil som programmet just har skapat på en plats där ingen fil fanns (t.ex. en MKV-kopia med undertexten inlagd).
        // Återställningen tar bort den, om den inte har ändrats sedan dess. Kastar om filen saknas eller manifestet inte går att skriva.
        public static UndoEntry RegisterCreated(UndoRun run, string path, string label)
        {
            if (run == null) throw new ArgumentNullException("run");
            lock (Lock)
            {
                string full = System.IO.Path.GetFullPath(path);
                var e = new UndoEntry { Path = full, Hash = FileStamp(full), Label = label };
                run.Entries.Add(e);
                try { Save(run); }
                catch { run.Entries.Remove(e); throw; }
                return e;
            }
        }

        // ------------------------------------------------------------ hjälpare
        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Hex(sha.ComputeHash(fs));
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // Mappar direkt under Root som innehåller ett manifest
        static List<string> RunDirs()
        {
            var res = new List<string>();
            string root = System.IO.Path.GetFullPath(Root);
            if (!Directory.Exists(root)) return res;
            foreach (var d in Directory.GetDirectories(root))
                if (File.Exists(System.IO.Path.Combine(d, ManifestName))) res.Add(d);
            return res;
        }

        // Sorteringsnyckel för mappnamnet: "20261111-120000-2" ska komma före "20261111-120000-10"
        static string DirKey(string dir)
        {
            string n = System.IO.Path.GetFileName(dir.TrimEnd('\\', '/'));
            int num;
            if (n.Length > 16 && n[15] == '-' && int.TryParse(n.Substring(16), NumberStyles.None, CultureInfo.InvariantCulture, out num))
                return n.Substring(0, 15) + "-" + num.ToString("0000000000", CultureInfo.InvariantCulture);
            return n.Length == 15 ? n + "-0000000001" : n;
        }

        // Skriv via temporär fil så att en krasch aldrig lämnar en halvskriven fil (som WriteSafely i MainForm.Save.cs)
        static void WriteFile(string path, byte[] bytes)
        {
            string tmp = path + TmpSuffix;
            try
            {
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        static void PutBack(string kept, string path)
        {
            string tmp = path + TmpSuffix;
            try
            {
                File.Copy(kept, tmp, true);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        static string Friendly(Exception ex)
        {
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException) return "Filen eller mappen hittades inte.";
            if (ex is UnauthorizedAccessException) return "Text Sync har inte behörighet till filen eller mappen.";
            if (ex is IOException) return "Filen kunde inte läsas eller skrivas. Den kan vara öppen i ett annat program, eller så är disken full.";
            return "Filen kunde inte återställas: " + ex.Message;
        }

        // ------------------------------------------------------------ manifestet
        // En textfil i UTF-8 med en rad per fält: "nyckel=värde". Bakstreck och radbrytningar i värdet skrivs som \\, \r och \n,
        // så att sökvägar med å/ä/ö, blanksteg, likhetstecken och andra tecken går att läsa tillbaka exakt.
        // Kopians sökväg sparas relativt körningens mapp, och godtas bara om den ligger kvar i den mappen.
        //   TextSync-undo 1
        //   title=...
        //   when=2026-11-11T12:00:00.0000000+01:00
        //   entry
        //   path=C:\...\Show.S04E01.sv.srt
        //   kept=0001\Show.S04E01.sv.srt        (saknas om filen inte fanns före)
        //   hash=<64 hextecken>
        //   label=S04E01
        //   restored=0
        //   end
        static void Save(UndoRun run)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            Field(sb, "title", run.Title);
            Field(sb, "when", run.When.ToString("o", CultureInfo.InvariantCulture));
            string prefix = run.Dir.TrimEnd('\\', '/') + "\\";
            foreach (var e in run.Entries)
            {
                sb.Append("entry\r\n");
                Field(sb, "path", e.Path);
                if (e.Kept != null)
                {
                    if (!e.Kept.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Kopian ligger utanför körningens mapp: " + e.Kept);
                    Field(sb, "kept", e.Kept.Substring(prefix.Length));
                }
                Field(sb, "hash", e.Hash);
                if (e.Label != null) Field(sb, "label", e.Label);
                Field(sb, "restored", e.Restored ? "1" : "0");
                sb.Append("end\r\n");
            }
            string file = System.IO.Path.Combine(run.Dir, ManifestName), tmp = file + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
        }

        static void Field(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append('=').Append((value ?? "").Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n")).Append("\r\n");
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                sb.Append(c == 'r' ? '\r' : c == 'n' ? '\n' : c);
            }
            return sb.ToString();
        }

        // Läser en körning från disken. Kastar InvalidDataException om manifestet är trasigt.
        static UndoRun Load(string dir)
        {
            dir = System.IO.Path.GetFullPath(dir);
            string[] lines = File.ReadAllLines(System.IO.Path.Combine(dir, ManifestName), Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != Header) throw new InvalidDataException("Manifestet har okänt format.");
            var run = new UndoRun { Dir = dir, Title = System.IO.Path.GetFileName(dir) };
            bool gotWhen = false;
            UndoEntry cur = null;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == "entry")
                {
                    if (cur != null) throw new InvalidDataException("Manifestet är trasigt (rad " + (i + 1) + ").");
                    cur = new UndoEntry();
                    continue;
                }
                if (line == "end")
                {
                    if (cur == null || string.IsNullOrEmpty(cur.Path) || !System.IO.Path.IsPathRooted(cur.Path) || !IsHash(cur.Hash))
                        throw new InvalidDataException("Manifestet är trasigt (rad " + (i + 1) + ").");
                    run.Entries.Add(cur);
                    cur = null;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq), val = Unescape(line.Substring(eq + 1));
                if (cur == null)
                {
                    if (key == "title") { if (val.Length > 0) run.Title = val; }
                    else if (key == "when")
                    {
                        if (!DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out run.When))
                            throw new InvalidDataException("Manifestet har en oläslig tid.");
                        gotWhen = true;
                    }
                }
                else if (key == "path") cur.Path = val;
                else if (key == "kept") cur.Kept = KeptPath(dir, val);
                else if (key == "hash") cur.Hash = val;
                else if (key == "label") cur.Label = val;
                else if (key == "restored") cur.Restored = val == "1";
            }
            if (cur != null || !gotWhen) throw new InvalidDataException("Manifestet är avhugget.");
            return run;
        }

        static bool IsHash(string h)
        {
            if (IsStamp(h)) return true;
            if (h == null || h.Length != 64) return false;
            foreach (char c in h) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        // Kopian måste ligga i körningens egen mapp: ingen rotad sökväg och inget ".."
        static string KeptPath(string dir, string rel)
        {
            if (rel.Length == 0 || System.IO.Path.IsPathRooted(rel) || rel.IndexOf(':') >= 0) throw new InvalidDataException("Manifestet pekar på en kopia utanför körningens mapp.");
            foreach (var part in rel.Split('\\', '/'))
                if (part == ".." || part == "." || part.Length == 0) throw new InvalidDataException("Manifestet pekar på en kopia utanför körningens mapp.");
            return System.IO.Path.Combine(dir, rel);
        }
    }
}
