using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Text Sync")]
[assembly: System.Reflection.AssemblyProduct("Text Sync")]
[assembly: System.Reflection.AssemblyDescription("Synka undertexter mot en referensundertext")]
[assembly: System.Reflection.AssemblyCopyright("MIT-licens")]
[assembly: System.Reflection.AssemblyVersion("2.4.22.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.4.22.0")]

namespace TextSync
{
    static class Program
    {
        public const string Version = "2.4.22";

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Log.Info("Startar");
            CleanupTemp();
            var f = new MainForm();
            // "Öppna med" / dra fil till exe: MKV → referens, SRT → mål
            f.Shown += (s, e) => f.RouteFiles(args, true);
            Application.Run(f);
        }

        // Ta bort rester från tidigare körningar som avbröts (ljudfiler ~115 MB/timme film, halvfärdiga nedladdningar).
        // Bara filer äldre än 6 timmar, så att ett annat öppet Text Sync-fönster inte störs.
        static void CleanupTemp()
        {
            try
            {
                var old = DateTime.Now.AddHours(-6);
                var files = new List<string>();
                foreach (var pat in new[] { "TextSync_*.wav", "TextSync_w_*.srt", "TextSync_*.srt", "TextSync_play_*.mkv" })
                    files.AddRange(System.IO.Directory.GetFiles(System.IO.Path.GetTempPath(), pat));
                string dl = System.IO.Path.Combine(Tools.Root, "downloads");
                if (System.IO.Directory.Exists(dl)) files.AddRange(System.IO.Directory.GetFiles(dl, "*.part"));
                foreach (var f in files)
                    try { if (System.IO.File.GetLastWriteTime(f) < old) System.IO.File.Delete(f); } catch { }
            }
            catch { }
        }

        static void ShowCrash(Exception ex)
        {
            Log.Error("Oväntat fel", ex);
            try
            {
                MessageBox.Show(
                    "Något gick oväntat fel i Text Sync.\n\n" + (ex != null ? ex.Message : "") +
                    "\n\nDetaljer har sparats i loggfilen:\n" + Log.FilePath +
                    "\n\nSkicka gärna den filen till den som gav dig programmet.",
                    "Text Sync " + Version, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
