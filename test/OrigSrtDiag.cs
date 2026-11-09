using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace TextSync
{
    // OrigSrtDiag <film.mkv> <svensk.srt> <spårnr för engelska>
    // Låtsas att filmens engelska textspår är en separat SRT som inte går i takt (förskjuten, fel bildfrekvens, ett klipp),
    // kör kedjan "engelsk SRT → ljud (Whisper) → svensk SRT" och jämför med facit (det orörda textspåret).
    static class OrigSrtDiag
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string mkv = args[0], srt = args[1]; ulong no = ulong.Parse(args[2]);
            List<Cue> track;
            using (var f = new MkvFile(mkv)) track = f.ReadSubtitles(f.Tracks.First(t => t.Number == no), p => { }, null);
            track = track.OrderBy(c => c.Start).Select((c, i) => new Cue(c.Start, c.End, "{#" + i + "}" + c.Text)).ToList();
            string lang;
            var words = Audio.GetWords(mkv, 0, (p, s) => { }, CancellationToken.None, out lang);
            var speech = Audio.GetReference(mkv, new AudioRefOption { Mode = AudioMode.Speech }, (p, s) => { }, CancellationToken.None).Cues.Select(c => new Iv(c.Start, c.End)).ToList();
            Console.WriteLine("Whisper: " + words.Count + " ord, " + TextCheck.Phrases(words).Count + " fraser, språk " + lang);

            var sv = Srt.Load(srt).Select((c, i) => new Cue(c.Start, c.End, "{@" + i + "}" + c.Text)).ToList();
            var truthSv = Index(SyncEngine.Sync(track, sv, new SyncOptions(), null).Output, '@');
            var truthEn = Index(track, '#');

            {   // jämförelse: svenska direkt mot ljudet (A+B), utan engelsk text
                var ab = Audio.Combine(speech, Audio.GetReference(mkv, new AudioRefOption { Mode = AudioMode.Combined }, (p, s) => { }, CancellationToken.None).Transcript);
                var direct = Index(SyncEngine.Sync(ab, sv, new SyncOptions(), null).Output, '@');
                var eD = truthSv.Keys.Where(direct.ContainsKey).Select(k => Math.Abs(direct[k] - truthSv[k])).OrderBy(x => x).ToList();
                Console.WriteLine("Jämförelse – svenska direkt mot ljud A+B: median " + eD[eD.Count / 2].ToString("0") + " ms, inom 0,3 s " + Pct(eD, 300) + ", >1 s: " + eD.Count(x => x > 1000));
            }
            var cases = new List<Tuple<string, Func<Cue, Cue>>> {
                Tuple.Create<string, Func<Cue, Cue>>("orörd", c => c.Clone()),
                Tuple.Create<string, Func<Cue, Cue>>("+7 s", c => new Cue(c.Start + 7000, c.End + 7000, c.Text)),
                Tuple.Create<string, Func<Cue, Cue>>("25→23,976 fps −3 s", c => new Cue(c.Start * 25 / 23.976 - 3000, c.End * 25 / 23.976 - 3000, c.Text)),
                Tuple.Create<string, Func<Cue, Cue>>("+2 s, klipp vid 20:00 +4 s", c => c.Start < 1200000 ? new Cue(c.Start + 2000, c.End + 2000, c.Text) : new Cue(c.Start + 6000, c.End + 6000, c.Text)),
                Tuple.Create<string, Func<Cue, Cue>>("orörd utom 10–15 min +3 s", c => c.Start >= 600000 && c.Start < 900000 ? new Cue(c.Start + 3000, c.End + 3000, c.Text) : c.Clone()),
            };
            foreach (var cs in cases)
            {
                var broken = track.Select(cs.Item2).ToList();
                SyncResult coarse;
                var r = TextCheck.AlignToAudio(broken, words, args.Length > 3 ? null : speech, new SyncOptions(), (p, s) => { }, out coarse);
                var en = Index(r.Corrected, '#');
                var eEn = truthEn.Keys.Where(en.ContainsKey).Select(k => Math.Abs(en[k] - truthEn[k])).OrderBy(x => x).ToList();
                var svOut = Index(SyncEngine.Sync(r.Corrected, sv, new SyncOptions(), null).Output, '@');
                var eSv = truthSv.Keys.Where(svOut.ContainsKey).Select(k => Math.Abs(svOut[k] - truthSv[k])).OrderBy(x => x).ToList();
                Console.WriteLine("\n" + cs.Item1 + ":  grov synk = " + coarse.Method);
                Console.WriteLine("   " + r.Summary());
                Console.WriteLine("   engelska mot facit: median " + eEn[eEn.Count / 2].ToString("0") + " ms, inom 0,3 s " + Pct(eEn, 300) + ", >1 s: " + eEn.Count(x => x > 1000));
                Console.WriteLine("   svenska mot facit:  median " + eSv[eSv.Count / 2].ToString("0") + " ms, inom 0,3 s " + Pct(eSv, 300) + ", >1 s: " + eSv.Count(x => x > 1000));
            }
            return 0;
        }

        static string Pct(List<double> e, double lim) { return (100.0 * e.Count(x => x <= lim) / e.Count).ToString("0") + " %"; }

        static Dictionary<int, double> Index(List<Cue> cues, char tag)
        {
            var d = new Dictionary<int, double>();
            foreach (var c in cues) { int bar = c.Text.IndexOf('}'); if (c.Text.Length > 2 && c.Text[1] == tag && bar > 2) d[int.Parse(c.Text.Substring(2, bar - 2))] = c.Start; }
            return d;
        }
    }
}
