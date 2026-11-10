// Text Sync - bedömer hur säker en synk är, och tolkar avsnittsnummer i filnamn (varning för fel avsnitt)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TextSync
{
    public enum Verdict { Good = 0, Check = 1, Unsure = 2 }

    public class QualityResult
    {
        public Verdict Verdict;
        public double Matched;   // andel synkade rader som har referens samtidigt
        public double Onset;     // andel synkade rader som börjar inom 0,5 s från en referensrad
        public double Score;     // motorns överlappspoäng efter synk
        public List<Iv> Weak = new List<Iv>();   // svaga ställen (tidsintervall i synkad tid)
        public bool Sparse;      // referensen har för lite text för att synken ska kunna bedömas (t.ex. bara tvingad text)
        public string LogText()
        {
            var sv = System.Globalization.CultureInfo.InvariantCulture;
            return "besked " + Verdict + ", rader med referens " + (Matched * 100).ToString("0.0", sv) + " %, starter inom 0,5 s " +
                (Onset * 100).ToString("0.0", sv) + " %, poäng " + Score.ToString("0.00", sv) + ", svaga ställen " + Weak.Count;
        }
    }

    public static class Quality
    {
        // Gränserna är satta mot facit i testerna och mot Blacklist S04E01 (se beslutsloggen 2026-11-10).
        public const double OnsetTol = 500;          // ms
        // Blacklist: rätt text 99,5 % / 83 % / 0,92. Samma text baklänges i tiden (som ett annat avsnitt): 79 % / 35 % / 0,62.
        public const double GoodMatched = 0.95, GoodOnset = 0.65;
        public const double UnsureOnset = 0.45, UnsureMatched = 0.90, UnsureScore = 0.35;
        const double WeakWindow = 120000;            // svaga ställen bedöms i fönster om 2 minuter
        const double WeakOnset = 0.45;               // ett fönster är svagt om färre än så börjar i takt
        const int WeakMinLines = 8;

        // textRef = referensen är riktig text (textspår, SRT). Mot ljud (taldetektering) börjar raderna inte lika exakt samtidigt,
        // så där bedöms bara överlappet.
        public static QualityResult Assess(List<Cue> refc, List<Cue> synced, SyncResult r, bool textRef)
        {
            var q = new QualityResult { Score = r != null ? r.ScoreAfter : 0 };
            var rs = refc.OrderBy(c => c.Start).ToList();
            double[] starts = rs.Select(c => c.Start).ToArray();
            var lines = synced.Where(c => c.End > 0).OrderBy(c => c.Start).ToList();
            if (lines.Count == 0 || rs.Count == 0) { q.Verdict = Verdict.Unsure; return q; }
            var near = new bool[lines.Count];
            int matched = 0, onset = 0, ri = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var c = lines[i];
                while (ri < rs.Count && rs[ri].End < c.Start) ri++;
                bool m = false;
                for (int j = Math.Max(0, ri - 2); j < rs.Count && rs[j].Start < c.End; j++)
                    if (Math.Min(c.End, rs[j].End) - Math.Max(c.Start, rs[j].Start) > 0) { m = true; break; }
                if (m) matched++;
                int k = Array.BinarySearch(starts, c.Start); if (k < 0) k = ~k;
                double d = Math.Min(k < starts.Length ? Math.Abs(starts[k] - c.Start) : double.MaxValue, k > 0 ? Math.Abs(starts[k - 1] - c.Start) : double.MaxValue);
                near[i] = textRef ? d <= OnsetTol : m;
                if (d <= OnsetTol) onset++;
            }
            q.Matched = matched / (double)lines.Count;
            q.Onset = onset / (double)lines.Count;

            // svaga ställen: fönster där få rader börjar i takt med referensen (t.ex. ett klipp som synken missade)
            double weakLimit = textRef ? WeakOnset : 0.6;
            for (int a = 0; a < lines.Count; )
            {
                int b = a; int ok = 0;
                while (b < lines.Count && lines[b].Start < lines[a].Start + WeakWindow) { if (near[b]) ok++; b++; }
                if (b - a >= WeakMinLines && ok < weakLimit * (b - a))
                {
                    var iv = new Iv(lines[a].Start, lines[b - 1].End);
                    if (q.Weak.Count > 0 && q.Weak[q.Weak.Count - 1].E >= iv.S - 1000) q.Weak[q.Weak.Count - 1] = new Iv(q.Weak[q.Weak.Count - 1].S, iv.E);
                    else q.Weak.Add(iv);
                }
                a = b;
            }

            // gles referens (färre än hälften så många rader, samma gräns som för förslagen): för lite att bedöma mot
            q.Sparse = textRef && rs.Count < 0.5 * lines.Count;
            if (q.Sparse) { q.Weak.Clear(); q.Verdict = Verdict.Check; return q; }
            bool notes = r != null && r.Notes.Count > 0;
            if (q.Score < UnsureScore || (textRef && q.Onset < UnsureOnset && q.Matched < UnsureMatched)) q.Verdict = Verdict.Unsure;
            else if (q.Matched >= GoodMatched && (!textRef || q.Onset >= GoodOnset) && q.Weak.Count == 0 && !notes) q.Verdict = Verdict.Good;
            else q.Verdict = Verdict.Check;
            return q;
        }
    }

    // Säsong och avsnitt ur ett filnamn: S04E01, s4e1, 4x01, "Season 4 Episode 1", "Säsong 4 Avsnitt 1"
    public class EpisodeId
    {
        public int Season, Episode;
        public override string ToString() { return "säsong " + Season + ", avsnitt " + Episode; }
        public bool Same(EpisodeId o) { return o != null && o.Season == Season && o.Episode == Episode; }

        static readonly Regex[] Patterns = {
            new Regex(@"(?<![a-z0-9])s(\d{1,2})[ ._-]?e(\d{1,3})(?!\d)", RegexOptions.IgnoreCase),
            new Regex(@"(?<![a-z0-9])(?:season|säsong)[ ._-]?(\d{1,2})[ ._-]*(?:episode|avsnitt|ep)[ ._-]?(\d{1,3})(?!\d)", RegexOptions.IgnoreCase),
            new Regex(@"(?<![a-z0-9])(\d{1,2})x(\d{2,3})(?![0-9a-z])", RegexOptions.IgnoreCase),   // inte 1920x1080 eller x264
        };

        public static EpisodeId Parse(string path)
        {
            string name;
            try { name = System.IO.Path.GetFileNameWithoutExtension(path ?? ""); } catch { return null; }
            foreach (var rx in Patterns)
            {
                var m = rx.Match(name);
                if (m.Success) return new EpisodeId { Season = int.Parse(m.Groups[1].Value), Episode = int.Parse(m.Groups[2].Value) };
            }
            return null;
        }

        // Svensk varningstext om filnamnen pekar på olika avsnitt, annars null
        // what = vad den andra filen är ("Texten", "Undertexten på originalspråket"), refWhat = "filmen" eller "referensen"
        public static string Mismatch(string refPath, string tgtPath, string what, string refWhat)
        {
            EpisodeId a = Parse(refPath), b = Parse(tgtPath);
            if (a == null || b == null || a.Same(b)) return null;
            if (a.Season == b.Season) return what + " verkar höra till avsnitt " + b.Episode + ", men " + refWhat + " är avsnitt " + a.Episode + ".";
            return what + " verkar höra till " + b + ", men " + refWhat + " är " + a + ".";
        }

        // Svensk varningstext om texterna är mycket olika långa (annan film eller annan version), annars null.
        // Gränsen är generös (10 minuter och 30 %), så att förtexter och eftertexter utan text inte ger någon varning.
        // Glesa texter (t.ex. bara tvingad text) bedöms inte.
        public static string LengthMismatch(List<Cue> refc, List<Cue> tgt, string refWhat)
        {
            if (refc == null || tgt == null || refc.Count < 50 || tgt.Count < 50) return null;
            double a = refc.Max(c => c.End), b = tgt.Max(c => c.End);
            double diff = Math.Abs(a - b), longer = Math.Max(a, b);
            if (diff < 600000 || diff < 0.3 * longer) return null;
            return "Texten slutar efter " + Math.Round(b / 60000) + " minuter, men " + refWhat + " efter " + Math.Round(a / 60000) + " minuter.";
        }
    }
}
