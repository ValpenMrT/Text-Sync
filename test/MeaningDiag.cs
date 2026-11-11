// Felsökning: mäter betydelsejämförelsen (Meaning.cs) mot en riktig film. Kräver att llama.cpp och bge-m3 är hämtade.
//   MeaningDiag <film.mkv> <undertext.srt>
// 1) Vanlig synk mot filmens textspår, sedan betydelsejämförelsen: vad den flyttar, och hur lång tid det tar.
// 2) Den synkade texten förstörs med flit (delar flyttas), och jämförelsen ska lägga tillbaka dem. Facit = den synkade texten.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace TextSync
{
    static class MeaningDiag
    {
        static string T(double ms) { return Srt.FormatTime(Math.Max(0, ms)).Substring(0, 12); }
        static string One(string s) { s = s.Replace("\n", " / "); return s.Length > 60 ? s.Substring(0, 60) + "…" : s; }

        static Cue Nearest(List<Cue> refc, double t)
        {
            Cue best = null;
            foreach (var c in refc) if (best == null || Math.Abs(c.Start - t) < Math.Abs(best.Start - t)) best = c;
            return best;
        }

        // Flyttar raderna i [from, to) med shift ms och ser om jämförelsen lägger tillbaka dem
        static void Broken(string name, List<Cue> refc, List<Cue> facit, double from, double to, double shift)
        {
            var broken = facit.Select(c => c.Clone()).ToList();
            int n = 0;
            foreach (var c in broken) if (c.Start >= from && c.Start < to) { c.Start += shift; c.End += shift; n++; }
            var r = new SyncResult { Output = broken };
            var rep = Meaning.Apply(refc, r, (p, s) => { }, CancellationToken.None);
            int restored = 0, touched = 0; double worst = 0;
            for (int i = 0; i < facit.Count; i++)
            {
                bool inside = facit[i].Start >= from && facit[i].Start < to;
                double err = Math.Abs(r.Output[i].Start - facit[i].Start);
                if (inside) { if (err < 300) restored++; }
                else if (err > 1) { touched++; if (err > worst) worst = err; }
            }
            Console.WriteLine("{0,-46} tillbaka inom 0,3 s: {1} av {2}   andra rader ändrade: {3}{4}", name, restored, n, touched, touched > 0 ? " (som mest " + worst.ToString("0") + " ms)" : "");
            Console.WriteLine("      " + rep.Summary());
            Console.WriteLine("      " + rep.LogText());
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("Användning: MeaningDiag <film.mkv> <undertext.srt>"); return 2; }
            if (!Tools.MeaningReady()) { Console.WriteLine("Verktygen saknas: kryssa i \"Jämför också vad raderna betyder\" i Inställningar och synka en gång, så hämtas de."); return 2; }
            // tredje argumentet: hur många referensrader i följd en rad får paras med (1 = bara rad mot rad), för att jämföra
            int mg;
            if (args.Length > 2 && int.TryParse(args[2], out mg)) Meaning.MaxGroup = mg;
            // fjärde argumentet: vad ett byte av nivå kostar (Meaning.JumpCost), för att prova gränsen
            double jc;
            if (args.Length > 3 && double.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out jc)) Meaning.JumpCost = jc;
            Console.WriteLine("En rad får paras med upp till " + Meaning.MaxGroup + " referensrader i följd, byte av nivå kostar " + Meaning.JumpCost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            List<Cue> refc;
            using (var f = new MkvFile(args[0]))
            {
                var text = f.Tracks.Where(t => t.IsSubtitle && t.IsText).ToList();
                var pick = text[MainForm.PickTextTrack(text)];
                Console.WriteLine("Referens: " + pick);
                refc = Markup.FixAll(f.ReadSubtitles(pick, p => { }, () => false));
            }
            var tgt = Markup.FixAll(Srt.Load(args[1]));
            Console.WriteLine("Referens: " + refc.Count + " rader, undertext: " + tgt.Count + " rader");

            var r = SyncEngine.Sync(refc, tgt, new SyncOptions(), null);
            var facit = r.Output.Select(c => c.Clone()).ToList();
            Console.WriteLine("Före:  " + Quality.Assess(refc, r.Output, r, true).LogText() + "  (" + r.Method + ")");
            var sw = Stopwatch.StartNew();
            var rep = Meaning.Apply(refc, r, (p, s) => { }, CancellationToken.None);
            Console.WriteLine("Första körningen (utan cache om texten är ny): " + sw.ElapsedMilliseconds + " ms");
            Console.WriteLine("Efter: " + Quality.Assess(refc, r.Output, r, true).LogText());
            Console.WriteLine(rep.Summary());
            Console.WriteLine(rep.LogText());
            for (int i = 0; i < facit.Count; i++)
                if (Math.Abs(r.Output[i].Start - facit[i].Start) > 1)
                {
                    var nr = Nearest(refc, r.Output[i].Start);
                    Console.WriteLine("  rad {0,4}  {1} → {2}  {3}", i + 1, T(facit[i].Start), T(r.Output[i].Start), One(r.Output[i].Text));
                    Console.WriteLine("            referens {0}  {1}", T(nr.Start), One(nr.Text));
                }

            Console.WriteLine();
            Console.WriteLine("Förstörd med flit (facit = synken ovan, utan betydelsejämförelsen):");
            sw.Restart();
            Broken("+1,0 s i en minut (10:00–11:00)", refc, facit, 600000, 660000, 1000);
            Console.WriteLine("      (med cache: " + sw.ElapsedMilliseconds + " ms)");
            Broken("-1,5 s i 25 s (20:00–20:25)", refc, facit, 1200000, 1225000, -1500);
            Broken("+0,7 s i tre minuter (15:00–18:00)", refc, facit, 900000, 1080000, 700);
            Broken("+3,0 s från 30:00 till slutet", refc, facit, 1800000, double.MaxValue, 3000);
            Broken("-0,3 s i två minuter (25:00–27:00)", refc, facit, 1500000, 1620000, -300);
            Broken("+8,0 s i 40 s (5:00–5:40)", refc, facit, 300000, 340000, 8000);
            Broken("Ingenting förstört", refc, facit, 0, 0, 0);
            return 0;
        }
    }
}
