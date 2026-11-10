// Text Sync - tar bort text för hörselskadade: ljudbeskrivningar ([MUSIK], (skrattar), ♪) och talarnamn (JOHN:)
// Grundregel: en rad som inte innehåller något av mönstren lämnas exakt som den är.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TextSync
{
    public static class Sdh
    {
        // taggar (<i>, {\an8}), replikstreck och blanksteg som får stå före eller efter en ljudbeskrivning
        const string Lead = @"(?:<[^>]+>|\{[^}]*\}|[-–—]|\s)*";
        const string Trail = @"(?:</[^>]+>|\s)*";
        static readonly Regex Brackets = new Regex(@"\[[^\[\]]*\]");                                   // [MUSIK] – alltid ljudbeskrivning, även över flera rader
        static readonly Regex WholeParen = new Regex(@"^" + Lead + @"\([^()]*\)" + Trail + "$");       // hela texten eller hela raden är en parentes
        static readonly Regex ParenStart = new Regex(@"^(" + Lead + @")\([^()]*\)");                   // (skrattar) Det var kul
        static readonly Regex ParenCaps = new Regex(@"\([^()\p{Ll}]*\)");                              // (DÖRR SLÅR IGEN) var som helst på raden
        // ljudbeskrivning sist på raden efter en hel mening: Vänta! (springer)   <i>Vänta!</i> (springer)
        static readonly Regex ParenEnd = new Regex(@"(?<=[.!?…](?:</[^>]+>|[""”'’])*)\s*\([^()]*\)(" + Trail + ")$");
        // talarnamn i versaler först på raden: JOHN:  - MARIA:  MAN 2:  JOHN (skrattar):   (inte "12:30", "Klockan: 12" eller "FBI är här")
        static readonly Regex Speaker = new Regex(@"^(" + Lead + @")(\p{Lu}[\p{Lu}\d .'’\-]{1,24}?)\s*(?:\([^()]*\))?:(?=\s|<|$)\s*");
        // versaler före kolon som inte är talarnamn
        static readonly HashSet<string> NotSpeakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "OBS", "PS", "P.S", "P.S.", "OK", "NB", "N.B", "N.B.", "DAG", "ÅR", "KAPITEL", "DEL", "AKT", "SCEN", "NOTE", "DAY", "YEAR", "CHAPTER", "PART", "TID", "PLATS", "KL", "KL." };
        static readonly Regex EmptyTags = new Regex(@"<(\w+)[^>]*>\s*</\1>");
        static readonly Regex Spaces = new Regex(@"[ \t]{2,}");
        static readonly Regex HasWord = new Regex(@"[\p{L}\p{Nd}]");
        static readonly Regex Tags = new Regex(@"<[^>]+>|\{[^}]*\}");
        static readonly Regex Dash = new Regex(@"^((?:<[^>]+>|\{[^}]*\})*)\s*[-–—]\s*");
        static readonly Regex SentenceEnd = new Regex(@"[.!?…](?:</[^>]+>|[""”'’]|\s)*$");

        // Namnet i ett möjligt talarnamn först på raden ("JOHN" i "- JOHN: Hej"), annars null
        public static string SpeakerName(string line)
        {
            var m = Speaker.Match(line ?? "");
            if (!m.Success) return null;
            string name = m.Groups[2].Value.Trim();
            // "DAG 3", "ÅR 1987": första ordet avgör
            string first = name.Split(' ')[0];
            return NotSpeakers.Contains(name) || NotSpeakers.Contains(first) ? null : name;
        }

        public static string Clean(string text) { return Clean(text, null); }

        // En cues text utan text för hörselskadade. null = hela raden ska tas bort.
        // speakers = talarnamn som får tas bort (namn som återkommer i filen). null = alla som ser ut som talarnamn.
        public static string Clean(string text, HashSet<string> speakers)
        {
            string all = (text ?? "").Replace("\r\n", "\n");
            if (WholeParen.IsMatch(all.Replace("\n", " "))) return null;   // (skrattar / högt) över flera rader
            string noBr = Brackets.Replace(all, "");                         // hakparenteser kan gå över flera rader
            var src = all.Split('\n');
            var cur = noBr.Split('\n');
            // hakparentes över flera rader: radindelningen stämmer inte längre med originalet, så allt räknas som ändrat
            bool sameLines = src.Length == cur.Length;
            int dashBefore = src.Count(l => Dash.IsMatch(l));
            var res = new List<string>();
            for (int i = 0; i < cur.Length; i++)
            {
                string orig = sameLines ? src[i] : null;
                string l = cur[i];
                l = ParenCaps.Replace(l, m =>
                {
                    // förkortningar mitt i en mening står kvar: (WHO), (B). Ljudbeskrivningar är längre eller flera ord.
                    string inner = m.Value.Substring(1, m.Value.Length - 2).Trim();
                    int letters = inner.Count(char.IsLetter);
                    return letters >= 6 || (letters >= 2 && inner.Contains(" ")) ? "" : m.Value;
                });
                if (WholeParen.IsMatch(l)) l = "";
                // parentes först på raden. På rad 2 och senare bara om raden före slutar en mening,
                // annars är det en parentes mitt i en mening: "Jag träffade Anna / (min syster) i går."
                bool mayStart = i == 0 || SentenceEnd.IsMatch(cur[i - 1]) || !HasWord.IsMatch(Tags.Replace(cur[i - 1], ""));
                if (mayStart) l = ParenStart.Replace(l, "$1");
                string name = SpeakerName(l);
                if (name != null && (speakers == null || speakers.Contains(name)))
                {
                    l = Speaker.Replace(l, "$1");
                    l = ParenStart.Replace(l, "$1");      // "JOHN: (suckar) Jaha."
                }
                l = ParenEnd.Replace(l, "$1");
                if (orig != null && l == orig)
                {
                    // orörd rad: lämnas exakt som den är. Bara rader som enbart är noter (♪ ♫) tas bort.
                    string bare = Tags.Replace(l, "");
                    if ((bare.Contains("♪") || bare.Contains("♫")) && !HasWord.IsMatch(bare)) continue;
                    res.Add(l);
                    continue;
                }
                // ändrad rad: städa efter det som togs bort
                l = EmptyTags.Replace(l, "");
                l = Regex.Replace(l, @"^((?:<[^/>][^>]*>|\{[^}]*\})+)\s+", "$1");   // blanksteg efter en tagg först på raden
                l = Regex.Replace(l, @"\s+((?:</[^>]+>)+)$", "$1");                  // och före en tagg sist på raden
                l = Regex.Replace(l, @"\s+([.,!?])", "$1");
                l = Spaces.Replace(l, " ").Trim();
                if (!HasWord.IsMatch(Tags.Replace(l, ""))) continue;                // inget kvar att läsa
                res.Add(l);
            }
            if (res.Count == 0) return null;
            // två talare blev en: ta bort replikstrecket
            if (res.Count == 1 && dashBefore >= 2) res[0] = Dash.Replace(res[0], "$1");
            return string.Join("\n", res);
        }

        // Rensar alla rader. changed = rader som fick ny text, removed = rader som togs bort helt.
        // Talarnamn tas bara bort om samma namn står först på minst två rader i filen. Då är det ett namn
        // och inte en rubrik eller förkortning som råkar stå i versaler ("USA: 3, Sverige: 2").
        public static List<Cue> Apply(List<Cue> cues, out int changed, out int removed)
        {
            changed = 0; removed = 0;
            var count = new Dictionary<string, int>();
            foreach (var c in cues)
                foreach (string line in (c.Text ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    string n = SpeakerName(Brackets.Replace(line, ""));
                    if (n != null) { int k; count.TryGetValue(n, out k); count[n] = k + 1; }
                }
            var speakers = new HashSet<string>(count.Where(p => p.Value >= 2).Select(p => p.Key));
            var res = new List<Cue>(cues.Count);
            foreach (var c in cues)
            {
                string t = Clean(c.Text, speakers);
                if (t == null) { removed++; continue; }
                if (t != (c.Text ?? "").Replace("\r\n", "\n")) { changed++; var n = c.Clone(); n.Text = t; res.Add(n); }
                else res.Add(c);
            }
            return res;
        }

        public static string Summary(int changed, int removed)
        {
            if (changed == 0 && removed == 0) return "Ingen text för hörselskadade hittades.";
            return "Text för hörselskadade borttagen: " + changed + (changed == 1 ? " rad ändrad" : " rader ändrade") + ", " + removed + (removed == 1 ? " rad borttagen" : " rader borttagna") + ".";
        }
    }
}
