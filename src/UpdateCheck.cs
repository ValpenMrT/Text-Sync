using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TextSync
{
    // Kontrollerar vid start om det finns en nyare version bland releaserna på GitHub, och hämtar vad som är ändrat i varje version
    // (bannern i huvudfönstret och fönstret Nyheter, NewsDialog).
    // Ingenting laddas ner av programmet: bannern i huvudfönstret öppnar filen i webbläsaren, som laddar ner den.
    // Går kontrollen inte att göra (inget nät, GitHub svarar inte) händer ingenting, och det står i loggen.
    public static class UpdateCheck
    {
        public const string ReleasesUrl = "https://github.com/ValpenMrT/Text-Sync/releases";
        // Bara filer som ligger bland projektets egna releaser får öppnas från bannern.
        const string DownloadPrefix = ReleasesUrl + "/download/";

        public class Release
        {
            public string Version;   // till exempel "2.4.30"
            public string Url;       // exe-filen i releasen, eller sidan med releaserna om filen inte hittades
            public string Notes = "";   // vad som är ändrat i versionen, som vanlig text (CleanNotes)
        }

        // Läser versionen (taggen "v2.4.30") och exe-filens adress ur GitHubs svar. Null om svaret inte går att tolka.
        public static Release Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var t = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d+(?:\\.\\d+){0,3})\"");
            if (!t.Success) return null;
            var r = new Release { Version = t.Groups[1].Value, Url = ReleasesUrl + "/latest" };
            var u = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"\\\\]+\\.exe)\"");
            if (u.Success && u.Groups[1].Value.StartsWith(DownloadPrefix, StringComparison.Ordinal)) r.Url = u.Groups[1].Value;
            return r;
        }

        // Sant om "latest" är en högre version än "current". 2.5 och 2.5.0 räknas som samma version.
        public static bool IsNewer(string latest, string current)
        {
            int[] a = Parts(latest), b = Parts(current);
            if (a == null || b == null) return false;
            for (int i = 0; i < 4; i++) if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }

        static int[] Parts(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var p = v.Trim().TrimStart('v', 'V').Split('.');
            if (p.Length > 4) return null;
            var r = new int[4];
            for (int i = 0; i < p.Length; i++) if (!int.TryParse(p[i], out r[i]) || r[i] < 0) return null;
            return r;
        }

        // ---- Nyheter: vad som är ändrat i varje version. Texten i en release är det GitHub-jobbet skrev (release.yml):
        // en fast inledning om nedladdningen, rubriken nedan och sedan commit-meddelandet.
        const string ListUrl = "https://api.github.com/repos/ValpenMrT/Text-Sync/releases?per_page=30";
        const string NotesHeading = "Ändrat i den här versionen";   // "## Ändrat …" i API:ets text, en rubrik utan ## i flödets

        // Det som är ändrat i versionen, som vanlig text: inledningen och rubriken tas bort, liksom raden Co-Authored-By
        // och versionsnumret först i commit-meddelandet ("2.4.30: listan syns …" blir "Listan syns …").
        public static string CleanNotes(string body, string version)
        {
            if (string.IsNullOrEmpty(body)) return "";
            string s = body.Replace("\r\n", "\n").Replace('\r', '\n');
            int at = s.IndexOf(NotesHeading, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) s = s.Substring(at + NotesHeading.Length);
            var lines = new System.Collections.Generic.List<string>();
            foreach (string l in s.Split('\n'))
            {
                string t = l.TrimEnd();
                if (t.TrimStart().StartsWith("Co-Authored-By:", StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Length == 0 && (lines.Count == 0 || lines[lines.Count - 1].Length == 0)) continue;   // högst en tom rad i följd
                lines.Add(t);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count == 0) return "";
            string first = Regex.Replace(lines[0], @"^\s*v?" + Regex.Escape(version ?? "") + @"\s*[:–-]\s*", "", RegexOptions.IgnoreCase).TrimStart();
            if (first.Length > 0) first = char.ToUpper(first[0], System.Globalization.CultureInfo.GetCultureInfo("sv-SE")) + first.Substring(1);
            lines[0] = first;
            return string.Join("\n", lines).Trim();
        }

        // Alla releaser i GitHubs svar (en lista), den nyaste versionen först. Utkast och förhandsversioner tas inte med.
        // En tom lista om svaret inte går att tolka.
        public static System.Collections.Generic.List<Release> ParseList(string json)
        {
            var res = new System.Collections.Generic.List<Release>();
            if (string.IsNullOrEmpty(json)) return res;
            try
            {
                var arr = Json.Parse(json) as System.Collections.Generic.List<object>;
                if (arr == null) return res;
                foreach (object o in arr)
                {
                    var d = o as System.Collections.Generic.Dictionary<string, object>;
                    if (d == null) continue;
                    object v;
                    if (d.TryGetValue("draft", out v) && v is bool && (bool)v) continue;
                    if (d.TryGetValue("prerelease", out v) && v is bool && (bool)v) continue;
                    string tag = d.TryGetValue("tag_name", out v) ? v as string : null;
                    if (tag == null || Parts(tag) == null || !Regex.IsMatch(tag, @"^v?\d+(\.\d+){0,3}$")) continue;
                    var r = new Release { Version = tag.TrimStart('v', 'V'), Url = ReleasesUrl + "/latest" };
                    r.Notes = CleanNotes(d.TryGetValue("body", out v) ? v as string : null, r.Version);
                    var assets = d.TryGetValue("assets", out v) ? v as System.Collections.Generic.List<object> : null;
                    if (assets != null)
                        foreach (object a in assets)
                        {
                            var ad = a as System.Collections.Generic.Dictionary<string, object>;
                            string u = ad != null && ad.TryGetValue("browser_download_url", out v) ? v as string : null;
                            if (u != null && u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && u.StartsWith(DownloadPrefix, StringComparison.Ordinal)) { r.Url = u; break; }
                        }
                    res.Add(r);
                }
            }
            catch (Exception) { return new System.Collections.Generic.List<Release>(); }
            res.Sort((a, b) => IsNewer(a.Version, b.Version) ? -1 : IsNewer(b.Version, a.Version) ? 1 : 0);
            return res;
        }

        // Antal versioner i listan som är nyare än "current"
        public static int NewerCount(System.Collections.Generic.List<Release> all, string current)
        {
            int n = 0;
            if (all != null) foreach (var r in all) if (IsNewer(r.Version, current)) n++;
            return n;
        }

        // Första raden av det som är ändrat i versionen, högst max tecken (för bannern)
        public static string FirstLine(Release r, int max)
        {
            string s = r == null || r.Notes == null ? "" : r.Notes.Split('\n')[0].Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";
        }

        // Texten i fönstret Nyheter: en rubrik per version, den nyaste först, och under den vad som är ändrat.
        // Versioner som är nyare än "current" är märkta (ny), och den egna versionen (den du har).
        public static string NewsText(System.Collections.Generic.List<Release> all, string current)
        {
            var sb = new StringBuilder();
            if (all == null) return "";
            foreach (var r in all)
            {
                bool newer = IsNewer(r.Version, current), same = !newer && !IsNewer(current, r.Version);
                if (sb.Length > 0) sb.Append("\r\n\r\n");
                sb.Append("Version ").Append(r.Version).Append(newer ? "  (ny)" : same ? "  (den du har)" : "").Append("\r\n");
                sb.Append(string.IsNullOrEmpty(r.Notes) ? "Ingen beskrivning." : r.Notes.Replace("\n", "\r\n"));
            }
            return sb.ToString();
        }

        // ---- Reservväg: releasernas flöde på github.com (Atom). GitHubs API svarar bara på 60 frågor i timmen per nätverksadress
        // utan inloggning, och den gränsen delas av alla datorer bakom samma router. Flödet har ingen sådan gräns.
        // Det innehåller de senaste releaserna med texten som HTML.
        const string FeedUrl = ReleasesUrl + ".atom";
        static readonly Regex FeedEntry = new Regex(@"<entry>(.*?)</entry>", RegexOptions.Singleline);
        static readonly Regex FeedTag = new Regex(@"/releases/tag/(v?\d+(?:\.\d+){0,3})""");
        static readonly Regex FeedContent = new Regex(@"<content[^>]*>(.*?)</content>", RegexOptions.Singleline);

        // HTML från flödet → vanlig text: <br> och slutet på ett stycke blir radbrytningar, punktlistor får streck
        static string HtmlToText(string html)
        {
            string s = Regex.Replace(html.Replace("\r", ""), @"<br\s*/?>\s*", "\u0001", RegexOptions.IgnoreCase).Replace("\n", " ");
            s = Regex.Replace(s, @"<li[^>]*>\s*", "- ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*</li>\s*", "\u0001", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*</(?:p|h\d|ul|ol)>\s*", "\u0001\u0001", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^<>]+>", "");
            return Regex.Replace(WebUtility.HtmlDecode(s).Replace('\u0001', '\n'), @"[ \t]*\n[ \t]*", "\n");
        }

        // Releaserna i flödet, den nyaste versionen först. En tom lista om flödet inte går att tolka.
        public static System.Collections.Generic.List<Release> ParseFeed(string xml)
        {
            var res = new System.Collections.Generic.List<Release>();
            if (string.IsNullOrEmpty(xml)) return res;
            try
            {
                foreach (Match e in FeedEntry.Matches(xml))
                {
                    var t = FeedTag.Match(e.Groups[1].Value);
                    if (!t.Success || Parts(t.Groups[1].Value) == null) continue;
                    string tag = t.Groups[1].Value, ver = tag.TrimStart('v', 'V');
                    // exe-filen heter alltid TextSync-<version>.exe och ligger under taggen (release.yml)
                    var r = new Release { Version = ver, Url = DownloadPrefix + tag + "/TextSync-" + ver + ".exe" };
                    var c = FeedContent.Match(e.Groups[1].Value);
                    if (c.Success) r.Notes = CleanNotes(HtmlToText(WebUtility.HtmlDecode(c.Groups[1].Value)), ver);
                    res.Add(r);
                }
            }
            catch (Exception) { return new System.Collections.Generic.List<Release>(); }
            res.Sort((a, b) => IsNewer(a.Version, b.Version) ? -1 : IsNewer(b.Version, a.Version) ? 1 : 0);
            return res;
        }

        static string Download(string url, string accept)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "TextSync/" + Program.Version;
            req.Accept = accept;
            req.Timeout = 10000; req.ReadWriteTimeout = 10000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return rd.ReadToEnd();
        }

        // Hämtar listan över releaser från GitHub, den nyaste först: först från API:et, och går det inte (t.ex. för många frågor
        // från samma nätverksadress) från releasernas flöde. null om inget av dem gick (inget nät, GitHub svarar inte).
        // Körs i en bakgrundstråd. Kastar aldrig.
        public static System.Collections.Generic.List<Release> Fetch()
        {
            System.Collections.Generic.List<Release> all = null;
            try
            {
                all = ParseList(Download(ListUrl, "application/vnd.github+json"));
                if (all.Count == 0) Log.Info("Versionskontroll: svaret från GitHubs API gick inte att tolka. Provar flödet.");
            }
            catch (Exception ex) { Log.Info("Versionskontroll: GitHubs API svarade inte (" + ex.Message + "). Provar flödet."); }
            if (all == null || all.Count == 0)
            {
                try
                {
                    all = ParseFeed(Download(FeedUrl, "application/atom+xml"));
                    if (all.Count == 0) { Log.Info("Versionskontroll: flödet gick inte att tolka."); return null; }
                }
                catch (Exception ex) { Log.Info("Versionskontroll: gick inte att göra (" + ex.Message + ")."); return null; }
            }
            if (IsNewer(all[0].Version, Program.Version)) Log.Info("Versionskontroll: version " + all[0].Version + " finns (du har " + Program.Version + ").");
            return all;
        }
    }
}
