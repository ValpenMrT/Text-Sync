// Felsökning: kör kedjan "separat SRT på originalspråket → filmens ljud (Whisper) → undertexten som ska synkas" mot riktiga filer,
// som valet "Separat SRT … + Whisper-kontroll" i listan Referens, och visar vad som händer i ett tidsfönster.
//   OrigRunDiag <film.mkv> <original.srt> <undertext.srt> <från s> <till s>
// Tiderna gäller filmen. Skriver ut: den grova synkens avsnitt, Whisper-kontrollens rättade avsnitt, och i fönstret
// Whispers ord, ankarna (radens avvikelse mot Whispers ord), originaltexten efter varje steg och den synkade undertexten.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace TextSync
{
    static class OrigRunDiag
    {
        static string T(double ms) { return Srt.FormatTime(Math.Max(0, ms)).Substring(0, 12); }
        static string One(string s) { s = s.Replace("\n", " / "); return s.Length > 56 ? s.Substring(0, 56) + "…" : s; }
        static string Sec(double ms) { return (ms / 1000).ToString("+0.00;-0.00") + " s"; }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 5) { Console.WriteLine("Användning: OrigRunDiag <film.mkv> <original.srt> <undertext.srt> <från s> <till s>"); return 2; }
            string mkv = args[0];
            double from = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) * 1000, to = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) * 1000;
            // originaltexten märks med radnummer, så att samma rad går att följa genom stegen
            var orig = Markup.FixAll(Srt.Load(args[1])).OrderBy(c => c.Start).ToList();
            var origTagged = orig.Select((c, i) => new Cue(c.Start, c.End, "{#" + i + "}" + c.Text)).ToList();
            var tgt = Markup.FixAll(Srt.Load(args[2]));

            Audio.WhisperDevice = Tools.HasNvidia() && Tools.FindWhisper(true) != null ? 0 : -1;
            Console.WriteLine("Whisper körs på " + (Audio.WhisperDevice >= 0 ? "grafikkortet (NVIDIA)" : "processorn"));
            int lastP = -1;
            Action<int, string> prog = (p, s) => { if (p / 10 != lastP) { lastP = p / 10; Console.WriteLine("  " + s); } };
            var speech = Audio.GetReference(mkv, new AudioRefOption { Mode = AudioMode.Speech, AudioIndex = 0 }, prog, CancellationToken.None).Cues.Select(c => new Iv(c.Start, c.End)).ToList();
            string lang;
            var words = Audio.GetWords(mkv, 0, prog, CancellationToken.None, out lang);
            Console.WriteLine("Whisper: " + words.Count + " ord, språk " + lang + ", talavsnitt " + speech.Count);

            SyncResult coarse;
            var chk = TextCheck.AlignToAudio(origTagged, words, speech, new SyncOptions { Mode = SyncMode.Smart, DetectFramerate = true }, (p, s) => { }, out coarse);
            Console.WriteLine();
            Console.WriteLine("Grov synk av originaltexten mot ljudet: " + coarse.Method + " " + coarse.FpsText);
            foreach (var s in coarse.Segments) Console.WriteLine("  avsnitt från " + T(s.FromMs) + "  " + Sec(s.OffsetMs));
            Console.WriteLine(chk.Summary("originaltexten"));
            foreach (var r in chk.Regions) Console.WriteLine("  rättat " + T(r.FromMs) + "–" + T(r.ToMs) + "  " + Sec(r.OffsetMs) + "  " + r.Lines + " rader");
            Console.WriteLine("  rättningar som togs tillbaka (passade talet sämre): " + chk.Rejected);

            Console.WriteLine();
            Console.WriteLine("Whispers ord i fönstret:");
            var sb = new StringBuilder(); double lastT = -1e9;
            foreach (var w in words.Where(w => w.Start >= from && w.Start < to).OrderBy(w => w.Start))
            {
                if (w.Start - lastT > 1500) { if (sb.Length > 0) Console.WriteLine(sb.ToString()); sb.Clear(); sb.Append("  " + T(w.Start) + " "); }
                sb.Append(" " + w.Text.Trim()); lastT = w.End;
            }
            if (sb.Length > 0) Console.WriteLine(sb.ToString());
            Console.WriteLine("Talavsnitt (A) i fönstret:");
            foreach (var iv in speech.Where(x => x.E > from && x.S < to)) Console.WriteLine("  " + T(iv.S) + "–" + T(iv.E));

            // samma rad genom stegen: originalets tid, efter den grova synken, efter Whisper-kontrollen
            var coarseAt = Index(coarse.Output); var finalAt = Index(chk.Corrected);
            // samma grova synk utan finjusteringen per rad (SyncEngine.RefineCues), för att se vad den ändrar
            var phr = TextCheck.Phrases(words);
            var plainRef = Audio.Combine(speech, phr); if (plainRef.Count < 20) plainRef = phr;
            var plainAt = Index(SyncEngine.Sync(plainRef, origTagged, new SyncOptions { Mode = SyncMode.Advanced, DetectFramerate = true, RefineCues = false }, null).Output);
            Console.WriteLine();
            Console.WriteLine("Originaltexten i fönstret (förskjutning mot filen: styckvis utan finjustering per rad | grov synk → efter Whisper-kontrollen):");
            for (int i = 0; i < orig.Count; i++)
            {
                if (!finalAt.ContainsKey(i) || !coarseAt.ContainsKey(i)) continue;
                if (finalAt[i] < from || finalAt[i] >= to) continue;
                Console.WriteLine("  {0,4} {1}  {2,9} | {3,9} → {4,9}  {5}", i + 1, T(finalAt[i]), plainAt.ContainsKey(i) ? Sec(plainAt[i] - orig[i].Start) : "", Sec(coarseAt[i] - orig[i].Start), Sec(finalAt[i] - orig[i].Start), One(orig[i].Text));
            }
            Console.WriteLine("Ankare i fönstret (radens tid efter den grova synken, avvikelse mot Whispers ord):");
            foreach (var a in chk.Anchors.Where(a => a.Key >= from - 5000 && a.Key < to + 5000)) Console.WriteLine("  " + T(a.Key) + "  " + Sec(a.Value));

            // undertexten synkas mot den rättade originaltexten, som i programmet (utan betydelsejämförelsen)
            var refc = chk.Corrected.Select(c => new Cue(c.Start, c.End, c.Text.Substring(c.Text.IndexOf('}') + 1))).ToList();
            var r2 = SyncEngine.Sync(refc, tgt, new SyncOptions(), null);
            Console.WriteLine();
            Console.WriteLine("Undertexten mot den rättade originaltexten: " + r2.Method);
            foreach (var s in r2.Segments) Console.WriteLine("  avsnitt från " + T(s.FromMs) + "  " + Sec(s.OffsetMs));
            Console.WriteLine(Quality.Assess(refc, r2.Output, r2, true).LogText());
            // betydelsejämförelsen (inställning), om verktygen är hämtade: vilka rader den flyttar
            if (Tools.MeaningReady())
            {
                var plain = r2.Output.Select(c => c.Clone()).ToList();
                var rep = Meaning.Apply(refc, r2, (p, s) => { }, CancellationToken.None);
                Console.WriteLine(rep.Summary());
                for (int i = 0; i < r2.Output.Count && i < plain.Count; i++)
                    if (Math.Abs(r2.Output[i].Start - plain[i].Start) > 1)
                        Console.WriteLine("  flyttad rad {0,4} {1} → {2}  {3}", i + 1, T(plain[i].Start), T(r2.Output[i].Start), One(r2.Output[i].Text));
                Console.WriteLine("Efter betydelsejämförelsen: " + Quality.Assess(refc, r2.Output, r2, true).LogText());
            }
            // rader som börjar mer än 0,5 s från närmaste referensrad, i hela filmen
            var far = r2.Output.Where(c => refc.Min(x => Math.Abs(x.Start - c.Start)) > 500).ToList();
            Console.WriteLine("Rader som börjar mer än 0,5 s från närmaste referensrad: " + far.Count + " av " + r2.Output.Count);
            foreach (var c in far) Console.WriteLine("  " + T(c.Start) + "  " + One(c.Text));
            Console.WriteLine("I fönstret:");
            for (int i = 0; i < r2.Output.Count; i++)
            {
                var c = r2.Output[i];
                if (c.Start < from || c.Start >= to) continue;
                var near = refc.OrderBy(x => Math.Abs(x.Start - c.Start)).First();
                Console.WriteLine("  {0,4} {1}  {2,-58} [{3} {4}]", i + 1, T(c.Start), One(c.Text), T(near.Start), One(near.Text));
            }
            return 0;
        }

        static Dictionary<int, double> Index(List<Cue> cues)
        {
            var d = new Dictionary<int, double>();
            foreach (var c in cues) { int bar = c.Text.IndexOf('}'); if (c.Text.StartsWith("{#") && bar > 2) d[int.Parse(c.Text.Substring(2, bar - 2))] = c.Start; }
            return d;
        }
    }
}
