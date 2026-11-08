using System;
using System.Threading;

namespace TextSync
{
    // Hämtar ljudsynkverktygen med samma kod som programmet använder (testar nedladdning + kontrollsumma)
    static class ToolsDl
    {
        static int Main(string[] args)
        {
            foreach (var p in new[] { Tools.WhisperCpu, Tools.WhisperGpu, Tools.ModelTurbo })
            {
                if (Tools.IsInstalled(p)) { Console.WriteLine("finns redan: " + p.Name); continue; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long last = -1;
                Tools.Download(p, (d, t) => { long pct = d * 100 / Math.Max(1, t); if (pct / 10 != last) { last = pct / 10; Console.WriteLine(p.Name + " " + pct + "%"); } }, CancellationToken.None);
                Console.WriteLine("klar: " + p.Name + " på " + sw.Elapsed.TotalSeconds.ToString("0") + " s, installerad=" + Tools.IsInstalled(p));
            }
            Console.WriteLine("whisper cpu: " + Tools.FindWhisper(false));
            Console.WriteLine("whisper gpu: " + Tools.FindWhisper(true));
            Console.WriteLine("ffmpeg: " + Tools.FindFfmpeg() + " / " + Tools.FindFfprobe());
            Console.WriteLine("nvidia: " + Tools.HasNvidia());
            return 0;
        }
    }
}
