using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace TextSync
{
    // TextCheckDiag <film.mkv> <svensk.srt> <spårnr>
    static class TextCheckDiag
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string mkv = args[0], srt = args[1]; ulong no = ulong.Parse(args[2]);
            List<Cue> track;
            using (var f = new MkvFile(mkv)) track = f.ReadSubtitles(f.Tracks.First(t => t.Number == no), p => { }, null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string lang;
            var words = Audio.GetWords(mkv, 0, (p, s) => { }, CancellationToken.None, out lang);
            Console.WriteLine("Whisper ordläge: " + words.Count + " ord, språk " + lang + ", " + sw.Elapsed.TotalSeconds.ToString("0") + " s");

            var tgt = Srt.Load(srt).Select((c, i) => new Cue(c.Start, c.End, "#" + i + "|" + c.Text)).ToList();
            var truth = Index(SyncEngine.Sync(track, tgt, new SyncOptions(), null).Output);

            // 1) spåret som det är
            var r1 = TextCheck.Check(track, words);
            Console.WriteLine("\n1) Textspåret orört:\n   " + r1.Summary() + "  (typisk avvikelse " + r1.MedianAbsMs.ToString("0") + " ms)");

            // 2) del av spåret förskjuten med flit: 10:00–15:00 +3 s
            var broken = track.Select(c => c.Start >= 600000 && c.Start < 900000 ? new Cue(c.Start + 3000, c.End + 3000, c.Text) : c.Clone()).ToList();
            var r2 = TextCheck.Check(broken, words);
            Console.WriteLine("   ankare 8:00–17:00 (tid: avvikelse ms):");
            Console.WriteLine("   " + string.Join("  ", r2.Anchors.Where(a => a.Key > 480000 && a.Key < 1020000).Select(a => Srt.FormatTime(a.Key).Substring(3, 5) + ":" + a.Value.ToString("0"))));
            var hist = r1.Anchors.Select(a => a.Value).OrderBy(v => v).ToList();
            Console.WriteLine("   orört spår, avvikelser percentiler 10/25/50/75/90: " + string.Join(" / ", new[] { 0.1, 0.25, 0.5, 0.75, 0.9 }.Select(p => hist[(int)(p * (hist.Count - 1))].ToString("0"))));
            Console.WriteLine("\n2) 10:00–15:00 förskjutet +3 s med flit:\n   " + r2.Summary());
            var errBroken = new List<double>(); var errFixed = new List<double>();
            var trackSorted = track.OrderBy(c => c.Start).ToList();
            for (int i = 0; i < trackSorted.Count; i++)
            {
                var orig = trackSorted[i];
                if (orig.Start < 600000 || orig.Start >= 900000) continue;
                errBroken.Add(3000);
                errFixed.Add(Math.Abs(r2.Corrected[i].Start - orig.Start));
            }
            errFixed.Sort();
            Console.WriteLine("   rader i det förskjutna området: " + errFixed.Count + ", fel före kontroll 3000 ms, efter: median " + errFixed[errFixed.Count / 2].ToString("0") +
                " ms, max " + errFixed.Last().ToString("0") + " ms, " + errFixed.Count(e => e < 400) + " rader inom 0,4 s");
            int touched = 0;
            for (int i = 0; i < trackSorted.Count; i++)
                if ((trackSorted[i].Start < 600000 || trackSorted[i].Start >= 900000) && Math.Abs(r2.Corrected[i].Start - trackSorted[i].Start) > 1) touched++;
            Console.WriteLine("   rader utanför området som ändrades: " + touched);

            // 3) svensk synk mot trasigt spår vs rättat spår, jämfört med facit
            foreach (var pair in new[] { Tuple.Create("mot trasigt spår", broken), Tuple.Create("mot rättat spår", r2.Corrected) })
            {
                var got = Index(SyncEngine.Sync(pair.Item2, tgt, new SyncOptions(), null).Output);
                var e = truth.Keys.Where(got.ContainsKey).Select(k => Math.Abs(got[k] - truth[k])).OrderBy(x => x).ToList();
                Console.WriteLine("3) Svensk synk " + pair.Item1 + ": median " + e[e.Count / 2].ToString("0") + " ms, >1 s: " + e.Count(x => x > 1000) + " rader");
            }
            return 0;
        }

        static Dictionary<int, double> Index(List<Cue> cues)
        {
            var d = new Dictionary<int, double>();
            foreach (var c in cues) { int bar = c.Text.IndexOf('|'); if (c.Text.StartsWith("#") && bar > 1) d[int.Parse(c.Text.Substring(1, bar - 1))] = c.Start; }
            return d;
        }
    }
}
