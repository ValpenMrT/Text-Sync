using System;
using System.Collections.Generic;
using System.Linq;

namespace TextSync
{
    // Ett förslag på att slå ihop två synkade rader (2 ↔ 1) eller dela en rad i två (1 ↔ 2),
    // så att raderna följer referensens radindelning och tider.
    public class AlignProposal
    {
        public bool IsMerge;        // true = två rader blir en, false = en rad blir två
        public bool IsMove;         // replikerna i några rader i följd fördelas som i referensen (från betydelsejämförelsen, Meaning.cs)
        public int First;           // första raden i den synkade texten som berörs
        public int Count;           // 2 vid sammanslagning, 1 vid delning
        public Cue[] Refs;          // referensraderna (1 vid sammanslagning, 2 vid delning)
        public List<Cue> NewCues;   // ersätter raderna First..First+Count-1 (tider från referensen)
        public string Key;          // känner igen förslaget efter en ändring, t.ex. för att komma ihåg avvisade

        public string Describe()
        {
            if (IsMove)
                return "Förslag: flytta repliker mellan raderna, så att de står i samma rad som i referensen (" + string.Join(", ", Refs.Select(r => Srt.FormatTime(r.Start))) +
                    ") → " + string.Join("  |  ", NewCues.Select(c => Flat(c.Text)));
            if (IsMerge)
                return "Förslag: slå ihop med nästa rad, så att den motsvarar referensraden " + Srt.FormatTime(Refs[0].Start) + " → " + Flat(NewCues[0].Text);
            return "Förslag: dela raden i två, så att den motsvarar referensraderna " + Srt.FormatTime(Refs[0].Start) + " och " + Srt.FormatTime(Refs[1].Start) +
                " → " + Flat(NewCues[0].Text) + "  |  " + Flat(NewCues[1].Text);
        }
        static string Flat(string t) { return t.Replace("\n", " / "); }
    }

    // Matchar raderna i den synkade texten mot referensens rader. Texterna är redan synkade, så det räcker att
    // jämföra tider. Programmet behöver inte förstå språken.
    //
    // Metod (dynamisk programmering, samma idé som meningsmatchning för översättningar):
    // D[i,j] = bästa poäng för de första i synkade raderna mot de första j referensraderna. Varje steg är ett av
    //   hoppa över en synkad rad (0 p), hoppa över en referensrad (0 p),
    //   1↔1 (2 × överlapp), 2↔1 och 1↔2 (3 × överlapp − Penalty).
    // Överlapp = tid som båda täcker / hela tiden från första start till sista slut (0–1).
    // Sammanslagning och delning kostar Penalty, så att de bara väljs när de är tydligt bättre.
    // Till sist följs de bästa stegen baklänges från D[n,m], och varje 2↔1 och 1↔2 blir ett förslag.
    public static class LineAligner
    {
        public const int MaxChars = 42;       // tecken per rad, vanligt för undertexter på TV
        public const int MaxLines = 2;        // rader per undertext
        public const double MinOverlap = 0.6; // sammanslagning och delning kräver minst så här bra tidsöverlapp
        public const double Penalty = 0.5;
        public const double MaxGapMs = 1000;  // längsta paus mellan två rader som slås ihop eller delas
        public const double MaxRatioDiff = 0.25; // delningens längder får skilja så här mycket från referensens
        public const double MinRefShare = 0.5;   // referensen måste ha minst hälften så många textrader
        const double MinDurMs = 500;

