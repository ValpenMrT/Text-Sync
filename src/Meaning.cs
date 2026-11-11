// Text Sync - jämför vad raderna betyder (t.ex. svenska mot engelska) med llama.cpp och en flerspråkig språkmodell.
// Synkmotorn jämför bara när det pratas. Här paras raderna ihop efter betydelse, och där flera par i rad visar
// att texten ligger fel flyttas raderna dit. Kör efter den vanliga synken, och bara mot en referens som är text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TextSync
{
    public class MeaningReport
    {
        public int Lines;        // rader i texten som är långa nog att jämföra
        public int Paired;       // rader som parades ihop med en referensrad efter betydelse
        public int Anchors;      // par som är säkra nog att styra tiderna
        public int Moved;        // rader som flyttades
        public int Places;       // antal ställen (rader i följd med samma flytt)
        public double MaxMs;     // största flytten
        public bool TooFew;      // för få säkra par: ingenting ändrades
        public string Error;     // jämförelsen kunde inte göras (texten visas som den är)
        public bool Canceled;    // användaren avbröt jämförelsen: synken är kvar som den var
        public bool Worse;       // flytten gjorde synken sämre och togs tillbaka
        public double Sigma, MatchMin, AnchorMin;   // till loggen, för att kunna justera gränserna mot riktiga filmer
        public string Levels = "";

        static string Sec(double ms) { return (ms / 1000).ToString("0.0", CultureInfo.GetCultureInfo("sv-SE")); }

        // En mening till Detaljer och till raden i Hel säsong
        public string Summary()
        {
            if (Canceled) return "Betydelsejämförelsen avbröts, så synken är gjord som vanligt.";
            if (Error != null) return "Betydelsejämförelsen kunde inte göras, så synken är gjord som vanligt. " + Error;
            string pairs = Paired + " av " + Lines + " rader parades ihop efter betydelse.";
            if (Worse) return "Betydelsejämförelsen ändrade ingenting, eftersom texten passade referensen sämre efter flytten. " + pairs;
            if (TooFew) return "Betydelsejämförelsen ändrade ingenting, eftersom för få rader gick att para ihop säkert. " + pairs;
            if (Moved == 0) return "Betydelsejämförelsen hittade inget att rätta. " + pairs;
            return "Betydelsejämförelsen flyttade " + (Moved == 1 ? "1 rad" : Moved + " rader") + " på " + (Places == 1 ? "1 ställe" : Places + " ställen") +
                " (som mest " + Sec(MaxMs) + " s). " + pairs;
        }

        // Kort text till rutan bredvid siffrorna i resultatet
        public string Chip()
        {
            if (Canceled) return "Betydelse: avbruten";
            if (Error != null) return "⚠ Betydelse: kunde inte jämföras";
            if (TooFew || Worse) return "Betydelse: inget ändrat";
            return Moved == 0 ? "Betydelse: inget att rätta" : "Betydelse: " + (Moved == 1 ? "1 rad flyttad" : Moved + " rader flyttade");
        }

        public string LogText()
        {
            var inv = CultureInfo.InvariantCulture;
            if (Canceled) return "avbruten";
            if (Error != null) return "fel: " + Error;
            return (Worse ? "togs tillbaka (sämre poäng), " : "") + "rader " + Lines + ", par " + Paired + ", säkra par " + Anchors + ", flyttade " + Moved + " på " + Places + " ställen, största " + MaxMs.ToString("0", inv) +
                " ms, sigma " + Sigma.ToString("0.000", inv) + ", gränser " + MatchMin.ToString("0.000", inv) + "/" + AnchorMin.ToString("0.000", inv) +
                (TooFew ? ", för få säkra par" : "") + (Levels.Length > 0 ? ", nivåer " + Levels : "");
        }
    }

    public static class Meaning
    {
        // Gränserna är satta mot påhittade data i testerna och mätta mot en film, Blacklist S04E01 (beslutsloggen 2026-11-11, 2.4.7):
        // den vanliga synken ändras inte och fel som varar i minuter rättas, men korta fel (under ungefär en minut) rättas inte.
        const double Tol = 400;        // ms: ett par stöder en nivå om det ligger så nära den
        const double JumpCost = 2.0;   // byte av nivå kostar som två par som inte stämmer: ett nytt avsnitt behöver minst tre par
        const double Deadband = 150;   // ms: mindre flyttar görs inte, synkmotorn är redan så exakt
        const double LevelStep = 50;   // ms
        const int MinLetters = 8;      // kortare rader ("Ja.", "Okej.") säger för lite om betydelsen
        const int MinSection = 3;      // säkra par som krävs för att ett avsnitt ska få flyttas
        const double MatchSigmas = 2.5, AnchorSigmas = 4;
        const double MaxShift = 10000; // ms: par som ligger längre från varandra styr inte tiderna (en scen som upprepas, eller fel avsnitt)
        const double WorseMargin = 0.01; // så mycket får överlappspoängen sjunka innan flytten tas tillbaka

        // Texten som jämförs: utan taggar, radbrytningar och replikstreck
        public static string Clean(string text)
        {
            string s = Regex.Replace(text ?? "", @"<[^>]*>|\{[^}]*\}", " ");
            s = Regex.Replace(s, @"(^|\n)\s*[-–]\s*", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 300 ? s.Substring(0, 300) : s;
        }

        public static bool Usable(string cleaned) { return cleaned != null && cleaned.Count(char.IsLetter) >= MinLetters; }

        static float Dot(float[] a, float[] b)
        {
            double s = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) s += a[i] * b[i];
            return (float)s;
        }

        public static void Normalize(float[] v)
        {
            double s = 0;
            for (int i = 0; i < v.Length; i++) s += (double)v[i] * v[i];
            if (s <= 0) return;
            float k = (float)(1 / Math.Sqrt(s));
            for (int i = 0; i < v.Length; i++) v[i] *= k;
        }

        // Hela steget: vektorer för båda texterna (ur cachen eller från llama-server), parning och rättning.
        // Kastar aldrig utom vid avbrott: går något fel står det i rapporten, och synken lämnas som den var.
        public static MeaningReport Apply(List<Cue> refc, SyncResult r, Action<int, string> progress, CancellationToken ct)
        {
            var rep = new MeaningReport();
            try
            {
                string exe = Tools.FindLlamaServer(), model = Tools.FindModel(Tools.ModelBgeM3);
                if (exe == null || model == null) throw new UserMessageException("Verktygen saknas. Synka igen, så erbjuder Text Sync att ladda ner dem.");
                var sw = Stopwatch.StartNew();
                var rt = refc.Select(c => Clean(c.Text)).ToList();
                var tt = r.Output.Select(c => Clean(c.Text)).ToList();
                float[][] rv, tv;
                using (var srv = new LlamaServer(exe, model))
                {
                    rv = Embedder.Get(rt, model, srv, p => progress(p / 2, "Jämför vad raderna betyder… " + p / 2 + " %"), s => progress(0, s), ct);
                    tv = Embedder.Get(tt, model, srv, p => progress(50 + p / 2, "Jämför vad raderna betyder… " + (50 + p / 2) + " %"), s => progress(50, s), ct);
                }
                ct.ThrowIfCancellationRequested();
                var outp = Refine(refc, r.Output, rv, tv, out rep);
                if (rep.Moved > 0)
                {
                    // gränserna är inte mätta mot riktiga filmer: passar texten referensen sämre efter flytten tas den tillbaka
                    double before = SyncEngine.Score(refc, r.Output), after = SyncEngine.Score(refc, outp);
                    if (after < before - WorseMargin) { rep.Worse = true; rep.Moved = 0; rep.Places = 0; }
                    else { r.Output = outp; r.ScoreAfter = after; }
                }
                Log.Info("Betydelsejämförelse: " + rep.LogText() + ", " + sw.ElapsedMilliseconds + " ms");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
                Log.Error("Betydelsejämförelsen misslyckades", ex);
                // användaren får en kort mening, detaljerna står i loggen
                rep = new MeaningReport { Error = ex is UserMessageException ? ex.Message : "Detaljer finns i loggfilen (Hjälp → Öppna mappen med loggfilen)." };
            }
            r.Meaning = rep;
            return rep;
        }

        class Anchor { public int Line, Ref; public double Delta; public int State; }

        // Avståndet från t till närmaste värde i den sorterade listan
        static double NearestDist(double[] sorted, double t)
        {
            int k = Array.BinarySearch(sorted, t);
            if (k >= 0) return 0;
            k = ~k;
            return Math.Min(k < sorted.Length ? sorted[k] - t : double.MaxValue, k > 0 ? t - sorted[k - 1] : double.MaxValue);
        }

        // Parar ihop raderna efter betydelse och rättar tiderna. refVec[j] hör till refc[j], tgtVec[i] till synced[i];
        // null = raden jämförs inte (för kort). Returnerar en ny lista i samma ordning som synced.
        //
        // 1) Likhet mellan varje rad och varje referensrad (cosinus), minus radens och referensradens medellikhet.
        //    Det tar bort rader som liknar allt ("Vad menar du?"). Gränserna sätts efter spridningen i just de här texterna.
        // 2) Dynamisk programmering: bästa sättet att para ihop raderna utan att byta ordning på dem.
        // 3) Varje säkert par säger hur långt raden ligger från sin referensrad. En Viterbi över paren delar in texten
        //    i avsnitt med var sin nivå. Enstaka par som avviker (t.ex. en rad som svenskan har slagit ihop) räknas inte.
        // 4) Avsnitt där nivån är minst Deadband flyttas dit. Övriga rader lämnas precis som synkmotorn lade dem.
        public static List<Cue> Refine(List<Cue> refc, List<Cue> synced, float[][] refVec, float[][] tgtVec, out MeaningReport rep)
        {
            rep = new MeaningReport();
            var outp = synced.Select(c => c.Clone()).ToList();
            int[] ti = Enumerable.Range(0, synced.Count).Where(i => tgtVec[i] != null && synced[i].End > 0).OrderBy(i => synced[i].Start).ToArray();
            int[] rj = Enumerable.Range(0, refc.Count).Where(j => refVec[j] != null).OrderBy(j => refc[j].Start).ToArray();
            int n = ti.Length, m = rj.Length;
            rep.Lines = n;
            if (n < 5 || m < 5) { rep.TooFew = true; return outp; }

            // 1) likhet
            var sim = new float[n][];
            Parallel.For(0, n, a =>
            {
                var row = new float[m]; var v = tgtVec[ti[a]];
                for (int b = 0; b < m; b++) row[b] = Dot(v, refVec[rj[b]]);
                sim[a] = row;
            });
            var rowMean = new double[n]; var colMean = new double[m];
            for (int a = 0; a < n; a++) for (int b = 0; b < m; b++) { rowMean[a] += sim[a][b]; colMean[b] += sim[a][b]; }
            for (int a = 0; a < n; a++) rowMean[a] /= m;
            for (int b = 0; b < m; b++) colMean[b] /= n;
            double sum = 0, sum2 = 0;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < m; b++)
                {
                    float s = (float)(sim[a][b] - 0.5 * (rowMean[a] + colMean[b]));
                    sim[a][b] = s; sum += s; sum2 += (double)s * s;
                }
            double mean = sum / ((double)n * m);
            double sigma = Math.Max(0.02, Math.Sqrt(Math.Max(0, sum2 / ((double)n * m) - mean * mean)));
            float matchMin = (float)(mean + MatchSigmas * sigma), anchorMin = (float)(mean + AnchorSigmas * sigma);
            rep.Sigma = sigma; rep.MatchMin = matchMin; rep.AnchorMin = anchorMin;

            // 2) bästa parningen i ordning: 1 = hoppa över textraden, 2 = hoppa över referensraden, 3 = par
            var D = new float[n + 1][]; var back = new byte[n + 1][];
            for (int a = 0; a <= n; a++) { D[a] = new float[m + 1]; back[a] = new byte[m + 1]; }
            for (int a = 1; a <= n; a++)
            {
                var row = sim[a - 1];
                for (int b = 1; b <= m; b++)
                {
                    float best = D[a - 1][b]; byte bk = 1;
                    if (D[a][b - 1] > best) { best = D[a][b - 1]; bk = 2; }
                    float g = row[b - 1] - matchMin;
                    if (g > 0 && D[a - 1][b - 1] + g > best) { best = D[a - 1][b - 1] + g; bk = 3; }
                    D[a][b] = best; back[a][b] = bk;
                }
            }
            var anchors = new List<Anchor>();
            for (int a = n, b = m; a > 0 && b > 0; )
            {
                if (back[a][b] == 3)
                {
                    rep.Paired++;
                    if (sim[a - 1][b - 1] >= anchorMin && Math.Abs(refc[rj[b - 1]].Start - synced[ti[a - 1]].Start) <= MaxShift)
                        anchors.Add(new Anchor { Line = ti[a - 1], Ref = rj[b - 1], Delta = refc[rj[b - 1]].Start - synced[ti[a - 1]].Start });
                    a--; b--;
                }
                else if (back[a][b] == 1) a--;
                else b--;
            }
            anchors.Reverse();
            rep.Anchors = anchors.Count;
            if (anchors.Count < Math.Max(8, 0.1 * n)) { rep.TooFew = true; return outp; }

            // 3) nivåer: Viterbi över paren. Tillstånd = möjliga nivåer (parens egna avvikelser, avrundade).
            double[] lv = anchors.Select(x => Math.Round(x.Delta / LevelStep) * LevelStep).Distinct().OrderBy(x => x).ToArray();
            int K = lv.Length, A = anchors.Count;
            var cost = new double[K]; var from = new int[A][];
            for (int s = 0; s < K; s++) cost[s] = Math.Min(1, Math.Abs(anchors[0].Delta - lv[s]) / Tol);
            for (int k = 1; k < A; k++)
            {
                int arg = 0;
                for (int s = 1; s < K; s++) if (cost[s] < cost[arg]) arg = s;
                double jump = cost[arg] + JumpCost;
                var nc = new double[K]; from[k] = new int[K];
                for (int s = 0; s < K; s++)
                {
                    if (cost[s] <= jump) { nc[s] = cost[s]; from[k][s] = s; } else { nc[s] = jump; from[k][s] = arg; }
                    nc[s] += Math.Min(1, Math.Abs(anchors[k].Delta - lv[s]) / Tol);
                }
                cost = nc;
            }
            int st = 0;
            for (int s = 1; s < K; s++) if (cost[s] < cost[st]) st = s;
            for (int k = A - 1; k >= 0; k--) { anchors[k].State = st; if (k > 0) st = from[k][st]; }

            // nivån för varje avsnitt (par i följd med samma tillstånd): medianen av de par som stöder den
            var level = new double[A];
            var lvText = new List<string>();
            for (int k = 0; k < A; )
            {
                int e = k;
                while (e < A && anchors[e].State == anchors[k].State) e++;
                var inl = new List<double>();
                for (int q = k; q < e; q++) if (Math.Abs(anchors[q].Delta - lv[anchors[k].State]) < Tol) inl.Add(anchors[q].Delta);
                inl.Sort();
                double L = inl.Count >= MinSection ? inl[inl.Count / 2] : 0;
                if (Math.Abs(L) < Deadband) L = 0;
                for (int q = k; q < e; q++) level[q] = L;
                if (lvText.Count < 12) lvText.Add(L.ToString("0", CultureInfo.InvariantCulture) + "×" + inl.Count);
                k = e;
            }
            rep.Levels = string.Join(" ", lvText);

            // 4) flytten för varje rad, i tidsordning
            double[] refStarts = refc.Select(c => c.Start).OrderBy(x => x).ToArray();
            int[] order = Enumerable.Range(0, synced.Count).Where(i => synced[i].End > 0).OrderBy(i => synced[i].Start).ToArray();
            var pos = new Dictionary<int, int>();
            for (int p = 0; p < order.Length; p++) pos[order[p]] = p;
            var corr = new double[order.Length];
            int first = pos[anchors[0].Line], last = pos[anchors[A - 1].Line];
            for (int p = 0; p <= first; p++) corr[p] = level[0];
            for (int p = last; p < order.Length; p++) corr[p] = level[A - 1];
            for (int k = 0; k + 1 < A; k++)
            {
                int pa = pos[anchors[k].Line], pb = pos[anchors[k + 1].Line];
                int brk = pb;
                if (level[k] != level[k + 1] && pb - pa > 1)
                {
                    // raderna mellan paren har ingen egen parning: gränsen läggs där deras starter hamnar närmast referensens starter
                    double bestCost = double.MaxValue;
                    for (int cand = pa + 1; cand <= pb; cand++)
                    {
                        double c0 = 0;
                        for (int p = pa + 1; p < pb; p++)
                            c0 += Math.Min(1000, NearestDist(refStarts, synced[order[p]].Start + (p < cand ? level[k] : level[k + 1])));
                        if (c0 < bestCost) { bestCost = c0; brk = cand; }
                    }
                }
                for (int p = pa; p < pb; p++) corr[p] = p < brk ? level[k] : level[k + 1];
                corr[pb] = level[k + 1];
            }

            // raderna får inte byta ordning, och inte överlappa raden före mer än de gjorde innan (samma regel som synkmotorns finjustering)
            double prev = double.MinValue, prevEnd = double.MinValue, prevOldEnd = double.MinValue, lastCorr = 0;
            for (int p = 0; p < order.Length; p++)
            {
                var c = outp[order[p]];
                double d = corr[p];
                if (c.Start + d < 0) d = -c.Start;
                if (c.Start + d < prev) d = prev - c.Start;
                if (p > 0)
                {
                    double minStart = prevEnd - Math.Max(0, prevOldEnd - c.Start);
                    if (c.Start + d < minStart) d = minStart - c.Start;
                }
                prevOldEnd = c.End;
                c.Start += d; c.End += d;
                prev = c.Start; prevEnd = c.End;
                if (d != 0)
                {
                    rep.Moved++;
                    if (Math.Abs(d) > rep.MaxMs) rep.MaxMs = Math.Abs(d);
                    if (Math.Abs(d - lastCorr) > 1) rep.Places++;
                }
                lastCorr = d;
            }
            return outp;
        }
    }

    // Vektorer ("inbäddningar") för textrader, sparade i cachen så att en ny synk av samma text går direkt
    public static class Embedder
    {
        static readonly string CacheDir = Path.Combine(Log.Dir, "cache");
        const int Batch = 16;

        // En vektor per rad, null för rader som är för korta att jämföra. progress: 0–100. status: text medan modellen startar.
        public static float[][] Get(List<string> texts, string model, LlamaServer srv, Action<int> progress, Action<string> status, CancellationToken ct)
        {
            var res = new float[texts.Count][];
            var idx = Enumerable.Range(0, texts.Count).Where(i => Meaning.Usable(texts[i])).ToList();
            if (idx.Count == 0) return res;
            string cache = null;
            try
            {
                var fi = new FileInfo(model);
                // höj "v1" om llama.cpp-versionen eller flaggorna till servern (t.ex. --pooling) ändras
                string raw = fi.Name + "|" + fi.Length + "|v1\n" + string.Join("\n", idx.Select(i => texts[i]));
                using (var sha = SHA1.Create())
                    cache = Path.Combine(CacheDir, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-", "").Substring(0, 20) + ".emb");
                var got = ReadCache(cache, idx.Count);
                if (got != null) { for (int k = 0; k < idx.Count; k++) res[idx[k]] = got[k]; progress(100); return res; }
            }
            catch (Exception ex) { Log.Info("Cachen för betydelsejämförelsen kunde inte läsas: " + ex.Message); }

            srv.Start(status, ct);
            var all = new List<float[]>();
            for (int k = 0; k < idx.Count; k += Batch)
            {
                ct.ThrowIfCancellationRequested();
                var part = idx.Skip(k).Take(Batch).Select(i => texts[i]).ToList();
                var v = srv.Embed(part, ct);
                foreach (var x in v) { Meaning.Normalize(x); all.Add(x); }
                progress(Math.Min(100, 100 * (k + part.Count) / idx.Count));
            }
            for (int k = 0; k < idx.Count; k++) res[idx[k]] = all[k];
            try { if (cache != null) WriteCache(cache, all); } catch (Exception ex) { Log.Info("Cachen för betydelsejämförelsen kunde inte sparas: " + ex.Message); }
            return res;
        }

        public static void WriteCache(string path, List<float[]> v)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".part";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(v.Count); w.Write(v[0].Length);
                var buf = new byte[v[0].Length * 4];
                foreach (var x in v) { Buffer.BlockCopy(x, 0, buf, 0, buf.Length); w.Write(buf); }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // null om filen saknas eller inte stämmer med antalet rader
        public static float[][] ReadCache(string path, int count)
        {
            if (!File.Exists(path)) return null;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                int n = r.ReadInt32(), dim = r.ReadInt32();
                if (n != count || dim <= 0 || dim > 16384 || r.BaseStream.Length != 8 + (long)n * dim * 4) return null;
                var res = new float[n][];
                for (int i = 0; i < n; i++)
                {
                    var b = r.ReadBytes(dim * 4);
                    res[i] = new float[dim];
                    Buffer.BlockCopy(b, 0, res[i], 0, b.Length);
                }
                return res;
            }
        }
    }

    // llama-server från llama.cpp, startad dold och bara nåbar från den här datorn (127.0.0.1).
    // Paketet för Windows innehåller inget eget program för inbäddningar, därför används servern.
    // Den startas först när något saknas i cachen, och stängs när jämförelsen är klar.
    public sealed class LlamaServer : IDisposable
    {
        readonly string exe, model;
        Process proc;
        int port;
        readonly Queue<string> tail = new Queue<string>();

        public LlamaServer(string exe, string model) { this.exe = exe; this.model = model; }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            try { return ((IPEndPoint)l.LocalEndpoint).Port; } finally { l.Stop(); }
        }

        string Tail() { lock (tail) return string.Join("\n", tail); }

        public void Start(Action<string> status, CancellationToken ct)
        {
            if (proc != null) return;
            if (status != null) status("Startar språkmodellen…");
            port = FreePort();
            int threads = Math.Max(1, Math.Min(16, Environment.ProcessorCount - 2));
            string args = "-m \"" + Audio.AnsiSafePath(model) + "\" --embedding --pooling cls --host 127.0.0.1 --port " + port + " --no-webui -t " + threads;
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe),
                RedirectStandardError = true, RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
            };
            var p = new Process { StartInfo = psi };
            DataReceivedEventHandler h = (s, e) =>
            {
                if (e.Data == null) return;
                lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 30) tail.Dequeue(); }
            };
            p.ErrorDataReceived += h; p.OutputDataReceived += h;
            try { p.Start(); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                p.Dispose();
                throw new UserMessageException("Windows stoppade verktyget llama-server.exe (" + ex.Message.TrimEnd('.') + "). " +
                    "Det beror oftast på Smart App Control i Windows-säkerhet, som blockerar program som inte är signerade. " +
                    "Kryssa ur \"Jämför också vad raderna betyder\" i Inställningar om det händer varje gång.");
            }
            proc = p;
            KillWithParent(p);
            p.BeginErrorReadLine(); p.BeginOutputReadLine();
            Log.Info("llama-server startad (port " + port + ", " + threads + " trådar)");
            var sw = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (p.HasExited) throw new InvalidOperationException("Språkmodellen kunde inte startas (llama-server avslutades med kod " + p.ExitCode + "). Slutet av utskriften:\n" + Tail());
                if (Healthy()) break;
                if (sw.Elapsed.TotalSeconds > 180) throw new UserMessageException("Språkmodellen startade inte inom tre minuter.");
                Thread.Sleep(300);
            }
            Log.Info("Språkmodellen laddad på " + sw.ElapsedMilliseconds + " ms");
        }

        bool Healthy()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/health");
                req.Proxy = null; req.Timeout = 2000;
                using (var resp = (HttpWebResponse)req.GetResponse()) return resp.StatusCode == HttpStatusCode.OK;
            }
            catch (WebException) { return false; }
        }

        // En vektor per text, i samma ordning som texterna
        public float[][] Embed(List<string> texts, CancellationToken ct)
        {
            byte[] body = Encoding.UTF8.GetBytes(BuildRequest(texts));
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/v1/embeddings");
            req.Proxy = null; req.Method = "POST"; req.ContentType = "application/json";
            req.ServicePoint.Expect100Continue = false;   // skicka frågan direkt, utan att först vänta på ett "fortsätt" från servern
            req.Timeout = 600000; req.ReadWriteTimeout = 600000;
            string json;
            try
            {
                using (ct.Register(() => { try { req.Abort(); } catch { } }))
                {
                    using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = rd.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
                string detail = ex.Message;
                try { if (ex.Response != null) using (var rd = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8)) detail += " " + rd.ReadToEnd(); } catch { }
                throw new InvalidOperationException("Språkmodellen svarade inte som väntat: " + detail + "\n" + Tail(), ex);
            }
            return ParseResponse(json, texts.Count);
        }

        // {"input":["…","…"],"encoding_format":"float"}
        public static string BuildRequest(List<string> texts)
        {
            var sb = new StringBuilder("{\"input\":[");
            for (int i = 0; i < texts.Count; i++) { if (i > 0) sb.Append(','); JsonString(sb, texts[i]); }
            return sb.Append("],\"encoding_format\":\"float\"}").ToString();
        }

        // Allt utanför ASCII skrivs som \uXXXX, så att teckenkodningen aldrig spelar någon roll. Trasiga teckenpar byts mot "?".
        static void JsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                { sb.Append("\\u").Append(((int)c).ToString("x4")).Append("\\u").Append(((int)s[i + 1]).ToString("x4")); i++; }
                else if (char.IsSurrogate(c)) sb.Append('?');
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20 || c > 0x7e) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // Svaret är som OpenAI:s: {"data":[{"index":0,"embedding":[…]},…]}. Vektorerna läggs efter index, inte efter ordningen i svaret.
        public static float[][] ParseResponse(string json, int count)
        {
            var root = Json.Parse(json) as Dictionary<string, object>;
            var data = root != null && root.ContainsKey("data") ? root["data"] as List<object> : null;
            if (data == null || data.Count != count) throw new InvalidDataException("Oväntat svar från språkmodellen (" + (data == null ? "inga" : data.Count.ToString()) + " vektorer, väntade " + count + ").");
            var res = new float[count][];
            int dim = -1;
            foreach (var o in data)
            {
                var d = o as Dictionary<string, object>;
                var emb = d != null && d.ContainsKey("embedding") ? d["embedding"] as List<object> : null;
                if (emb == null || !d.ContainsKey("index") || !(d["index"] is double)) throw new InvalidDataException("Oväntat svar från språkmodellen (en vektor saknas).");
                int i = (int)(double)d["index"];
                if (i < 0 || i >= count || res[i] != null) throw new InvalidDataException("Oväntat svar från språkmodellen (fel numrering).");
                if (dim < 0) dim = emb.Count;
                if (emb.Count != dim || dim == 0) throw new InvalidDataException("Oväntat svar från språkmodellen (vektorerna är olika långa).");
                var v = new float[dim];
                for (int k = 0; k < dim; k++)
                {
                    if (!(emb[k] is double)) throw new InvalidDataException("Oväntat svar från språkmodellen (ett värde är inte ett tal).");
                    v[k] = (float)(double)emb[k];
                }
                res[i] = v;
            }
            return res;
        }

        public void Dispose()
        {
            var p = proc; proc = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
        }

        // ---- servern stängs av Windows om Text Sync avslutas oväntat (annars ligger den kvar dold med modellen i minnet)
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attr, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static IntPtr job = IntPtr.Zero;
        static readonly object jobLock = new object();

        static void KillWithParent(Process p)
        {
            try
            {
                lock (jobLock)
                {
                    if (job == IntPtr.Zero)
                    {
                        IntPtr j = CreateJobObject(IntPtr.Zero, null);
                        if (j == IntPtr.Zero) return;
                        var info = new ExtendedLimits();
                        info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        int len = Marshal.SizeOf(typeof(ExtendedLimits));
                        IntPtr mem = Marshal.AllocHGlobal(len);
                        try
                        {
                            Marshal.StructureToPtr(info, mem, false);
                            if (!SetInformationJobObject(j, 9, mem, (uint)len)) return;   // 9 = JobObjectExtendedLimitInformation
                        }
                        finally { Marshal.FreeHGlobal(mem); }
                        job = j;   // handtaget stängs aldrig: det stängs av Windows när Text Sync avslutas, och då stängs servern
                    }
                    AssignProcessToJobObject(job, p.Handle);
                }
            }
            catch (Exception ex) { Log.Info("llama-server kunde inte knytas till Text Sync: " + ex.Message); }
        }
    }

    // Liten JSON-läsare (objekt → Dictionary, lista → List<object>, tal → double, text → string, true/false → bool, null → null)
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new InvalidDataException("Ogiltig JSON (tecken efter slutet).");
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new InvalidDataException("Ogiltig JSON (tog slut för tidigt).");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new InvalidDataException("Ogiltig JSON (namn saknas).");
                    string key = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new InvalidDataException("Ogiltig JSON (kolon saknas).");
                    i++;
                    d[key] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new InvalidDataException("Ogiltig JSON (objektet avslutas inte).");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new InvalidDataException("Ogiltig JSON (listan avslutas inte).");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            double v;
            if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new InvalidDataException("Ogiltig JSON (okänt värde).");
            return v;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                if (e == 'n') sb.Append('\n');
                else if (e == 't') sb.Append('\t');
                else if (e == 'r') sb.Append('\r');
                else if (e == 'b') sb.Append('\b');
                else if (e == 'f') sb.Append('\f');
                else if (e == 'u' && i + 4 <= s.Length) { sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; }
                else sb.Append(e);
            }
            if (i >= s.Length) throw new InvalidDataException("Ogiltig JSON (texten avslutas inte).");
            i++;
            return sb.ToString();
        }
    }
}
