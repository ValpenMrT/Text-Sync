// Text Sync - gör en undertext av det Whisper hörde ord för ord (Audio.GetWords): rader som går att läsa,
// delade vid meningsslut, pauser och byte av talare, med tider efter talet. Används när filmen saknar text helt.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TextSync
{
    // Val i referenslistan: texten som Whisper hör i filmens ljud, som en riktig undertext. Till skillnad från Ljud A, B och A+B
    // är referensen då en text, så förslag om radindelning och betydelsejämförelsen fungerar.
    public class SpeechTextOption
    {
        public int AudioIndex;
        public override string ToString() { return "Ljud C – text ur ljudet med Whisper (nytt, ger radindelning)"; }
    }

    public static class Subtitler
    {
        // Gränserna är satta mot Blacklist S04E01, där filmens engelska textspår är facit (test/SubDiag.cs)
        public const int MaxChars = LineAligner.MaxChars * LineAligner.MaxLines;   // 2 rader à 42 tecken
        public static double MaxDurMs = 6000;    // längsta undertext
        public static double MinDurMs = 1000;    // kortaste, om nästa rad inte kommer tidigare
        public static double PauseMs = 900;      // en så lång paus mellan två ord börjar alltid en ny undertext
        public static double GapMs = 80;         // minsta lucka till nästa undertext
        public static double LingerMs = 300;     // texten står kvar så här länge efter sista ordet, om det finns plats
        public static double LeadMs = 200;       // texten visas så här långt före första ordet, som i vanliga undertexter
        public static double CharsPerSec = 17;   // läshastighet: texten står kvar minst så länge, om det finns plats

        static readonly Regex Noise = new Regex(@"\[[^\]]*\]|\([^)]*\)|[♪♫]|\*[^*]*\*", RegexOptions.Compiled);

        // Ett ord med egen tid. Dash = Whisper skrev ett replikstreck före ordet (en ny talare börjar).
        public class Word
        {
            public double Start, End; public string Text; public bool Dash;
        }

        // Whisper skriver ljud som [MUSIC] och (laughs): de är inte tal och tas bort
        public static string Clean(string t)
        {
            return Regex.Replace(Noise.Replace(t ?? "", " "), @"\s+", " ").Trim();
        }

        static bool IsDash(string t) { return t == "-" || t == "–" || t == "—"; }

        static bool EndsSentence(string w)
        {
            string s = w.TrimEnd('"', '\'', '”', '’', ')');
            return s.Length > 0 && ".?!…".IndexOf(s[s.Length - 1]) >= 0 && !s.EndsWith("Mr.") && !s.EndsWith("Mrs.") && !s.EndsWith("Dr.") && !s.EndsWith("Ms.");
        }

        static bool EndsClause(string w) { return w.Length > 0 && ",;:".IndexOf(w[w.Length - 1]) >= 0; }

        // Orden i tidsordning. Ett ensamt streck före ett ord märker ordet som början på en ny replik.
        // Andra skiljetecken som står för sig, och ändelser som "'s", hör till ordet före.
        public static List<Word> Words(IEnumerable<Cue> raw)
        {
            var w = new List<Word>();
            bool dash = false;
            double prevStart = 0;
            // Ordningen i Whispers utdata är den rätta: tiderna kan gå lite omlott, så orden sorteras inte efter tid
            foreach (var x0 in raw)
            {
                // sista ordet före en tystnad får ofta hela tystnaden som längd: ett ord är aldrig så långt
                double st = Math.Max(x0.Start, prevStart);
                var x = new Cue(st, Math.Max(st + 50, Math.Min(x0.End, st + 400 + 60 * (x0.Text ?? "").Trim().Length)), x0.Text);
                prevStart = st;
                string t = Clean(x.Text);
                if (t.Length == 0) continue;
                if (IsDash(t)) { dash = true; continue; }
                bool punctOnly = !t.Any(char.IsLetterOrDigit);
                bool suffix = t[0] == '\'' || t[0] == '’';   // "'s", "'t"
                if (w.Count > 0 && (punctOnly || suffix) && !dash && x.Start - w[w.Count - 1].End < 500)
                {
                    w[w.Count - 1].Text += t;
                    w[w.Count - 1].End = Math.Max(w[w.Count - 1].End, x.End);
                    continue;
                }
                if (punctOnly) continue;
                // "-Hello" i ett stycke: strecket hör till talarbytet, inte till ordet
                if (t.Length > 1 && IsDash(t.Substring(0, 1)) && char.IsLetterOrDigit(t[1])) { dash = true; t = t.Substring(1); }
                w.Add(new Word { Start = x.Start, End = Math.Max(x.End, x.Start + 50), Text = t, Dash = dash });
                dash = false;
            }
            MoveDangling(w);
            return w;
        }

        // Ett ord som börjar en ny mening men ligger före en lång paus ("He's gone. As … you well know") är tidsatt för tidigt:
        // Whisper lägger det direkt efter meningen före. Det sägs efter pausen, tillsammans med resten av sin mening.
        // Görs också efter SnapToSpeech, som kan ha öppnat en sådan paus.
        public static void MoveDangling(List<Word> w)
        {
            for (int k = 0; k + 1 < w.Count; k++)
            {
                bool startsSentence = k == 0 || EndsSentence(w[k - 1].Text);
                double gapAfter = w[k + 1].Start - w[k].End, gapBefore = k == 0 ? double.MaxValue : w[k].Start - w[k - 1].End;
                if (startsSentence && !EndsSentence(w[k].Text) && gapAfter >= PauseMs && gapBefore < gapAfter)
                {
                    double len = Math.Min(w[k].End - w[k].Start, 400);
                    w[k].End = w[k + 1].Start; w[k].Start = w[k].End - len;
                }
            }
        }

        // Texten för orden a..b som en undertext: en talare ställs upp på högst två rader, två talare får var sin rad med replikstreck.
        // null = får inte plats (för långt, eller fler än två talare).
        public static string CueText(List<Word> w, int a, int b)
        {
            int split = -1;
            for (int k = a + 1; k <= b; k++)
                if (w[k].Dash) { if (split >= 0) return null; split = k; }
            if (split < 0) return LineAligner.Layout(Join(w, a, b));
            string x = "- " + Join(w, a, split - 1), y = "- " + Join(w, split, b);
            return x.Length <= LineAligner.MaxChars && y.Length <= LineAligner.MaxChars ? x + "\n" + y : null;
        }

        static string Join(List<Word> w, int a, int b)
        {
            return string.Join(" ", w.Skip(a).Take(b - a + 1).Select(x => x.Text));
        }

        public static List<Cue> FromWords(IEnumerable<Cue> raw) { return FromWords(raw, null); }

        // Whisper lägger ibland första ordet efter en tystnad för tidigt, mitt i tystnaden. Taldetekteringen (A) vet när talet börjar:
        // ord som ligger utanför allt tal flyttas fram till nästa talavsnitt, om det börjar inom MaxSnapMs. Orden behåller sin ordning.
        public static double MaxSnapMs = 8000;
        public static void SnapToSpeech(List<Word> w, IList<Cue> speech)
        {
            if (speech == null || speech.Count == 0) return;
            var sp = speech.OrderBy(c => c.Start).ToList();
            int k = 0;
            double floor = 0;   // tidigaste start för nästa ord: efter ett flyttat ord läggs orden efter varandra, inte på samma tid
            foreach (var x in w)
            {
                double len = x.End - x.Start;
                bool moved = false;
                if (x.Start < floor) { x.Start = floor; x.End = floor + len; moved = true; }
                while (k < sp.Count && sp[k].End < x.Start) k++;
                // utanför tal (med 150 ms marginal), och nästa tal börjar snart: ordet sägs där.
                // (En marginal efter talavsnittet före provades 2026-11-13 och gav sämre synk: 87 % mot 94 % inom 0,5 s.)
                if (k < sp.Count && sp[k].Start - 150 > x.Start && sp[k].Start - x.Start <= MaxSnapMs)
                { x.Start = sp[k].Start; x.End = x.Start + len; moved = true; }
                floor = moved ? x.Start + Math.Min(len, 300) : x.Start;
            }
        }

        // Orden blir undertexter. Varje undertext börjar strax före sitt första ord.
        // speech = talavsnitten från taldetekteringen (A), eller null.
        public static List<Cue> FromWords(IEnumerable<Cue> raw, IList<Cue> speech)
        {
            var w = Words(raw);
            SnapToSpeech(w, speech);
            if (speech != null && speech.Count > 0) MoveDangling(w);
            var res = new List<Cue>();
            var firstWord = new List<double>(); var lastWord = new List<double>();
            int i = 0, n = w.Count;
            while (i < n)
            {
                int bestJ = i; double bestScore = double.NegativeInfinity; string bestText = w[i].Text;
                int len = 0;
                for (int j = i; j < n; j++)
                {
                    if (j > i && w[j].Start - w[j - 1].End >= PauseMs) break;
                    len += w[j].Text.Length + (j > i ? 1 : 0);
                    if (j > i && (len > MaxChars || w[j].End - w[i].Start > MaxDurMs)) break;
                    string text = j == i ? w[j].Text : CueText(w, i, j);
                    if (text == null) continue;   // går inte att ställa upp på två rader
                    double gap = j + 1 < n ? w[j + 1].Start - w[j].End : PauseMs;
                    bool nextSpeaker = j + 1 < n && w[j + 1].Dash;
                    double score = (EndsSentence(w[j].Text) ? 4 : EndsClause(w[j].Text) ? 1.5 : 0)
                        + (nextSpeaker ? 1.5 : 0)
                        + Math.Min(gap, PauseMs) / 250
                        + 2.0 * len / MaxChars
                        - (len < 12 && gap < 400 && !nextSpeaker && !EndsSentence(w[j].Text) ? 2 : 0)   // inte ett ensamt kort ord mitt i en mening
                        - (j > i && EndsSentence(w[j - 1].Text) && !EndsSentence(w[j].Text) && !EndsClause(w[j].Text) ? 3 : 0);   // "He's gone. As": sluta vid punkten
                    if (score > bestScore) { bestScore = score; bestJ = j; bestText = text; }
                }
                res.Add(new Cue(w[i].Start, w[bestJ].End, bestText));
                firstWord.Add(w[i].Start); lastWord.Add(w[bestJ].End);
                i = bestJ + 1;
            }
            // tider: texten visas strax före första ordet, står kvar en stund efter det sista och så länge att den hinner läsas,
            // men går aldrig in i undertexten före eller efter
            for (int k = 0; k < res.Count; k++)
            {
                var c = res[k];
                double prevEnd = k > 0 ? res[k - 1].End + GapMs : 0;
                c.Start = Math.Max(Math.Min(prevEnd, firstWord[k]), firstWord[k] - LeadMs);
                double want = Math.Max(MinDurMs, c.Text.Replace("\n", " ").Length / CharsPerSec * 1000);
                double end = Math.Max(lastWord[k] + LingerMs, c.Start + want);
                if (k + 1 < res.Count) end = Math.Min(end, firstWord[k + 1] - LeadMs - GapMs);
                c.End = Math.Max(lastWord[k], end);
                if (k + 1 < res.Count && c.End > firstWord[k + 1] - 1) c.End = Math.Max(c.Start + 1, firstWord[k + 1] - 1);
            }
            return res;
        }
    }
}