        public static List<AlignProposal> Propose(IList<Cue> tgt, IList<Cue> refAll)
        {
            var res = new List<AlignProposal>();
            if (tgt == null || refAll == null) return res;
            // "(tal)" från taldetekteringen är ingen text och kan inte matchas
            var rc = refAll.Where(r => r.Text != "(tal)" && r.Text.Trim().Length > 0 && r.End > r.Start).OrderBy(r => r.Start).ToList();
            int n = tgt.Count, m = rc.Count;
            // en gles referens (t.ex. ett spår med bara tvingad text) räcker inte för att avgöra radindelningen
            if (n == 0 || m < MinRefShare * n) return res;

            var D = new float[n + 1, m + 1];
            var B = new byte[n + 1, m + 1];   // 1 = hoppa synkad, 2 = hoppa referens, 3 = 1↔1, 4 = 2↔1, 5 = 1↔2
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= m; j++)
                {
                    if (i == 0 && j == 0) continue;
                    float best = float.NegativeInfinity; byte how = 0;
                    if (i > 0 && D[i - 1, j] > best) { best = D[i - 1, j]; how = 1; }
                    if (j > 0 && D[i, j - 1] > best) { best = D[i, j - 1]; how = 2; }
                    if (i > 0 && j > 0)
                    {
                        var t = tgt[i - 1]; var r = rc[j - 1];
                        if (r.Start < t.End && t.Start < r.End)
                        {
                            float v = D[i - 1, j - 1] + (float)(2 * Overlap(t.Start, t.End, r.Start, r.End));
                            if (v > best) { best = v; how = 3; }
                            if (i > 1 && tgt[i - 1].Start - tgt[i - 2].End <= MaxGapMs)
                            {
                                double ov = Overlap(tgt[i - 2].Start, t.End, r.Start, r.End);
                                v = D[i - 2, j - 1] + (float)(3 * ov - Penalty);
                                if (ov >= MinOverlap && v > best && MergeText(tgt[i - 2].Text, t.Text, r.Text) != null) { best = v; how = 4; }
                            }
                            if (j > 1 && rc[j - 1].Start - rc[j - 2].End <= MaxGapMs)
                            {
                                double ov = Overlap(t.Start, t.End, rc[j - 2].Start, r.End);
                                v = D[i - 1, j - 2] + (float)(3 * ov - Penalty);
                                if (ov >= MinOverlap && v > best && SplitText(t.Text, rc[j - 2].Text, r.Text) != null) { best = v; how = 5; }
                            }
                        }
                    }
                    D[i, j] = best; B[i, j] = how;
                }
            }

