// Text Sync - referens från filmens ljud: A = taldetektering, B = taligenkänning (Whisper), A+B = kombinerat
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TextSync
{
    // Fel med ett färdigt, begripligt meddelande på svenska som visas som det är
    public class UserMessageException : Exception
    {
        public UserMessageException(string msg) : base(msg) { }
        public UserMessageException(string msg, Exception inner) : base(msg, inner) { }
    }

    public enum AudioMode { Speech = 0, Whisper = 1, Combined = 2 }

    // Ett val i referenslistan: "använd ljudspår N med metod X"
    public class AudioRefOption
    {
        public AudioMode Mode;
        public int AudioIndex;      // n:te ljudspåret (0-baserat), som ffmpeg -map 0:a:n
        public string AudioDesc = "";
        public override string ToString()
        {
            string s;
            if (Mode == AudioMode.Speech) s = "Ljud A – taldetektering (snabb)";
            else if (Mode == AudioMode.Whisper) s = "Ljud B – taligenkänning med Whisper";
            else s = "Ljud A+B – kombinerat (säkrast)";
            return s + (AudioDesc.Length > 0 ? "   [" + AudioDesc + "]" : "");
        }
    }

    public class AudioRefResult
    {
        public List<Cue> Cues;          // referensrepliker (tal-segment och/eller igenkänd text)
        public List<Cue> Transcript;    // Whisper-text (B/A+B), annars null
        public string Language;         // språk som Whisper hörde, t.ex. "en"
        public string Info = "";
    }

    public static class Audio
    {
        public const int Rate = 16000;
        public static int WhisperDevice = -1;  // CUDA-enhet (NVIDIA), -1 = processorn. Sätts av gränssnittet när grafikkorten har hittats.
        public static bool UsedGpu;            // om senaste Whisper-körningen gick på grafikkortet
        static readonly string CacheDir = Path.Combine(Log.Dir, "cache");

        // ---------------------------------------------------------- hjälp för externa program
        static int Run(string exe, string args, Action<string> onStderr, CancellationToken ct, out string stderrTail)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
            };
            var tail = new Queue<string>();
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler h = (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 30) tail.Dequeue(); }
                    // aldrig kasta här: händelsen körs i en egen tråd och ett undantag skulle stänga programmet
                    if (onStderr != null) { try { onStderr(e.Data); } catch { } }
                };
                p.ErrorDataReceived += h; p.OutputDataReceived += h;
                try { p.Start(); }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new UserMessageException("Windows stoppade verktyget " + Path.GetFileName(exe) + " (" + ex.Message.TrimEnd('.') + ").\n\n" +
                        "Det beror oftast på Smart App Control i Windows-säkerhet, som blockerar program som inte är signerade. " +
                        "Ljudsynk går då inte att använda på den här datorn – synka mot en undertext (textspår eller SRT) i stället.");
                }
                p.BeginErrorReadLine(); p.BeginOutputReadLine();
                using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                    p.WaitForExit();
                p.WaitForExit(); // töm utdata
                lock (tail) stderrTail = string.Join("\n", tail);
                ct.ThrowIfCancellationRequested();
                return p.ExitCode;
            }
        }

        static string Q(string s) { return "\"" + s + "\""; }

        // Antal kanaler och längd för ljudspår n (ffprobe)
        public static void Probe(string file, int audioIndex, out int channels, out double durationMs, out string lang, CancellationToken ct)
        {
            channels = 2; durationMs = 0; lang = "";
            string probe = Tools.FindFfprobe();
            if (probe == null) return;
            var lines = new List<string>();
            string tail;
            Run(probe, "-v error -select_streams a:" + audioIndex + " -show_entries stream=channels:stream_tags=language:format=duration -of default=nw=1 " + Q(file),
                l => { lock (lines) lines.Add(l); }, ct, out tail);
            foreach (var l in lines)
            {
                if (l.StartsWith("channels=")) int.TryParse(l.Substring(9), out channels);
                else if (l.StartsWith("duration=")) { double d; if (double.TryParse(l.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) durationMs = d * 1000; }
                else if (l.StartsWith("TAG:language=")) lang = l.Substring(13);
            }
        }

        // ---------------------------------------------------------- ljud → 16 kHz mono WAV
        // Vid 5.1/7.1 används bara mittkanalen: där ligger dialogen, medan musik och effekter mest ligger i övriga kanaler.
        public static void ExtractWav(string file, int audioIndex, int channels, double durationMs, string wavOut, Action<int> progress, CancellationToken ct)
        {
            string ff = Tools.FindFfmpeg();
            if (ff == null) throw new UserMessageException("Verktyget ffmpeg saknas. Försök synka igen – Text Sync erbjuder då att ladda ner det.");
            string mix = channels >= 3 ? "-af \"pan=mono|c0=FC\"" : "-ac 1";
            string args = "-nostdin -hide_banner -y -i " + Q(file) + " -map 0:a:" + audioIndex + " -vn -sn -dn " + mix + " -ar " + Rate + " -c:a pcm_s16le " + Q(wavOut);
            var rx = new Regex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)");
            string tail;
            int code = Run(ff, args, l =>
            {
                var m = rx.Match(l);
                if (m.Success && durationMs > 0)
                {
                    double t = (int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)) * 1000;
                    progress((int)Math.Min(100, 100 * t / durationMs));
                }
            }, ct, out tail);
            if (code != 0 && channels >= 3)
            {
                // ovanlig kanallayout utan mittkanal → blanda ner allt
                code = Run(ff, args.Replace(mix, "-ac 1"), null, ct, out tail);
            }
            if (code != 0 || !File.Exists(wavOut))
            {
                // ffmpegs (engelska) felutskrift hamnar i loggen, inte i meddelandet
                Log.Info("ffmpeg misslyckades, kod " + code + ": " + Last(tail, 1000));
                throw new UserMessageException("Det gick inte att läsa ljudet ur filmen. Filen kan sakna ljud, vara skadad eller ha ett ljudformat som inte stöds.\n\nDetaljer finns i loggfilen (Hjälp → Öppna mappen med loggfilen).");
            }
        }

        // ---------------------------------------------------------- ljudspår
        public class AudioStream
        {
            public string Language = "", Name = "";
            public bool Default, Commentary;
        }

        // Kommentarspår och syntolkning ska inte väljas automatiskt
        public static bool LooksLikeCommentary(string name)
        {
            return Regex.IsMatch(name ?? "", @"comment|kommentar|director|descri|syntolk|\bAD\b", RegexOptions.IgnoreCase);
        }

        // Förvalt ljudspår: engelska först (originalspråket för de flesta filmer), annars standardspåret. Aldrig kommentar eller syntolkning om det finns annat.
        public static int PickAudio(List<AudioStream> a)
        {
            int i = a.FindIndex(s => !s.Commentary && s.Language.StartsWith("en"));
            if (i < 0) i = a.FindIndex(s => !s.Commentary && s.Default);
            if (i < 0) i = a.FindIndex(s => !s.Commentary);
            return Math.Max(0, i);
        }

        // Ljudspår i andra filmformat än MKV (MP4 m.fl.) via ffprobe. Tom lista om ffprobe saknas eller inte svarar inom 10 s.
        public static List<AudioStream> ListAudioStreams(string file)
        {
            var res = new List<AudioStream>();
            string probe = Tools.FindFfprobe();
            if (probe == null) return res;
            var lines = new List<string>();
            try
            {
                using (var cts = new CancellationTokenSource(10000))
                {
                    string tail;
                    Run(probe, "-v error -select_streams a -show_entries stream=index:stream_disposition=default,comment,visual_impaired:stream_tags=language,title -of default=nw=1 " + Q(file),
                        l => { lock (lines) lines.Add(l); }, cts.Token, out tail);
                }
            }
            catch (Exception ex) { Log.Info("ffprobe kunde inte lista ljudspåren: " + ex.Message); return res; }
            AudioStream cur = null;
            foreach (var l in lines)
            {
                if (l.StartsWith("index=")) { cur = new AudioStream(); res.Add(cur); }
                else if (cur == null) continue;
                else if (l == "DISPOSITION:default=1") cur.Default = true;
                else if (l == "DISPOSITION:comment=1" || l == "DISPOSITION:visual_impaired=1") cur.Commentary = true;
                else if (l.StartsWith("TAG:language=")) cur.Language = l.Substring(13);
                else if (l.StartsWith("TAG:title=")) cur.Name = l.Substring(10);
            }
            foreach (var s in res) if (LooksLikeCommentary(s.Name)) s.Commentary = true;
            return res;
        }

        static string Last(string s, int n) { s = (s ?? "").Trim(); return s.Length <= n ? s : "…" + s.Substring(s.Length - n); }

        static short[] ReadWav(string path)
        {
            var b = File.ReadAllBytes(path);
            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                if (id == "data")
                {
                    int start = pos + 8;
                    if (size <= 0 || start + size > b.Length) size = b.Length - start;
                    var s = new short[size / 2];
                    Buffer.BlockCopy(b, start, s, 0, s.Length * 2);
                    return s;
                }
                pos += 8 + size + (size & 1);
            }
            throw new InvalidDataException("Ogiltig WAV-fil från ffmpeg.");
        }

        // ---------------------------------------------------------- A: taldetektering
        // Energi i talets frekvensband (150–3800 Hz) jämfört med en rörlig brusnivå.
        // Gränsen är självjusterande: start 18 dB över bakgrunden (bäst mot facit i test),
        // skärps tills tal täcker högst ~50 % av tiden. Tal "överallt" ger annars fel förskjutning.
        public static List<Iv> DetectSpeech(string wavPath, CancellationToken ct)
        {
            double[] db, floor;
            Analyze(ReadWav(wavPath), out db, out floor, ct);
            double total = db.Length * 10.0;
            List<Iv> best = null;
            foreach (double rel in new[] { 18.0, 21, 24, 27, 30 })
            {
                best = Threshold(db, floor, rel, -40);
                double cov = best.Sum(x => x.E - x.S) / Math.Max(1, total);
                if (cov <= 0.50) break;
            }
            return best;
        }

        public static void AnalyzeFile(string wavPath, out double[] db, out double[] floor) { Analyze(ReadWav(wavPath), out db, out floor, CancellationToken.None); }

        static void Analyze(short[] x, out double[] dbOut, out double[] floorOut, CancellationToken ct)
        {
            const int Hop = 160;          // 10 ms
            int frames = x.Length / Hop;
            var db = new double[frames];
            // bandpass: andra ordningens högpass 150 Hz + lågpass 3800 Hz (biquad)
            double[] hp = Biquad(false, 150), lp = Biquad(true, 3800);
            double h1 = 0, h2 = 0, hy1 = 0, hy2 = 0, l1 = 0, l2 = 0, ly1 = 0, ly2 = 0;
            for (int f = 0; f < frames; f++)
            {
                if ((f & 4095) == 0) ct.ThrowIfCancellationRequested();   // var 41:a sekund ljud: Avbryt fungerar även här
                double sum = 0;
                for (int i = f * Hop; i < (f + 1) * Hop; i++)
                {
                    double v = x[i] / 32768.0;
                    double y = hp[0] * v + hp[1] * h1 + hp[2] * h2 - hp[3] * hy1 - hp[4] * hy2;
                    h2 = h1; h1 = v; hy2 = hy1; hy1 = y;
                    double z = lp[0] * y + lp[1] * l1 + lp[2] * l2 - lp[3] * ly1 - lp[4] * ly2;
                    l2 = l1; l1 = y; ly2 = ly1; ly1 = z;
                    sum += z * z;
                }
                db[f] = 10 * Math.Log10(sum / Hop + 1e-10);
            }
            // brusnivå: 10:e percentilen per sekund, sedan lägsta inom ±4 s
            int blk = 100, nb = (frames + blk - 1) / blk;
            var bp = new double[nb];
            for (int b = 0; b < nb; b++)
            {
                int len = Math.Min(blk, frames - b * blk);
                var seg = new double[len];
                Array.Copy(db, b * blk, seg, 0, len);   // (Skip/Take vore kvadratiskt långsamt på långa filmer)
                Array.Sort(seg);
                bp[b] = seg[seg.Length / 10];
            }
            var floor = new double[nb];
            for (int b = 0; b < nb; b++)
            {
                double m = double.MaxValue;
                for (int k = Math.Max(0, b - 4); k <= Math.Min(nb - 1, b + 4); k++) m = Math.Min(m, bp[k]);
                floor[b] = m;
            }
            dbOut = db; floorOut = new double[frames];
            for (int f = 0; f < frames; f++) floorOut[f] = floor[f / blk];
        }

        public static List<Iv> Threshold(double[] db, double[] floor, double rel, double abs)
        {
            int frames = db.Length;
            var on = new bool[frames];
            for (int f = 0; f < frames; f++)
                on[f] = db[f] > abs && db[f] > floor[f] + rel;
            // jämna ut: fyll luckor < 250 ms, ta bort tal < 150 ms
            Fill(on, 25, true); Fill(on, 15, false);
            var res = new List<Iv>();
            for (int f = 0; f < frames; )
            {
                if (!on[f]) { f++; continue; }
                int g = f; while (g < frames && on[g]) g++;
                res.Add(new Iv(f * 10.0, g * 10.0));
                f = g;
            }
            return res;
        }

        // fyller korta luckor av !val med val (val=true: täpp små pauser; val=false: ta bort korta pip)
        static void Fill(bool[] a, int maxLen, bool val)
        {
            int n = a.Length;
            for (int i = 0; i < n; )
            {
                if (a[i] == val) { i++; continue; }
                int j = i; while (j < n && a[j] != val) j++;
                bool inside = i > 0 && j < n;
                if (inside && j - i < maxLen) for (int k = i; k < j; k++) a[k] = val;
                i = j;
            }
        }

        static double[] Biquad(bool lowpass, double fc)
        {
            double w = 2 * Math.PI * fc / Rate, c = Math.Cos(w), s = Math.Sin(w), q = Math.Sqrt(0.5), al = s / (2 * q);
            double b0, b1, b2, a0 = 1 + al, a1 = -2 * c, a2 = 1 - al;
            if (lowpass) { b0 = (1 - c) / 2; b1 = 1 - c; b2 = (1 - c) / 2; }
            else { b0 = (1 + c) / 2; b1 = -(1 + c); b2 = (1 + c) / 2; }
            return new[] { b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0 };
        }

        // ---------------------------------------------------------- B: Whisper
        public static List<Cue> RunWhisper(string wavPath, double durationMs, Action<int> progress, CancellationToken ct, out string language)
        {
            return RunWhisper(wavPath, durationMs, progress, ct, out language, false, null);
        }

        // whisper-cli läser sökvägar i Windows teckentabell (ANSI). Tecken utanför den (t.ex. kyrilliska i användarnamnet,
        // som finns i %TEMP% och %LOCALAPPDATA%) blir då "?". Sådana sökvägar ersätts med Windows korta namn (8.3).
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint size);

        public static bool IsAnsi(string s)
        {
            var enc = Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            return enc.GetString(enc.GetBytes(s)) == s;
        }

        // Sökväg som whisper-cli kan läsa. Filen behöver inte finnas, men mappen måste göra det.
        public static string AnsiSafePath(string path)
        {
            if (IsAnsi(path)) return path;
            string dir = Path.GetDirectoryName(path), name = Path.GetFileName(path);
            if (!IsAnsi(name) && File.Exists(path)) { dir = path; name = ""; }
            var sb = new StringBuilder(1024);
            uint n = GetShortPathName(dir, sb, (uint)sb.Capacity);
            string res = n > 0 && n < sb.Capacity ? (name.Length > 0 ? Path.Combine(sb.ToString(), name) : sb.ToString()) : path;
            if (!IsAnsi(res)) Log.Info("Sökvägen kan inte skrivas om till tecken som Whisper kan läsa (korta namn är avstängda på disken?): " + path);
            return res;
        }

        // DTW ger exaktare tid per ord; kräver att modellens namn anges
        static string DtwArg(string model)
        {
            string m = Path.GetFileName(model);
            if (m.Contains("large-v3-turbo")) return " -dtw large.v3.turbo";
            if (m.Contains("small")) return " -dtw small";
            if (m.Contains("base")) return " -dtw base";
            return "";
        }

        // words=true: ett ord per rad med egen tid (-ml 1 -sow), används för att kontrollera ett textspår ord för ord
        // onCpuRetry: anropas när grafikkortet inte fungerade och Whisper börjar om från början på processorn
        public static List<Cue> RunWhisper(string wavPath, double durationMs, Action<int> progress, CancellationToken ct, out string language, bool words, Action onCpuRetry)
        {
            language = "";
            var model = Tools.FindModel(Tools.ModelFor(WhisperDevice >= 0)) ?? Tools.FindModel(Tools.ModelTurbo) ?? Tools.FindModel(Tools.ModelSmall);
            if (model == null) throw new UserMessageException("Språkmodellen för Whisper saknas. Försök synka igen – Text Sync erbjuder då att ladda ner den.");
            string outBase = Path.Combine(Path.GetTempPath(), "TextSync_w_" + Guid.NewGuid().ToString("N"));
            string lang = "";
            var rxLang = new Regex(@"auto-detected language:\s*([a-z]{2,3})");
            var rxProg = new Regex(@"progress\s*=\s*(\d+)%");
            int threads = Math.Max(1, Math.Min(16, Environment.ProcessorCount - 2));
            // Silero-taldetektering i Whisper: färre grova tidsfel och snabbare (testat mot facit). -dev: valt grafikkort
            string vad = Tools.FindModel(Tools.SileroVad);
            string args = "-m " + Q(AnsiSafePath(model)) + " -f " + Q(AnsiSafePath(wavPath)) + " -l auto" + (words ? " -ml 1 -sow" + DtwArg(model) : "") +
                (vad != null ? " --vad -vm " + Q(AnsiSafePath(vad)) : "") + " -osrt -of " + Q(AnsiSafePath(outBase)) + " -pp -t " + threads;
            bool gpuFailed = false;
            Action<string> onLine = l =>
            {
                var m = rxLang.Match(l); if (m.Success) lang = m.Groups[1].Value;
                var p = rxProg.Match(l); if (p.Success) progress(int.Parse(p.Groups[1].Value));
            };
            string tail = "";
            int code = -1;
            bool done = false;
            // valt grafikkort först (snabbt); fungerar det inte används processorn
            var tries = new List<bool>();
            if (WhisperDevice >= 0) tries.Add(true);
            tries.Add(false);
            foreach (bool gpu in tries)
            {
                string exe = Tools.FindWhisper(gpu) ?? (gpu ? null : Tools.FindWhisper(true));
                if (exe == null) continue;
                string a = args + (gpu ? " -dev " + WhisperDevice : " -ng");
                Log.Info("Whisper på " + (gpu ? "grafikkort " + WhisperDevice : "processorn"));
                if (!gpu && gpuFailed && onCpuRetry != null) { onCpuRetry(); progress(0); }
                try { code = Run(exe, a, onLine, ct, out tail); }
                catch (UserMessageException ex)
                {
                    if (!gpu) throw;   // grafikkortsversionen blockerad: försök med processorn
                    Log.Info("Whisper (GPU) kunde inte startas: " + ex.Message);
                    gpuFailed = true;
                    Tools.SetSetting("gpuwhisperfailed", "1");
                    continue;
                }
                if (code == 0 && File.Exists(outBase + ".srt")) { done = true; UsedGpu = gpu; break; }
                Log.Info("Whisper (" + (gpu ? "GPU" : "CPU") + ") misslyckades, kod " + code + ": " + Last(tail, 1000));
                if (gpu) { gpuFailed = true; Tools.SetSetting("gpuwhisperfailed", "1"); }
            }
            try
            {
                if (!done) throw new UserMessageException("Taligenkänningen (Whisper) misslyckades." +
                    (Tools.FindWhisper(false) == null ? " Välj processorn i listan Grafikkort och synka igen. Text Sync erbjuder då att ladda ner Whisper för processorn (21 MB)." : "") +
                    " Du kan också välja \"Ljud A – taldetektering\", som inte behöver Whisper.\n\nDetaljer finns i loggfilen (Hjälp → Öppna mappen med loggfilen).");
                language = lang;
                return Srt.Load(outBase + ".srt", 65001).Where(c => c.Text.Trim().Length > 0).ToList();
            }
            finally { try { File.Delete(outBase + ".srt"); } catch { } }
        }

        // ---------------------------------------------------------- A+B
        // A står för tiderna (skarpast i test), B rensar: talavsnitt där Whisper inte hörde några ord
        // (musik, effekter) tas bort. A:s avsnitt får Whispers text så att förhandsvisningen visar vad som sägs.
        // Blir för mycket bortrensat (t.ex. språk som Whisper inte klarar) används A oförändrad.
        public static List<Cue> Combine(List<Iv> speech, List<Cue> whisper)
        {
            var w = whisper.OrderBy(c => c.Start).ToList();
            var res = new List<Cue>();
            int k = 0;
            double keptMs = 0, allMs = 0;
            foreach (var s in speech)
            {
                allMs += s.E - s.S;
                while (k < w.Count && w[k].End + 500 < s.S) k++;
                Cue best = null; double bestOv = 0;
                for (int j = k; j < w.Count && w[j].Start - 500 < s.E; j++)
                {
                    double ov = Math.Min(s.E, w[j].End + 500) - Math.Max(s.S, w[j].Start - 500);
                    if (ov > bestOv) { bestOv = ov; best = w[j]; }
                }
                if (best != null) { res.Add(new Cue(s.S, s.E, best.Text)); keptMs += s.E - s.S; }
            }
            if (allMs > 0 && keptMs < 0.5 * allMs)
                return speech.Select(s => new Cue(s.S, s.E, "(tal)")).ToList();
            return res;
        }
        // Visas när Whisper börjar om på processorn, så att det syns varför förloppet gick tillbaka
        const string CpuRetryText = "Grafikkortet fungerade inte – Whisper börjar om på processorn (tar längre tid)… ";

        // ---------------------------------------------------------- hela kedjan, med cache
        static string CacheKey(string file, int audioIndex)
        {
            var fi = new FileInfo(file);
            string raw = fi.FullName.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + audioIndex + "|v3";
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-", "").Substring(0, 20);
        }

        // Finns Whisper-resultatet redan i cachen? (då går det snabbt även utan grafikkort)
        public static bool HasWhisperCache(string file, int audioIndex, bool words)
        {
            try { return File.Exists(Path.Combine(CacheDir, CacheKey(file, audioIndex) + (words ? ".words.srt" : ".whisper.srt"))); }
            catch { return false; }
        }

        public static AudioRefResult GetReference(string file, AudioRefOption opt, Action<int, string> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(CacheDir);
            string key = CacheKey(file, opt.AudioIndex);
            string vadFile = Path.Combine(CacheDir, key + ".tal.txt");
            string wFile = Path.Combine(CacheDir, key + ".whisper.srt");
            string langFile = Path.Combine(CacheDir, key + ".lang.txt");
            bool needSpeech = opt.Mode != AudioMode.Whisper, needWhisper = opt.Mode != AudioMode.Speech;
            bool haveSpeech = File.Exists(vadFile), haveWhisper = File.Exists(wFile);

            List<Iv> speech = haveSpeech ? LoadIv(vadFile) : null;
            List<Cue> transcript = haveWhisper ? Srt.Load(wFile, 65001) : null;
            string lang = File.Exists(langFile) ? File.ReadAllText(langFile).Trim() : "";

            if ((needSpeech && !haveSpeech) || (needWhisper && !haveWhisper))
            {
                int ch; double dur; string alang;
                progress(0, "Undersöker ljudspåret…");
                Probe(file, opt.AudioIndex, out ch, out dur, out alang, ct);
                string wav = Path.Combine(Path.GetTempPath(), "TextSync_" + key + ".wav");
                try
                {
                    var sw = Stopwatch.StartNew();
                    ExtractWav(file, opt.AudioIndex, ch, dur, wav, p => progress(p * (needWhisper && !haveWhisper ? 30 : 80) / 100, "Läser ljudet ur filmen… " + p + " %"), ct);
                    Log.Info("Ljud uppläst på " + sw.Elapsed.TotalSeconds.ToString("0") + " s (" + ch + " kanaler" + (ch >= 3 ? ", mittkanalen" : "") + ")");
                    if (needSpeech && !haveSpeech)
                    {
                        progress(needWhisper && !haveWhisper ? 30 : 80, "Letar efter tal i ljudet…");
                        speech = DetectSpeech(wav, ct);
                        SaveIv(vadFile, speech);
                    }
                    if (needWhisper && !haveWhisper)
                    {
                        sw.Restart();
                        string l;
                        string phase = "Taligenkänning (Whisper)… ";
                        transcript = RunWhisper(wav, dur, p => progress(30 + p * 65 / 100, phase + p + " %"), ct, out l, false, () => phase = CpuRetryText);
                        lang = l;
                        Srt.Save(wFile, transcript);
                        File.WriteAllText(langFile, lang);
                        Log.Info("Whisper klar på " + sw.Elapsed.TotalSeconds.ToString("0") + " s, " + transcript.Count + " rader, språk " + lang);
                    }
                }
                finally { try { File.Delete(wav); } catch { } }
            }

            var res = new AudioRefResult { Language = lang };
            if (opt.Mode == AudioMode.Speech)
            {
                res.Cues = speech.Select(s => new Cue(s.S, s.E, "(tal)")).ToList();
                res.Info = speech.Count + " talavsnitt hittades i ljudet";
            }
            else if (opt.Mode == AudioMode.Whisper)
            {
                res.Cues = transcript; res.Transcript = transcript;
                res.Info = transcript.Count + " repliker igenkända" + (lang.Length > 0 ? " (" + MkvTrack.LanguageName(lang) + ")" : "");
            }
            else
            {
                res.Cues = Combine(speech, transcript); res.Transcript = transcript;
                res.Info = "taldetektering " + speech.Count + " avsnitt, " + res.Cues.Count + " bekräftade av Whisper";
            }
            return res;
        }

        // Whisper i ordläge (för kontroll av textspår). Cachas per film och ljudspår.
        public static List<Cue> GetWords(string file, int audioIndex, Action<int, string> progress, CancellationToken ct, out string language)
        {
            Directory.CreateDirectory(CacheDir);
            string key = CacheKey(file, audioIndex);
            string wFile = Path.Combine(CacheDir, key + ".words.srt");
            string langFile = Path.Combine(CacheDir, key + ".words.lang.txt");
            if (File.Exists(wFile))
            {
                language = File.Exists(langFile) ? File.ReadAllText(langFile).Trim() : "";
                return Srt.Load(wFile, 65001);
            }
            int ch; double dur; string alang;
            progress(0, "Undersöker ljudspåret…");
            Probe(file, audioIndex, out ch, out dur, out alang, ct);
            string wav = Path.Combine(Path.GetTempPath(), "TextSync_" + key + "_w.wav");
            try
            {
                ExtractWav(file, audioIndex, ch, dur, wav, p => progress(p * 30 / 100, "Läser ljudet ur filmen… " + p + " %"), ct);
                var sw = Stopwatch.StartNew();
                string phase = "Whisper lyssnar ord för ord… ";
                var words = RunWhisper(wav, dur, p => progress(30 + p * 65 / 100, phase + p + " %"), ct, out language, true, () => phase = CpuRetryText);
                Srt.Save(wFile, words);
                File.WriteAllText(langFile, language);
                Log.Info("Whisper ordläge klar på " + sw.Elapsed.TotalSeconds.ToString("0") + " s, " + words.Count + " ord, språk " + language);
                return words;
            }
            finally { try { File.Delete(wav); } catch { } }
        }

        static void SaveIv(string path, List<Iv> iv)
        {
            var sb = new StringBuilder();
            foreach (var x in iv) sb.Append(((long)x.S).ToString(CultureInfo.InvariantCulture)).Append(' ').Append(((long)x.E).ToString(CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        static List<Iv> LoadIv(string path)
        {
            var res = new List<Iv>();
            foreach (var l in File.ReadAllLines(path))
            {
                var p = l.Split(' ');
                if (p.Length == 2) res.Add(new Iv(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture)));
            }
            return res;
        }

        public static bool IsVideo(string f)
        {
            string x = Path.GetExtension(f).ToLowerInvariant();
            return new[] { ".mkv", ".mka", ".mp4", ".m4v", ".mov", ".avi", ".ts", ".m2ts", ".mts", ".wmv", ".webm", ".mpg", ".mpeg", ".flv" }.Contains(x);
        }
    }
}
