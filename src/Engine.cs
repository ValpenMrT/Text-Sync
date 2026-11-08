// Text Sync - synkmotor
// Synkar en undertext mot en referensundertext (annat språk) genom att jämföra
// när det pratas (tidsmönstret), inte vad som sägs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextSync
{
    public class Cue
    {
        public double Start; // ms
        public double End;   // ms
        public string Text;
        public Cue(double s, double e, string t) { Start = s; End = e; Text = t ?? ""; }
        public Cue Clone() { return new Cue(Start, End, Text); }
    }

    public struct Iv
    {
        public double S, E;
        public Iv(double s, double e) { S = s; E = e; }
    }

    // ------------------------------------------------------------------ SRT
    public static class Srt
    {
        static readonly Regex TimeRx = new Regex(
            @"(\d+):(\d{1,2}):(\d{1,2})(?:[,.:](\d{1,3}))?\s*-+>\s*(\d+):(\d{1,2}):(\d{1,2})(?:[,.:](\d{1,3}))?",
            RegexOptions.Compiled);

        // Teckenkodningar man kan välja manuellt (codepage, namn)
        public static readonly object[][] Encodings = {
            new object[] { 0, "Automatisk" },
            new object[] { 65001, "UTF-8" },
            new object[] { 1252, "Västeuropeisk (sv, en, de, fr…)" },
            new object[] { 1250, "Centraleuropeisk (pl, cs, hu, hr…)" },
            new object[] { 1251, "Kyrillisk (ru, uk, bg, sr…)" },
            new object[] { 1253, "Grekisk" },
            new object[] { 1254, "Turkisk" },
            new object[] { 1257, "Baltisk (lt, lv, et)" },
        };

        // Gissa gammal 8-bitars teckenkodning när filen inte är UTF-8.
        // Kyrilliska/grekiska texter består mest av bytes ≥ 0xC0; svenska/engelska har bara enstaka (å ä ö).
        public static int GuessLegacyCodepage(byte[] b)
        {
            int ascii = 0, high = 0, pl = 0, sv = 0, greekOnly = 0;
            foreach (byte x in b)
            {
                if ((x >= 'a' && x <= 'z') || (x >= 'A' && x <= 'Z')) ascii++;
                else if (x >= 0xC0) high++;
                // typiska polska/tjeckiska tecken i 1250 som är ovanliga i 1252: ł ą ś ź ż ę ě č ř š ž
                if (x == 0xB3 || x == 0xB9 || x == 0x9C || x == 0x9F || x == 0xBF || x == 0xEA || x == 0xEC || x == 0xE8 || x == 0xF8 || x == 0x9A || x == 0x9E) pl++;
                if (x == 0xE5 || x == 0xE4 || x == 0xF6 || x == 0xC5 || x == 0xC4 || x == 0xD6) sv++;
                if (x == 0xA2 || x == 0xB8 || x == 0xB9 || x == 0xBA || x == 0xBC || x == 0xBE || x == 0xBF) greekOnly++;
            }
            if (high > ascii) return greekOnly > high / 50 + 2 ? 1253 : 1251;
            if (pl > sv * 2 && pl > 5) return 1250;
            return 1252;
        }

        public static string ReadTextFile(string path) { return ReadTextFile(path, 0); }

        // codepage 0 = automatisk
        public static string ReadTextFile(string path, int codepage)
        {
            byte[] b = File.ReadAllBytes(path);
            if (codepage != 0)
            {
                int skip = (codepage == 65001 && b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) ? 3 : 0;
                return Encoding.GetEncoding(codepage).GetString(b, skip, b.Length - skip);
            }
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
                return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
                return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(b);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(GuessLegacyCodepage(b)).GetString(b);
            }
        }

        public static List<Cue> Load(string path) { return Parse(ReadTextFile(path, 0)); }
        public static List<Cue> Load(string path, int codepage) { return Parse(ReadTextFile(path, codepage)); }

        static double Ms(Group h, Group m, Group s, Group f)
        {
            double ms = 0;
            if (f.Success)
            {
                string v = f.Value;
                ms = int.Parse(v, CultureInfo.InvariantCulture);
                if (v.Length == 1) ms *= 100; else if (v.Length == 2) ms *= 10;
            }
            return ((int.Parse(h.Value) * 60.0 + int.Parse(m.Value)) * 60.0 + int.Parse(s.Value)) * 1000.0 + ms;
        }

        public static List<Cue> Parse(string text)
        {
            var cues = new List<Cue>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                Match m = TimeRx.Match(lines[i]);
                if (!m.Success) { i++; continue; }
                double st, en;
                try
                {
                    st = Ms(m.Groups[1], m.Groups[2], m.Groups[3], m.Groups[4]);
                    en = Ms(m.Groups[5], m.Groups[6], m.Groups[7], m.Groups[8]);
                }
                catch (OverflowException) { i++; continue; } // orimlig tid – hoppa över
                i++;
                var sb = new List<string>();
                while (i < lines.Length)
                {
                    string l = lines[i];
                    if (l.Trim().Length == 0) break;
                    // Saknad tomrad: nästa block börjar (nummer + tidrad)
                    if (i + 1 < lines.Length && IsIndex(l) && TimeRx.IsMatch(lines[i + 1])) break;
                    if (TimeRx.IsMatch(l)) break;
                    sb.Add(l.TrimEnd());
                    i++;
                }
                if (en < st) en = st + 1000;
                cues.Add(new Cue(st, en, string.Join("\n", sb)));
            }
            cues.Sort((a, b) => a.Start.CompareTo(b.Start));
            return cues;
        }

        static bool IsIndex(string l)
        {
            l = l.Trim();
            if (l.Length == 0) return false;
            foreach (char c in l) if (c < '0' || c > '9') return false;
            return true;
        }

        static readonly Regex OneTimeRx = new Regex(@"^\s*(\d{1,2}):(\d{1,2}):(\d{1,2})(?:[,.](\d{1,3}))?\s*$");

        // "00:01:23,456" → millisekunder. Används när man ändrar en rad för hand.
        public static bool TryParseTime(string s, out double ms)
        {
            ms = 0;
            var m = OneTimeRx.Match(s ?? "");
            if (!m.Success) return false;
            int mm = int.Parse(m.Groups[2].Value), ss = int.Parse(m.Groups[3].Value);
            if (mm > 59 || ss > 59) return false;
            ms = Ms(m.Groups[1], m.Groups[2], m.Groups[3], m.Groups[4]);
            return true;
        }

        public static string FormatTime(double ms)
        {
            if (ms < 0) ms = 0;
            long t = (long)Math.Round(ms);
            long h = t / 3600000; t %= 3600000;
            long m = t / 60000; t %= 60000;
            long s = t / 1000; long f = t % 1000;
            return string.Format("{0:00}:{1:00}:{2:00},{3:000}", h, m, s, f);
        }

        public static string ToSrt(List<Cue> cues)
        {
            var sb = new StringBuilder();
            int n = 1;
            foreach (var c in cues)
            {
                if (c.End <= 0) continue;
                sb.Append(n++).Append("\r\n");
                sb.Append(FormatTime(c.Start)).Append(" --> ").Append(FormatTime(c.End)).Append("\r\n");
                sb.Append((c.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        public static void Save(string path, List<Cue> cues)
        {
            File.WriteAllText(path, ToSrt(cues), new UTF8Encoding(true));
        }
    }

    // ------------------------------------------------------------------ Synk
    public enum SyncMode { Smart = 0, Simple = 1, Advanced = 2 }

    public class SyncOptions
    {
        public SyncMode Mode = SyncMode.Smart;
        public bool DetectFramerate = true;
        public double MaxOffsetSec = 900;   // global sökning ±15 min
        public double LocalRangeSec = 20;   // lokal sökning ±20 s (klipp/pauser)
    }

    public class Segment
    {
        public double FromMs;   // i synkad tid
        public double OffsetMs; // utöver global transform
    }

    public class SyncResult
    {
        public List<Cue> Output;
        public double Scale = 1;
        public string FpsText = "";
        public double OffsetMs;        // global förskjutning
        public double DriftMsPerHour;  // linjär driftkorrigering
        public List<Segment> Segments = new List<Segment>();
        public double ScoreBefore, ScoreAfter;
        public string Method = "";
        public List<string> Notes = new List<string>();
    }

    public static class SyncEngine
    {
        const double Bin = 20;          // ms per sampel vid global sökning
        const double GridStep = 10000;  // ms mellan lokala mätpunkter
        const double Window = 30000;    // ± fönster för lokala mätpunkter
        const double LocalStep = 50;    // ms upplösning lokalt

        static readonly double[][] FpsPairs = {
            new double[] { 25, 23.976 }, new double[] { 23.976, 25 },
            new double[] { 24, 23.976 }, new double[] { 23.976, 24 },
            new double[] { 25, 24 },     new double[] { 24, 25 },
        };

        // ---------- hjälpare
        public static Iv[] Merge(IEnumerable<Iv> src)
        {
            var l = src.Where(x => x.E > x.S).OrderBy(x => x.S).ToList();
            var r = new List<Iv>();
            foreach (var x in l)
            {
                if (r.Count > 0 && x.S <= r[r.Count - 1].E)
                {
                    var last = r[r.Count - 1];
                    if (x.E > last.E) { last.E = x.E; r[r.Count - 1] = last; }
                }
                else r.Add(x);
            }
            return r.ToArray();
        }

        static Iv[] ToIv(List<Cue> cues, Func<double, double> f)
        {
            return Merge(cues.Select(c => new Iv(f(c.Start), f(c.End))));
        }

        static double Total(Iv[] a) { double t = 0; foreach (var x in a) t += x.E - x.S; return t; }

        public static double Overlap(Iv[] a, Iv[] b)
        {
            int i = 0, j = 0; double ov = 0;
            while (i < a.Length && j < b.Length)
            {
                double s = Math.Max(a[i].S, b[j].S), e = Math.Min(a[i].E, b[j].E);
                if (e > s) ov += e - s;
                if (a[i].E < b[j].E) i++; else j++;
            }
            return ov;
        }

        public static double Score(Iv[] a, Iv[] b)
        {
            double ta = Total(a), tb = Total(b);
            if (ta <= 0 || tb <= 0) return 0;
            return Overlap(a, b) / Math.Sqrt(ta * tb);
        }

        public static double Score(List<Cue> refc, List<Cue> tgt)
        {
            return Score(ToIv(refc, x => x), ToIv(tgt, x => x));
        }

        static double[] Signal(Iv[] iv, int n)
        {
            var a = new double[n];
            foreach (var x in iv)
            {
                double s = x.S / Bin, e = x.E / Bin;
                if (e <= 0) continue;
                if (s < 0) s = 0;
                int i0 = (int)Math.Floor(s), i1 = (int)Math.Ceiling(e);
                for (int i = i0; i < i1 && i < n; i++)
                {
                    double ov = Math.Min(e, i + 1) - Math.Max(s, i);
                    if (ov > 0) a[i] = Math.Min(1, a[i] + ov);
                }
            }
            return a;
        }

        static void FFT(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                int half = len >> 1;
                var wr = new double[half]; var wi = new double[half];
                for (int k = 0; k < half; k++) { wr[k] = Math.Cos(ang * k); wi[k] = Math.Sin(ang * k); }
                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        double xr = re[b] * wr[k] - im[b] * wi[k];
                        double xi = re[b] * wi[k] + im[b] * wr[k];
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                    }
                }
            }
            if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
        }

        static string Fmt(double v) { return v.ToString("0.###", CultureInfo.GetCultureInfo("sv-SE")); }

        // ---------- repliksstarter: översättningar börjar nästan alltid samtidigt som originalet,
        // vilket ger en mycket skarpare träff än bara "när det pratas" i tät dialog.
        const double OnsetTol = 300; // ms

        static int Nearest(double[] a, double v)
        {
            int i = Array.BinarySearch(a, v);
            if (i >= 0) return i;
            i = ~i;
            if (i == 0) return 0;
            if (i >= a.Length) return a.Length - 1;
            return (v - a[i - 1] <= a[i] - v) ? i - 1 : i;
        }

        static double OnsetKernel(double[] refSt, double t)
        {
            if (refSt.Length == 0) return 0;
            double d = Math.Abs(refSt[Nearest(refSt, t)] - t);
            return d >= OnsetTol ? 0 : 1 - d / OnsetTol;
        }

        static double Onset(double[] refSt, IEnumerable<double> starts)
        {
            double sum = 0; int n = 0;
            foreach (var s in starts) { sum += OnsetKernel(refSt, s); n++; }
            return n == 0 ? 0 : sum / n;
        }

        // kombinerad passform: överlapp + repliksstarter
        static double Fit(Iv[] refIv, double[] refSt, List<Cue> tgt, Func<double, double> f)
        {
            return 0.5 * Score(refIv, ToIv(tgt, f)) + 0.5 * Onset(refSt, tgt.Select(c => f(c.Start)));
        }

        // ---------- huvudfunktion
        public static SyncResult Sync(List<Cue> refCues, List<Cue> tgtCues, SyncOptions opt, Action<int, string> progress)
        {
            if (progress == null) progress = (p, s) => { };
            if (refCues.Count < 5) throw new Exception("Referensen har för få repliker (" + refCues.Count + ").");
            if (tgtCues.Count < 5) throw new Exception("Undertexten som ska synkas har för få repliker (" + tgtCues.Count + ").");

            var res = new SyncResult();
            // orimliga tider (t.ex. en felaktig rad på 500 timmar) skulle annars äta upp minnet
            const double MaxTime = 24 * 3600000.0;
            int badRef = refCues.Count(c => c.Start > MaxTime);
            int badTgt = tgtCues.Count(c => c.Start > MaxTime);
            if (badRef > 0) refCues = refCues.Where(c => c.Start <= MaxTime).ToList();
            if (badTgt > 0)
            {
                tgtCues = tgtCues.Where(c => c.Start <= MaxTime).ToList();
                res.Notes.Add(badTgt + " rad(er) med orimlig tid (över 24 timmar) hoppades över.");
            }
            // väldigt långa repliker (skyltar, sångtexter) styr inte synken
            refCues = refCues.Select(c => new Cue(c.Start, Math.Min(c.End, c.Start + 15000), c.Text)).ToList();
            Iv[] refIv = ToIv(refCues, x => x);
            if (refIv.Length < 5 || ToIv(tgtCues, x => x).Length < 5)
                throw new InvalidDataException("Undertexterna har för få repliker med giltiga tider för att kunna synkas.");
            double[] refSt = refCues.Select(c => c.Start).OrderBy(x => x).ToArray();
            res.ScoreBefore = Score(refIv, ToIv(tgtCues, x => x));

            // 1) Global sökning: förskjutning (+ bildfrekvens) via korskorrelation
            progress(5, "Söker global förskjutning…");
            var scales = new List<double[]>();
            scales.Add(new double[] { 1, 0, 0 });
            if (opt.DetectFramerate)
                foreach (var p in FpsPairs) scales.Add(new double[] { p[0] / p[1], p[0], p[1] });

            double refEnd = refIv[refIv.Length - 1].E;
            double tgtEnd = tgtCues.Max(c => c.End) * scales.Max(s => s[0]);
            int la = (int)Math.Ceiling(refEnd / Bin) + 2;
            int lb = (int)Math.Ceiling(tgtEnd / Bin) + 2;
            int N = 1; while (N < la + lb) N <<= 1;

            var ar = new double[N]; var ai = new double[N];
            Array.Copy(Signal(refIv, la), ar, la);
            FFT(ar, ai, false);

            double bestNorm = -1, bestOff = 0, bestScale = 1, norm1 = -1; int bestIdx = 0;
            double[] perScaleNorm = new double[scales.Count];
            double[] perScaleOff = new double[scales.Count];
            int maxShift = (int)(opt.MaxOffsetSec * 1000 / Bin);
            for (int si = 0; si < scales.Count; si++)
            {
                double sc = scales[si][0];
                progress(5 + 40 * si / scales.Count, "Testar " + (sc == 1 ? "samma bildfrekvens" : Fmt(scales[si][1]) + " → " + Fmt(scales[si][2]) + " fps") + "…");
                Iv[] tIv = ToIv(tgtCues, x => x * sc);
                var br = new double[N]; var bi = new double[N];
                Array.Copy(Signal(tIv, lb), br, lb);
                FFT(br, bi, false);
                for (int k = 0; k < N; k++)
                {
                    double r = ar[k] * br[k] + ai[k] * bi[k];
                    double im = ai[k] * br[k] - ar[k] * bi[k];
                    br[k] = r; bi[k] = im;
                }
                FFT(br, bi, true);
                double best = -1; int bestK = 0;
                for (int k = 0; k < N; k++)
                {
                    int shift = k < N / 2 ? k : k - N;
                    if (Math.Abs(shift) > maxShift) continue;
                    if (br[k] > best) { best = br[k]; bestK = shift; }
                }
                // finjustera exakt (1 ms) runt toppen
                double off0 = bestK * Bin, fineBest = -1, fineOff = off0;
                for (double d = off0 - 2 * Bin; d <= off0 + 2 * Bin; d += 2)
                {
                    double dd = d;
                    double s = Fit(refIv, refSt, tgtCues, x => x * sc + dd);
                    if (s > fineBest) { fineBest = s; fineOff = d; }
                }
                perScaleNorm[si] = fineBest; perScaleOff[si] = fineOff;
                if (si == 0) norm1 = fineBest;
                if (fineBest > bestNorm) { bestNorm = fineBest; bestOff = fineOff; bestScale = sc; bestIdx = si; }
            }
            // kräv tydlig vinst för att byta bildfrekvens
            if (bestIdx != 0 && bestNorm < norm1 * 1.06 + 0.01)
            {
                bestIdx = 0; bestScale = 1; bestNorm = norm1; bestOff = perScaleOff[0];
            }
            res.Scale = bestScale;
            res.OffsetMs = bestOff;
            if (bestIdx != 0)
                res.FpsText = "Undertexten var gjord för " + Fmt(scales[bestIdx][1]) + " fps – omräknad till " + Fmt(scales[bestIdx][2]) + " fps";

            double gScale = bestScale, gOff = bestOff;
            Func<double, double> gT = x => x * gScale + gOff;
            Iv[] tgtG = null; double[] tgtStG = null; double scoreGlobal = 0;
            double R = opt.LocalRangeSec * 1000;
            int K = (int)Math.Round(2 * R / LocalStep) + 1;
            List<double> grid = null; double[][] scores = null; double[] speech = null;
            double la_ = 0, lb_ = 0; int inl = 0, pts = 0;

            for (int pass = 0; pass < 2; pass++)
            {
                tgtG = ToIv(tgtCues, gT);
                tgtStG = tgtCues.Select(c => gT(c.Start)).OrderBy(x => x).ToArray();
                scoreGlobal = Fit(refIv, refSt, tgtCues, gT);

                // 2) Lokala mätpunkter
                progress(50, "Mäter lokala avvikelser…");
                double gFrom = Math.Max(0, tgtG[0].S), gTo = tgtG[tgtG.Length - 1].E;
                grid = new List<double>();
                for (double g = gFrom; g <= gTo + 1; g += GridStep) grid.Add(g);
                scores = new double[grid.Count][];
                speech = new double[grid.Count];
                for (int i = 0; i < grid.Count; i++)
                {
                    scores[i] = LocalScores(refIv, tgtG, refSt, tgtStG, grid[i], Window, R, LocalStep, K, out speech[i]);
                    if (i % 20 == 0) progress(50 + 30 * i / Math.Max(1, grid.Count), "Mäter lokala avvikelser…");
                }

                // 2a) Linjär driftkorrigering (enkel/smart)
                FitLinear(grid, scores, speech, R, K, out la_, out lb_, out inl, out pts);

                // Motsvarar driften en känd bildfrekvensomräkning (t.ex. 4,3 % = 25 → 23,976) som den globala
                // sökningen missade (vanligt med ljud som referens eller korta klipp)? Byt till exakt bildfrekvens och gör om.
                if (pass > 0 || !opt.DetectFramerate || pts < 5 || Math.Abs(la_) < 0.0005) break;
                double total = gScale * (1 + la_);
                int snap = -1;
                for (int si = 1; si < scales.Count; si++) if (Math.Abs(total - scales[si][0]) < 0.0025) snap = si;
                if (snap < 0 || Math.Abs(scales[snap][0] - gScale) < 1e-6) break;
                double newScale = scales[snap][0], guess = gOff * (1 + la_) + lb_, bestS = -1, bestO = guess;
                for (double d = guess - 1500; d <= guess + 1500; d += 20)
                {
                    double dd = d;
                    double s = Fit(refIv, refSt, tgtCues, x => x * newScale + dd);
                    if (s > bestS) { bestS = s; bestO = d; }
                }
                gScale = newScale; gOff = bestO; bestIdx = snap;
                res.Scale = gScale; res.OffsetMs = gOff;
                res.FpsText = "Undertexten var gjord för " + Fmt(scales[snap][1]) + " fps – omräknad till " + Fmt(scales[snap][2]) + " fps";
            }            if (Math.Abs(la_) > 0.02) { la_ = 0; lb_ = 0; pts = 0; } // större drift än 2 % utan känd bildfrekvens är orimlig
            List<Cue> outLinear = null; double scoreLinear = scoreGlobal;
            {
                double a = la_, b = lb_;
                Func<double, double> lt = x => { double y = gT(x); return y + a * y + b; };
                double s = Fit(refIv, refSt, tgtCues, lt);
                if (pts >= 5 && s > scoreGlobal)
                {
                    // finjustera b
                    double bb = b, best = s;
                    for (double d = b - 60; d <= b + 60; d += 5)
                    {
                        double dd = d;
                        double s2 = Fit(refIv, refSt, tgtCues, x => { double y = gT(x); return y + a * y + dd; });
                        if (s2 > best) { best = s2; bb = d; }
                    }
                    b = bb;
                    Func<double, double> lt2 = x => { double y = gT(x); return y + a * y + bb; };
                    outLinear = tgtCues.Select(c => new Cue(lt2(c.Start), lt2(c.End), c.Text)).ToList();
                    scoreLinear = best;
                    la_ = a; lb_ = b;
                }
            }
            if (outLinear == null)
            {
                outLinear = tgtCues.Select(c => new Cue(gT(c.Start), gT(c.End), c.Text)).ToList();
                la_ = 0; lb_ = 0;
            }
            double inlierFrac = pts > 0 ? (double)inl / pts : 1;

            List<Cue> output = outLinear; double scoreOut = scoreLinear;
            res.Method = "Förskjutning" + (bestIdx != 0 ? " + bildfrekvens" : "") + (la_ != 0 ? " + driftkorrigering" : "");
            res.OffsetMs = gOff + lb_;
            res.DriftMsPerHour = la_ * 3600000;

            // 2b) Styckvis (klipp/reklampauser)
            if (opt.Mode != SyncMode.Simple)
            {
                progress(82, "Letar efter klipp och pauser…");
                List<Segment> segs;
                List<Cue> outPw = Piecewise(refIv, refSt, tgtCues, gT, grid, scores, R, K, out segs);
                double scorePw = Fit(refIv, refSt, outPw, x => x);
                bool usePw = opt.Mode == SyncMode.Advanced
                    ? scorePw > scoreOut
                    : (segs.Count > 1 && scorePw > scoreOut + 0.015 && inlierFrac < 0.9);
                if (usePw)
                {
                    output = outPw; scoreOut = scorePw;
                    res.Segments = segs;
                    res.Method = "Styckvis synk (" + segs.Count + " avsnitt)" + (bestIdx != 0 ? " + bildfrekvens" : "");
                    res.DriftMsPerHour = 0;
                    res.OffsetMs = gOff + (segs.Count > 0 ? segs[0].OffsetMs : 0);
                    for (int i = 0; i < segs.Count; i++) segs[i].OffsetMs += gOff;
                }
                else if (opt.Mode == SyncMode.Smart && inlierFrac < 0.7)
                {
                    res.Notes.Add("Delar av texten följer inte samma förskjutning. Prova metoden \"Avancerad\" om resultatet är fel.");
                }
            }

            foreach (var c in output) { if (c.Start < 0) c.Start = 0; if (c.End < c.Start) c.End = c.Start; }
            output.Sort((x, y) => x.Start.CompareTo(y.Start));
            res.Output = output;
            res.ScoreAfter = Score(refIv, ToIv(output, x => x));
            if (res.ScoreAfter < 0.35)
                res.Notes.Add("Låg matchning. Kontrollera att referensen och undertexten hör till samma film/avsnitt.");
            progress(100, "Klart");
            return res;
        }

        // Poäng (andel av måltextens tal som överlappar referensen) för varje förskjutning -R..R
        static double[] LocalScores(Iv[] refM, Iv[] tgt, double[] refSt, double[] tgtSt, double g, double W, double R, double step, int K, out double tgtSpeech)
        {
            var ov = OverlapScores(refM, tgt, g, W, R, step, K, out tgtSpeech);
            // repliksstarter
            var on = new double[K];
            int n = 0;
            int i0 = Array.BinarySearch(tgtSt, g - W); if (i0 < 0) i0 = ~i0;
            for (int i = i0; i < tgtSt.Length && tgtSt[i] < g + W; i++)
            {
                double ts = tgtSt[i]; n++;
                int r0 = Array.BinarySearch(refSt, ts - R - OnsetTol); if (r0 < 0) r0 = ~r0;
                for (int r = r0; r < refSt.Length && refSt[r] <= ts + R + OnsetTol; r++)
                {
                    double d0 = refSt[r] - ts;
                    int k0 = Math.Max(0, (int)Math.Ceiling((d0 - OnsetTol + R) / step));
                    int k1 = Math.Min(K - 1, (int)Math.Floor((d0 + OnsetTol + R) / step));
                    for (int k = k0; k <= k1; k++)
                    {
                        double w = 1 - Math.Abs(-R + k * step - d0) / OnsetTol;
                        if (w > 0) on[k] += w;
                    }
                }
            }
            if (n > 0) for (int k = 0; k < K; k++) ov[k] = 0.5 * ov[k] + 0.5 * Math.Min(1, on[k] / n);
            else for (int k = 0; k < K; k++) ov[k] *= 0.5;
            return ov;
        }

        static double[] OverlapScores(Iv[] refM, Iv[] tgt, double g, double W, double R, double step, int K, out double tgtSpeech)
        {
            var sc = new double[K];
            double lo = g - W, hi = g + W;
            tgtSpeech = 0;
            var tl = new List<Iv>();
            int t0 = LowerBoundE(tgt, lo);
            for (int t = t0; t < tgt.Length && tgt[t].S < hi; t++)
            {
                double s = Math.Max(tgt[t].S, lo), e = Math.Min(tgt[t].E, hi);
                if (e > s) { tl.Add(new Iv(s, e)); tgtSpeech += e - s; }
            }
            if (tgtSpeech <= 0) return sc;
            int r0 = LowerBoundE(refM, lo - R);
            for (int r = r0; r < refM.Length && refM[r].S < hi + R; r++)
            {
                double rs = refM[r].S, re = refM[r].E;
                foreach (var t in tl)
                {
                    double dmin = rs - t.E, dmax = re - t.S;
                    int k0 = Math.Max(0, (int)Math.Ceiling((dmin + R) / step));
                    int k1 = Math.Min(K - 1, (int)Math.Floor((dmax + R) / step));
                    for (int k = k0; k <= k1; k++)
                    {
                        double d = -R + k * step;
                        double ov = Math.Min(re, t.E + d) - Math.Max(rs, t.S + d);
                        if (ov > 0) sc[k] += ov;
                    }
                }
            }
            for (int k = 0; k < K; k++) sc[k] /= tgtSpeech;
            return sc;
        }

        static int LowerBoundE(Iv[] a, double v)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi) { int m = (lo + hi) / 2; if (a[m].E <= v) lo = m + 1; else hi = m; }
            return lo;
        }

        static void FitLinear(List<double> grid, double[][] scores, double[] speech, double R, int K,
            out double a, out double b, out int inliers, out int points)
        {
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i < grid.Count; i++)
            {
                if (speech[i] < 3000) continue;
                double[] s = scores[i];
                int bk = 0; for (int k = 1; k < K; k++) if (s[k] > s[bk]) bk = k;
                double second = 0;
                int ex = (int)(700 / LocalStep);
                for (int k = 0; k < K; k++) if (Math.Abs(k - bk) > ex && s[k] > second) second = s[k];
                if (s[bk] < 0.4 || s[bk] - second < 0.08) continue;
                xs.Add(grid[i]); ys.Add(-R + bk * LocalStep);
            }
            points = xs.Count; a = 0; b = 0; inliers = 0;
            if (points < 5) return;
            var sorted = ys.OrderBy(v => v).ToList();
            b = sorted[sorted.Count / 2];
            double[] thr = { 500, 300, 200, 150 };
            foreach (double th in thr)
            {
                double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = 0;
                for (int i = 0; i < xs.Count; i++)
                {
                    if (Math.Abs(ys[i] - (a * xs[i] + b)) > th) continue;
                    sx += xs[i]; sy += ys[i]; sxx += xs[i] * xs[i]; sxy += xs[i] * ys[i]; n++;
                }
                if (n < 5) break;
                double den = n * sxx - sx * sx;
                if (Math.Abs(den) < 1e-9) { a = 0; b = sy / n; }
                else { a = (n * sxy - sx * sy) / den; b = (sy - a * sx) / n; }
                inliers = n;
            }
            // orimlig drift (> 2 %) → ignorera lutning
            if (Math.Abs(a) > 0.06) { a = 0; b = sorted[sorted.Count / 2]; }
        }

        static List<Cue> Piecewise(Iv[] refIv, double[] refSt, List<Cue> tgt, Func<double, double> gT, List<double> grid,
            double[][] scores, double R, int K, out List<Segment> segs)
        {
            int G = grid.Count;
            const double Lambda = 0.03;   // kostnad för små steg (drift)
            const double Jump = 1.2;      // kostnad för hopp (klipp)
            const double Prior = 0.05;    // liten dragning mot 0
            var dp = new double[K]; var nd = new double[K];
            var back = new int[G][];
            for (int k = 0; k < K; k++) dp[k] = -scores[0][k] + Prior * Math.Abs(-R + k * LocalStep) / R;
            for (int i = 1; i < G; i++)
            {
                back[i] = new int[K];
                int bp = 0; for (int k = 1; k < K; k++) if (dp[k] < dp[bp]) bp = k;
                for (int k = 0; k < K; k++)
                {
                    double best = dp[k]; int arg = k;
                    if (k > 0 && dp[k - 1] + Lambda < best) { best = dp[k - 1] + Lambda; arg = k - 1; }
                    if (k < K - 1 && dp[k + 1] + Lambda < best) { best = dp[k + 1] + Lambda; arg = k + 1; }
                    if (dp[bp] + Jump < best) { best = dp[bp] + Jump; arg = bp; }
                    nd[k] = best - scores[i][k];
                    back[i][k] = arg;
                }
                var t = dp; dp = nd; nd = t;
            }
            var path = new int[G];
            int last = 0; for (int k = 1; k < K; k++) if (dp[k] < dp[last]) last = k;
            path[G - 1] = last;
            for (int i = G - 1; i > 0; i--) path[i - 1] = back[i][path[i]];
            var off = path.Select(k => -R + k * LocalStep).ToArray();

            // per replik: välj förskjutning från närmaste punkter
            var gArr = grid.ToArray();
            var outc = new List<Cue>();
            var cueOff = new double[tgt.Count];
            var mids = new double[tgt.Count];
            for (int ci = 0; ci < tgt.Count; ci++)
            {
                var c = tgt[ci];
                double mid = (gT(c.Start) + gT(c.End)) / 2;
                mids[ci] = mid;
                int i = Array.BinarySearch(gArr, mid);
                if (i < 0) i = ~i - 1;
                if (i < 0) i = 0;
                double o;
                if (i >= G - 1) o = off[G - 1];
                else if (Math.Abs(off[i] - off[i + 1]) <= LocalStep * 1.01)
                {
                    double f = (mid - gArr[i]) / (gArr[i + 1] - gArr[i]);
                    o = off[i] + (off[i + 1] - off[i]) * f;
                }
                else o = (mid - gArr[i] < gArr[i + 1] - mid) ? off[i] : off[i + 1];
                cueOff[ci] = o;
            }
            // gräns mellan två avsnitt: mätpunkterna ligger glest (10 s) och ser ±30 s,
            // så den verkliga brytpunkten kan ligga en bit från dem. Pröva alla repliker
            // ±25 s runt gränsen och välj EN brytpunkt som passar bäst.
            var boundary = new Dictionary<int, List<int>>();
            for (int i = 0; i < G - 1; i++)
            {
                if (Math.Abs(off[i] - off[i + 1]) <= LocalStep * 1.01) continue;
                double lo = gArr[i] - 25000, hi = gArr[i + 1] + 25000;
                if (i > 0 && Math.Abs(off[i - 1] - off[i]) > LocalStep * 1.01) lo = Math.Max(lo, (gArr[i - 1] + gArr[i]) / 2);
                if (i + 2 < G && Math.Abs(off[i + 1] - off[i + 2]) > LocalStep * 1.01) hi = Math.Min(hi, (gArr[i + 1] + gArr[i + 2]) / 2);
                var l = new List<int>();
                for (int ci = 0; ci < tgt.Count; ci++) if (mids[ci] >= lo && mids[ci] < hi) l.Add(ci);
                if (l.Count > 0) boundary[i] = l;
            }
            foreach (var kv in boundary)
            {
                int i = kv.Key; var idx = kv.Value; int m = idx.Count;
                var fa = idx.Select(ci => CueFit(refIv, refSt, tgt, gT, ci, off[i])).ToArray();
                var fb = idx.Select(ci => CueFit(refIv, refSt, tgt, gT, ci, off[i + 1])).ToArray();
                double tot = fb.Sum(), best = tot; int bestP = 0; double run = tot;
                for (int p = 1; p <= m; p++)
                {
                    run += fa[p - 1] - fb[p - 1];
                    if (run > best + 1e-6) { best = run; bestP = p; }
                }
                for (int j = 0; j < m; j++) cueOff[idx[j]] = j < bestP ? off[i] : off[i + 1];
            }

            // segmentlista + finjustering per avsnitt (±60 ms)
            segs = new List<Segment>();
            int startIdx = 0;
            for (int ci = 1; ci <= tgt.Count; ci++)
            {
                bool brk = ci == tgt.Count || Math.Abs(cueOff[ci] - cueOff[ci - 1]) > 500;
                if (!brk) continue;
                int a0 = startIdx, a1 = ci; // [a0, a1)
                var part = tgt.GetRange(a0, a1 - a0);
                var offs = cueOff.Skip(a0).Take(a1 - a0).ToArray();
                double bestD = 0, bestS = -1;
                double lo = part.Count > 0 ? gT(part[0].Start) - 3000 : 0;
                double hi = part.Count > 0 ? gT(part[part.Count - 1].End) + 3000 : 0;
                var refPart = Merge(refIv.Where(x => x.E > lo - R && x.S < hi + R));
                for (double d = -60; d <= 60; d += 5)
                {
                    var iv = new List<Iv>();
                    for (int j = 0; j < part.Count; j++) iv.Add(new Iv(gT(part[j].Start) + offs[j] + d, gT(part[j].End) + offs[j] + d));
                    double sc = 0.5 * Score(refPart, Merge(iv));
                    double on = 0;
                    for (int j = 0; j < part.Count; j++) on += OnsetKernel(refSt, gT(part[j].Start) + offs[j] + d);
                    sc += 0.5 * on / Math.Max(1, part.Count);
                    if (sc > bestS) { bestS = sc; bestD = d; }
                }
                for (int j = 0; j < part.Count; j++)
                    outc.Add(new Cue(gT(part[j].Start) + offs[j] + bestD, gT(part[j].End) + offs[j] + bestD, part[j].Text));
                if (part.Count > 0)
                {
                    double avg = offs.Average() + bestD;
                    segs.Add(new Segment { FromMs = gT(part[0].Start) + offs[0] + bestD, OffsetMs = avg });
                }
                startIdx = ci;
            }
            return outc;
        }

        static double CueFit(Iv[] refIv, double[] refSt, List<Cue> tgt, Func<double, double> gT, int ci, double o)
        {
            double sum = 0;
            for (int j = ci; j <= ci; j++)
            {
                double w = 1;
                double s = gT(tgt[j].Start) + o, e = gT(tgt[j].End) + o;
                sum += w * 2000 * OnsetKernel(refSt, s);
                int r = LowerBoundE(refIv, s);
                for (; r < refIv.Length && refIv[r].S < e; r++)
                {
                    double ov = Math.Min(e, refIv[r].E) - Math.Max(s, refIv[r].S);
                    if (ov > 0) sum += w * ov;
                }
            }
            return sum;
        }

        public static List<Cue> ApplyExtra(List<Cue> cues, double extraMs)
        {
            return cues.Select(c => new Cue(Math.Max(0, c.Start + extraMs), Math.Max(0, c.End + extraMs), c.Text)).ToList();
        }
    }
}
