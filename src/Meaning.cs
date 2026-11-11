// Text Sync - jämför vad raderna betyder (t.ex. svenska mot engelska) med llama.cpp och en flerspråkig språkmodell.
// Synkmotorn jämför bara när det pratas. Här paras raderna ihop efter betydelse, och där flera par i rad visar
// att texten ligger fel flyttas raderna dit. Kör efter den vanliga synken, och bara mot en referens som är text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TextSync
{
    // Ett par: rad Line i texten betyder samma sak som referensrad Ref och de Size-1 följande referensraderna
    public class MeaningPair
    {
        public int Line, Ref, Size;
        public double Delta;   // ms från radens start till gruppens första referensrad
        public double Z;       // likheten i standardavvikelser
    }

    // En replik: en del av en rad. Rad Row i listan, del Part av Parts. Text = repliken utan replikstreck och radbrytningar.
    public class MeaningUnit
    {
        public int Row, Part, Parts;
        public string Text;
    }

    // Förslag om att fördela replikerna i några rader i följd som referensen gör. Raderna känns igen på tid före synk och text,
    // så att förslaget kan visas även efter andra ändringar i listan.
    public class MeaningMove
    {
        public double[] OrigStarts;   // raderna som ersätts
        public string[] Texts;
        public List<Cue> NewCues;     // nya rader, med referensradernas tider
        public Cue[] Refs;            // referensraden för varje ny rad
    }

    public class MeaningReport
    {
        public List<MeaningPair> Pairs = new List<MeaningPair>();   // alla par, i tidsordning
        public List<MeaningMove> Moves = new List<MeaningMove>();   // förslag om att flytta repliker mellan rader
        public int UnitPairs;    // repliker som parades säkert med en replik i referensen
        public int Snapped;      // enskilda rader som lades på referensradens starttid
        public int Lines;        // rader i texten som är långa nog att jämföra
        public int Paired;       // rader som parades ihop med en referensrad efter betydelse
        public int Grouped;      // varav med två eller tre referensrader i följd
        public int Anchors;      // par som är säkra nog att styra tiderna
        public int Moved;        // rader som flyttades
        public int Places;       // antal ställen (rader i följd med samma flytt)
        public double MaxMs;     // största flytten
        public bool TooFew;      // för få säkra par: ingenting ändrades
        public string Error;     // jämförelsen kunde inte göras (texten visas som den är)
        public bool Canceled;    // användaren avbröt jämförelsen: synken är kvar som den var
        public bool Worse;       // flytten gjorde synken sämre och togs tillbaka
        // Andel rader som börjar inom 0,5 s från en referensrad, med den vanliga synken och efter flytten (NaN = inget flyttat)
        public double OnsetBefore = double.NaN, OnsetAfter = double.NaN;
        public double Sigma, MatchMin, AnchorMin;   // till loggen, för att kunna justera gränserna mot riktiga filmer
        public string Levels = "";

        static string Sec(double ms) { return (ms / 1000).ToString("0.0", CultureInfo.GetCultureInfo("sv-SE")); }

        // En mening till Detaljer och till raden i Hel säsong
        public string Summary()
        {
            if (Canceled) return "Betydelsejämförelsen avbröts, så synken är gjord som vanligt.";
            if (Error != null) return "Betydelsejämförelsen kunde inte göras, så synken är gjord som vanligt. " + Error;
            string pairs = Paired + " av " + Lines + " rader parades ihop efter betydelse.";
            if (TooFew) return "Betydelsejämförelsen ändrade ingenting, eftersom för få rader gick att para ihop säkert. " + pairs;
            var did = new List<string>();
            if (Moved > 0) did.Add("flyttade " + (Moved == 1 ? "1 rad" : Moved + " rader") + " på " + (Places == 1 ? "1 ställe" : Places + " ställen") + " (som mest " + Sec(MaxMs) + " s)");
            if (Snapped > 0) did.Add("lade " + (Snapped == 1 ? "1 enskild rad" : Snapped + " enskilda rader") + " på referensradens starttid");
            if (Moves.Count > 0) did.Add("har " + (Moves.Count == 1 ? "1 förslag" : Moves.Count + " förslag") + " om att flytta repliker mellan rader (märkta Flytta?)");
            string worse = Worse ? " En flytt av ett helt stycke togs tillbaka, eftersom texten passade referensen sämre efter den." : "";
            if (did.Count == 0) return (Worse ? "Betydelsejämförelsen ändrade ingenting." : "Betydelsejämförelsen hittade inget att rätta.") + worse + " " + pairs;
            return "Betydelsejämförelsen " + string.Join(did.Count > 2 ? ", " : " och ", did) + "." + worse + " " + pairs;
        }

        // Blev det bättre? Jämför hur många rader som börjar i takt med referensen före och efter flytten. null = inget flyttat.
        public string Compare()
        {
            if (double.IsNaN(OnsetBefore) || double.IsNaN(OnsetAfter)) return null;
            var sv = CultureInfo.GetCultureInfo("sv-SE");
            string b = (Math.Floor(OnsetBefore * 1000) / 10).ToString("0.#", sv) + " %", a = (Math.Floor(OnsetAfter * 1000) / 10).ToString("0.#", sv) + " %";
            string what = "Rader som börjar inom 0,5 s från en referensrad: " + b + " med den vanliga synken, " + a + " med betydelsejämförelsen.";
            if (a == b) return "Lika bra. " + what;
            return (OnsetAfter > OnsetBefore ? "Bättre. " : "Sämre. ") + what;
        }

        // Kort text till rutan bredvid siffrorna i resultatet
        public string Chip()
        {
            if (Canceled) return "Betydelse: avbruten";
            if (Error != null) return "⚠ Betydelse: kunde inte jämföras";
            int n = Moved + Snapped;
            if (n == 0 && Moves.Count > 0) return "Betydelse: " + (Moves.Count == 1 ? "1 förslag" : Moves.Count + " förslag");
            if (TooFew || (Worse && n == 0)) return "Betydelse: inget ändrat";
            return n == 0 ? "Betydelse: inget att rätta" : "Betydelse: " + (n == 1 ? "1 rad flyttad" : n + " rader flyttade");
        }

        public string LogText()
        {
            var inv = CultureInfo.InvariantCulture;
            if (Canceled) return "avbruten";
            if (Error != null) return "fel: " + Error;
            return (Worse ? "togs tillbaka (sämre poäng), " : "") + "rader " + Lines + ", par " + Paired + " (" + Grouped + " mot flera rader), säkra par " + Anchors + ", flyttade " + Moved + " på " + Places + " ställen, största " + MaxMs.ToString("0", inv) +
                " ms, sigma " + Sigma.ToString("0.000", inv) + ", gränser " + MatchMin.ToString("0.000", inv) + "/" + AnchorMin.ToString("0.000", inv) +
                (TooFew ? ", för få säkra par" : "") + (Levels.Length > 0 ? ", nivåer " + Levels : "") +
                ", repliker med säkert par " + UnitPairs + ", enskilda rader flyttade " + Snapped + ", förslag om flytt " + Moves.Count;
        }
    }

    public static class Meaning
    {
        // Gränserna är satta mot påhittade data i testerna och mätta mot en film, Blacklist S04E01 (beslutsloggen 2026-11-11, 2.4.8):
        // den vanliga synken ändras inte, och fel på minst fem rader i följd rättas. Mät om med test/MeaningDiag.cs när något ändras:
        // JumpCost 2,0 missade fel på 5–6 rader, och 1,0 flyttade fyra rader som låg rätt.
        const double Tol = 400;        // ms: ett par stöder en nivå om det ligger så nära den
        const double Free = 100;       // ms: så nära nivån kostar ingenting
        public static double JumpCost = 1.5;   // byte av nivå kostar som ett och ett halvt par som inte stämmer: en utflykt och tillbaka behöver minst fyra par
        const double Deadband = 150;   // ms: mindre flyttar görs inte, synkmotorn är redan så exakt
        const double LevelStep = 50;   // ms
        const int MinLetters = 8;      // kortare rader ("Ja.", "Okej.") säger för lite om betydelsen
        const int MinSection = 3;      // säkra par som krävs för att ett avsnitt ska få flyttas
        const double MatchSigmas = 2.5, AnchorSigmas = 4;
        const double MaxShift = 10000; // ms: par som ligger längre från varandra styr inte tiderna (en scen som upprepas, eller fel avsnitt)
        const double WorseMargin = 0.01; // så mycket får överlappspoängen sjunka innan flytten tas tillbaka
        const double SnapMin = 150, SnapMax = 3000;  // ms: enskilda rader flyttas bara om de ligger så här långt från sin referensrad
        const double MinSnapDur = 700; // ms: kortare än så får en rad inte bli
        const int UnitLetters = 5;     // repliker får vara kortare än hela rader ("He lied."), eftersom ordningen runt omkring styr
        const double GroupGap = 2000;  // ms: längsta paus mellan referensrader som kan höra till samma översatta rad
        public static int MaxGroup = 3; // en rad får paras med upp till så många referensrader i följd (1 = bara rad mot rad)

        // Texten som jämförs: utan taggar, radbrytningar och replikstreck
        public static string Clean(string text)
        {
            string s = Regex.Replace(text ?? "", @"<[^>]*>|\{[^}]*\}", " ");
            s = Regex.Replace(s, @"(^|\n)\s*[-–]\s*", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 300 ? s.Substring(0, 300) : s;
        }

        public static bool Usable(string cleaned) { return Usable(cleaned, MinLetters); }
        public static bool Usable(string cleaned, int minLetters) { return cleaned != null && cleaned.Count(char.IsLetter) >= minLetters; }

        static float Dot(float[] a, float[] b)
        {
            double s = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) s += a[i] * b[i];
            return (float)s;
        }

        public static void Normalize(float[] v)
        {
            double s = 0;
            for (int i = 0; i < v.Length; i++) s += (double)v[i] * v[i];
            if (s <= 0) return;
            float k = (float)(1 / Math.Sqrt(s));
            for (int i = 0; i < v.Length; i++) v[i] *= k;
        }

        // Hela steget: vektorer för båda texterna (ur cachen eller från llama-server), parning och rättning.
        // Kastar aldrig utom vid avbrott: går något fel står det i rapporten, och synken lämnas som den var.
        public static MeaningReport Apply(List<Cue> refc, SyncResult r, Action<int, string> progress, CancellationToken ct)
        {
            var rep = new MeaningReport();
            try
            {
                string exe = Tools.FindLlamaServer(), model = Tools.FindModel(Tools.ModelBgeM3);
                if (exe == null || model == null) throw new UserMessageException("Verktygen saknas. Synka igen, så erbjuder Text Sync att ladda ner dem.");
                var sw = Stopwatch.StartNew();
                var rt = refc.Select(c => Clean(c.Text)).ToList();
                var tt = r.Output.Select(c => Clean(c.Text)).ToList();
                float[][] rv, tv;
                int G = Math.Max(0, MaxGroup - 1);
                int steps = 2 * (2 + G);   // rader: texten, referensen, grupper om två, tre, … Sedan samma sak för replikerna.
                var groups = new float[G][][];
                var known = new Dictionary<string, float[]>();   // samma text räknas bara en gång
                List<Cue> outp;
                using (var srv = new LlamaServer(exe, model))
                {
                    Func<int, Action<int>> part = k => p => { int pct = (k * 100 + p) / steps; progress(pct, "Jämför vad raderna betyder… " + pct + " %"); };
                    Func<int, Action<string>> stat = k => s => progress(k * 100 / steps, s);
                    rv = Embedder.Get(rt, model, srv, part(0), stat(0), ct, known);
                    tv = Embedder.Get(tt, model, srv, part(1), stat(1), ct, known);
                    for (int g = 0; g < G; g++)
                        groups[g] = Embedder.Get(GroupTexts(refc, g + 2), model, srv, part(2 + g), stat(2 + g), ct, known);
                    ct.ThrowIfCancellationRequested();

                    // 1) hela stycken som ligger fel
                    outp = Refine(refc, r.Output, rv, tv, out rep, groups);
                    if (rep.Moved > 0 && SyncEngine.Score(refc, outp) < SyncEngine.Score(refc, r.Output) - WorseMargin)
                    {
                        // passar texten referensen sämre efter flytten tas den tillbaka
                        rep.Worse = true; rep.Moved = 0; rep.Places = 0;
                        outp = r.Output.Select(c => c.Clone()).ToList();
                    }

                    // 2) replikerna: enskilda rader läggs på referensradens tid, och repliker i fel rad blir förslag
                    if (!rep.TooFew)
                    {
                        var tU = Units(outp); var rU = Units(refc);
                        int b = 2 + G;
                        var tuv = Embedder.Get(tU.Select(u => Clean(u.Text)).ToList(), model, srv, part(b), stat(b), ct, known, UnitLetters);
                        var ruv = Embedder.Get(rU.Select(u => Clean(u.Text)).ToList(), model, srv, part(b + 1), stat(b + 1), ct, known, UnitLetters);
                        var ug = new float[G][][];
                        for (int g = 0; g < G; g++)
                            ug[g] = Embedder.Get(UnitGroupTexts(rU, refc, g + 2), model, srv, part(b + 2 + g), stat(b + 2 + g), ct, known, UnitLetters);
                        ct.ThrowIfCancellationRequested();
                        var beforeSnap = outp.Select(c => c.Clone()).ToList();
                        RefineUnits(refc, outp, tU, tuv, rU, ruv, ug, rep);
                        if (rep.Snapped > 0 && SyncEngine.Score(refc, outp) < SyncEngine.Score(refc, beforeSnap) - WorseMargin) { outp = beforeSnap; rep.Snapped = 0; }
                    }
                }
                if (rep.Moved > 0 || rep.Snapped > 0)
                {
                    // båda varianterna finns kvar i raderna (MeaningAlt), så att fönstret kan växla mellan dem
                    rep.OnsetBefore = Quality.Assess(refc, r.Output, r, true).Onset;
                    MarkMoved(r.Output, outp);
                    r.Output = outp; r.ScoreAfter = SyncEngine.Score(refc, outp);
                    rep.OnsetAfter = Quality.Assess(refc, outp, r, true).Onset;
                }
                Log.Info("Betydelsejämförelse: " + rep.LogText() + ", " + sw.ElapsedMilliseconds + " ms");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
                Log.Error("Betydelsejämförelsen misslyckades", ex);
                // användaren får en kort mening, detaljerna står i loggen
                rep = new MeaningReport { Error = ex is UserMessageException ? ex.Message : "Detaljer finns i loggfilen (Hjälp → Öppna mappen med loggfilen)." };
            }
            r.Meaning = rep;
            return rep;
        }

        // ---- växla mellan den vanliga synken och synken med betydelsejämförelsen (båda tiderna finns i varje flyttad rad)

        // Märker raderna i moved som ligger på en annan tid än i plain (samma ordning i båda listorna)
        public static void MarkMoved(List<Cue> plain, List<Cue> moved)
        {
            for (int i = 0; i < moved.Count && i < plain.Count; i++)
                if (Math.Abs(moved[i].Start - plain[i].Start) > 0.5 || Math.Abs(moved[i].End - plain[i].End) > 0.5)
                { moved[i].MeaningAlt = plain[i].Start; moved[i].MeaningAltEnd = plain[i].End; moved[i].MeaningOff = false; }
        }

        // Antal rader som skiljer sig mellan varianterna (0 = inget att växla mellan)
        public static int MovedCount(List<Cue> cues) { return cues.Count(c => !double.IsNaN(c.MeaningAlt)); }

        // true = listan visar den vanliga synken, och betydelsejämförelsens tider är alternativet
        public static bool ShowsPlain(List<Cue> cues) { return cues.Any(c => !double.IsNaN(c.MeaningAlt) && c.MeaningOff); }

        // Den andra varianten: varje märkt rad byter till sin andra tid. Rader som har ändrats för hand är inte märkta och rörs inte.
        public static List<Cue> Switch(List<Cue> cues)
        {
            var res = new List<Cue>(cues.Count);
            foreach (var c in cues)
            {
                var n = c.Clone();
                if (!double.IsNaN(c.MeaningAlt))
                {
                    // slutet i den andra varianten finns sparat. Saknas det (NaN) följer slutet med starten.
                    n.Start = c.MeaningAlt;
                    n.End = double.IsNaN(c.MeaningAltEnd) ? c.End + (c.MeaningAlt - c.Start) : c.MeaningAltEnd;
                    n.MeaningAlt = c.Start; n.MeaningAltEnd = c.End; n.MeaningOff = !c.MeaningOff;
                }
                res.Add(n);
            }
            return res.OrderBy(c => c.Start).ToList();
        }

        // ================================================================ repliker
        // En rad kan innehålla två repliker ("- Var är du?" / "- Hemma."). Översättningen delar ibland replikerna på raderna
        // på ett annat sätt än originalet. Därför paras också replikerna ihop, var för sig:
        //  - En rad vars första replik hör till den första repliken i en referensrad läggs på den referensradens starttid (Snap).
        //  - Där replikerna är fördelade på raderna på ett annat sätt än i referensen blir det ett förslag (MeaningMove).

        // Textens repliker i tidsordning. En rad som börjar med replikstreck börjar en ny replik, andra rader hör till repliken före.
        public static List<MeaningUnit> Units(List<Cue> cues)
        {
            var res = new List<MeaningUnit>();
            foreach (int i in Enumerable.Range(0, cues.Count).OrderBy(k => cues[k].Start))
            {
                var parts = new List<string>();
                foreach (string raw in (cues[i].Text ?? "").Replace("\r", "").Split('\n'))
                {
                    string l = Regex.Replace(raw, @"<[^>]*>|\{[^}]*\}", "").Trim();
                    if (l.Length == 0) continue;
                    bool dash = l[0] == '-' || l[0] == '–' || l[0] == '—';
                    if (dash) l = l.Substring(1).TrimStart();
                    if (l.Length == 0) continue;
                    if (dash || parts.Count == 0) parts.Add(l); else parts[parts.Count - 1] += " " + l;
                }
                if (parts.Count == 0) parts.Add("");
                for (int k = 0; k < parts.Count; k++) res.Add(new MeaningUnit { Row = i, Part = k, Parts = parts.Count, Text = parts[k] });
            }
            return res;
        }

        // Texten för replik p tillsammans med de size-1 följande ("" = ingen grupp). Repliker i olika rader måste följa tätt på varandra.
        public static List<string> UnitGroupTexts(List<MeaningUnit> units, List<Cue> cues, int size)
        {
            var res = units.Select(u => "").ToList();
            for (int p = 0; p + size <= units.Count; p++)
            {
                bool ok = true;
                for (int q = p + 1; q < p + size && ok; q++)
                    if (units[q].Row != units[q - 1].Row && cues[units[q].Row].Start - cues[units[q - 1].Row].End > GroupGap) ok = false;
                if (ok && units.Skip(p).Take(size).All(u => u.Text.Length > 0)) res[p] = Clean(string.Join(" ", units.Skip(p).Take(size).Select(u => u.Text)));
            }
            return res;
        }

        // Bästa parningen i ordning mellan tgtVec och refVec (och grupper av referensvektorer), som i Refine steg 1–2.
        // Paren kommer i ordning. Line och Ref är index i vektorlistorna.
        static List<MeaningPair> PairUp(float[][] tgtVec, float[][] refVec, float[][][] refGroups)
        {
            var res = new List<MeaningPair>();
            int[] ti = Enumerable.Range(0, tgtVec.Length).Where(i => tgtVec[i] != null).ToArray();
            int[] ra = Enumerable.Range(0, refVec.Length).ToArray();
            int n = ti.Length, m = ra.Length;
            if (n < 5 || refVec.Count(v => v != null) < 5) return res;
            int G = 1 + (refGroups == null ? 0 : refGroups.Length);
            var z = new float[G][][];
            for (int g = 0; g < G; g++) { double sg; z[g] = Scores(tgtVec, ti, g == 0 ? refVec : refGroups[g - 1], ra, out sg); }
            if (z[0] == null) return res;
            var D = new float[n + 1][]; var back = new byte[n + 1][];
            for (int a = 0; a <= n; a++) { D[a] = new float[m + 1]; back[a] = new byte[m + 1]; }
            for (int a = 1; a <= n; a++)
                for (int b = 1; b <= m; b++)
                {
                    float best = D[a - 1][b]; byte bk = 1;
                    if (D[a][b - 1] > best) { best = D[a][b - 1]; bk = 2; }
                    for (int g = 0; g < G && g < b; g++)
                    {
                        if (z[g] == null) continue;
                        float gain = z[g][a - 1][b - 1 - g] - (float)MatchSigmas;
                        if (gain > 0 && D[a - 1][b - 1 - g] + gain > best) { best = D[a - 1][b - 1 - g] + gain; bk = (byte)(3 + g); }
                    }
                    D[a][b] = best; back[a][b] = bk;
                }
            for (int a = n, b = m; a > 0 && b > 0; )
            {
                int bk = back[a][b];
                if (bk >= 3)
                {
                    int g = bk - 3, col = b - 1 - g;
                    res.Add(new MeaningPair { Line = ti[a - 1], Ref = col, Size = g + 1, Z = z[g][a - 1][col] });
                    a--; b -= g + 1;
                }
                else if (bk == 1) a--;
                else b--;
            }
            res.Reverse();
            return res;
        }

        // Parar replikerna och gör två saker med resultatet: lägger enskilda rader på referensradens tid (ändrar outp),
        // och samlar förslag om att flytta repliker mellan rader (rep.Moves). tU/rU = replikerna i texten och i referensen (Units),
        // tVec/rVec = deras vektorer (null = för kort), rGroups[k][p] = vektor för referensreplik p och de k+1 följande.
        public static void RefineUnits(List<Cue> refc, List<Cue> outp, List<MeaningUnit> tU, float[][] tVec, List<MeaningUnit> rU, float[][] rVec, float[][][] rGroups, MeaningReport rep)
        {
            var pairs = PairUp(tVec, rVec, rGroups);
            // för varje replik i texten: referensraden där dess första referensreplik står (-1 = inget säkert par), om den står först där,
            // och referensraden där gruppens sista replik står
            int nU = tU.Count;
            var eRow = new int[nU]; var eFirst = new bool[nU]; var eLast = new int[nU];
            for (int u = 0; u < nU; u++) eRow[u] = -1;
            foreach (var p in pairs)
            {
                if (p.Z < AnchorSigmas) continue;
                int row = rU[p.Ref].Row;
                if (Math.Abs(refc[row].Start - outp[tU[p.Line].Row].Start) > MaxShift) continue;
                eRow[p.Line] = row; eFirst[p.Line] = rU[p.Ref].Part == 0; eLast[p.Line] = rU[p.Ref + p.Size - 1].Row;
            }
            rep.UnitPairs = eRow.Count(x => x >= 0);
            if (rep.UnitPairs < Math.Max(8, 0.1 * nU)) return;

            // ---- förslag: block av rader där replikerna är fördelade på ett annat sätt än i referensen
            // gräns efter replik u i texten (old) och enligt referensen (ny): old = nästa replik står i en annan rad
            for (int u0 = 0; u0 < nU; )
            {
                // ett block börjar vid en radgräns och slutar vid nästa ställe där texten och referensen båda har en gräns.
                // Utan säkert par på någon sida följs textens egna radgränser, och ett block med en sådan replik ger inget förslag.
                int u1 = u0; bool differs = false, known = eRow[u0] >= 0;
                while (u1 + 1 < nU)
                {
                    bool oldCut = tU[u1 + 1].Row != tU[u1].Row;
                    bool bothKnown = eRow[u1] >= 0 && eRow[u1 + 1] >= 0;
                    bool newCut = bothKnown ? eRow[u1 + 1] != eRow[u1] : oldCut;
                    if (oldCut && newCut) break;
                    if (oldCut != newCut) differs = true;
                    u1++;
                    if (eRow[u1] < 0) known = false;
                }
                if (differs && known)
                {
                    var move = MakeMove(refc, outp, tU, eRow, eLast, u0, u1);
                    if (move != null) rep.Moves.Add(move);
                }
                u0 = u1 + 1;
            }

            // ---- enskilda rader: första repliken hör till första repliken i en referensrad → raden börjar när referensraden börjar
            int[] order = Enumerable.Range(0, outp.Count).Where(i => outp[i].End > 0).OrderBy(i => outp[i].Start).ToArray();
            var firstUnit = new Dictionary<int, int>();
            for (int u = 0; u < nU; u++) if (tU[u].Part == 0) firstUnit[tU[u].Row] = u;
            // Skydd (satta efter mätningen mot Blacklist S04E01, där flyttar utan dem hamnade upp till 2,5 s fel):
            //  - parningen rad för rad (rep.Pairs) måste peka på samma referensrad som parningen av repliker,
            //  - referensraden måste själv säga något ("No." eller "Hey!" först i en grupp duger inte som starttid),
            //  - raden går aldrig in i raden före, och blir aldrig mycket kortare än den var.
            var claimed = new bool[refc.Count];   // referensrader som någon replik i texten har parats med
            foreach (var up in pairs)
                for (int q = up.Ref; q < up.Ref + up.Size && q < rU.Count; q++) claimed[rU[q].Row] = true;
            // referensrader som fortsätter en mening från raden före ("...your friend here was assisting him."): textens rad börjar
            // då oftast redan med början av meningen, och ska inte flyttas till fortsättningen
            var midSentence = new bool[refc.Count];
            int[] rOrder = Enumerable.Range(0, refc.Count).OrderBy(k => refc[k].Start).ToArray();
            for (int q = 0; q < rOrder.Length; q++)
            {
                string t = Clean(refc[rOrder[q]].Text).TrimStart('-', '–', ' ', '"', '\'');
                bool cont = t.StartsWith("...") || t.StartsWith("…") || (t.Length > 0 && char.IsLower(t[0]));
                if (!cont && q > 0 && refc[rOrder[q]].Start - refc[rOrder[q - 1]].End <= GroupGap) cont = LineAligner.EndsMidSentence(Clean(refc[rOrder[q - 1]].Text));
                midSentence[rOrder[q]] = cont;
            }
            var rowPair = new Dictionary<int, int>();
            foreach (var rp in rep.Pairs) if (rp.Z >= AnchorSigmas) rowPair[rp.Line] = rp.Ref;
            int N = order.Length;
            var oldS = order.Select(i => outp[i].Start).ToArray(); var oldE = order.Select(i => outp[i].End).ToArray();
            for (int p = 0; p < N; p++)
            {
                int u, rr;
                if (!firstUnit.TryGetValue(order[p], out u) || eRow[u] < 0 || !eFirst[u]) continue;
                if (!rowPair.TryGetValue(order[p], out rr) || rr != eRow[u]) continue;
                if (!Usable(Clean(refc[eRow[u]].Text), UnitLetters)) continue;
                if (midSentence[eRow[u]]) continue;
                // en rad som själv börjar mitt i en mening ("av sen 50-talsmodell.") hör ihop med raden före och flyttas inte
                string own = tU[u].Text.TrimStart('"', '\'', ' ');
                if (own.StartsWith("...") || own.StartsWith("…") || (own.Length > 0 && char.IsLower(own[0]))) continue;
                double target = refc[eRow[u]].Start, dev = target - oldS[p];
                if (Math.Abs(dev) < SnapMin || Math.Abs(dev) > SnapMax) continue;
                double ns, ne = oldE[p];
                if (dev < 0)
                {
                    // tidigare: bara fram till där raden före slutar. Slutet står kvar.
                    ns = Math.Max(target, p > 0 ? oldE[p - 1] : 0);
                    if (oldS[p] - ns < SnapMin) continue;
                }
                else
                {
                    // senare: inte om en referensrad som ingen replik har parats med börjar under tiden som raden skulle tappa.
                    // Då täcker raden troligen den referensraden också ("Livshotande träff." före "Han kan inte ha hunnit långt.").
                    bool covers = false;
                    for (int k = 0; k < refc.Count && !covers; k++)
                        covers = !claimed[k] && refc[k].Start >= oldS[p] - 300 && refc[k].Start < target - 100 && Clean(refc[k].Text).Length > 0;
                    if (covers) continue;
                    // slutet följer med så långt det går utan att gå in i nästa rad
                    ns = target;
                    double room = p + 1 < N ? Math.Max(oldE[p], oldS[p + 1]) : double.MaxValue;
                    ne = Math.Min(oldE[p] + dev, room);
                    if (ne - ns < Math.Max(MinSnapDur, 0.6 * (oldE[p] - oldS[p]))) continue;
                }
                outp[order[p]].Start = ns; outp[order[p]].End = ne;
                rep.Snapped++;
            }
        }

        // Ett förslag för replikerna u0..u1 (hela rader): nya rader efter referensens indelning, med referensradernas tider. null = går inte.
        static MeaningMove MakeMove(List<Cue> refc, List<Cue> outp, List<MeaningUnit> tU, int[] eRow, int[] eLast, int u0, int u1)
        {
            var oldRows = new List<int>();
            for (int u = u0; u <= u1; u++) if (oldRows.Count == 0 || oldRows[oldRows.Count - 1] != tU[u].Row) oldRows.Add(tU[u].Row);
            var groups = new List<List<int>>();
            for (int u = u0; u <= u1; u++)
            {
                if (groups.Count == 0 || eRow[u] != eRow[groups[groups.Count - 1][0]]) groups.Add(new List<int>());
                groups[groups.Count - 1].Add(u);
            }
            // bara omfördelning: rena delningar och sammanslagningar har egna förslag (Dela? och Ihop?)
            if (oldRows.Count < 2 || groups.Count < 2 || oldRows.Count > 4 || groups.Count > 4) return null;
            var cues = new List<Cue>(); var refs = new List<Cue>();
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                string text;
                if (g.Count == 1) text = LineAligner.Layout(tU[g[0]].Text);
                else if (g.Count == 2 && g.All(u => tU[u].Text.Length + 2 <= LineAligner.MaxChars)) text = "- " + tU[g[0]].Text + "\n- " + tU[g[1]].Text;
                else text = null;
                if (text == null) return null;
                var first = refc[eRow[g[0]]]; var last = refc[eLast[g[g.Count - 1]]];
                if (last.End < first.End) last = first;
                // tiden före synk finns bara för en ny rad som börjar där en gammal rad började
                cues.Add(new Cue(first.Start, last.End, text) { OrigStart = tU[g[0]].Part == 0 ? outp[tU[g[0]].Row].OrigStart : double.NaN });
                refs.Add(first);
            }
            // tiderna: i ordning, inte in i raderna utanför blocket, och minst MinSnapDur långa
            int[] order = Enumerable.Range(0, outp.Count).Where(i => outp[i].End > 0).OrderBy(i => outp[i].Start).ToArray();
            int pFirst = Array.IndexOf(order, oldRows[0]), pLast = Array.IndexOf(order, oldRows[oldRows.Count - 1]);
            if (pFirst < 0 || pLast < 0 || pLast - pFirst + 1 != oldRows.Count) return null;
            if (pFirst > 0) cues[0].Start = Math.Max(cues[0].Start, outp[order[pFirst - 1]].End);
            if (pLast + 1 < order.Length) cues[cues.Count - 1].End = Math.Min(cues[cues.Count - 1].End, outp[order[pLast + 1]].Start);
            for (int k = 0; k < cues.Count; k++)
            {
                if (k + 1 < cues.Count && cues[k].End > cues[k + 1].Start) cues[k].End = cues[k + 1].Start;
                if (cues[k].End - cues[k].Start < MinSnapDur) return null;
            }
            return new MeaningMove
            {
                OrigStarts = oldRows.Select(r => outp[r].OrigStart).ToArray(), Texts = oldRows.Select(r => outp[r].Text).ToArray(),
                NewCues = cues, Refs = refs.ToArray()
            };
        }

        class Anchor { public int Line, Ref; public double Delta; public int State; }

        // Vad det kostar att ett par ligger d ms från nivån: ingenting inom Free (översättningar börjar sällan exakt samtidigt),
        // sedan stigande till 1 vid Tol. Längre bort kostar lika mycket, så att ett enstaka par som avviker inte styr.
        static double Emit(double d)
        {
            return Math.Min(1, Math.Max(0, Math.Abs(d) - Free) / (Tol - Free));
        }

        // Avståndet från t till närmaste värde i den sorterade listan
        static double NearestDist(double[] sorted, double t)
        {
            int k = Array.BinarySearch(sorted, t);
            if (k >= 0) return 0;
            k = ~k;
            return Math.Min(k < sorted.Length ? sorted[k] - t : double.MaxValue, k > 0 ? t - sorted[k - 1] : double.MaxValue);
        }

        // Likheten mellan varje textrad (ti) och varje referensrad eller grupp (vecs, i ordningen ra), minus radens och kolumnens
        // medellikhet, uttryckt i standardavvikelser. Kolumner utan vektor får minus oändligheten. null = för få kolumner.
        static float[][] Scores(float[][] tgtVec, int[] ti, float[][] vecs, int[] ra, out double sigma)
        {
            sigma = 0;
            int n = ti.Length, m = ra.Length;
            int[] cols = Enumerable.Range(0, m).Where(b => vecs[ra[b]] != null).ToArray();
            if (cols.Length < 5) return null;
            var sim = new float[n][];
            Parallel.For(0, n, a =>
            {
                var row = new float[m]; var v = tgtVec[ti[a]];
                for (int b = 0; b < m; b++) row[b] = float.NegativeInfinity;
                foreach (int b in cols) row[b] = Dot(v, vecs[ra[b]]);
                sim[a] = row;
            });
            var rowMean = new double[n]; var colMean = new double[m];
            for (int a = 0; a < n; a++) foreach (int b in cols) { rowMean[a] += sim[a][b]; colMean[b] += sim[a][b]; }
            for (int a = 0; a < n; a++) rowMean[a] /= cols.Length;
            foreach (int b in cols) colMean[b] /= n;
            double sum = 0, sum2 = 0, cnt = (double)n * cols.Length;
            for (int a = 0; a < n; a++)
                foreach (int b in cols)
                {
                    float s = (float)(sim[a][b] - 0.5 * (rowMean[a] + colMean[b]));
                    sim[a][b] = s; sum += s; sum2 += (double)s * s;
                }
            double mean = sum / cnt;
            sigma = Math.Max(0.02, Math.Sqrt(Math.Max(0, sum2 / cnt - mean * mean)));
            for (int a = 0; a < n; a++) foreach (int b in cols) sim[a][b] = (float)((sim[a][b] - mean) / sigma);
            return sim;
        }

        // Texten för referensrad j tillsammans med de size-1 följande raderna i tidsordning ("" = ingen grupp).
        // Raderna måste följa tätt på varandra (under GroupGap), annars slår en översättning inte ihop dem.
        public static List<string> GroupTexts(List<Cue> refc, int size)
        {
            var res = refc.Select(c => "").ToList();
            int[] ra = Enumerable.Range(0, refc.Count).OrderBy(j => refc[j].Start).ToArray();
            for (int p = 0; p + size <= ra.Length; p++)
            {
                bool ok = true;
                var parts = new List<string>();
                for (int q = p; q < p + size && ok; q++)
                {
                    if (q > p && refc[ra[q]].Start - refc[ra[q - 1]].End > GroupGap) ok = false;
                    string t = Clean(refc[ra[q]].Text);
                    if (t.Length == 0) ok = false;
                    parts.Add(t);
                }
                if (ok) res[ra[p]] = Clean(string.Join(" ", parts));
            }
            return res;
        }

        // Parar ihop raderna efter betydelse och rättar tiderna. refVec[j] hör till refc[j], tgtVec[i] till synced[i];
        // null = raden jämförs inte (för kort). Returnerar en ny lista i samma ordning som synced.
        //
        // 1) Likhet mellan varje rad och varje referensrad (cosinus), minus radens och referensradens medellikhet.
        //    Det tar bort rader som liknar allt ("Vad menar du?"). Gränserna sätts efter spridningen i just de här texterna.
        // 2) Dynamisk programmering: bästa sättet att para ihop raderna utan att byta ordning på dem.
        // 3) Varje säkert par säger hur långt raden ligger från sin referensrad. En Viterbi över paren delar in texten
        //    i avsnitt med var sin nivå. Enstaka par som avviker (t.ex. en rad som svenskan har slagit ihop) räknas inte.
        // 4) Avsnitt där nivån är minst Deadband flyttas dit. Övriga rader lämnas precis som synkmotorn lade dem.
        //
        // refGroups[k][j] = vektor för referensrad j tillsammans med de k+1 följande raderna (se GroupTexts), null = ingen sådan grupp.
        // En översättning slår ofta ihop två eller tre rader i originalet. Då paras raden med hela gruppen, och avvikelsen
        // mäts mot gruppens första rad. Utan grupperna paras raden ibland med den andra raden, vilket ser ut som ett fel på en till tre sekunder.
        public static List<Cue> Refine(List<Cue> refc, List<Cue> synced, float[][] refVec, float[][] tgtVec, out MeaningReport rep, float[][][] refGroups = null)
        {
            rep = new MeaningReport();
            var outp = synced.Select(c => c.Clone()).ToList();
            int[] ti = Enumerable.Range(0, synced.Count).Where(i => tgtVec[i] != null && synced[i].End > 0).OrderBy(i => synced[i].Start).ToArray();
            int[] ra = Enumerable.Range(0, refc.Count).OrderBy(j => refc[j].Start).ToArray();   // alla referensrader i tidsordning
            int n = ti.Length, m = ra.Length;
            rep.Lines = n;
            if (n < 5 || refVec.Count(v => v != null) < 5) { rep.TooFew = true; return outp; }

            // 1) likhet, som antal standardavvikelser över det vanliga för de här två texterna. z[0] = enstaka rader, z[1] = två rader i följd, …
            int G = 1 + (refGroups == null ? 0 : refGroups.Length);
            var z = new float[G][][];
            for (int g = 0; g < G; g++)
            {
                double sg;
                z[g] = Scores(tgtVec, ti, g == 0 ? refVec : refGroups[g - 1], ra, out sg);
                if (g == 0) { rep.Sigma = sg; rep.MatchMin = MatchSigmas * sg; rep.AnchorMin = AnchorSigmas * sg; }
            }
            if (z[0] == null) { rep.TooFew = true; return outp; }

            // 2) bästa parningen i ordning: 1 = hoppa över textraden, 2 = hoppa över referensraden, 3 = par med en rad, 4 = med två rader, 5 = med tre
            var D = new float[n + 1][]; var back = new byte[n + 1][];
            for (int a = 0; a <= n; a++) { D[a] = new float[m + 1]; back[a] = new byte[m + 1]; }
            for (int a = 1; a <= n; a++)
            {
                for (int b = 1; b <= m; b++)
                {
                    float best = D[a - 1][b]; byte bk = 1;
                    if (D[a][b - 1] > best) { best = D[a][b - 1]; bk = 2; }
                    for (int g = 0; g < G && g < b; g++)
                    {
                        if (z[g] == null) continue;
                        float gain = z[g][a - 1][b - 1 - g] - (float)MatchSigmas;
                        if (gain > 0 && D[a - 1][b - 1 - g] + gain > best) { best = D[a - 1][b - 1 - g] + gain; bk = (byte)(3 + g); }
                    }
                    D[a][b] = best; back[a][b] = bk;
                }
            }
            var anchors = new List<Anchor>();
            for (int a = n, b = m; a > 0 && b > 0; )
            {
                int bk = back[a][b];
                if (bk >= 3)
                {
                    int g = bk - 3, col = b - 1 - g;   // gruppens första rad
                    rep.Paired++;
                    if (g > 0) rep.Grouped++;
                    double delta = refc[ra[col]].Start - synced[ti[a - 1]].Start;
                    rep.Pairs.Add(new MeaningPair { Line = ti[a - 1], Ref = ra[col], Size = g + 1, Delta = delta, Z = z[g][a - 1][col] });
                    if (z[g][a - 1][col] >= AnchorSigmas && Math.Abs(delta) <= MaxShift)
                        anchors.Add(new Anchor { Line = ti[a - 1], Ref = ra[col], Delta = delta });
                    a--; b -= g + 1;
                }
                else if (bk == 1) a--;
                else b--;
            }
            anchors.Reverse();
            rep.Pairs.Reverse();
            rep.Anchors = anchors.Count;
            if (anchors.Count < Math.Max(8, 0.1 * n)) { rep.TooFew = true; return outp; }

            // 3) nivåer: Viterbi över paren. Tillstånd = möjliga nivåer (parens egna avvikelser, avrundade).
            double[] lv = anchors.Select(x => Math.Round(x.Delta / LevelStep) * LevelStep).Distinct().OrderBy(x => x).ToArray();
            int K = lv.Length, A = anchors.Count;
            var cost = new double[K]; var from = new int[A][];
            for (int s = 0; s < K; s++) cost[s] = Emit(anchors[0].Delta - lv[s]);
            for (int k = 1; k < A; k++)
            {
                int arg = 0;
                for (int s = 1; s < K; s++) if (cost[s] < cost[arg]) arg = s;
                double jump = cost[arg] + JumpCost;
                var nc = new double[K]; from[k] = new int[K];
                for (int s = 0; s < K; s++)
                {
                    if (cost[s] <= jump) { nc[s] = cost[s]; from[k][s] = s; } else { nc[s] = jump; from[k][s] = arg; }
                    nc[s] += Emit(anchors[k].Delta - lv[s]);
                }
                cost = nc;
            }
            int st = 0;
            for (int s = 1; s < K; s++) if (cost[s] < cost[st]) st = s;
            for (int k = A - 1; k >= 0; k--) { anchors[k].State = st; if (k > 0) st = from[k][st]; }

            // nivån för varje avsnitt (par i följd med samma tillstånd): medianen av de par som stöder den
            var level = new double[A];
            var lvText = new List<string>();
            for (int k = 0; k < A; )
            {
                int e = k;
                while (e < A && anchors[e].State == anchors[k].State) e++;
                var inl = new List<double>();
                for (int q = k; q < e; q++) if (Math.Abs(anchors[q].Delta - lv[anchors[k].State]) < Tol) inl.Add(anchors[q].Delta);
                inl.Sort();
                double L = inl.Count >= MinSection ? inl[inl.Count / 2] : 0;
                if (Math.Abs(L) < Deadband) L = 0;
                for (int q = k; q < e; q++) level[q] = L;
                if (lvText.Count < 12) lvText.Add(L.ToString("0", CultureInfo.InvariantCulture) + "×" + inl.Count);
                k = e;
            }
            rep.Levels = string.Join(" ", lvText);

            // 4) flytten för varje rad, i tidsordning
            double[] refStarts = refc.Select(c => c.Start).OrderBy(x => x).ToArray();
            int[] order = Enumerable.Range(0, synced.Count).Where(i => synced[i].End > 0).OrderBy(i => synced[i].Start).ToArray();
            var pos = new Dictionary<int, int>();
            for (int p = 0; p < order.Length; p++) pos[order[p]] = p;
            var corr = new double[order.Length];
            int first = pos[anchors[0].Line], last = pos[anchors[A - 1].Line];
            for (int p = 0; p <= first; p++) corr[p] = level[0];
            for (int p = last; p < order.Length; p++) corr[p] = level[A - 1];
            for (int k = 0; k + 1 < A; k++)
            {
                int pa = pos[anchors[k].Line], pb = pos[anchors[k + 1].Line];
                int brk = pb;
                if (level[k] != level[k + 1] && pb - pa > 1)
                {
                    // raderna mellan paren har ingen egen parning: gränsen läggs där deras starter hamnar närmast referensens starter
                    double bestCost = double.MaxValue;
                    for (int cand = pa + 1; cand <= pb; cand++)
                    {
                        double c0 = 0;
                        for (int p = pa + 1; p < pb; p++)
                            c0 += Math.Min(1000, NearestDist(refStarts, synced[order[p]].Start + (p < cand ? level[k] : level[k + 1])));
                        if (c0 < bestCost) { bestCost = c0; brk = cand; }
                    }
                }
                for (int p = pa; p < pb; p++) corr[p] = p < brk ? level[k] : level[k + 1];
                corr[pb] = level[k + 1];
            }

            // raderna får inte byta ordning, och inte överlappa raden före mer än de gjorde innan (samma regel som synkmotorns finjustering)
            double prev = double.MinValue, prevEnd = double.MinValue, prevOldEnd = double.MinValue, lastCorr = 0;
            for (int p = 0; p < order.Length; p++)
            {
                var c = outp[order[p]];
                double d = corr[p];
                if (c.Start + d < 0) d = -c.Start;
                if (c.Start + d < prev) d = prev - c.Start;
                if (p > 0)
                {
                    double minStart = prevEnd - Math.Max(0, prevOldEnd - c.Start);
                    if (c.Start + d < minStart) d = minStart - c.Start;
                }
                prevOldEnd = c.End;
                c.Start += d; c.End += d;
                prev = c.Start; prevEnd = c.End;
                if (d != 0)
                {
                    rep.Moved++;
                    if (Math.Abs(d) > rep.MaxMs) rep.MaxMs = Math.Abs(d);
                    if (Math.Abs(d - lastCorr) > 1) rep.Places++;
                }
                lastCorr = d;
            }
            return outp;
        }
    }

    // Vektorer ("inbäddningar") för textrader, sparade i cachen så att en ny synk av samma text går direkt
    public static class Embedder
    {
        static readonly string CacheDir = Path.Combine(Log.Dir, "cache");
        const int Batch = 16;

        // En vektor per rad, null för rader som är för korta att jämföra. progress: 0–100. status: text medan modellen startar.
        // known = vektorer som redan är räknade i den här körningen (samma text ger samma vektor), fylls på med de nya.
        // minLetters = kortaste texten som jämförs (0 = som för hela rader).
        public static float[][] Get(List<string> texts, string model, LlamaServer srv, Action<int> progress, Action<string> status, CancellationToken ct,
            Dictionary<string, float[]> known = null, int minLetters = 0)
        {
            var res = new float[texts.Count][];
            var idx = Enumerable.Range(0, texts.Count).Where(i => minLetters > 0 ? Meaning.Usable(texts[i], minLetters) : Meaning.Usable(texts[i])).ToList();
            if (idx.Count == 0) return res;
            if (known == null) known = new Dictionary<string, float[]>();
            string cache = null;
            try
            {
                var fi = new FileInfo(model);
                // höj "v1" om llama.cpp-versionen eller flaggorna till servern (t.ex. --pooling) ändras
                string raw = fi.Name + "|" + fi.Length + "|v1\n" + string.Join("\n", idx.Select(i => texts[i]));
                using (var sha = SHA1.Create())
                    cache = Path.Combine(CacheDir, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-", "").Substring(0, 20) + ".emb");
                var got = ReadCache(cache, idx.Count);
                if (got != null)
                {
                    for (int k = 0; k < idx.Count; k++) { res[idx[k]] = got[k]; known[texts[idx[k]]] = got[k]; }
                    progress(100); return res;
                }
            }
            catch (Exception ex) { Log.Info("Cachen för betydelsejämförelsen kunde inte läsas: " + ex.Message); }

            // bara texter som inte redan har en vektor skickas till modellen
            var need = idx.Select(i => texts[i]).Where(t => !known.ContainsKey(t)).Distinct().ToList();
            if (need.Count > 0) srv.Start(status, ct);
            for (int k = 0; k < need.Count; k += Batch)
            {
                ct.ThrowIfCancellationRequested();
                var part = need.Skip(k).Take(Batch).ToList();
                var v = srv.Embed(part, ct);
                for (int q = 0; q < part.Count; q++) { Meaning.Normalize(v[q]); known[part[q]] = v[q]; }
                progress(Math.Min(100, 100 * (k + part.Count) / need.Count));
            }
            var all = idx.Select(i => known[texts[i]]).ToList();
            for (int k = 0; k < idx.Count; k++) res[idx[k]] = all[k];
            progress(100);
            try { if (cache != null) WriteCache(cache, all); } catch (Exception ex) { Log.Info("Cachen för betydelsejämförelsen kunde inte sparas: " + ex.Message); }
            return res;
        }

        public static void WriteCache(string path, List<float[]> v)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".part";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(v.Count); w.Write(v[0].Length);
                var buf = new byte[v[0].Length * 4];
                foreach (var x in v) { Buffer.BlockCopy(x, 0, buf, 0, buf.Length); w.Write(buf); }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // null om filen saknas eller inte stämmer med antalet rader
        public static float[][] ReadCache(string path, int count)
        {
            if (!File.Exists(path)) return null;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                int n = r.ReadInt32(), dim = r.ReadInt32();
                if (n != count || dim <= 0 || dim > 16384 || r.BaseStream.Length != 8 + (long)n * dim * 4) return null;
                var res = new float[n][];
                for (int i = 0; i < n; i++)
                {
                    var b = r.ReadBytes(dim * 4);
                    res[i] = new float[dim];
                    Buffer.BlockCopy(b, 0, res[i], 0, b.Length);
                }
                return res;
            }
        }
    }

    // llama-server från llama.cpp, startad dold och bara nåbar från den här datorn (127.0.0.1).
    // Paketet för Windows innehåller inget eget program för inbäddningar, därför används servern.
    // Den startas först när något saknas i cachen, och stängs när jämförelsen är klar.
    public sealed class LlamaServer : IDisposable
    {
        readonly string exe, model;
        Process proc;
        int port;
        readonly Queue<string> tail = new Queue<string>();

        public LlamaServer(string exe, string model) { this.exe = exe; this.model = model; }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            try { return ((IPEndPoint)l.LocalEndpoint).Port; } finally { l.Stop(); }
        }

        string Tail() { lock (tail) return string.Join("\n", tail); }

        public void Start(Action<string> status, CancellationToken ct)
        {
            if (proc != null) return;
            if (status != null) status("Startar språkmodellen…");
            port = FreePort();
            int threads = Math.Max(1, Math.Min(16, Environment.ProcessorCount - 2));
            string args = "-m \"" + Audio.AnsiSafePath(model) + "\" --embedding --pooling cls --host 127.0.0.1 --port " + port + " --no-webui -t " + threads;
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe),
                RedirectStandardError = true, RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
            };
            var p = new Process { StartInfo = psi };
            DataReceivedEventHandler h = (s, e) =>
            {
                if (e.Data == null) return;
                lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 30) tail.Dequeue(); }
            };
            p.ErrorDataReceived += h; p.OutputDataReceived += h;
            try { p.Start(); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                p.Dispose();
                throw new UserMessageException("Windows stoppade verktyget llama-server.exe (" + ex.Message.TrimEnd('.') + "). " +
                    "Det beror oftast på Smart App Control i Windows-säkerhet, som blockerar program som inte är signerade. " +
                    "Kryssa ur \"Jämför också vad raderna betyder\" i Inställningar om det händer varje gång.");
            }
            proc = p;
            KillWithParent(p);
            p.BeginErrorReadLine(); p.BeginOutputReadLine();
            Log.Info("llama-server startad (port " + port + ", " + threads + " trådar)");
            var sw = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (p.HasExited) throw new InvalidOperationException("Språkmodellen kunde inte startas (llama-server avslutades med kod " + p.ExitCode + "). Slutet av utskriften:\n" + Tail());
                if (Healthy()) break;
                if (sw.Elapsed.TotalSeconds > 180) throw new UserMessageException("Språkmodellen startade inte inom tre minuter.");
                Thread.Sleep(300);
            }
            Log.Info("Språkmodellen laddad på " + sw.ElapsedMilliseconds + " ms");
        }

        bool Healthy()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/health");
                req.Proxy = null; req.Timeout = 2000;
                using (var resp = (HttpWebResponse)req.GetResponse()) return resp.StatusCode == HttpStatusCode.OK;
            }
            catch (WebException) { return false; }
        }

        // En vektor per text, i samma ordning som texterna
        public float[][] Embed(List<string> texts, CancellationToken ct)
        {
            byte[] body = Encoding.UTF8.GetBytes(BuildRequest(texts));
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/v1/embeddings");
            req.Proxy = null; req.Method = "POST"; req.ContentType = "application/json";
            req.ServicePoint.Expect100Continue = false;   // skicka frågan direkt, utan att först vänta på ett "fortsätt" från servern
            req.Timeout = 600000; req.ReadWriteTimeout = 600000;
            string json;
            try
            {
                using (ct.Register(() => { try { req.Abort(); } catch { } }))
                {
                    using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = rd.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
                string detail = ex.Message;
                try { if (ex.Response != null) using (var rd = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8)) detail += " " + rd.ReadToEnd(); } catch { }
                throw new InvalidOperationException("Språkmodellen svarade inte som väntat: " + detail + "\n" + Tail(), ex);
            }
            return ParseResponse(json, texts.Count);
        }

        // {"input":["…","…"],"encoding_format":"float"}
        public static string BuildRequest(List<string> texts)
        {
            var sb = new StringBuilder("{\"input\":[");
            for (int i = 0; i < texts.Count; i++) { if (i > 0) sb.Append(','); JsonString(sb, texts[i]); }
            return sb.Append("],\"encoding_format\":\"float\"}").ToString();
        }

        // Allt utanför ASCII skrivs som \uXXXX, så att teckenkodningen aldrig spelar någon roll. Trasiga teckenpar byts mot "?".
        static void JsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                { sb.Append("\\u").Append(((int)c).ToString("x4")).Append("\\u").Append(((int)s[i + 1]).ToString("x4")); i++; }
                else if (char.IsSurrogate(c)) sb.Append('?');
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20 || c > 0x7e) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // Svaret är som OpenAI:s: {"data":[{"index":0,"embedding":[…]},…]}. Vektorerna läggs efter index, inte efter ordningen i svaret.
        public static float[][] ParseResponse(string json, int count)
        {
            var root = Json.Parse(json) as Dictionary<string, object>;
            var data = root != null && root.ContainsKey("data") ? root["data"] as List<object> : null;
            if (data == null || data.Count != count) throw new InvalidDataException("Oväntat svar från språkmodellen (" + (data == null ? "inga" : data.Count.ToString()) + " vektorer, väntade " + count + ").");
            var res = new float[count][];
            int dim = -1;
            foreach (var o in data)
            {
                var d = o as Dictionary<string, object>;
                var emb = d != null && d.ContainsKey("embedding") ? d["embedding"] as List<object> : null;
                if (emb == null || !d.ContainsKey("index") || !(d["index"] is double)) throw new InvalidDataException("Oväntat svar från språkmodellen (en vektor saknas).");
                int i = (int)(double)d["index"];
                if (i < 0 || i >= count || res[i] != null) throw new InvalidDataException("Oväntat svar från språkmodellen (fel numrering).");
                if (dim < 0) dim = emb.Count;
                if (emb.Count != dim || dim == 0) throw new InvalidDataException("Oväntat svar från språkmodellen (vektorerna är olika långa).");
                var v = new float[dim];
                for (int k = 0; k < dim; k++)
                {
                    if (!(emb[k] is double)) throw new InvalidDataException("Oväntat svar från språkmodellen (ett värde är inte ett tal).");
                    v[k] = (float)(double)emb[k];
                }
                res[i] = v;
            }
            return res;
        }

        public void Dispose()
        {
            var p = proc; proc = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
        }

        // ---- servern stängs av Windows om Text Sync avslutas oväntat (annars ligger den kvar dold med modellen i minnet)
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attr, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static IntPtr job = IntPtr.Zero;
        static readonly object jobLock = new object();

        static void KillWithParent(Process p)
        {
            try
            {
                lock (jobLock)
                {
                    if (job == IntPtr.Zero)
                    {
                        IntPtr j = CreateJobObject(IntPtr.Zero, null);
                        if (j == IntPtr.Zero) return;
                        var info = new ExtendedLimits();
                        info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        int len = Marshal.SizeOf(typeof(ExtendedLimits));
                        IntPtr mem = Marshal.AllocHGlobal(len);
                        try
                        {
                            Marshal.StructureToPtr(info, mem, false);
                            if (!SetInformationJobObject(j, 9, mem, (uint)len)) return;   // 9 = JobObjectExtendedLimitInformation
                        }
                        finally { Marshal.FreeHGlobal(mem); }
                        job = j;   // handtaget stängs aldrig: det stängs av Windows när Text Sync avslutas, och då stängs servern
                    }
                    AssignProcessToJobObject(job, p.Handle);
                }
            }
            catch (Exception ex) { Log.Info("llama-server kunde inte knytas till Text Sync: " + ex.Message); }
        }
    }

    // Liten JSON-läsare (objekt → Dictionary, lista → List<object>, tal → double, text → string, true/false → bool, null → null)
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new InvalidDataException("Ogiltig JSON (tecken efter slutet).");
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new InvalidDataException("Ogiltig JSON (tog slut för tidigt).");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new InvalidDataException("Ogiltig JSON (namn saknas).");
                    string key = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new InvalidDataException("Ogiltig JSON (kolon saknas).");
                    i++;
                    d[key] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new InvalidDataException("Ogiltig JSON (objektet avslutas inte).");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new InvalidDataException("Ogiltig JSON (listan avslutas inte).");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            double v;
            if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new InvalidDataException("Ogiltig JSON (okänt värde).");
            return v;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                if (e == 'n') sb.Append('\n');
                else if (e == 't') sb.Append('\t');
                else if (e == 'r') sb.Append('\r');
                else if (e == 'b') sb.Append('\b');
                else if (e == 'f') sb.Append('\f');
                else if (e == 'u' && i + 4 <= s.Length) { sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; }
                else sb.Append(e);
            }
            if (i >= s.Length) throw new InvalidDataException("Ogiltig JSON (texten avslutas inte).");
            i++;
            return sb.ToString();
        }
    }
}
