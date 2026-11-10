// Text Sync - HTML-kodade tecken (&amp;) och formateringstaggar (<i>, <b>, <u>, <s>, <font>) i undertexter
// Alltid: tecknen avkodas och trasiga taggar lagas. Som ett val: all formatering tas bort.
// Grundregel: en text utan kodade tecken och med hela taggar lämnas exakt som den är.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TextSync
{
    public static class Markup
    {
        // &amp;  &#39;  &#x27;  (måste sluta med semikolon, så att "AT&T" och "Q&A" står kvar)
        static readonly Regex Entity = new Regex(@"&(?:#(\d{1,7})|#[xX]([0-9a-fA-F]{1,6})|([A-Za-z][A-Za-z0-9]{1,31}));");
        // Bara rena taggar som hör hemma i SRT räknas som taggar. em och strong (från webbsidor) görs om till i och b.
        // Vanlig text inom vinkelparenteser är inte taggar: "<I morgon åker vi.>", "a < b > c", "a<b och c>d", "I <3 you".
        // Därför: inget blanksteg direkt efter <, inga attribut utom i <font>, och <font> måste ha attribut med = (eller inga alls).
        // Antalet blanksteg är begränsat, så att mönstret inte kan bli långsamt.
        static readonly Regex Tag = new Regex(
            @"<(?:(/)[ \t]{0,2})?(?:(i|b|u|s|em|strong|strike|del)[ \t]{0,2}(/?)|(font)((?:\s(?:[^<>""]|""[^""<>]*"")*)?))>", RegexOptions.IgnoreCase);
        // Rester från WebVTT som ingen spelare tolkar i en SRT. Tas bara bort när det syns att det är WebVTT:
        // <c.yellow>text</c> och <v Anna>text</v> (stängningen måste finnas), <ruby>/<rt> och tidskoder som <00:01:02.500>.
        static readonly Regex VttClass = new Regex(@"</?c(?:\.[\w.\-]+)?>");
        static readonly Regex VttVoice = new Regex(@"<v\s[^<>]{1,60}>|</v>");
        static readonly Regex VttOther = new Regex(@"</?(?:ruby|rt)>|<\d{1,2}:\d{2}(?::\d{2})?[.,]\d{3}>");
        // <br>, <br/>, <BR />: radbrytning i HTML. Ingen spelare tolkar den i en SRT, så den görs om till en riktig radbrytning.
        static readonly Regex Br = new Regex(@"<br[ \t]{0,2}/?>", RegexOptions.IgnoreCase);
        // Koder från ASS-formatet: {\an8} (text överst), {\i1}, {\pos(10,20)}
        static readonly Regex AssTag = new Regex(@"\{\\[^{}]*\}");
        static readonly Regex EmptyPair = new Regex(@"<(i|b|u|s|font)(?:\s[^<>]*)?>(\s*)</\1>");
        static readonly Regex Spaces = new Regex(@"[ \t]{2,}");
        const int MaxReopen = 4;   // så många öppna taggar stängs och öppnas igen för att rätta ordningen (skydd mot orimlig indata)

        // "Tom &amp; Jerry" → "Tom & Jerry". Okända koder ("&abc;") står kvar.
        public static string DecodeEntities(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;
            // kodat två gånger förekommer: &amp;amp; och &amp;lt;i&amp;gt;
            for (int pass = 0; pass < 3; pass++)
            {
                string d = Entity.Replace(s, m =>
                {
                    string v;
                    if (m.Groups[1].Success || m.Groups[2].Success)
                    {
                        int cp;
                        bool ok = m.Groups[1].Success ? int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out cp)
                            : int.TryParse(m.Groups[2].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out cp);
                        // utanför Unicode, eller en halva av ett teckenpar: står kvar
                        if (!ok || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return m.Value;
                        v = char.ConvertFromUtf32(cp);
                    }
                    else
                    {
                        v = WebUtility.HtmlDecode(m.Value);
                        // &AMP; och &Amp;: de vanligaste koderna gäller oavsett stora och små bokstäver
                        if (v == m.Value)
                        {
                            string low = m.Value.ToLowerInvariant();
                            if (low == "&amp;" || low == "&lt;" || low == "&gt;" || low == "&quot;" || low == "&apos;" || low == "&nbsp;") v = WebUtility.HtmlDecode(low);
                        }
                    }
                    if (v == " ") return " ";                                      // &nbsp; blir ett vanligt blanksteg
                    if (v.Length == 1 && char.IsControl(v[0])) return v == "\n" ? v : m.Value;   // styrtecken hör inte hemma i texten
                    return v;
                });
                if (d == s) break;
                s = d;
            }
            return s;
        }

        // En träff på Tag som verkligen är en tagg. <font utan attribut med => är vanlig text ("<font size>").
        static bool IsTag(Match m, out string name, out bool close, out string attrs)
        {
            bool font = m.Groups[4].Success;
            name = (font ? "font" : m.Groups[2].Value).ToLowerInvariant();
            if (name == "em") name = "i"; else if (name == "strong") name = "b"; else if (name == "strike" || name == "del") name = "s";
            attrs = font ? m.Groups[5].Value.Trim() : "";
            // <i/> är en felskriven </i>
            close = m.Groups[1].Value == "/" || (!font && m.Groups[3].Value == "/");
            if (font && !close && attrs.Length > 0 && attrs.IndexOf('=') < 0) return false;
            return true;
        }

        static string RemoveVtt(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            if (s.Contains("</c>")) s = VttClass.Replace(s, "");
            if (s.Contains("</v>")) s = VttVoice.Replace(s, "");
            return VttOther.Replace(s, "");
        }

        class Open { public string Name, Text; }

        // Lagar taggarna i en hel cue (taggar får gå över flera rader):
        //   <I>, </ i>, <em>              → <i>, </i>
        //   <i>text                       → <i>text</i>             (inte stängd: stängs sist)
        //   text</i>                      → <i>text</i>             (inte öppnad: öppnas först)
        //   <i><b>text</i></b>            → <i><b>text</b></i>      (fel ordning)
        //   <i><i>text</i>, <i></i>       → <i>text</i>, tomt       (dubbla och tomma)
        //   <i>text</i> mer</i>           → <i>text</i> mer         (en stängning för mycket)
        static string RepairTags(string text)
        {
            var sb = new StringBuilder(text.Length + 16);
            var open = new List<Open>();                      // öppna taggar, den yttersta först
            var extra = new Dictionary<string, int>();        // dubbla öppningar som hoppades över: deras stängningar hoppas också över
            var seen = new HashSet<string>();                 // taggar som har öppnats någon gång i texten
            string prefix = "";                               // öppningar som saknades och läggs först i texten
            int pos = 0;
            foreach (Match m in Tag.Matches(text))
            {
                string name, attrs; bool close;
                if (!IsTag(m, out name, out close, out attrs)) continue;   // vanlig text: följer med i nästa Append
                sb.Append(text, pos, m.Index - pos);
                pos = m.Index + m.Length;
                int n;
                if (!close)
                {
                    // <font> får ligga i varandra (färg i storlek), de andra har ingen mening två gånger
                    if (name != "font" && open.Any(o => o.Name == name)) { extra.TryGetValue(name, out n); extra[name] = n + 1; continue; }
                    string tag = name == "font" && attrs.Length > 0 ? "<font " + attrs + ">" : "<" + name + ">";
                    open.Add(new Open { Name = name, Text = tag });
                    seen.Add(name);
                    sb.Append(tag);
                    continue;
                }
                if (extra.TryGetValue(name, out n) && n > 0) { extra[name] = n - 1; continue; }
                int k = open.FindLastIndex(o => o.Name == name);
                int inner = open.Count - k - 1;               // taggar som öppnades senare och fortfarande är öppna
                // en stängning för mycket (taggen har redan öppnats och stängts) tas bort, liksom stängningar i orimligt djupa taggar
                if ((k < 0 && seen.Contains(name)) || inner > MaxReopen) continue;
                // taggar som öppnades senare stängs först och öppnas igen efteråt, så att ordningen blir rätt
                for (int j = open.Count - 1; j > k; j--) sb.Append("</" + open[j].Name + ">");
                sb.Append("</" + name + ">");
                for (int j = k + 1; j < open.Count; j++) sb.Append(open[j].Text);
                if (k < 0) { prefix = "<" + name + ">" + prefix; seen.Add(name); }   // stängd utan att vara öppnad: gäller från textens början
                else open.RemoveAt(k);
            }
            sb.Append(text, pos, text.Length - pos);
            for (int j = open.Count - 1; j >= 0; j--) sb.Append("</" + open[j].Name + ">");
            string res = sb.ToString();
            if (prefix.Length > 0)
            {
                // efter positionskoder som {\an8}, som måste stå allra först
                int at = 0;
                Match a;
                while ((a = AssTag.Match(res, at)).Success && a.Index == at) at += a.Length;
                res = res.Insert(at, prefix);
            }
            for (int pass = 0; pass < 8; pass++)
            {
                string d = EmptyPair.Replace(res, "$2");
                if (d == res) break;
                res = d;
            }
            return res;
        }

        // Rader som blev tomma tas bort, och blanksteg som blev över städas. Bara när texten har ändrats.
        static string Tidy(string text)
        {
            var lines = text.Split('\n').Select(l => Spaces.Replace(l, " ").Trim()).Where(l => l.Length > 0);
            return string.Join("\n", lines);
        }

        // Det som alltid görs med en text: kodade tecken blir riktiga tecken, och taggarna lagas.
        public static string Fix(string text)
        {
            bool e, t;
            return Fix(text, out e, out t);
        }

        public static string Fix(string text, out bool entities, out bool tags)
        {
            entities = false; tags = false;
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string src = text.Replace("\r\n", "\n");
            string s = DecodeEntities(src);
            entities = s != src;
            string r = s;
            if (s.IndexOf('<') >= 0)
            {
                r = RepairTags(RemoveVtt(Br.Replace(s, "\n")));
                tags = r != s;
            }
            return entities || tags ? Tidy(r) : src;
        }

        // Texten utan formatering: kursiv, fetstil, understruken, genomstruken, färg/typsnitt och koder som {\an8}.
        // För spelare (oftast TV-apparater) som visar taggarna som text.
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (text.IndexOf('<') < 0 && text.IndexOf('{') < 0) return text;
            string r = Tag.Replace(text, m =>
            {
                string name, attrs; bool close;
                return IsTag(m, out name, out close, out attrs) ? "" : m.Value;
            });
            r = AssTag.Replace(RemoveVtt(r), "");
            return r == text ? text : Tidy(r);
        }

        public static List<Cue> FixAll(List<Cue> cues)
        {
            int e, t, r;
            return FixAll(cues, out e, out t, out r);
        }

        // Rättar alla rader. En rättad rad får Raw = texten som stod i filen, så att listan kan visa vad som ändrades.
        // entities = rader där kodade tecken rättades, tags = rader där taggar lagades,
        // removed = rader som togs bort för att ingen text blev kvar (de bestod bara av formatering eller blanksteg).
        public static List<Cue> FixAll(List<Cue> cues, out int entities, out int tags, out int removed)
        {
            entities = 0; tags = 0; removed = 0;
            var res = new List<Cue>(cues.Count);
            foreach (var c in cues)
            {
                bool e, t;
                string s = Fix(c.Text, out e, out t);
                if (!e && !t) { res.Add(c); continue; }
                if (s.Trim().Length == 0) { removed++; continue; }
                if (e) entities++;
                if (t) tags++;
                var n = c.Clone(); n.Text = s; n.Raw = (c.Text ?? "").Replace("\r\n", "\n"); res.Add(n);
            }
            return res;
        }

        // Tar bort formateringen ur alla rader. Antalet rader och ordningen ändras inte. changed = rader som ändrades.
        public static List<Cue> StripAll(List<Cue> cues, out int changed)
        {
            changed = 0;
            var res = new List<Cue>(cues.Count);
            foreach (var c in cues)
            {
                string s = Strip(c.Text);
                if (s != (c.Text ?? "")) { changed++; var n = c.Clone(); n.Text = s; res.Add(n); }
                else res.Add(c);
            }
            return res;
        }

        // Besked efter inläsningen, t.ex. "Rättat: kodade tecken (t.ex. &amp;) på 14 rader, trasig formatering (t.ex. <i> utan avslut) på 3 rader."
        // Tomt om inget rättades. Visas i en Label: den måste ha UseMnemonic = false, annars blir "&a" ett understruket a.
        public static string Summary(int entities, int tags) { return Summary(entities, tags, 0); }

        public static string Summary(int entities, int tags, int removed)
        {
            string s = Summary2(entities, tags);
            if (removed > 0) s += (s.Length > 0 ? " " : "") + (removed == 1 ? "1 rad utan text borttagen." : removed + " rader utan text borttagna.");
            return s;
        }

        static string Summary2(int entities, int tags)
        {
            var parts = new List<string>();
            if (entities > 0) parts.Add("kodade tecken (t.ex. &amp;) på " + entities + (entities == 1 ? " rad" : " rader"));
            if (tags > 0) parts.Add("trasig formatering (t.ex. <i> utan avslut) på " + tags + (tags == 1 ? " rad" : " rader"));
            return parts.Count == 0 ? "" : "Rättat: " + string.Join(", ", parts) + ".";
        }
    }
}
