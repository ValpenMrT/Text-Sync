using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace TextSync
{
    // Jämför ljudsynk A, B och A+B mot facit (synk mot filmens undertextspår).
    // Användning: AudioDiag <film.mkv> <undertext.srt> <facit-spårnr>
    static class AudioDiag
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string mkv = args[0], srt = args[1];
            ulong trackNo = ulong.Parse(args[2]);
            // märk varje svensk rad med sitt nummer så att den kan hittas efter synk
            var tgt = Srt.Load(srt).Select((c, i) => new Cue(c.Start, c.End, "#" + i + "|" + c.Text)).ToList();

            List<Cue> subRef; int audioIdx = 0;
            using (var f = new MkvFile(mkv))
            {
                subRef = f.ReadSubtitles(f.Tracks.First(t => t.Number == trackNo), p => { }, null);
                var audio = f.Tracks.Where(t => t.Type == 2).ToList();
                int d = audio.FindIndex(t => t.Default); audioIdx = Math.Max(0, d);
            }
            var truth = Index(SyncEngine.Sync(subRef, tgt, new SyncOptions(), null).Output);
            Console.WriteLine("Facit: synk mot undertextspår " + trackNo + ", " + truth.Count + " rader");
            Console.WriteLine();
            Console.WriteLine(string.Format("{0,-22} {1,9} {2,9} {3,9} {4,8} {5,8} {6,7} {7,9} {8,8}",
                "Metod", "median", "90 %", "max", "<0,2 s", "<0,5 s", ">1 s", "radmatch", "tid"));

            foreach (AudioMode mode in new[] { AudioMode.Speech, AudioMode.Whisper, AudioMode.Combined })
            {
                var sw = Stopwatch.StartNew();
                var refRes = Audio.GetReference(mkv, new AudioRefOption { Mode = mode, AudioIndex = audioIdx }, (p, s) => { }, CancellationToken.None);
                double tRef = sw.Elapsed.TotalSeconds;
                var r = SyncEngine.Sync(refRes.Cues, tgt, new SyncOptions(), null);
                var got = Index(r.Output);
                var errs = truth.Keys.Where(got.ContainsKey).Select(k => Math.Abs(got[k] - truth[k])).OrderBy(e => e).ToList();
                // radmatch: andel svenska rader som överlappar referensen (samma mått som i programmet)
                int matched = r.Output.Count(c => refRes.Cues.Any(q => Math.Min(c.End, q.End) - Math.Max(c.Start, q.Start) > 0));
                string name = mode == AudioMode.Speech ? "A taldetektering" : mode == AudioMode.Whisper ? "B Whisper" : "A+B kombinerat";
                Console.WriteLine(string.Format("{0,-22} {1,7:0} ms {2,6:0} ms {3,6:0} ms {4,7:0} % {5,7:0} % {6,7} {7,8:0.0} % {8,6:0} s",
                    name, errs[errs.Count / 2], errs[(int)(errs.Count * 0.9)], errs.Last(),
                    100.0 * errs.Count(e => e < 200) / errs.Count, 100.0 * errs.Count(e => e < 500) / errs.Count, errs.Count(e => e > 1000),
                    100.0 * matched / r.Output.Count, tRef));
                Console.WriteLine("    " + r.Method + " · " + refRes.Info + (r.FpsText.Length > 0 ? " · " + r.FpsText : ""));
                if (r.Segments.Count > 1) Console.WriteLine("    avsnitt: " + string.Join("  ", r.Segments.Select(s => Srt.FormatTime(s.FromMs).Substring(0, 8) + " " + (s.OffsetMs / 1000).ToString("+0.00;-0.00"))));
                if (mode == AudioMode.Whisper)
                {
                    // hur nära ligger Whispers radstarter de riktiga engelska undertexternas?
                    var st = subRef.Select(c => c.Start).OrderBy(x => x).ToArray();
                    var d = refRes.Cues.Select(c => st.Min(x => Math.Abs(x - c.Start))).OrderBy(x => x).ToList();
                    Console.WriteLine("    Whisper-radstart mot närmaste engelska undertext: median " + d[d.Count / 2].ToString("0") + " ms, språk " + refRes.Language);
                    foreach (var c in refRes.Cues.Where(c => c.Start > 170000 && c.Start < 215000)) Console.WriteLine("      " + Srt.FormatTime(c.Start) + "  " + c.Text.Replace("\n", " / "));
                }
                if (mode == AudioMode.Combined) { var bad = errs.Count(e => e > 1000); }
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
