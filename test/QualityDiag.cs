// Felsökning: mäter beskedet om hur säker synken är mot en riktig film.
//   QualityDiag <film.mkv> <undertext.srt> <spårnr>
// Skriver ut måtten för rätt text, för samma text baklänges i tiden (beter sig som ett annat avsnitt)
// och för texten med ett klipp som synken inte får rätta (svagt ställe).
using System;
using System.Collections.Generic;
using System.Linq;

namespace TextSync
{
    static class QualityDiag
    {
        static void Show(string name, List<Cue> refc, List<Cue> tgt, SyncMode mode)
        {
            var r = SyncEngine.Sync(refc, tgt, new SyncOptions { Mode = mode }, null);
            var q = Quality.Assess(refc, r.Output, r, true);
            Console.WriteLine("{0,-44} {1}  ({2})", name, q.LogText(), r.Method);
            foreach (var w in q.Weak.Take(5)) Console.WriteLine("      svagt ställe " + Srt.FormatTime(w.S) + " – " + Srt.FormatTime(w.E));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length < 3) { Console.WriteLine("Användning: QualityDiag <film.mkv> <undertext.srt> <spårnr>"); return 2; }
            List<Cue> refc;
            using (var f = new MkvFile(args[0]))
            {
                var t = f.Tracks.First(x => x.Number == ulong.Parse(args[2]));
                refc = f.ReadSubtitles(t, p => { }, () => false);
            }
            var tgt = Srt.Load(args[1]);
            Console.WriteLine("Referens: " + refc.Count + " rader, undertext: " + tgt.Count + " rader");
            double T = tgt.Max(c => c.End);
            Show("Rätt text", refc, tgt, SyncMode.Smart);
            Show("Rätt text, förskjuten +7 s", refc, tgt.Select(c => new Cue(c.Start + 7000, c.End + 7000, c.Text)).ToList(), SyncMode.Smart);
            Show("Baklänges (som ett annat avsnitt)", refc, tgt.Select(c => new Cue(T - c.End, T - c.Start, c.Text)).OrderBy(c => c.Start).ToList(), SyncMode.Smart);
            Show("Baklänges, avancerad", refc, tgt.Select(c => new Cue(T - c.End, T - c.Start, c.Text)).OrderBy(c => c.Start).ToList(), SyncMode.Advanced);
            Show("Halvorna omkastade", refc, tgt.Select(c => c.Start < T / 2 ? new Cue(c.Start + T / 2, c.End + T / 2, c.Text) : new Cue(c.Start - T / 2, c.End - T / 2, c.Text)).OrderBy(c => c.Start).ToList(), SyncMode.Smart);
            Show("Klipp på 4 s i 3 minuter, enkel synk", refc, tgt.Select(c => c.Start > 600000 && c.Start < 780000 ? new Cue(c.Start + 4000, c.End + 4000, c.Text) : c.Clone()).ToList(), SyncMode.Simple);
            return 0;
        }
    }
}
