using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Text Sync")]
[assembly: System.Reflection.AssemblyProduct("Text Sync")]
[assembly: System.Reflection.AssemblyDescription("Synka undertexter mot en referensundertext")]
[assembly: System.Reflection.AssemblyCopyright("MIT-licens")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.0.0")]

namespace TextSync
{
    static class Program
    {
        public const string Version = "1.3";

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
            var f = new MainForm();
            // "Öppna med" / dra fil till exe: MKV → referens, SRT → mål
            f.Shown += (s, e) => f.RouteFiles(args, true);
            Application.Run(f);
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
