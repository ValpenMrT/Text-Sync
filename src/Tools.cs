// Text Sync - hittar och laddar ner externa verktyg för ljudsynk (ffmpeg, Whisper + modell)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;

namespace TextSync
{
    public class ToolPackage
    {
        public string Name;      // visas för användaren
        public string Url;
        public string Sha256;    // fast kontrollsumma – en manipulerad fil avvisas
        public long Size;
        public string Folder;    // undermapp i tools\
        public bool Zip;         // packas upp, annars sparas som fil
        public string FileName;  // filnamn om inte zip
    }

    public static class Tools
    {
        public static readonly string Root = Path.Combine(Log.Dir, "tools");

        public static readonly ToolPackage Ffmpeg = new ToolPackage
        {
            Name = "ffmpeg 9.0.2 (läser ljudet ur filmen)",
            Url = "https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip",
            Sha256 = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba",
            Size = 114819072, Folder = "ffmpeg", Zip = true
        };
        public static readonly ToolPackage WhisperCpu = new ToolPackage
        {
            Name = "Whisper (taligenkänning, processor)",
            Url = "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-blas-bin-x64.zip",
            Sha256 = "b1158bd10edf92c9799149a6fc5d3e58da3f495241d6fb3423088130e88ffbf3",
            Size = 21715607, Folder = "whisper-cpu", Zip = true
        };
        public static readonly ToolPackage WhisperGpu = new ToolPackage
        {
            Name = "Whisper för NVIDIA-grafikkort",
            Url = "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-win-cuda-12.4.0-x64.zip",
            Sha256 = "afef0b881c500958921c3f5523b50e59ee2ec9b6f5cbd25b324c51ed308a957a",
            Size = 684913404, Folder = "whisper-gpu", Zip = true
        };
        public static readonly ToolPackage ModelTurbo = new ToolPackage
        {
            Name = "Språkmodell large-v3-turbo",
            Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin",
            Sha256 = "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
            Size = 574041195, Folder = "models", FileName = "ggml-large-v3-turbo-q5_0.bin"
        };
        public static readonly ToolPackage SileroVad = new ToolPackage
        {
            Name = "Taldetektering för Whisper (Silero)",
            Url = "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin",
            Sha256 = "29940d98d42b91fbd05ce489f3ecf7c72f0a42f027e4875919a28fb4c04ea2cf",
            Size = 885098, Folder = "models", FileName = "ggml-silero-v5.1.2.bin"
        };
        public static readonly ToolPackage ModelSmall = new ToolPackage
        {
            Name = "Språkmodell small",
            Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
            Sha256 = "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b",
            Size = 487601967, Folder = "models", FileName = "ggml-small.bin"
        };

