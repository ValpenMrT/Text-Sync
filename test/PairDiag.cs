// Felsökning: visar hur betydelsejämförelsen parar ihop raderna i en riktig film, rad för rad.
//   PairDiag <film.mkv eller referens.srt> <undertext.srt> [sökord]
// Skriver ut fördelningen av avvikelserna (radens start mot den parade referensradens start) och alla par som avviker
// mer än 0,5 s. Med ett sökord visas också raderna runt varje rad som innehåller ordet.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TextSync
{
    static class PairDiag
    {
        static string T(double ms) { return Srt.FormatTime(Math.Max(0, ms)).Substring(0, 12); }
        static string One(string s) { s = s.Replace("\n", " / "); return s.Length > 48 ? s.Substring(0, 48) + "…" : s; }

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("Användning: PairDiag <film.mkv> <undertext.srt> [sökord]"); return 2; }
            if (!Tools.MeaningReady()) { Console.WriteLine("Verktygen för betydelsejämförelsen saknas."); return 2; }
            List<Cue> refc;
            // referensen får också vara en undertext (SRT), som när filmen saknar textspår
            if (args[0].EndsWith(".srt", StringComparison.OrdinalIgnoreCase)) refc = Markup.FixAll(Srt.Load(args[0]));
            else using (var f = new MkvFile(args[0]))
            {
                var text = f.Tracks.Where(t => t.IsSubtitle && t.IsText).ToList();
                refc = Markup.FixAll(f.ReadSubtitles(text[MainForm.PickTextTrack(text)], p => { }, () => false));
            }
            var tgt = Markup.FixAll(Srt.Load(args[1]));
            var r = SyncEngine.Sync(refc, tgt, new SyncOptions(), null);
            Console.WriteLine(r.Method);
            foreach (var s in r.Segments) Console.WriteLine("  avsnitt från " + T(s.FromMs) + "  " + (s.OffsetMs / 1000).ToString("+0.00;-0.00") + " s");
            var plain = r.Output.Select(c => c.Clone()).ToList();
            var rep = Meaning.Apply(refc, r, (p, s) => { }, CancellationToken.None);
            Console.WriteLine(rep.Summary());
            Console.WriteLine(rep.LogText());
            var outp = r.Output;
            var refSorted = refc.OrderBy(c => c.Start).ToList();
            Console.WriteLine(rep.Compare() ?? "(ingen jämförelse: inget flyttat)");

            Console.WriteLine();
            Console.WriteLine("Förslag om att flytta repliker mellan rader: " + rep.Moves.Count);
            foreach (var mv in rep.Moves)
            {
                Console.WriteLine("  nu:    " + string.Join("  |  ", mv.Texts.Select(One)));
                for (int k = 0; k < mv.NewCues.Count; k++)
                    Console.WriteLine("  blir:  " + T(mv.NewCues[k].Start) + "–" + T(mv.NewCues[k].End).Substring(6) + "  " + One(mv.NewCues[k].Text) + "      [" + One(mv.Refs[k].Text) + "]");
            }

            Console.WriteLine();
            Console.WriteLine("Rader som flyttades (vanlig synk → med betydelsejämförelsen), och närmaste referensrad efter flytten:");
            for (int i = 0; i < outp.Count && i < plain.Count; i++)
            {
                if (Math.Abs(outp[i].Start - plain[i].Start) < 1) continue;
                var nr = refSorted.OrderBy(x => Math.Abs(x.Start - outp[i].Start)).First();
                Console.WriteLine("  rad {0,4} {1} → {2} ({3,6:+0.00;-0.00} s)  {4,-50} [{5} {6}]", i + 1, T(plain[i].Start), T(outp[i].Start), (outp[i].Start - plain[i].Start) / 1000, One(outp[i].Text), T(nr.Start), One(nr.Text));
            }

            // avvikelsen räknas mot raderna som de ligger efter jämförelsen
            Func<MeaningPair, double> dev = p => refc[p.Ref].Start - outp[p.Line].Start;
            var sure = rep.Pairs.Where(p => p.Z >= 4).ToList();
            Console.WriteLine();
            Console.WriteLine("Säkra par: " + sure.Count + " av " + outp.Count + " rader");
            foreach (double lim in new[] { 100.0, 200, 300, 500, 1000, 2000 })
                Console.WriteLine("  inom {0,4} ms: {1,4}  ({2:0.0} %)", lim, sure.Count(p => Math.Abs(dev(p)) <= lim), 100.0 * sure.Count(p => Math.Abs(dev(p)) <= lim) / sure.Count);

            Console.WriteLine();
            Console.WriteLine("Säkra par som avviker mer än 0,5 s:");
            foreach (var p in sure.Where(p => Math.Abs(dev(p)) > 500))
            {
                var c = outp[p.Line];
                Console.WriteLine("  rad {0,4} {1}  {2,-50} {3,7:+0.00;-0.00} s  →  {4}  [{5} rad(er)] {6}", p.Line + 1, T(c.Start), One(c.Text), dev(p) / 1000, T(refc[p.Ref].Start), p.Size, One(refc[p.Ref].Text));
            }

            if (args.Length > 2)
            {
                Console.WriteLine();
                for (int i = 0; i < outp.Count; i++)
                {
                    if (outp[i].Text.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine("Runt rad " + (i + 1) + ":");
                    for (int k = Math.Max(0, i - 3); k <= Math.Min(outp.Count - 1, i + 3); k++)
                    {
                        var p = rep.Pairs.FirstOrDefault(x => x.Line == k);
                        Console.WriteLine("  rad {0,4} {1}–{2}  {3,-50} {4}", k + 1, T(outp[k].Start), T(outp[k].End).Substring(6), One(outp[k].Text),
                            p == null ? "(inget par)" : "par: " + T(refc[p.Ref].Start) + " [" + p.Size + "] " + One(refc[p.Ref].Text) + "  z=" + p.Z.ToString("0.0"));
                    }
                    Console.WriteLine("  Referensen samma tid:");
                    foreach (var rc in refSorted.Where(x => x.End > outp[Math.Max(0, i - 3)].Start && x.Start < outp[Math.Min(outp.Count - 1, i + 3)].End))
                        Console.WriteLine("       {0}–{1}  {2}", T(rc.Start), T(rc.End).Substring(6), One(rc.Text));
                }
            }
            return 0;
        }
    }
}
