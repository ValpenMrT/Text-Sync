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
        public bool Tar;         // .tar.bz2: packas upp med tar.exe som finns i Windows 10 och 11 (.NET Framework kan inte bzip2)
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
        // Betydelsejämförelsen (Meaning.cs). Paketet för processorn: det fungerar på alla datorer och är litet.
        // Innehållet är kontrollerat 2026-11-11: llama-server.exe finns med, llama-embedding.exe gör det inte.
        public static readonly ToolPackage LlamaCpu = new ToolPackage
        {
            Name = "llama.cpp (jämför vad raderna betyder)",
            Url = "https://github.com/ggml-org/llama.cpp/releases/download/b12291/llama-b12291-bin-win-cpu-x64.zip",
            Sha256 = "6b712208c51fa71108351f9dd4d31c7d6b9f1e3646199494698f0677008b71cb",
            Size = 25057929, Folder = "llama-cpu", Zip = true
        };
        public static readonly ToolPackage ModelBgeM3 = new ToolPackage
        {
            Name = "Språkmodell bge-m3 (förstår över 100 språk)",
            Url = "https://huggingface.co/gpustack/bge-m3-GGUF/resolve/main/bge-m3-Q4_K_M.gguf",
            Sha256 = "6d39681b26c61279ac1f82db35a04a05009e94c415b51c858ff571489a82fc06",
            Size = 437778496, Folder = "models", FileName = "bge-m3-Q4_K_M.gguf"
        };

        // Parakeet (NVIDIA) som alternativ till Whisper för text ur ljudet. Körs med sherpa-onnx på processorn.
        // Innehållet är kontrollerat 2026-11-14: sherpa-onnx-offline.exe finns i paketet, och modellen har encoder, decoder, joiner och tokens.
        public static readonly ToolPackage SherpaOnnx = new ToolPackage
        {
            Name = "sherpa-onnx 2.0.1 (kör Parakeet)",
            Url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/v2.0.1/sherpa-onnx-v2.0.1-win-x64-shared-MT-Release-no-tts.tar.bz2",
            Sha256 = "a644dfe2f7944afec9a356d2557c0452380f20ffcf2f8c704653cfce37558596",
            Size = 25567228, Folder = "sherpa-onnx", Tar = true
        };
        public static readonly ToolPackage ModelParakeet = new ToolPackage
        {
            Name = "Språkmodell Parakeet v3 (25 europeiska språk)",
            Url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2",
            Sha256 = "1495e91b59d7fc53ddca91f3431758ec99f8fa253fdb0e1e851be0f72b14b0f9",
            Size = 487211443, Folder = "parakeet-v3", Tar = true
        };

        public static string FindSherpa() { return FindIn(Path.Combine(Root, SherpaOnnx.Folder), "sherpa-onnx-offline.exe"); }

        // Mappen med Parakeet-modellens filer, eller null
        public static string FindParakeet()
        {
            string enc = FindIn(Path.Combine(Root, ModelParakeet.Folder), "encoder.int8.onnx");
            if (enc == null) return null;
            string dir = Path.GetDirectoryName(enc);
            return File.Exists(Path.Combine(dir, "decoder.int8.onnx")) && File.Exists(Path.Combine(dir, "joiner.int8.onnx")) && File.Exists(Path.Combine(dir, "tokens.txt")) ? dir : null;
        }

        // Det som saknas för Parakeet (utöver ffmpeg)
        public static List<ToolPackage> ParakeetNeeds()
        {
            var need = new List<ToolPackage>();
            if (!IsInstalled(SherpaOnnx)) need.Add(SherpaOnnx);
            if (!IsInstalled(ModelParakeet)) need.Add(ModelParakeet);
            return need;
        }

        public static string FindLlamaServer() { return FindIn(Path.Combine(Root, LlamaCpu.Folder), "llama-server.exe"); }

        // Det som saknas för betydelsejämförelsen
        public static List<ToolPackage> MeaningNeeds()
        {
            var need = new List<ToolPackage>();
            if (!IsInstalled(LlamaCpu)) need.Add(LlamaCpu);
            if (!IsInstalled(ModelBgeM3)) need.Add(ModelBgeM3);
            return need;
        }

        public static bool MeaningReady() { return MeaningNeeds().Count == 0; }

        // NVIDIA-drivrutinen installerar nvcuda.dll i System32
        public static bool HasNvidia()
        {
            try { return File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll")); } catch { return false; }
        }

        // Whisper-versionen är byggd för CUDA 12.4: kräver kort från Maxwell (compute capability 5.0) och drivrutin 527 eller senare.
        const double MinComputeCap = 5.0, MinDriver = 527;

        // NVIDIA-kort som Whisper (CUDA) kan använda: (index, namn). Intel/AMD-grafik stöds inte av den här Whisper-versionen.
        // För gamla kort och drivrutiner tas bort (skrivs i loggen). nvidia-smi får högst 5 s, så att en hängande drivrutin inte stoppar programmet.
        public static List<KeyValuePair<int, string>> ListNvidiaGpus()
        {
            var res = new List<KeyValuePair<int, string>>();
            try
            {
                string smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                if (!File.Exists(smi)) return res;
                var psi = new System.Diagnostics.ProcessStartInfo(smi, "--query-gpu=index,name,compute_cap,driver_version --format=csv,noheader")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var read = p.StandardOutput.ReadToEndAsync();
                    if (!read.Wait(5000))
                    {
                        try { p.Kill(); } catch { }
                        Log.Info("nvidia-smi svarade inte inom 5 s – Whisper körs på processorn");
                        return res;
                    }
                    res = ParseNvidiaSmi(read.Result);
                    if (res.Count == 0) Log.Info("nvidia-smi hittade inget grafikkort som Whisper kan använda: " + read.Result.Trim());
                }
            }
            catch (Exception ex) { Log.Info("nvidia-smi kunde inte köras: " + ex.Message); }
            return res;
        }

        // Rader som "0, NVIDIA RTX A2000, 8.6, 560.94". Gamla drivrutiner känner inte till compute_cap och ger då bara ett felmeddelande, alltså inga kort.
        public static List<KeyValuePair<int, string>> ParseNvidiaSmi(string output)
        {
            var res = new List<KeyValuePair<int, string>>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var line in (output ?? "").Split('\n'))
            {
                var parts = line.Split(',').Select(s => s.Trim()).ToArray();
                int i; double cc, drv;
                if (parts.Length < 4 || !int.TryParse(parts[0], out i)) continue;
                string name = string.Join(", ", parts.Skip(1).Take(parts.Length - 3));
                string ccs = parts[parts.Length - 2], drvs = parts[parts.Length - 1];
                if (!double.TryParse(ccs, System.Globalization.NumberStyles.Float, inv, out cc) || !double.TryParse(drvs.Split('.')[0], System.Globalization.NumberStyles.Integer, inv, out drv))
                { Log.Info("Grafikkort " + i + " (" + name + ") hoppas över: okänd version (" + ccs + ", " + drvs + ")"); continue; }
                if (cc < MinComputeCap) { Log.Info("Grafikkort " + i + " (" + name + ") är för gammalt för Whisper (compute capability " + ccs + ")"); continue; }
                if (drv < MinDriver) { Log.Info("Grafikkort " + i + " (" + name + "): drivrutinen " + drvs + " är för gammal för Whisper (behöver " + MinDriver + " eller senare)"); continue; }
                res.Add(new KeyValuePair<int, string>(i, name));
            }
            return res;
        }

        // Sökningen efter grafikkort görs en gång i bakgrunden, aldrig på gränssnittets tråd
        static System.Threading.Tasks.Task<List<KeyValuePair<int, string>>> gpuScan;
        static readonly object gpuLock = new object();
        public static System.Threading.Tasks.Task<List<KeyValuePair<int, string>>> GpuScan()
        {
            lock (gpuLock) { if (gpuScan == null) gpuScan = System.Threading.Tasks.Task.Run(() => ListNvidiaGpus()); return gpuScan; }
        }

        // Ledigt utrymme på disken där verktygen sparas (-1 om det inte går att läsa)
        public static long FreeBytes()
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Root))).AvailableFreeSpace; } catch { return -1; }
        }

        // Utrymme som en nedladdning behöver: zip-filen och det uppackade samtidigt (uppackat är oftast större än zip-filen)
        public static long SpaceNeeded(IEnumerable<ToolPackage> pkgs)
        {
            return pkgs.Sum(p => p.Zip || p.Tar ? (long)(p.Size * 2.5) : p.Size) + 100L * 1048576;
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

        // Fönstrets plats och storlek sparas som "x,y,bredd,höjd,maximerat" (`window` i settings.txt)
        public static string FormatWindow(System.Drawing.Rectangle b, bool maximized)
        {
            return b.X + "," + b.Y + "," + b.Width + "," + b.Height + "," + (maximized ? "1" : "0");
        }

        public static bool ParseWindow(string s, out System.Drawing.Rectangle b, out bool maximized)
        {
            b = System.Drawing.Rectangle.Empty; maximized = false;
            var p = (s ?? "").Split(',');
            int x, y, w, h;
            if (p.Length != 5 || !int.TryParse(p[0], out x) || !int.TryParse(p[1], out y) || !int.TryParse(p[2], out w) || !int.TryParse(p[3], out h) || w <= 0 || h <= 0) return false;
            b = new System.Drawing.Rectangle(x, y, w, h);
            maximized = p[4] == "1";
            return true;
        }

        // Lägger det sparade fönstret på den skärm det överlappar mest (area = skärmens arbetsyta), och helt inom den.
        // Finns skärmen inte kvar (t.ex. en bortkopplad extern skärm) hamnar fönstret på fallback.
        // Fönstret blir aldrig större än arbetsytan, och aldrig mindre än min om arbetsytan räcker till.
        public static System.Drawing.Rectangle FitWindow(System.Drawing.Rectangle saved, System.Drawing.Rectangle[] areas, System.Drawing.Rectangle fallback, System.Drawing.Size min, out System.Drawing.Rectangle area)
        {
            area = fallback;
            long best = 0;
            foreach (var a in areas)
            {
                var i = System.Drawing.Rectangle.Intersect(a, saved);
                long sz = (long)i.Width * i.Height;
                if (sz > best) { best = sz; area = a; }
            }
            int w = Math.Min(Math.Max(saved.Width, min.Width), area.Width), h = Math.Min(Math.Max(saved.Height, min.Height), area.Height);
            int x = Math.Max(area.Left, Math.Min(saved.X, area.Right - w)), y = Math.Max(area.Top, Math.Min(saved.Y, area.Bottom - h));
            if (best == 0) { x = area.Left + (area.Width - w) / 2; y = area.Top + (area.Height - h) / 2; }   // skärmen finns inte kvar: mitt på fallback
            return new System.Drawing.Rectangle(x, y, w, h);
        }

        // Språkmodell efter var Whisper körs: turbo på grafikkortet (bäst), small på processorn (snabbare där)
        public static ToolPackage ModelFor(bool gpu) { return gpu ? ModelTurbo : ModelSmall; }

        // Det som behövs för Whisper på valt grafikkort (gpu = true) eller på processorn. Finns redan en språkmodell
        // används den, så att ingen ny modell hämtas bara för att man har bytt mellan grafikkort och processor.
        public static List<ToolPackage> WhisperNeeds(bool gpu)
        {
            var need = new List<ToolPackage>();
            if (gpu) { if (!IsInstalled(WhisperGpu)) need.Add(WhisperGpu); }
            // processorn: grafikkortsversionen kan också köras på processorn (-ng) på en dator med NVIDIA-drivrutin,
            // men inte om den redan har misslyckats här (t.ex. blockerad av Smart App Control). Då hämtas processorpaketet (21 MB).
            else if (!IsInstalled(WhisperCpu) && !(HasNvidia() && IsInstalled(WhisperGpu) && GetSetting("gpuwhisperfailed", "") != "1")) need.Add(WhisperCpu);
            if (FindModel(ModelTurbo) == null && FindModel(ModelSmall) == null) need.Add(ModelFor(gpu));
            if (!IsInstalled(SileroVad)) need.Add(SileroVad);
            return need;
        }

        // Finns allt för Whisper redan (på något sätt)? Avgör förvalet A+B i stället för A.
        public static bool WhisperReady()
        {
            return (FindWhisper(true) != null || FindWhisper(false) != null) && (FindModel(ModelTurbo) != null || FindModel(ModelSmall) != null);
        }

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
        public static string FindFfplay() { return FindExe("ffplay.exe"); }   // spelar upp filmen runt en rad (följer med i ffmpeg-paketet)

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
            if (p == LlamaCpu) return FindLlamaServer() != null;
            if (p == SherpaOnnx) return FindSherpa() != null;
            if (p == ModelParakeet) return FindParakeet() != null;
            return FindModel(p) != null;
        }

        // Packar upp ett .tar.bz2-arkiv med tar.exe ur Windows (System32, finns från Windows 10 version 1803). Arkivet är redan kontrollerat mot SHA-256.
        static void Untar(string archive, string dir, CancellationToken ct)
        {
            string tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
            if (!File.Exists(tar)) throw new UserMessageException("Verktyget kunde inte packas upp, eftersom tar.exe saknas i Windows. Det finns i Windows 10 (version 1803 och senare) och Windows 11.");
            var psi = new System.Diagnostics.ProcessStartInfo(tar, "-xf \"" + archive + "\" -C \"" + dir + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (var pr = System.Diagnostics.Process.Start(psi))
            using (ct.Register(() => { try { if (!pr.HasExited) pr.Kill(); } catch { } }))
            {
                string err = pr.StandardError.ReadToEnd();
                pr.WaitForExit();
                ct.ThrowIfCancellationRequested();
                if (pr.ExitCode != 0)
                {
                    Log.Info("tar misslyckades, kod " + pr.ExitCode + ": " + err.Trim());
                    throw new UserMessageException("Verktyget kunde inte packas upp. Det kan vara för lite ledigt utrymme på disken. Mer står i loggen (Hjälp → loggmapp).");
                }
            }
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
                else if (p.Tar)
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    string unz = target + ".unzip";
                    if (Directory.Exists(unz)) Directory.Delete(unz, true);
                    Directory.CreateDirectory(unz);
                    try { Untar(tmp, unz, ct); }
                    catch { try { Directory.Delete(unz, true); } catch { } throw; }
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
