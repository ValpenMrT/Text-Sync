// Text Sync - enkel loggfil för felsökning (%LOCALAPPDATA%\TextSync\log.txt)
using System;
using System.IO;
using System.Text;

namespace TextSync
{
    public static class Log
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextSync");
        public static string FilePath { get { return Path.Combine(Dir, "log.txt"); } }
        static readonly object Lock = new object();

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Error(string msg, Exception ex) { Write("FEL", msg + (ex != null ? "\r\n" + ex : "")); }

        static void Write(string level, string msg)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Dir);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1000000)
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] v" + Program.Version + "  " + msg + "\r\n",
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
