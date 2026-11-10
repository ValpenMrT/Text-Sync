// Text Sync - användargränssnitt: spara SRT och lägga in i MKV
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TextSync
{
    public partial class MainForm
    {
        // ------------------------------------------------------------ spara
        bool TryGetLang(out string lang)
        {
            lang = langBox.Text.Trim().ToLowerInvariant();
            if (Regex.IsMatch(lang, @"^[a-z]{2,3}(-[a-z0-9]{2,8})*$")) return true;
            MessageBox.Show(this, "Skriv språkkoden med 2–3 bokstäver, t.ex. sv, en eller no.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            langBox.Focus(); langBox.SelectAll();
            return false;
        }

        string DefaultSrtPath(string lang)
        {
            if (mkvPath != null)
                return Path.Combine(Path.GetDirectoryName(mkvPath), Path.GetFileNameWithoutExtension(mkvPath) + "." + lang + ".srt");
            return Path.Combine(Path.GetDirectoryName(tgtLoadedPath), Path.GetFileNameWithoutExtension(tgtLoadedPath) + ".synkad.srt");
        }

        // Skriv via temporär fil så att en krasch aldrig lämnar en halvskriven undertext
        static void WriteSafely(string path, string text)
        {
            string tmp = path + ".textsync-tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(true));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        // Skriver man över originalet (den osynkade filen) sparas först en kopia i en undermapp,
        // så att spelare som VLC/Plex inte visar den som ett extra spår. En befintlig kopia skrivs aldrig över.
        // Returnerar sökvägen till kopian, eller null om ingen behövdes.
        public static string SaveWithBackup(string path, string originalPath, List<Cue> cues)
        {
            string backup = null;
            if (File.Exists(path) && SamePath(path, originalPath))
            {
                string bdir = Path.Combine(Path.GetDirectoryName(path), BackupFolder);
                backup = Path.Combine(bdir, Path.GetFileName(path));
                Directory.CreateDirectory(bdir);
                if (!File.Exists(backup)) File.Copy(path, backup);
            }
            WriteSafely(path, Srt.ToSrt(cues));
            return backup;
        }

        void SaveSrt()
        {
            if (synced == null) return;
            string lang;
            if (!TryGetLang(out lang)) return;
            if (!ConfirmLowMatch()) return;
            string def = DefaultSrtPath(lang);
            using (var d = new SaveFileDialog { Title = "Spara synkad undertext", Filter = "SRT-undertext (*.srt)|*.srt", FileName = Path.GetFileName(def), InitialDirectory = Path.GetDirectoryName(def), OverwritePrompt = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string path = d.FileName;
                try
                {
                    string backup = SaveWithBackup(path, tgtLoadedPath, synced);
                    string backupNote = backup != null ? "  Originalet finns kvar i mappen \"" + BackupFolder + "\"." : "";
                    if (backup != null) Log.Info("Säkerhetskopia av original: " + backup);
                    unsaved = false;
                    lastOutput = path;
                    string auto = "";
                    if (mkvPath != null && SamePath(Path.GetDirectoryName(path), Path.GetDirectoryName(mkvPath))
                        && Path.GetFileName(path).StartsWith(Path.GetFileNameWithoutExtension(mkvPath) + ".", StringComparison.OrdinalIgnoreCase))
                        auto = "  Spelare som VLC, Plex och Kodi hittar den automatiskt.";
                    SetStatus("✔ Sparad: " + Path.GetFileName(path) + "." + auto + backupNote, true);
                    openLink.Visible = true; undoLink.Visible = false;
                    Log.Info("Sparad: " + path);
                }
                catch (Exception ex)
                {
                    Log.Error("Kunde inte spara " + path, ex);
                    MessageBox.Show(this, "Kunde inte spara: " + Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static string FindMkvMerge()
        {
            var cands = new List<string>();
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (dir.Trim().Length > 0) cands.Add(Path.Combine(dir.Trim().Trim('"'), "mkvmerge.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MKVToolNix", "mkvmerge.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MKVToolNix", "mkvmerge.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MKVToolNix", "mkvmerge.exe"));
            foreach (var c in cands) { try { if (File.Exists(c)) return c; } catch { } }
            return null;
        }

        static readonly Dictionary<string, string> LangNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"sv","Svenska"},{"swe","Svenska"},{"en","English"},{"eng","English"},{"no","Norsk"},{"nor","Norsk"},
            {"da","Dansk"},{"dan","Dansk"},{"fi","Suomi"},{"fin","Suomi"},{"de","Deutsch"},{"ger","Deutsch"},{"deu","Deutsch"},
            {"fr","Français"},{"fre","Français"},{"fra","Français"},{"es","Español"},{"spa","Español"} };

        // Argument till mkvmerge: den nya texten blir standardspår, befintliga undertextspår behålls men är inte standard.
        // mkvmerge numrerar spåren 0,1,2… i samma ordning som de står i filen.
        public static string BuildMuxArgs(List<MkvTrack> srcTracks, string src, string outFile, string lang, string srtFile)
        {
            string name; if (!LangNames.TryGetValue(lang, out name)) name = lang;
            var args = new StringBuilder("--gui-mode -o " + Q(outFile) + " ");
            for (int i = 0; i < srcTracks.Count; i++)
                if (srcTracks[i].IsSubtitle) args.Append("--default-track-flag " + i + ":no ");
            args.Append(Q(src) + " --language 0:" + lang + " --track-name " + Q("0:" + name) + " --default-track-flag 0:yes --sub-charset 0:UTF-8 ");
            args.Append(Q(srtFile));
            return args.ToString();
        }

        public static string FindMkvMergePublic() { return FindMkvMerge(); }

        static string Gb(long b) { return Fmt(b / 1073741824.0, "0.0") + " GB"; }

        async Task MuxIntoMkv()
        {
            if (synced == null || busy) return;
            string lang;
            if (!TryGetLang(out lang)) return;
            if (!ConfirmLowMatch()) return;
            string exe = FindMkvMerge();
            if (exe == null)
            {
                var a = MessageBox.Show(this,
                    "För att lägga in undertexten i själva MKV-filen behövs det kostnadsfria programmet MKVToolNix.\n\n" +
                    "Vill du öppna nedladdningssidan?\n\n(Enklare alternativ: \"Spara SRT…\" bredvid filmen med samma namn – de flesta spelare hittar den då automatiskt.)",
                    Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (a == DialogResult.Yes) { try { Process.Start("https://mkvtoolnix.download/downloads.html#windows"); } catch { } }
                return;
            }
            string src = mkvPath;
            if (src == null)
            {
                using (var d = new OpenFileDialog { Title = "Välj MKV-filen som undertexten ska läggas in i", Filter = "MKV (*.mkv)|*.mkv" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    src = d.FileName;
                }
            }
            string outPath;
            using (var d = new SaveFileDialog { Title = "Spara ny MKV-fil (originalet ändras inte)", Filter = "MKV (*.mkv)|*.mkv", InitialDirectory = Path.GetDirectoryName(src), FileName = Path.GetFileNameWithoutExtension(src) + " (Text Sync).mkv", OverwritePrompt = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                outPath = d.FileName;
            }
            if (SamePath(outPath, src))
            {
                MessageBox.Show(this, "Välj ett annat filnamn än originalet – originalfilmen ska inte skrivas över.", Caption); return;
            }

            List<MkvTrack> srcTracks;
            long srcSize;
            try
            {
                if (IsMkv(src)) { using (var f = new MkvFile(src)) srcTracks = f.Tracks; } else srcTracks = new List<MkvTrack>();
                srcSize = new FileInfo(src).Length;
            }
            catch (Exception ex)
            {
                Log.Error("Kunde inte läsa " + src, ex);
                MessageBox.Show(this, "Kunde inte läsa MKV-filen: " + Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // diskutrymme: den nya filen blir ungefär lika stor som originalet
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outPath)));
                long need = srcSize + 200L * 1024 * 1024;
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                {
                    MessageBox.Show(this, "Det finns inte tillräckligt med plats på " + drive.Name + "\n\nBehövs: " + Gb(need) + "\nLedigt: " + Gb(drive.AvailableFreeSpace) +
                        "\n\nVälj en annan disk, eller använd \"Spara SRT…\" som inte tar någon plats.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch { } // t.ex. nätverkssökväg – hoppa över kontrollen

            muxTmp = Path.Combine(Path.GetTempPath(), "TextSync_" + Guid.NewGuid().ToString("N") + ".srt");
            muxPart = outPath + ".part";
            var args = new StringBuilder(BuildMuxArgs(srcTracks, src, muxPart, lang, muxTmp));

            cts = new CancellationTokenSource();
            var token = cts.Token;
            SetBusy(true, "Skapar MKV…");
            var log = new StringBuilder();
            int code = -1;
            bool cancelled = false;
            try
            {
                Srt.Save(muxTmp, synced);
                string argStr = args.ToString();
                Log.Info("mkvmerge " + argStr);
                code = await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo(exe, argStr) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
                    using (var p = Process.Start(psi))
                    {
                        muxProc = p;
                        // Avbryt klickades innan mkvmerge hade startat: då fanns ingen process att stoppa, så den stoppas här
                        if (token.IsCancellationRequested) { try { p.Kill(); } catch { } }
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            var m = Regex.Match(line, @"#GUI#progress\s+(\d+)%");
                            if (m.Success) Progress(int.Parse(m.Groups[1].Value), "Skapar MKV… " + m.Groups[1].Value + " %");
                            else if (line.StartsWith("#GUI#error") || line.StartsWith("#GUI#warning")) log.AppendLine(line.Substring(5));
                        }
                        p.WaitForExit();
                        muxProc = null;
                        return p.ExitCode;
                    }
                });
                cancelled = token.IsCancellationRequested;
                if (!cancelled && (code == 0 || code == 1))
                {
                    if (File.Exists(outPath)) File.Delete(outPath);
                    File.Move(muxPart, outPath);
                }
            }
            catch (Exception ex) { log.AppendLine(ex.ToString()); }
            finally
            {
                muxProc = null; cts = null;
                CleanupMux(!File.Exists(outPath) || cancelled || !(code == 0 || code == 1));
                SetBusy(false, "");
            }
            if (cancelled) { SetStatus("Avbrutet – ingen ny fil skapades.", false); return; }
            if (code == 0 || code == 1)
            {
                lastOutput = outPath;
                SetStatus("✔ Klar: " + Path.GetFileName(outPath) + (code == 1 ? " (mkvmerge gav varningar – se loggen)" : ""), true);
                openLink.Visible = true;
                if (code == 1) Log.Info("mkvmerge-varningar:\r\n" + log);
                Log.Info("MKV skapad: " + outPath);
            }
            else
            {
                Log.Error("mkvmerge misslyckades (kod " + code + "):\r\n" + log, null);
                MessageBox.Show(this, "Det gick inte att skapa MKV-filen. Ingen ny fil har sparats och originalet är orört.\n\n" +
                    "Detaljer (från mkvmerge):\n" + Truncate(log.ToString(), 600) + "\n\nMer information finns i loggen (Hjälp → loggmapp).",
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void CleanupMux(bool deletePart)
        {
            try { if (muxTmp != null && File.Exists(muxTmp)) File.Delete(muxTmp); } catch { }
            try { if (deletePart && muxPart != null && File.Exists(muxPart)) File.Delete(muxPart); } catch { }
            muxTmp = null; muxPart = null;
        }

        static string Truncate(string s, int n) { s = (s ?? "").Trim(); return s.Length <= n ? s : s.Substring(0, n) + "…"; }

        static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
    }
}
