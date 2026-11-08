using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TextSync
{
    static class Diag
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string mkvPath = args[0], srtPath = args[1];
            double from = double.Parse(args[2]) * 1000, to = double.Parse(args[3]) * 1000;
            List<Cue> refc;
            using (var f = new MkvFile(mkvPath))
            {
                foreach (var t in f.Tracks) Console.WriteLine("  track " + t.Number + " type " + t.Type + " " + t.Codec + " " + t.Language + " '" + t.Name + "' forced=" + t.Forced);
                var tr = f.Tracks.First(t => t.Number == ulong.Parse(args[4]));
                refc = f.ReadSubtitles(tr, p => { }, null);
            }
            var tgt = Srt.Load(srtPath);
            Console.WriteLine("ref " + refc.Count + " cues, tgt " + tgt.Count);
            var r = SyncEngine.Sync(refc, tgt, new SyncOptions(), null);
            Console.WriteLine(r.Method + " score " + r.ScoreBefore.ToString("0.00") + "->" + r.ScoreAfter.ToString("0.00") + " " + r.FpsText);
            foreach (var s in r.Segments) Console.WriteLine("  seg " + Srt.FormatTime(s.FromMs) + " " + s.OffsetMs.ToString("0"));
            var rs = refc.Select(c => c.Start).OrderBy(x => x).ToArray();
            var dists = r.Output.Select(c => rs.Min(x => Math.Abs(x - c.Start))).OrderBy(x => x).ToList();
            Console.WriteLine("start-to-nearest-ref: median " + dists[dists.Count / 2].ToString("0") + " ms, <300ms " + (100.0 * dists.Count(x => x < 300) / dists.Count).ToString("0.0") + "%, >1s " + dists.Count(x => x > 1000));
            foreach (var c in r.Output)
                if (!refc.Any(q => Math.Min(c.End, q.End) - Math.Max(c.Start, q.Start) > 0))
                    Console.WriteLine("NO-REF " + Srt.FormatTime(c.Start) + "  " + c.Text.Replace("\n", " / "));
            // onset-kurva för mål-repliker i [from,to] (original tid * skala)
            double scale = r.Scale;
            var part = tgt.Where(c => c.Start * scale > from - 20000 && c.Start * scale < to - 20000).ToList();
            Console.WriteLine("onset curve for " + part.Count + " cues (orig*scale), offsets 0..14s:");
            var curve = new List<Tuple<double, double, double>>();
            for (double d = 0; d <= 14000; d += 50)
            {
                double on = 0, ov = 0;
                foreach (var c in part)
                {
                    double s = c.Start * scale + d, e = c.End * scale + d;
                    double best = rs.Min(x => Math.Abs(x - s));
                    if (best < 300) on += 1 - best / 300;
                    foreach (var q in refc) { double o = Math.Min(e, q.End) - Math.Max(s, q.Start); if (o > 0) ov += o; }
                }
                curve.Add(Tuple.Create(d, on / part.Count, ov / part.Sum(c => (c.End - c.Start) * scale)));
            }
            foreach (var t in curve.OrderByDescending(t => t.Item2 + t.Item3).Take(12))
                Console.WriteLine("  d=" + t.Item1 + " onset=" + t.Item2.ToString("0.00") + " overlap=" + t.Item3.ToString("0.00"));
            Console.WriteLine("---- REF");
            foreach (var c in refc.Where(c => c.End > from && c.Start < to)) Console.WriteLine(Srt.FormatTime(c.Start) + "-" + Srt.FormatTime(c.End) + "  " + c.Text.Replace("\n", " / "));
            Console.WriteLine("---- SYNCED (orig time)");
            for (int i = 0; i < r.Output.Count; i++)
            {
                var c = r.Output[i];
                if (c.End > from && c.Start < to)
                {
                    var o = tgt.First(x => x.Text == c.Text);
                    Console.WriteLine((i + 1) + "  " + Srt.FormatTime(c.Start) + "-" + Srt.FormatTime(c.End) + "  (orig " + Srt.FormatTime(o.Start) + ")  " + c.Text.Replace("\n", " / "));
                }
            }
            return 0;
        }
    }
}
