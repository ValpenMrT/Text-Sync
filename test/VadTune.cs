using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace TextSync
{
    // Justerar taldetekteringen (A) mot facit: filmens riktiga undertexter.
    // VadTune <film.mkv> <svensk.srt> <facit-spårnr> <wav (skapas om den saknas)>
    static class VadTune
    {
        static int Main(string[] args)
        {
            string mkv = args[0], srt = args[1], wav = args[3];
            ulong trackNo = ulong.Parse(args[2]);
            List<Cue> subRef;
            using (var f = new MkvFile(mkv)) subRef = f.ReadSubtitles(f.Tracks.First(t => t.Number == trackNo), p => { }, null);
            if (!System.IO.File.Exists(wav))
            {
                int ch; double dur; string l;
                Audio.Probe(mkv, 0, out ch, out dur, out l, CancellationToken.None);
                Audio.ExtractWav(mkv, 0, ch, dur, wav, p => { }, CancellationToken.None);
            }
            var tgt = Srt.Load(srt).Select((c, i) => new Cue(c.Start, c.End, "#" + i + "|" + c.Text)).ToList();
            var truth = Index(SyncEngine.Sync(subRef, tgt, new SyncOptions(), null).Output);
            var subIv = SyncEngine.Merge(subRef.Select(c => new Iv(c.Start, c.End)));
            double subTot = subIv.Sum(x => x.E - x.S);
            double[] db, floor;
            Audio.AnalyzeFile(wav, out db, out floor);
            double durMs = db.Length * 10.0;
            Console.WriteLine("Undertexter täcker " + (100 * subTot / durMs).ToString("0") + " % av tiden");
            Console.WriteLine("rel  abs   tal%  precision  recall   median-fel  <0,5s   >1s");
            foreach (double rel in new[] { 9.0, 12, 15, 18, 21, 24 })
                foreach (double abs in new[] { -52.0, -45, -40, -35 })
                {
                    var sp = Audio.Threshold(db, floor, rel, abs);
                    var spIv = SyncEngine.Merge(sp);
                    double tot = spIv.Sum(x => x.E - x.S);
                    if (tot < 1000) continue;
                    double ov = SyncEngine.Overlap(spIv, subIv);
                    var r = SyncEngine.Sync(sp.Select(s => new Cue(s.S, s.E, "(tal)")).ToList(), tgt, new SyncOptions(), null);
                    var got = Index(r.Output);
                    var errs = truth.Keys.Where(got.ContainsKey).Select(k => Math.Abs(got[k] - truth[k])).OrderBy(e => e).ToList();
                    Console.WriteLine(string.Format("{0,3} {1,4} {2,6:0} {3,9:0} % {4,7:0} % {5,9:0} ms {6,5:0} % {7,5}",
                        rel, abs, 100 * tot / durMs, 100 * ov / tot, 100 * ov / subTot, errs[errs.Count / 2],
                        100.0 * errs.Count(e => e < 500) / errs.Count, errs.Count(e => e > 1000)));
                }
            return 0;
        }

        static Dictionary<int, double> Index(List<Cue> cues)
        {
            var d = new Dictionary<int, double>();
            foreach (var c in cues)
            {
                int bar = c.Text.IndexOf('|');
                if (c.Text.StartsWith("#") && bar > 1) d[int.Parse(c.Text.Substring(1, bar - 1))] = c.Start;
            }
            return d;
        }
    }
}