            // följ de bästa stegen baklänges
            for (int i = n, j = m; i > 0 || j > 0;)
            {
                switch (B[i, j])
                {
                    case 1: i--; break;
                    case 2: j--; break;
                    case 3: i--; j--; break;
                    case 4:
                        {
                            var p = MakeMerge(tgt, i - 2, rc[j - 1]);
                            if (p != null) res.Add(p);
                            i -= 2; j--; break;
                        }
                    case 5:
                        {
                            var p = MakeSplit(tgt, i - 1, rc[j - 2], rc[j - 1]);
                            if (p != null) res.Add(p);
                            i--; j -= 2; break;
                        }
                    default: i = 0; j = 0; break;
                }
            }
            res.Reverse();
            return res;
        }

        // Förslagen från betydelsejämförelsen (Flytta?) som förslag i listan. Raderna känns igen på tid före synk och text,
        // så ett förslag vars rader har ändrats sedan dess följer inte med. plain = formateringen är borttagen ur det som visas.
        public static List<AlignProposal> MoveProposals(IList<Cue> synced, IList<MeaningMove> moves, bool plain, double extraMs)
        {
            var res = new List<AlignProposal>();
            if (synced == null || moves == null) return res;
            foreach (var mv in moves)
            {
                int first = -1;
                for (int i = 0; i + mv.Texts.Length <= synced.Count && first < 0; i++)
                {
                    bool same = true;
                    for (int k = 0; k < mv.Texts.Length && same; k++)
                        same = synced[i + k].OrigStart == mv.OrigStarts[k] && (synced[i + k].Text == mv.Texts[k] || (plain && synced[i + k].Text == Markup.Strip(mv.Texts[k])));
                    if (same) first = i;
                }
                if (first < 0) continue;
                res.Add(new AlignProposal
                {
                    IsMove = true, First = first, Count = mv.Texts.Length, Refs = mv.Refs, Key = "V|" + mv.OrigStarts[0] + "|" + string.Join("|", mv.Texts),
                    NewCues = mv.NewCues.Select(c => new Cue(c.Start + extraMs, c.End + extraMs, c.Text) { OrigStart = c.OrigStart }).ToList()
                });
            }
            return res;
        }

        // Förslagen om flytt går före förslag som bara bygger på tider och berör samma rader
        public static List<AlignProposal> WithMoves(List<AlignProposal> props, IEnumerable<AlignProposal> moves)
        {
            foreach (var mp in moves)
            {
                int f = mp.First, cnt = mp.Count;
                props.RemoveAll(p => p.First < f + cnt && f < p.First + p.Count);
                props.Add(mp);
            }
            return props.OrderBy(p => p.First).ToList();
        }

        // Gör som förslagen säger: raderna First..First+Count-1 byts mot NewCues. added = de nya raderna.
        // extraMs = extra justering som visas men inte ligger i texten (dras ifrån igen).
        public static List<Cue> Apply(IList<Cue> cues, IList<AlignProposal> ps, double extraMs, out List<Cue> added)
        {
            var replaced = new HashSet<int>(); added = new List<Cue>();
            foreach (var p in ps)
            {
                for (int k = 0; k < p.Count; k++) replaced.Add(p.First + k);
                // en delad rad kan få halva taggar ("<i>Rad ett." och "Rad två.</i>"): de lagas
                foreach (var c in p.NewCues) added.Add(new Cue(c.Start - extraMs, c.End - extraMs, Markup.Fix(c.Text)) { OrigStart = c.OrigStart });
            }
            return cues.Where((c, i) => !replaced.Contains(i)).Concat(added).OrderBy(c => c.Start).ToList();
        }

        // "1 flytt av repliker och 2 sammanslagningar"
        public static string CountText(IList<AlignProposal> ps)
        {
            int moves = ps.Count(p => p.IsMove), merged = ps.Count(p => p.IsMerge && !p.IsMove), splits = ps.Count - merged - moves;
            var parts = new List<string>();
            if (moves > 0) parts.Add(moves == 1 ? "1 flytt av repliker" : moves + " flyttar av repliker");
            if (merged > 0) parts.Add(merged == 1 ? "1 sammanslagning" : merged + " sammanslagningar");
            if (splits > 0) parts.Add(splits == 1 ? "1 delning" : splits + " delningar");
            return string.Join(" och ", parts);
        }

        // Inställningen "Godkänn förslag om radindelning automatiskt", utan fönster (Hel säsong): alla förslag godkänns på en gång.
        // what = vad som gjordes ("2 sammanslagningar och 1 delning"), null = inga förslag och texten är orörd.
        public static List<Cue> AcceptAll(List<Cue> cues, IList<Cue> refc, IList<MeaningMove> moves, out string what)
        {
            what = null;
            var ps = Propose(cues, refc);
            if (moves != null && moves.Count > 0) ps = WithMoves(ps, MoveProposals(cues, moves, false, 0));
            if (ps.Count == 0) return cues;
            what = CountText(ps);
            List<Cue> added;
            return Apply(cues, ps, 0, out added);
        }

        static AlignProposal MakeMerge(IList<Cue> tgt, int first, Cue r)
        {
            string text = MergeText(tgt[first].Text, tgt[first + 1].Text, r.Text);
            if (text == null) return null;
            double s, e;
            if (!Clamp(tgt, first, 2, r.Start, r.End, out s, out e)) return null;
            var c = new Cue(s, e, text) { OrigStart = tgt[first].OrigStart };
            return new AlignProposal
            {
                IsMerge = true, First = first, Count = 2, Refs = new[] { r }, NewCues = new List<Cue> { c },
                Key = "M|" + tgt[first].OrigStart + "|" + tgt[first].Text + "|" + tgt[first + 1].Text + "|" + r.Text
            };
        }

        static AlignProposal MakeSplit(IList<Cue> tgt, int i, Cue ra, Cue rb)
        {
            var parts = SplitText(tgt[i].Text, ra.Text, rb.Text);
            if (parts == null) return null;
            double s, e;
            if (!Clamp(tgt, i, 1, ra.Start, rb.End, out s, out e)) return null;
            // andra delen börjar där referensens andra rad börjar, inom det tillåtna spannet
            double mid = Math.Max(s + MinDurMs, Math.Min(e - MinDurMs, rb.Start));
            double end1 = Math.Min(ra.End, mid);
            if (end1 - s < MinDurMs || e - mid < MinDurMs) return null;
            var o = tgt[i];
            var a = new Cue(s, end1, parts[0]) { OrigStart = o.OrigStart };
            var b = new Cue(mid, e, parts[1]) { OrigStart = double.IsNaN(o.OrigStart) ? double.NaN : o.OrigStart + (mid - o.Start) };
            return new AlignProposal
            {
                IsMerge = false, First = i, Count = 1, Refs = new[] { ra, rb }, NewCues = new List<Cue> { a, b },
                Key = "S|" + o.OrigStart + "|" + o.Text + "|" + ra.Text + "|" + rb.Text
            };
        }

        // ---------------------------------------------------------------- dela en rad för hand (Dela rad… i listan)
        // Stället mellan två ord som ligger närmast tecknet pos (markören), så att ett ord aldrig delas.
        // -1 = det finns bara text på ena sidan (markören står före första ordet eller efter sista).
        public static int SnapSplit(string text, int pos)
        {
            string s = text ?? "";
            pos = Math.Max(0, Math.Min(s.Length, pos));
            int a = pos, b = pos;
            while (InWord(s, a)) a--;   // ordets början
            while (InWord(s, b)) b++;   // ordets slut
            bool okA = HasText(s, 0, a) && HasText(s, a, s.Length), okB = HasText(s, 0, b) && HasText(s, b, s.Length);
            if (okA && okB) return pos - a <= b - pos ? a : b;
            return okA ? a : okB ? b : -1;
        }

        static bool InWord(string s, int p) { return p > 0 && p < s.Length && !char.IsWhiteSpace(s[p - 1]) && !char.IsWhiteSpace(s[p]); }
        static bool HasText(string s, int from, int to) { for (int i = from; i < to; i++) if (!char.IsWhiteSpace(s[i])) return true; return false; }

        // Där markören står från början: helst vid en radbrytning, annars efter . ? ! …, annars mellan två ord.
        // Av flera lika bra ställen väljs det närmast mitten. -1 = texten är ett enda ord.
        public static int DefaultSplit(string text)
        {
            string s = text ?? "";
            int best = -1, bestRank = int.MaxValue; double bestDist = double.MaxValue;
            for (int p = 1; p < s.Length; p++)
            {
                if (!char.IsWhiteSpace(s[p]) || char.IsWhiteSpace(s[p - 1]) || !HasText(s, p, s.Length)) continue;
                int rank = s[p] == '\n' || s[p] == '\r' ? 0 : ".?!…".IndexOf(s[p - 1]) >= 0 ? 1 : 2;
                double dist = Math.Abs(p - s.Length / 2.0);
                if (rank < bestRank || (rank == bestRank && dist < bestDist)) { best = p; bestRank = rank; bestDist = dist; }
            }
            return best;
        }

        // Texten före och efter tecknet pos. Radbrytningarna behålls, och ett ensamt replikstreck tas bort
        // ("- Hem." blir "Hem."), som när ett förslag delar raden. null = en av delarna blir tom.
        public static string[] SplitAt(string text, int pos)
        {
            string s = text ?? "";
            if (pos <= 0 || pos >= s.Length) return null;
            string a = ManualPart(s.Substring(0, pos)), b = ManualPart(s.Substring(pos));
            return a == null || b == null ? null : new[] { a, b };
        }

        static string ManualPart(string part)
        {
            var lines = Lines(part);
            if (lines.Count == 1) lines[0] = StripDash(lines[0]);
            return lines.Count == 0 || lines[0].Length == 0 ? null : string.Join("\n", lines);
        }

        // Tiden då andra delen börjar: radens tid fördelas efter hur många bokstäver delarna har.
        // Ingen del blir kortare än en halv sekund, om raden är lång nog för det.
        public static double SplitTime(double start, double end, string a, string b)
        {
            int la = Letters(Markup.Strip(a)), lb = Letters(Markup.Strip(b));
            double dur = end - start, share = la + lb == 0 ? 0.5 : (double)la / (la + lb);
            double min = Math.Min(MinDurMs, dur / 3);
            return Math.Round(Math.Max(start + min, Math.Min(end - min, start + dur * share)));
        }

        // Rad i blir två: första delen slutar och andra börjar vid tiden mid. Halva taggar lagas, som för förslagen.
        public static List<Cue> SplitManually(IList<Cue> cues, int i, string a, string b, double mid, out Cue first)
        {
            var o = cues[i];
            first = new Cue(o.Start, mid, Markup.Fix(a)) { OrigStart = o.OrigStart };
            var second = new Cue(mid, o.End, Markup.Fix(b)) { OrigStart = double.IsNaN(o.OrigStart) ? double.NaN : o.OrigStart + (mid - o.Start) };
            var res = new List<Cue>(cues);
            res[i] = first; res.Insert(i + 1, second);
            return res;
        }

        // Tiderna tas från referensen, men får inte gå in i raderna före och efter i den synkade texten
        static bool Clamp(IList<Cue> tgt, int first, int count, double s, double e, out double cs, out double ce)
        {
            cs = s; ce = e;
            if (first > 0) cs = Math.Max(cs, tgt[first - 1].End);
            if (first + count < tgt.Count) ce = Math.Min(ce, tgt[first + count].Start);
            return ce - cs >= MinDurMs;
        }

        // Andel av tiden som båda täcker (0–1)
        public static double Overlap(double aS, double aE, double bS, double bE)
        {
            double inter = Math.Min(aE, bE) - Math.Max(aS, bS);
            double union = Math.Max(aE, bE) - Math.Min(aS, bS);
            return inter <= 0 || union <= 0 ? 0 : inter / union;
        }

        // ---------------------------------------------------------------- text
        static List<string> Lines(string t)
        {
            return (t ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        static bool IsDash(char c) { return c == '-' || c == '–' || c == '—'; }
        static bool IsDialogue(string line) { return line.Length > 0 && IsDash(line[0]); }
        static string StripDash(string line) { return IsDialogue(line) ? line.Substring(1).TrimStart() : line; }

        // Ställer upp texten på högst MaxLines rader med högst MaxChars tecken. null = går inte.
        // Rader som redan får plats behålls som de är. Annars bryts texten om vid det mellanslag som ger jämnast rader.
        public static string Layout(string text)
        {
            var lines = Lines(text);
            if (lines.Count == 0) return null;
            if (lines.Count <= MaxLines && lines.All(l => l.Length <= MaxChars)) return string.Join("\n", lines);
            if (lines.Count(IsDialogue) > 0) return null;   // repliker från olika personer bryts inte om
            var words = string.Join(" ", lines).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string flat = string.Join(" ", words);
            if (flat.Length <= MaxChars) return flat;
            string best = null; int bestMax = int.MaxValue;
            for (int k = 1; k < words.Length; k++)
            {
                string a = string.Join(" ", words.Take(k)), b = string.Join(" ", words.Skip(k));
                int mx = Math.Max(a.Length, b.Length);
                if (mx <= MaxChars && mx < bestMax) { bestMax = mx; best = a + "\n" + b; }
            }
            return best;
        }

        // Två rader blir en. null = texten blir för lång för en undertext.
        public static string MergeText(string a, string b, string refText)
        {
            var la = Lines(a); var lb = Lines(b);
            if (la.Count == 0 || lb.Count == 0) return null;
            // referensen visar två personer som pratar ("- Var är du?" / "- Hemma."): gör likadant
            bool refDialogue = Lines(refText).Count(IsDialogue) >= 2;
            if (refDialogue && la.Count == 1 && lb.Count == 1 && !IsDialogue(la[0]) && !IsDialogue(lb[0]))
            {
                string x = "- " + la[0], y = "- " + lb[0];
                return x.Length <= MaxChars && y.Length <= MaxChars ? x + "\n" + y : null;
            }
            var all = la.Concat(lb).ToList();
            if (all.Count <= MaxLines && all.All(l => l.Length <= MaxChars)) return string.Join("\n", all);
            return Layout(string.Join(" ", all));
        }

        // En rad blir två, delad där referensens två rader har motsvarande längd.
        // Delar bara vid radbrytning eller efter . ? ! … (följt av mellanslag). null = inget tydligt ställe.
        public static string[] SplitText(string text, string refA, string refB)
        {
            string s = (text ?? "").Replace("\r", "").Trim();
            int la = Letters(refA), lb = Letters(refB);
            if (s.Length == 0 || la + lb == 0) return null;
            double want = (double)la / (la + lb);
            // mitt i en mening (radbrytning utan . ? ! före) delas bara om referensens första rad också slutar mitt i en mening
            bool refContinues = EndsMidSentence(refA);
            string[] best = null; double bestDiff = double.MaxValue;
            for (int p = 1; p < s.Length; p++)
            {
                char prev = s[p - 1], next = s[p];
                bool sentenceEnd = ".?!…".IndexOf(prev) >= 0;
                bool cut = (next == '\n' && (sentenceEnd || refContinues)) || (next == ' ' && sentenceEnd);
                if (!cut) continue;
                string p1 = CleanPart(s.Substring(0, p)), p2 = CleanPart(s.Substring(p));
                if (p1 == null || p2 == null) continue;
                int l1 = Letters(p1), l2 = Letters(p2);
                if (l1 == 0 || l2 == 0) continue;
                double diff = Math.Abs((double)l1 / (l1 + l2) - want);
                if (diff <= MaxRatioDiff && diff < bestDiff) { bestDiff = diff; best = new[] { p1, p2 }; }
            }
            return best;
        }

        // En del av en delad rad: ensam replikmarkering tas bort ("- Hemma." blir "Hemma."), sedan ställs texten upp
        static string CleanPart(string part)
        {
            var lines = Lines(part);
            if (lines.Count == 0) return null;
            if (lines.Count == 1) lines[0] = StripDash(lines[0]);
            return Layout(string.Join("\n", lines));
        }

        static int Letters(string t) { return (t ?? "").Count(char.IsLetterOrDigit); }

        // "...", komma eller ingen avslutande punkt = meningen fortsätter på nästa rad
        public static bool EndsMidSentence(string t)
        {
            string s = string.Join(" ", Lines(t)).TrimEnd('"', '\'', '”', '»', ' ');
            if (s.Length == 0) return false;
            if (s.EndsWith("...") || s.EndsWith("…")) return true;
            return ".?!".IndexOf(s[s.Length - 1]) < 0;
        }
    }
}
