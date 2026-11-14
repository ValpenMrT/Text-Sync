using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TextSync
{
    // Kontrollerar vid start om det finns en nyare version bland releaserna på GitHub.
    // Ingenting laddas ner av programmet: bannern i huvudfönstret öppnar filen i webbläsaren, som laddar ner den.
    // Går kontrollen inte att göra (inget nät, GitHub svarar inte) händer ingenting, och det står i loggen.
    public static class UpdateCheck
    {
        public const string ReleasesUrl = "https://github.com/ValpenMrT/Text-Sync/releases";
        const string ApiUrl = "https://api.github.com/repos/ValpenMrT/Text-Sync/releases/latest";
        // Bara filer som ligger bland projektets egna releaser får öppnas från bannern.
        const string DownloadPrefix = ReleasesUrl + "/download/";

        public class Release
        {
            public string Version;   // till exempel "2.4.30"
            public string Url;       // exe-filen i releasen, eller sidan med releaserna om filen inte hittades
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

        // Frågar GitHub efter den senaste releasen. Returnerar den bara om den är nyare än programmet, annars null.
        // Körs i en bakgrundstråd. Kastar aldrig.
        public static Release CheckNewer()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.UserAgent = "TextSync/" + Program.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000; req.ReadWriteTimeout = 10000;
                string json;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = rd.ReadToEnd();
                var rel = Parse(json);
                if (rel == null) { Log.Info("Versionskontroll: svaret från GitHub gick inte att tolka."); return null; }
                if (!IsNewer(rel.Version, Program.Version)) return null;
                Log.Info("Versionskontroll: version " + rel.Version + " finns (du har " + Program.Version + ").");
                return rel;
            }
            catch (Exception ex)
            {
                Log.Info("Versionskontroll: gick inte att göra (" + ex.Message + ").");
                return null;
            }
        }
    }
}
