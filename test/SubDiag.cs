using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace TextSync
{
    // Mäter "text ur ljudet" (Subtitler) mot en riktig film: Whisper ord för ord → undertext, jämförd med filmens textspår (facit).
    // Användning: SubDiag <film.mkv> <svensk.srt eller -> <utfil.srt eller ->
    // Skriver ut: antal rader, hur nära radstarterna ligger facit, radlängder, och hur den svenska texten synkas mot den nya texten
    // jämfört med mot facit och mot ljudet (A+B).
    static class SubDiag
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string mkv = args[0], srt = args.Length > 1 && args[1] != "-" ? args[1] : null, outFile = args.Length > 2 && args[2] != "-" ? args[2] : null;
            List<Cue> truth; int audioIdx;
            using (var f = new MkvFile(mkv))
            {
                var text = f.Tracks.Where(t => t.IsSubtitle && t.IsText).ToList();
                var pick = text[MainForm.PickTextTrack(text)];
                truth = Markup.FixAll(f.ReadSubtitles(pick, p => { }, null));
                var audio = f.Tracks.Where(t => t.Type == 2).ToList();
                audioIdx = Math.Max(0, audio.FindIndex(t => t.Default));
                Console.WriteLine("Facit: " + pick + ", " + truth.Count + " rader");
            }
            Audio.WhisperDevice = Tools.HasNvidia() && Tools.FindWhisper(true) != null ? 0 : -1;
            var sw = Stopwatch.StartNew();
            string lang; int lastP = -1;
            // fjärde argumentet: en färdig ordfil från whisper-cli (-ml 1 -sow -osrt), för att jämföra olika sätt att köra Whisper
            List<Cue> words;
            if (args.Length > 3 && args[3] == "parakeet")
                words = Audio.GetParakeetWords(mkv, audioIdx, (p, s) => { if (p / 10 != lastP) { lastP = p / 10; Console.WriteLine("  " + s); } }, CancellationToken.None, out lang);
            else if (args.Length > 3) { words = Srt.LoadInOrder(args[3], 65001); lang = "(ordfil)"; }
            else words = Audio.GetTextWords(mkv, audioIdx, (p, s) => { if (p / 10 != lastP) { lastP = p / 10; Console.WriteLine("  " + s); } }, CancellationToken.None, out lang);
            Console.WriteLine("Whisper ord för ord: " + words.Count + " ord, språk " + lang + ", " + sw.Elapsed.TotalSeconds.ToString("0") + " s (" + (Audio.WhisperDevice >= 0 ? "grafikkort" : "processor") + ")");
            Console.WriteLine("De första orden: " + string.Join("|", words.Take(40).Select(w => w.Text)));

            var speechA = Audio.GetReference(mkv, new AudioRefOption { Mode = AudioMode.Speech, AudioIndex = audioIdx }, (p, s) => { }, CancellationToken.None).Cues;
            Report("Text ur ljudet utan taldetekteringen", Subtitler.FromWords(words), truth);
            bool pk = args.Length > 3 && args[3] == "parakeet";
            if (pk) Report("Parakeet utan taldetekteringen, utan försprång", Subtitler.FromWords(words, null, 0), truth);
            var subs = pk ? Subtitler.FromWords(words, null, Subtitler.ParakeetLeadMs) : Subtitler.FromWords(words, speechA);
            if (outFile != null) { Srt.Save(outFile, subs); Console.WriteLine("Sparad: " + outFile); }
            Report("Text ur ljudet", subs, truth);
            Report("Facit mot sig självt (jämförelse)", truth, truth);

            Console.WriteLine();
            Console.WriteLine("Rader 03:00–04:00, text ur ljudet:");
            foreach (var c in subs.Where(c => c.Start > 180000 && c.Start < 240000)) Console.WriteLine("  " + Srt.FormatTime(c.Start) + " → " + Srt.FormatTime(c.End) + "  " + c.Text.Replace("\n", " / "));
            Console.WriteLine("Rader 03:00–04:00, facit:");
            foreach (var c in truth.Where(c => c.Start > 180000 && c.Start < 240000)) Console.WriteLine("  " + Srt.FormatTime(c.Start) + " → " + Srt.FormatTime(c.End) + "  " + c.Text.Replace("\n", " / "));

            if (srt != null)
            {
                // den svenska texten synkad mot facit, mot den nya texten och mot ljudet (A+B)
                var tgt = Srt.Load(srt).Select((c, i) => new Cue(c.Start, c.End, "#" + i + "|" + c.Text)).ToList();
                var want = Index(SyncEngine.Sync(truth, tgt, new SyncOptions(), null).Output);
                Console.WriteLine();
                Console.WriteLine(string.Format("{0,-26} {1,9} {2,9} {3,9} {4,8} {5,8} {6,7}", "Svensk text synkad mot", "median", "90 %", "max", "<0,2 s", "<0,5 s", ">1 s"));
                Line("text ur ljudet", SyncEngine.Sync(subs, tgt, new SyncOptions(), null), want);
                var ab = Audio.GetReference(mkv, new AudioRefOption { Mode = AudioMode.Combined, AudioIndex = audioIdx }, (p, s) => { }, CancellationToken.None);
                Line("ljudet A+B", SyncEngine.Sync(ab.Cues, tgt, new SyncOptions(), null), want);
                var a = Audio.GetReference(mkv, new AudioRefOption { Mode = AudioMode.Speech, AudioIndex = audioIdx }, (p, s) => { }, CancellationToken.None);
                Line("ljudet A", SyncEngine.Sync(a.Cues, tgt, new SyncOptions(), null), want);
            }
            return 0;
        }

        static void Report(string name, List<Cue> subs, List<Cue> truth)
        {
            var st = truth.Select(c => c.Start).OrderBy(x => x).ToArray();
            var d = subs.Select(c => st.Min(x => Math.Abs(x - c.Start))).OrderBy(x => x).ToList();
            // med tecken: plus = texten ur ljudet börjar senare än facit
            var sd = subs.Select(c => { double b = st.OrderBy(x => Math.Abs(x - c.Start)).First(); return c.Start - b; }).Where(x => Math.Abs(x) < 1000).OrderBy(x => x).ToList();
            if (sd.Count > 0) Console.WriteLine("  (median med tecken, bland dem inom 1 s: " + sd[sd.Count / 2].ToString("+0;-0") + " ms)");
            var dur = subs.Select(c => c.End - c.Start).OrderBy(x => x).ToList();
            var len = subs.Select(c => c.Text.Replace("\n", " ").Length).OrderBy(x => x).ToList();
            int tooLong = subs.Count(c => c.Text.Split('\n').Length > 2 || c.Text.Split('\n').Any(l => l.Length > LineAligner.MaxChars));
            int fast = subs.Count(c => c.Text.Replace("\n", "").Length / Math.Max(0.1, (c.End - c.Start) / 1000) > 20);
            Console.WriteLine();
            Console.WriteLine(name + ": " + subs.Count + " rader");
            Console.WriteLine("  radstart mot närmaste rad i facit: median " + d[d.Count / 2].ToString("0") + " ms, 90 % " + d[(int)(d.Count * 0.9)].ToString("0") + " ms, inom 0,5 s " + (100.0 * d.Count(x => x < 500) / d.Count).ToString("0.0") + " %");
            Console.WriteLine("  längd: median " + (dur[dur.Count / 2] / 1000).ToString("0.0") + " s, kortaste " + (dur[0] / 1000).ToString("0.00") + " s, längsta " + (dur[dur.Count - 1] / 1000).ToString("0.0") + " s, kortare än 1 s: " + dur.Count(x => x < 1000));
            Console.WriteLine("  tecken: median " + len[len.Count / 2] + ", längsta " + len[len.Count - 1] + ", för långa för 2 × 42: " + tooLong + ", över 20 tecken/s: " + fast);
        }

        static void Line(string name, SyncResult r, Dictionary<int, double> want)
        {
            var got = Index(r.Output);
            var errs = want.Keys.Where(got.ContainsKey).Select(k => Math.Abs(got[k] - want[k])).OrderBy(e => e).ToList();
            Console.WriteLine(string.Format("{0,-26} {1,6:0} ms {2,6:0} ms {3,6:0} ms {4,6:0} % {5,6:0} % {6,7}",
                name, errs[errs.Count / 2], errs[(int)(errs.Count * 0.9)], errs.Last(),
                100.0 * errs.Count(e => e < 200) / errs.Count, 100.0 * errs.Count(e => e < 500) / errs.Count, errs.Count(e => e > 1000)));
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
