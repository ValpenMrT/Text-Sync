// Text Sync - kontrollerar ett textspår (t.ex. engelska) mot det Whisper hör i ljudet, ord för ord,
// och rättar de delar av textspåret som inte går i takt med ljudet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextSync
{
    // Val i referenslistan: textspår + kontroll med Whisper
    public class TextCheckOption
    {
        public MkvTrack Track;
        public int AudioIndex;
        public override string ToString() { return Track + "  + Whisper-kontroll"; }
    }

    // Val i referenslistan: separat SRT på filmens originalspråk (t.ex. engelska) + kontroll med Whisper
    public class OrigSrtOption
    {
        public string Path;
        public List<Cue> Cues;
        public int AudioIndex;
        public override string ToString() { return "Separat SRT: " + System.IO.Path.GetFileName(Path) + "  + Whisper-kontroll"; }
    }

    public class CheckedRegion { public double FromMs, ToMs, OffsetMs; public int Lines; }

    public class TextCheckResult
    {
        public List<Cue> Corrected;           // textspåret med rättade tider
        public int Lines, Anchored, Confirmed; // antal rader / rader med ankare / ankare som stämde (±0,4 s)
        public List<CheckedRegion> Regions = new List<CheckedRegion>();
        public double MedianAbsMs;            // typisk avvikelse där det stämde
        public List<KeyValuePair<double, double>> Anchors = new List<KeyValuePair<double, double>>(); // (tid, avvikelse) för felsökning
        public string Summary() { return Summary("textspåret"); }
        public string Summary(string what)
        {
            if (Anchored < 10) return "Whisper hittade för få gemensamma ord för att kunna kontrollera " + what + " (annat språk?) – " + what + " används oförändrat.";
            var sb = new StringBuilder();
            sb.Append("Whisper-kontroll: " + Anchored + " av " + Lines + " rader jämförda, " + Math.Round(100.0 * Confirmed / Anchored) + " % inom 1 s");
            if (Regions.Count == 0) sb.Append(" – " + what + " går i takt med ljudet.");
            else
            {
                sb.Append(" – " + Regions.Sum(r => r.Lines) + " rader rättade i " + Regions.Count + " avsnitt: ");
                sb.Append(string.Join(", ", Regions.Take(6).Select(r => Srt.FormatTime(r.FromMs).Substring(0, 8) + "–" + Srt.FormatTime(r.ToMs).Substring(0, 8) + " " +
                    (r.OffsetMs >= 0 ? "+" : "−") + (Math.Abs(r.OffsetMs) / 1000).ToString("0.0", CultureInfo.GetCultureInfo("sv-SE")) + " s")));
                if (Regions.Count > 6) sb.Append(" …");
            }
            return sb.ToString();
        }
    }

    public static class TextCheck
    {
        static readonly Regex Tags = new Regex(@"<[^>]*>|\{[^}]*\}|\[[^\]]*\]|\([^)]*\)");
        static readonly Regex NonWord = new Regex(@"[^\p{L}\p{Nd}]+");

        public static string[] Words(string text)
        {
            string t = Tags.Replace(text ?? "", " ").ToLowerInvariant().Replace("'", "").Replace("’", "");
            return NonWord.Split(t).Where(w => w.Length > 0).ToArray();
        }

        const double Window = 60000;   // sök ±60 s runt textspårets tid
        const double Tol = 400;        // under detta räknas raden som i takt

        public static TextCheckResult Check(List<Cue> track, List<Cue> whisperWords)
        {
            // Whispers ord i tidsordning
            var wTime = new List<double>(); var wWord = new List<string>();
            foreach (var c in whisperWords.OrderBy(c => c.Start))
                foreach (var w in Words(c.Text)) { wTime.Add(c.Start); wWord.Add(w); }
            var times = wTime.ToArray();

            var cues = track.OrderBy(c => c.Start).ToList();
            int n = cues.Count;
            var offs = new double[n];
            for (int i = 0; i < n; i++) offs[i] = double.NaN;
            var res = new TextCheckResult { Lines = n };

            // ankare: radens första ord (3, annars 2) finns EN gång i Whisper inom ±60 s
            for (int i = 0; i < n; i++)
            {
                var ws = Words(cues[i].Text);
                if (ws.Length < 2) continue;
                int k = Math.Min(3, ws.Length);
                if (k == 2 && ws[0].Length + ws[1].Length < 7) continue; // för kort/vanligt ("i am")
                int lo = LowerBound(times, cues[i].Start - Window), hi = LowerBound(times, cues[i].Start + Window);
                int found = -1, count = 0;
                for (int j = lo; j + k <= wWord.Count && j < hi; j++)
                {
                    bool eq = true;
                    for (int q = 0; q < k && eq; q++) eq = wWord[j + q] == ws[q];
                    if (eq) { count++; found = j; }
                }
                if (count == 1) offs[i] = times[found] - cues[i].Start;
            }
            var anchored = Enumerable.Range(0, n).Where(i => !double.IsNaN(offs[i])).ToList();
            foreach (var i in anchored) res.Anchors.Add(new KeyValuePair<double, double>(cues[i].Start, offs[i]));
            res.Anchored = anchored.Count;
            if (anchored.Count < 10) { res.Corrected = cues.Select(c => c.Clone()).ToList(); return res; }

            // Whispers ordtider är brusiga (±0,5–1 s). Typisk nivå = median av alla ankare (Whisper ligger ofta lite tidigt).
            var all = anchored.Select(i => offs[i]).OrderBy(v => v).ToList();
            double baseline = all[all.Count / 2];
            // utjämnad avvikelse per rad: median av de 9 närmaste ankarna; rätta bara där minst 6 av 9 är överens (±0,8 s)
            var smooth = new double[n];
            var agree = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var near = anchored.OrderBy(a => Math.Abs(a - i)).Take(9).ToList();
                var vals = near.Select(a => offs[a]).OrderBy(v => v).ToList();
                double med = vals[vals.Count / 2];
                smooth[i] = med;
                agree[i] = near.All(a => Math.Abs(a - i) <= 40) && vals.Count(v => Math.Abs(v - med) <= 800) >= 6;
            }
            if (Math.Abs(baseline) > 2000) baseline = 0; // hela spåret förskjutet: rätta allt
            res.Confirmed = anchored.Count(i => Math.Abs(offs[i] - baseline) <= 1000);
            var absOk = anchored.Select(i => Math.Abs(offs[i] - baseline)).Where(v => v <= 1000).OrderBy(v => v).ToList();
            res.MedianAbsMs = absOk.Count > 0 ? absOk[absOk.Count / 2] : 0;
            res.Corrected = new List<Cue>(n);
            CheckedRegion cur = null;
            for (int i = 0; i < n; i++)
            {
                double d = smooth[i] - baseline;
                bool fix = agree[i] && Math.Abs(d) > 1000;
                var c = cues[i];
                res.Corrected.Add(fix ? new Cue(c.Start + d, c.End + d, c.Text) : c.Clone());
                if (fix)
                {
                    if (cur == null || Math.Abs(cur.OffsetMs - d) > 300 || i == 0 || !(res.Corrected.Count > 1))
                    {
                        cur = new CheckedRegion { FromMs = c.Start + d, OffsetMs = d };
                        res.Regions.Add(cur);
                    }
                    cur.ToMs = c.End + d; cur.Lines++;
                }
                else cur = null;
            }
            res.Regions = res.Regions.Where(r => r.Lines >= 2).ToList();
            return res;
        }

        // Whispers ord → fraser (ord med kortare paus än 400 ms emellan), som referens för den grova synken
        public static List<Cue> Phrases(List<Cue> words)
        {
            var res = new List<Cue>();
            Cue cur = null;
            foreach (var w in words.OrderBy(c => c.Start))
            {
                if (Words(w.Text).Length == 0) continue;
                if (cur != null && w.Start - cur.End < 400 && w.End - cur.Start < 8000)
                { cur.End = Math.Max(cur.End, w.End); cur.Text += " " + w.Text.Trim(); }
                else { cur = new Cue(w.Start, Math.Max(w.End, w.Start + 100), w.Text.Trim()); res.Add(cur); }
            }
            return res;
        }

        // Separat SRT på originalspråket: 1) grov synk mot Whispers fraser (förskjutning, bildfrekvens, klipp),
        // 2) ord för ord-kontroll som rättar det som fortfarande avviker. Coarse = resultatet av steg 1.
        // speech = taldetekteringens avsnitt (A) eller null. A ger skarpast tider; Whispers fraser rensar bort musik och effekter (A+B).
        public static TextCheckResult AlignToAudio(List<Cue> srt, List<Cue> words, List<Iv> speech, SyncOptions opt, Action<int, string> progress, out SyncResult coarse)
        {
            var phrases = Phrases(words);
            if (phrases.Count < 20)
                throw new UserMessageException("Whisper hörde nästan inget tal i ljudet, så undertexten på originalspråket kan inte kontrolleras. Välj ett annat alternativ i listan Referens.");
            var refc = speech != null ? Audio.Combine(speech, phrases) : phrases;
            if (refc.Count < 20) refc = phrases;
            coarse = SyncEngine.Sync(refc, srt, opt, (p, s) => progress(p, "Synkar originaltexten mot ljudet… " + p + " %"));
            var res = Check(coarse.Output, words);
            res.Corrected = res.Corrected.OrderBy(c => c.Start).ToList();   // rättade avsnitt kan hamna i fel ordning
            if (res.Anchored < 10)
                throw new UserMessageException("Undertexten på originalspråket och det Whisper hörde har nästan inga ord gemensamt (" + res.Anchored + " rader). " +
                    "Är det rätt språk? Den ska vara på samma språk som filmens ljud, t.ex. engelska.");
            return res;
        }

        static int LowerBound(double[] a, double v)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi) { int m = (lo + hi) / 2; if (a[m] < v) lo = m + 1; else hi = m; }
            return lo;
        }
    }
}