        // NVIDIA-drivrutinen installerar nvcuda.dll i System32
        public static bool HasNvidia()
        {
            try { return File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll")); } catch { return false; }
        }

        // NVIDIA-kort som Whisper (CUDA) kan använda: (index, namn). Intel/AMD-grafik stöds inte av den här Whisper-versionen.
        public static List<KeyValuePair<int, string>> ListNvidiaGpus()
        {
            var res = new List<KeyValuePair<int, string>>();
            try
            {
                string smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                if (!File.Exists(smi)) return res;
                var psi = new System.Diagnostics.ProcessStartInfo(smi, "--query-gpu=index,name --format=csv,noheader")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string all = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    foreach (var line in all.Split('\n'))
                    {
                        var parts = line.Split(new[] { ',' }, 2);
                        int i;
                        if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out i)) res.Add(new KeyValuePair<int, string>(i, parts[1].Trim()));
                    }
                }
            }
            catch { }
            return res;
        }

        // Inställningar som sparas mellan körningar (t.ex. valt grafikkort)
        static string SettingsFile { get { return Path.Combine(Log.Dir, "settings.txt"); } }
        public static string GetSetting(string key, string def)
        {
            try
            {
                if (File.Exists(SettingsFile))
                    foreach (var l in File.ReadAllLines(SettingsFile))
                        if (l.StartsWith(key + "=")) return l.Substring(key.Length + 1);
            }
            catch { }
            return def;
        }
        public static void SetSetting(string key, string value)
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                var lines = File.Exists(SettingsFile) ? File.ReadAllLines(SettingsFile).Where(l => !l.StartsWith(key + "=")).ToList() : new List<string>();
                lines.Add(key + "=" + value);
                File.WriteAllLines(SettingsFile, lines);
            }
            catch { }
        }

        public static ToolPackage WhisperPackage { get { return HasNvidia() ? WhisperGpu : WhisperCpu; } }
        public static ToolPackage ModelPackage { get { return HasNvidia() ? ModelTurbo : ModelSmall; } }

        static string FindIn(string dir, string exe)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                return Directory.GetFiles(dir, exe, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch { return null; }
        }

        public static string FindFfmpeg() { return FindExe("ffmpeg.exe"); }
        public static string FindFfprobe() { return FindExe("ffprobe.exe"); }

        static string FindExe(string exe)
        {
            string own = FindIn(Path.Combine(Root, "ffmpeg"), exe);
            if (own != null) return own;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Trim().Length > 0) { var p = Path.Combine(dir.Trim().Trim('"'), exe); if (File.Exists(p)) return p; } } catch { }
            }
            // winget-installation (Gyan.FFmpeg) som ännu inte finns i PATH för det här programmet
            string wg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            try
            {
                if (Directory.Exists(wg))
                    foreach (var d in Directory.GetDirectories(wg, "*FFmpeg*"))
                    {
                        var p = FindIn(d, exe);
                        if (p != null) return p;
                    }
            }
            catch { }
            return null;
        }

        public static string FindWhisper(bool gpu) { return FindIn(Path.Combine(Root, gpu ? WhisperGpu.Folder : WhisperCpu.Folder), "whisper-cli.exe"); }

        public static string FindModel(ToolPackage m)
        {
            string p = Path.Combine(Root, m.Folder, m.FileName);
            return File.Exists(p) ? p : null;
        }

        public static bool IsInstalled(ToolPackage p)
        {
            if (p == Ffmpeg) return FindFfmpeg() != null && FindFfprobe() != null;
            if (p == WhisperCpu) return FindWhisper(false) != null;
            if (p == WhisperGpu) return FindWhisper(true) != null;
            return FindModel(p) != null;
        }

        // Ladda ner, kontrollera SHA-256 och packa upp. Kastar vid fel; inget halvfärdigt lämnas kvar.
        public static void Download(ToolPackage p, Action<long, long> progress, CancellationToken ct)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string dl = Path.Combine(Root, "downloads");
            Directory.CreateDirectory(dl);
            string tmp = Path.Combine(dl, Path.GetFileName(new Uri(p.Url).AbsolutePath) + ".part");
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(p.Url);
                req.UserAgent = "TextSync/" + Program.Version;
                req.AllowAutoRedirect = true;
                req.Timeout = 30000; req.ReadWriteTimeout = 60000;
                using (ct.Register(() => { try { req.Abort(); } catch { } }))
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var input = resp.GetResponseStream())
                using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var sha = SHA256.Create())
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : p.Size, done = 0;
                    var buf = new byte[1 << 16];
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(buf, 0, n);
                        sha.TransformBlock(buf, 0, n, null, 0);
                        done += n;
                        progress(done, total);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    string got = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    if (got != p.Sha256)
                        throw new UserMessageException("Nedladdningen av " + p.Name + " stämmer inte med den förväntade kontrollsumman och har kastats. Försök igen senare.");
                }
                ct.ThrowIfCancellationRequested();
                string target = Path.Combine(Root, p.Folder);
                if (p.Zip)
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    string unz = target + ".unzip";
                    if (Directory.Exists(unz)) Directory.Delete(unz, true);
                    ZipFile.ExtractToDirectory(tmp, unz);
                    Directory.Move(unz, target);
                }
                else
                {
                    Directory.CreateDirectory(target);
                    string dest = Path.Combine(target, p.FileName);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                }
                Log.Info("Hämtade " + p.Name + " från " + p.Url);
            }
            catch (WebException ex)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
                throw new UserMessageException("Kunde inte ladda ner " + p.Name + ". Kontrollera att datorn är ansluten till internet och försök igen.\n\n(" + ex.Message + ")", ex);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }
}
