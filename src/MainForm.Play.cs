// Text Sync - användargränssnitt: spela upp filmen runt en rad (▶ i listan), med den synkade texten ovanpå
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TextSync
{
    public partial class MainForm
    {
        public const double PlayMarginMs = 5000;   // så mycket spelas före och efter raden
        Process playProc;          // ffmpeg (klipper ut) eller ffplay (spelar) som körs just nu
        int playGen;               // ökas vid varje ny uppspelning och vid stopp, så att en äldre uppspelning kan se att den är ersatt
        string playMovie;          // film som användaren har valt för uppspelning när referensen är en SRT
        bool playStarting;
        int lastPlayRow = -1; int lastPlayTick;

        // Raderna som ska visas ovanpå klippet: de synkade raderna i fönstret (med osparade ändringar),
        // med tider räknade från klippets början, eftersom klippet börjar på 0.
        public static List<Cue> PlayCues(List<Cue> synced, double startMs, double endMs)
        {
            return synced.Where(c => c.End > startMs && c.Start < endMs && c.Text.Trim().Length > 0).OrderBy(c => c.Start)
                .Select(c => new Cue(Math.Max(0, c.Start - startMs), Math.Min(endMs, c.End) - startMs, c.Text)).ToList();
        }

        // Steg 1: ffmpeg klipper ut sekunderna runt raden till en liten tillfällig film (960 px bred) med texten inbränd.
        // -ss före -i ger exakt start och tider från 0. Då slutar uppspelningen exakt där den ska, även i en mycket stor film,
        // och det fungerar för format där filmens tider inte börjar på 0 (t.ex. .ts).
        // srtName och clipName är filnamn utan mapp (ffmpeg startas i mappen där de ligger), så att sökvägen inte
        // behöver skrivas om för ffmpegs filter. srtName = null: ingen text ovanpå.
        public static string BuildClipArgs(string movie, string srtName, string clipName, double startMs, double durMs, int audioIndex)
        {
            var inv = CultureInfo.InvariantCulture;
            string vf = "scale=960:-2" + (srtName != null ? ",subtitles=" + srtName : "");
            return "-nostdin -hide_banner -loglevel error -y -ss " + (startMs / 1000).ToString("0.000", inv) + " -t " + (durMs / 1000).ToString("0.000", inv) +
                " -i " + Q(movie) + " -map 0:v:0 -map 0:a:" + Math.Max(0, audioIndex) + "? -sn -dn -vf " + Q(vf) +
                " -c:v libx264 -preset ultrafast -crf 22 -pix_fmt yuv420p -c:a aac -ac 2 -b:a 160k " + clipName;
        }

        // Steg 2: ffplay spelar klippet i ett eget fönster och stänger sig själv när det är slut
        public static string BuildPlayArgs(string clipName, string title)
        {
            return "-hide_banner -loglevel error -autoexit -alwaysontop -window_title " + Q(title) + " " + clipName;
        }
        // Filmen som hör till texten när referensen är en SRT: samma avsnittsnummer, annars den enda filmen i mappen,
        // annars en film vars namn börjar som textens. null = det går inte att avgöra.
        public static string PickMovie(IEnumerable<string> videoFiles, string refPath, string tgtPath)
        {
            var vids = videoFiles.ToList();
            var ep = EpisodeId.Parse(tgtPath) ?? EpisodeId.Parse(refPath);
            if (ep != null)
            {
                var same = vids.Where(v => ep.Same(EpisodeId.Parse(v))).ToList();
                if (same.Count == 1) return same[0];
                if (same.Count > 1) return null;
            }
            if (vids.Count == 1 && (ep == null || EpisodeId.Parse(vids[0]) == null)) return vids[0];
            foreach (string p in new[] { tgtPath, refPath })
            {
                if (string.IsNullOrEmpty(p)) continue;
                string n = Path.GetFileNameWithoutExtension(p);
                var m = vids.Where(v => n.StartsWith(Path.GetFileNameWithoutExtension(v), StringComparison.OrdinalIgnoreCase)).ToList();
                if (m.Count == 1) return m[0];
            }
            return null;
        }

        // Filmen att spela: referensfilmen, annars en film bredvid filerna, annars får användaren välja (en gång)
        string MovieForPlay()
        {
            if (mkvPath != null && (IsMkv(mkvPath) || Audio.IsVideo(mkvPath)) && !mkvPath.EndsWith(".mks", StringComparison.OrdinalIgnoreCase)) return mkvPath;
            if (playMovie != null && File.Exists(playMovie)) return playMovie;
            var vids = new List<string>();
            foreach (string p in new[] { tgtLoadedPath, refLoadedPath })
            {
                try
                {
                    string dir = string.IsNullOrEmpty(p) ? null : Path.GetDirectoryName(Path.GetFullPath(p));
                    if (dir == null || !Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir))
                        if (Audio.IsVideo(f) && !vids.Contains(f, StringComparer.OrdinalIgnoreCase)) vids.Add(f);
                }
                catch { }
            }
            playMovie = PickMovie(vids, refLoadedPath, tgtLoadedPath);
            if (playMovie != null) { Log.Info("Film för uppspelning hittad bredvid texten: " + playMovie); return playMovie; }
            using (var d = new OpenFileDialog { Title = "Välj filmen som texten hör till", Filter = "Film|*.mkv;*.mp4;*.m4v;*.mov;*.avi;*.ts;*.m2ts;*.wmv;*.webm;*.mpg|Alla filer (*.*)|*.*" })
            {
                try { if (!string.IsNullOrEmpty(tgtLoadedPath)) d.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(tgtLoadedPath)); } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return null;
                playMovie = d.FileName;
                Log.Info("Film för uppspelning vald: " + playMovie);
                return playMovie;
            }
        }

        // ffmpeg och ffplay följer med i samma paket som ljudsynken använder. Frågar först och laddar ner det om något saknas.
        async Task<bool> EnsurePlayer()
        {
            var need = new[] { Tools.Ffmpeg };
            var a = MessageBox.Show(this,
                "För att spela upp filmen behövs gratisverktyget ffmpeg med spelaren ffplay (" + Fmt(Tools.Ffmpeg.Size / 1048576.0, "0") + " MB), som inte finns i Text Sync ännu.\n\n" +
                "Det laddas ner en gång till\n" + Tools.Root + "\noch kontrolleras så att det inte har ändrats.\n\nVill du ladda ner det nu?",
                Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (a != DialogResult.Yes) return false;
            long free = Tools.FreeBytes(), want = Tools.SpaceNeeded(need);
            if (free >= 0 && free < want)
            {
                MessageBox.Show(this, "Det finns för lite ledigt utrymme på disken för verktyget. Det behöver cirka " + Fmt(want / 1048576.0, "0") + " MB under nedladdningen, men bara " +
                    Fmt(free / 1048576.0, "0") + " MB är ledigt. Frigör utrymme och försök igen.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            cts = new CancellationTokenSource();
            var token = cts.Token;
            SetBusy(true, "Laddar ner…");
            string final = "";
            try
            {
                long total = Tools.Ffmpeg.Size;
                await Task.Run(() => Tools.Download(Tools.Ffmpeg, (d, t) => Progress((int)(100 * d / Math.Max(1, total)),
                    "Laddar ner ffmpeg för uppspelning… " + Fmt(d / 1048576.0, "0") + " av " + Fmt(total / 1048576.0, "0") + " MB"), token));
                return true;
            }
            catch (OperationCanceledException) { final = "Avbrutet."; return false; }
            catch (Exception ex)
            {
                Log.Error("Kunde inte ladda ner ffmpeg för uppspelning", ex);
                MessageBox.Show(this, Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                cts = null; SetBusy(false, final);
                if (undoLink != null) undoLink.Visible = undo.Count > 0;   // SetBusy dolde länken
            }
        }

        // Stoppar det som spelas eller förbereds. Returnerar true om något faktiskt stoppades.
        bool StopPlay()
        {
            playGen++;
            Process p;
            lock (playLock) { p = playProc; playProc = null; }
            if (p == null) return false;
            try { if (!p.HasExited) p.Kill(); } catch { }
            return true;
        }
        readonly object playLock = new object();

        // Kör ett verktyg till slut och returnerar (felkod, sista raderna på stderr). Avbryts av StopPlay.
        Tuple<int, string> RunPlayTool(string exe, string args, string dir, int gen)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            var tail = new Queue<string>();
            using (var p = new Process { StartInfo = psi })
            {
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 15) tail.Dequeue(); } };
                lock (playLock)
                {
                    if (gen != playGen) return Tuple.Create(-2, "");   // stoppad innan den hann starta
                    p.Start();
                    playProc = p;
                }
                p.BeginErrorReadLine();
                p.WaitForExit();
                p.WaitForExit();   // töm stderr
                lock (playLock) { if (playProc == p) playProc = null; }
                string err; lock (tail) err = string.Join(" | ", tail);
                return Tuple.Create(p.ExitCode, err);
            }
        }

        static void DeleteQuiet(params string[] files)
        {
            foreach (string f in files) { try { if (f != null && File.Exists(f)) File.Delete(f); } catch { } }
        }

        // Spelar filmen från 5 s före raden till 5 s efter, med de synkade raderna ovanpå
        async void PlayRow(int row)
        {
            if (busy || playStarting || synced == null || rowCue == null || row < 0 || row >= rowCue.Length) return;
            // andra klicket i ett dubbelklick ska inte starta om uppspelningen
            int now = Environment.TickCount;
            if (row == lastPlayRow && unchecked(now - lastPlayTick) < SystemInformation.DoubleClickTime) return;
            lastPlayRow = row; lastPlayTick = now;
            int ci = rowCue[row];
            Cue c = ci >= 0 && ci < synced.Count ? synced[ci] : rowRef[row];
            if (c == null) return;
            string srtPath = null, clipPath = null;
            try
            {
                string movie, ffmpeg, ffplay;
                playStarting = true;
                try
                {
                    movie = MovieForPlay();
                    if (movie == null) return;
                    if (!File.Exists(movie)) { SetStatus("Filmen finns inte längre: " + Path.GetFileName(movie), false); playMovie = null; return; }
                    ffmpeg = Tools.FindFfmpeg(); ffplay = Tools.FindFfplay();
                    if (ffmpeg == null || ffplay == null)
                    {
                        if (!await EnsurePlayer()) return;
                        ffmpeg = Tools.FindFfmpeg(); ffplay = Tools.FindFfplay();
                        if (ffmpeg == null || ffplay == null) { SetStatus("Verktyget för uppspelning (ffplay) kunde inte hittas efter nedladdningen.", false); return; }
                        if (synced == null) return;   // något annat lästes in under nedladdningen
                    }
                }
                finally { playStarting = false; }

                StopPlay();
                int gen = playGen;
                double start = Math.Max(0, c.Start - PlayMarginMs), end = c.End + PlayMarginMs;
                string tmpDir = Path.GetTempPath();
                string id = "TextSync_play_" + Guid.NewGuid().ToString("N");   // rester rensas också vid nästa start (Program.CleanupTemp)
                string srtName = null, clipName = id + ".mkv";
                clipPath = Path.Combine(tmpDir, clipName);
                var cues = PlayCues(synced, start, end);
                if (cues.Count > 0) { srtName = id + ".srt"; srtPath = Path.Combine(tmpDir, srtName); Srt.Save(srtPath, cues); }
                string span = Srt.FormatTime(start).Substring(0, 8) + "–" + Srt.FormatTime(end).Substring(0, 8);
                SetStatus("▶ Förbereder " + span + "…", true);
                string clipArgs = BuildClipArgs(movie, srtName, clipName, start, end - start, refAudioIndex);
                Log.Info("Uppspelning: ffmpeg " + clipArgs);
                var sw = Stopwatch.StartNew();
                var r = await Task.Run(() => RunPlayTool(ffmpeg, clipArgs, tmpDir, gen));
                if (gen != playGen || IsDisposed) return;   // stoppad eller ersatt av en ny uppspelning
                if (r.Item1 != 0 || !File.Exists(clipPath))
                {
                    Log.Info("ffmpeg kunde inte klippa ut stället, kod " + r.Item1 + ": " + r.Item2);
                    SetStatus("Det gick inte att spela upp stället i filmen. Filmen kan sakna bild eller ha ett format som inte stöds. Detaljer finns i loggfilen (Hjälp → Öppna mappen med loggfilen).", false);
                    return;
                }
                Log.Info("Klippet klart på " + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                SetStatus("▶ Spelar " + span + " i ett eget fönster. Esc stänger det.", true);
                string playArgs = BuildPlayArgs(clipName, "Text Sync – " + Srt.FormatTime(c.Start).Substring(0, 8) + " (Esc stänger)");
                r = await Task.Run(() => RunPlayTool(ffplay, playArgs, tmpDir, gen));
                if (gen != playGen || IsDisposed) return;
                if (r.Item1 != 0)
                {
                    Log.Info("ffplay misslyckades, kod " + r.Item1 + ": " + r.Item2);
                    SetStatus("Det gick inte att spela upp filmen. Detaljer finns i loggfilen (Hjälp → Öppna mappen med loggfilen).", false);
                }
                else if (statusLabel.Text.StartsWith("▶")) SetStatus("", true);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Error("Verktyget för uppspelning kunde inte startas", ex);
                MessageBox.Show(this, "Windows stoppade verktyget för uppspelning (" + ex.Message.TrimEnd('.') + ").\n\n" +
                    "Det beror oftast på Smart App Control i Windows-säkerhet, som blockerar program som inte är signerade. " +
                    "Uppspelningen går då inte att använda på den här datorn. Öppna filmen i din vanliga spelare och jämför med tiderna i listan i stället.",
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (statusLabel.Text.StartsWith("▶")) SetStatus("", true);
            }
            catch (Exception ex)
            {
                Log.Error("Uppspelningen misslyckades", ex);
                if (!IsDisposed) SetStatus("Det gick inte att spela upp filmen: " + Friendly(ex), false);
            }
            finally { DeleteQuiet(srtPath, clipPath); }
        }

        void PlaySelected()
        {
            if (list.SelectedIndices.Count > 0) PlayRow(list.SelectedIndices[0]);
        }
    }
}