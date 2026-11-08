using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace TextSync
{
    static class Tests
    {
        static Random rnd = new Random(42);
        static double N(double s) { double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble(); return s * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }

        static List<Cue> MakeRef(double minutes)
        {
            var l = new List<Cue>(); double t = 5000;
            while (t < minutes * 60000)
            {
                double gap = rnd.NextDouble() < 0.1 ? 5000 + rnd.NextDouble() * 40000 : 200 + rnd.NextDouble() * 2500;
                t += gap; double d = 900 + rnd.NextDouble() * 4000;
                l.Add(new Cue(t, t + d, "Ref line " + l.Count)); t += d;
            }
            return l;
        }

        // annat "språk": slå ihop, dela, tappa, jittra. Text = sann starttid.
        static List<Cue> MakeOther(List<Cue> r)
        {
            var o = new List<Cue>();
            for (int i = 0; i < r.Count; i++)
            {
                double p = rnd.NextDouble();
                if (p < 0.07) continue;
                double s = r[i].Start + N(150), e = r[i].End + N(250);
                if (p < 0.2 && i + 1 < r.Count && r[i + 1].Start - r[i].End < 1500) { e = r[i + 1].End + N(200); i++; }
                else if (p > 0.9 && e - s > 2500) { double m = (s + e) / 2; o.Add(new Cue(s, m - 80, "T|" + s)); s = m; }
                if (e < s + 300) e = s + 300;
                o.Add(new Cue(s, e, "T|" + s));
            }
            int extra = o.Count / 30;
            for (int k = 0; k < extra; k++) { double s = rnd.NextDouble() * r[r.Count - 1].End; o.Add(new Cue(s, s + 1500, "X")); }
            o.Sort((a, b) => a.Start.CompareTo(b.Start));
            return o;
        }

        static List<Cue> Distort(List<Cue> c, Func<double, double> f)
        {
            return c.Select(x => new Cue(f(x.Start), f(x.Start) + (x.End - x.Start), x.Text)).ToList();
        }

        static void Eval(string name, List<Cue> refc, List<Cue> tgt, SyncMode mode)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = SyncEngine.Sync(refc, tgt, new SyncOptions { Mode = mode }, null);
            sw.Stop();
            var errs = new List<double>();
            foreach (var c in r.Output)
            {
                if (!c.Text.StartsWith("T|")) continue;
                double truth = double.Parse(c.Text.Substring(2));
                errs.Add(Math.Abs(c.Start - truth));
            }
            errs.Sort();
            double med = errs[errs.Count / 2], p95 = errs[(int)(errs.Count * 0.95)];
            double within = errs.Count(e => e < 300) * 100.0 / errs.Count;
            Console.WriteLine("{0,-34} {1,-10} {2,6:0}ms median {3,7:0}ms p95  {4,5:0.0}% <300ms  score {5:0.00}->{6:0.00}  {7}  [{8} ms]  segs={9} off={10:0} drift={11:0}",
                name, mode, med, p95, within, r.ScoreBefore, r.ScoreAfter, r.Method, sw.ElapsedMilliseconds, r.Segments.Count, r.OffsetMs, r.DriftMsPerHour);
            if (r.FpsText != "") Console.WriteLine("    " + r.FpsText);
            foreach (var n in r.Notes) Console.WriteLine("    NOTE " + n);
        }

        // ---------------- MKV-skrivare (för test)
        static byte[] Id(uint id)
        {
            if (id > 0xFFFFFF) return new[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            if (id > 0xFFFF) return new[] { (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            if (id > 0xFF) return new[] { (byte)(id >> 8), (byte)id };
            return new[] { (byte)id };
        }
        static byte[] El(uint id, byte[] data)
        {
            var ms = new MemoryStream(); var i = Id(id); ms.Write(i, 0, i.Length);
            ulong n = (ulong)data.Length; ms.WriteByte(0x01);
            for (int k = 6; k >= 0; k--) ms.WriteByte((byte)(n >> (8 * k)));
            ms.Write(data, 0, data.Length); return ms.ToArray();
        }
        static byte[] U(uint id, ulong v) { var b = new List<byte>(); do { b.Insert(0, (byte)v); v >>= 8; } while (v > 0); return El(id, b.ToArray()); }
        static byte[] S(uint id, string s) { return El(id, Encoding.UTF8.GetBytes(s)); }
        static byte[] Cat(params byte[][] p) { var ms = new MemoryStream(); foreach (var x in p) ms.Write(x, 0, x.Length); return ms.ToArray(); }

        static byte[] Zlib(byte[] d)
        {
            var ms = new MemoryStream(); ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (var z = new DeflateStream(ms, CompressionMode.Compress, true)) z.Write(d, 0, d.Length);
            ms.Write(new byte[4], 0, 4); // (adler, ignoreras)
            return ms.ToArray();
        }

        static void WriteMkv(string path, List<Cue> subs, bool zlib, bool unknownCluster, bool noLang = false)
        {
            var subEntry = noLang ? Cat(U(0xD7, 3), U(0x83, 0x11), S(0x86, "S_TEXT/UTF8")) : Cat(U(0xD7, 3), U(0x83, 0x11), S(0x86, "S_TEXT/UTF8"), S(0x22B59C, "eng"), S(0x536E, "English"));
            if (zlib) subEntry = Cat(subEntry, El(0x6D80, El(0x6240, Cat(U(0x5032, 2), El(0x5034, U(0x4254, 0))))));
            var tracks = El(0x1654AE6B, Cat(
                El(0xAE, Cat(U(0xD7, 1), U(0x83, 1), S(0x86, "V_MPEG4/ISO/AVC"))),
                El(0xAE, Cat(U(0xD7, 2), U(0x83, 2), S(0x86, "A_AC3"), S(0x22B59C, "swe"))),
                El(0xAE, subEntry)));
            var info = El(0x1549A966, Cat(U(0x2AD7B1, 1000000), S(0x4D80, "test")));
            var body = new MemoryStream();
            body.Write(info, 0, info.Length); body.Write(tracks, 0, tracks.Length);
            double end = subs[subs.Count - 1].End + 5000;
            int si = 0;
            for (double ct = 0; ct < end; ct += 5000)
            {
                var cl = new MemoryStream();
                var tc = U(0xE7, (ulong)ct); cl.Write(tc, 0, tc.Length);
                for (int f = 0; f < 5000; f += 200)
                {
                    var payload = new byte[300 + rnd.Next(3000)];
                    var blk = Cat(new byte[] { 0x81, (byte)(f >> 8), (byte)f, 0x80 }, payload);
                    var e = El(0xA3, blk); cl.Write(e, 0, e.Length);
                    var ab = El(0xA3, Cat(new byte[] { 0x82, (byte)(f >> 8), (byte)f, 0x80 }, new byte[200])); cl.Write(ab, 0, ab.Length);
                }
                while (si < subs.Count && subs[si].Start < ct + 5000)
                {
                    var c = subs[si++];
                    int rel = (int)Math.Round(c.Start) - (int)ct;
                    var txt = Encoding.UTF8.GetBytes(c.Text);
                    if (zlib) txt = Zlib(txt);
                    var blk = Cat(new byte[] { 0x83, (byte)(rel >> 8), (byte)rel, 0x00 }, txt);
                    var grp = El(0xA0, Cat(El(0xA1, blk), U(0x9B, (ulong)Math.Round(c.End - c.Start))));
                    cl.Write(grp, 0, grp.Length);
                }
                byte[] cb;
                if (unknownCluster) { cb = Cat(Id(0x1F43B675), new byte[] { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, cl.ToArray()); }
                else cb = El(0x1F43B675, cl.ToArray());
                body.Write(cb, 0, cb.Length);
            }
            var header = El(0x1A45DFA3, Cat(U(0x4286, 1), S(0x4282, "matroska")));
            File.WriteAllBytes(path, Cat(header, El(0x18538067, body.ToArray())));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string dir = args.Length > 0 ? args[0] : ".";
            var refc = MakeRef(100);
            var other = MakeOther(refc);
            Console.WriteLine("ref cues {0}, target cues {1}", refc.Count, other.Count);

            foreach (SyncMode m in new[] { SyncMode.Smart, SyncMode.Simple, SyncMode.Advanced })
            {
                Eval("A offset +3.7s", refc, Distort(other, t => t + 3700), m);
                Eval("B 25fps-version (-1.2s)", refc, Distort(other, t => t * 23.976 / 25 - 1200), m);
                Eval("C cuts (+2s, +10s@40m, +5s@70m)", refc, Distort(other, t => t + 2000 + (t > 2400000 ? 8000 : 0) - (t > 4200000 ? 5000 : 0)), m);
                Eval("D drift 0.08% + 600ms", refc, Distort(other, t => t * 1.0008 + 600), m);
                Eval("E big offset -95s", refc, Distort(other, t => t - 95000), m);
                Eval("F fps + cut", refc, Distort(other, t => t * 25 / 23.976 + 1500 + (t > 3000000 ? 12000 : 0)), m);
            }

            int n = 0;
            Srt.Save(Path.Combine(dir, "Film.sv.srt"), Distort(other, t => t * 23.976 / 25 + 2000 + (t > 2400000 ? 8000 : 0)).Select(c => new Cue(c.Start, c.End, "Svensk rad " + (++n))).ToList());

            // SRT-runda
            string p = Path.Combine(dir, "t.srt");
            var withText = refc.Take(20).Select(c => new Cue(c.Start, c.End, "Hej åäö\nrad 2")).ToList();
            Srt.Save(p, withText);
            var back = Srt.Load(p);
            Console.WriteLine("SRT roundtrip: {0} cues, first '{1}', time ok {2}", back.Count, back[0].Text.Replace("\n", "/"),
                back.Zip(withText, (a, b) => Math.Abs(a.Start - Math.Round(b.Start)) < 1.01).All(x => x));
            File.WriteAllText(p, "1\r\n00:00:01,500 --> 00:00:03,000\r\nRad ett\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\nRad två\r\n", Encoding.GetEncoding(1252));
            back = Srt.Load(p);
            Console.WriteLine("Sloppy 1252 SRT: {0} cues, '{1}' '{2}'", back.Count, back[0].Text, back[1].Text);

            // Dra-och-släpp: vilken fil blir referens/mål
            {
                string rf, tf, note;
                MainForm.PlanRoute(new[] { @"C:\a\Film.sv.srt", @"C:\a\Film.mkv" }, "sv", out rf, out tf, out note);
                bool r1 = rf == @"C:\a\Film.mkv" && tf == @"C:\a\Film.sv.srt" && note == null;
                MainForm.PlanRoute(new[] { @"C:\a\Film.sv.srt", @"C:\a\Film.en.srt" }, "sv", out rf, out tf, out note);
                bool r2 = rf == @"C:\a\Film.en.srt" && tf == @"C:\a\Film.sv.srt";
                MainForm.PlanRoute(new[] { @"C:\a\Film.eng.srt", @"C:\a\Film.swe.srt" }, "sv", out rf, out tf, out note);
                bool r3 = rf == @"C:\a\Film.eng.srt" && tf == @"C:\a\Film.swe.srt";
                MainForm.PlanRoute(new[] { @"C:\a\svensk.srt", @"C:\a\Film.en.srt" }, "sv", out rf, out tf, out note);
                bool r4 = rf == @"C:\a\Film.en.srt" && tf == @"C:\a\svensk.srt" && note == null;
                MainForm.PlanRoute(new[] { @"C:\a\A.srt", @"C:\a\B.srt" }, "sv", out rf, out tf, out note);
                bool r5 = rf == @"C:\a\A.srt" && tf == @"C:\a\B.srt" && note != null;
                MainForm.PlanRoute(new[] { @"C:\a\bild.jpg" }, "sv", out rf, out tf, out note);
                bool r6 = rf == null && tf == null && note != null;
                Console.WriteLine("Route: mkv+srt={0} sv+en={1} 3letter={2} oneUnknown={3} bothUnknown={4} unsupported={5}", r1, r2, r3, r4, r5, r6);
            }

            // SRT-specialfall och teckenkodningar
            {
                var c1 = Srt.Parse("1\n00:00:01.500 --> 00:00:02.000\nPunkt som decimal\n2\n00:00:03,000 --> 00:00:04,000\nUtan tomrad\n\n3\n99999999999:00:00,000 --> 99999999999:00:01,000\nOrimlig\n");
                Console.WriteLine("SRT edge: count={0} firstStart={1} secondText='{2}'", c1.Count, c1[0].Start, c1.Count > 1 ? c1[1].Text : "");
                string p1 = Path.Combine(dir, "ru.srt");
                File.WriteAllBytes(p1, Encoding.GetEncoding(1251).GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nПривет, как дела? Это проверка кириллицы.\r\n"));
                string p2 = Path.Combine(dir, "pl.srt");
                File.WriteAllBytes(p2, Encoding.GetEncoding(1250).GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nZażółć gęślą jaźń. Łódź, źródło, świat, będę.\r\n"));
                string p3 = Path.Combine(dir, "bom.srt");
                File.WriteAllBytes(p3, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nÅäö med BOM\n")).ToArray());
                Console.WriteLine("Encodings: ru='{0}' pl='{1}' bom='{2}' forced1251='{3}'",
                    Srt.Load(p1)[0].Text.Substring(0, 6), Srt.Load(p2)[0].Text.Substring(0, 12), Srt.Load(p3)[0].Text, Srt.Load(p1, 1251)[0].Text.Substring(0, 6));
                double tm; bool okT = Srt.TryParseTime("00:01:23,456", out tm) && tm == 83456 && !Srt.TryParseTime("1:99:00,000", out tm) && !Srt.TryParseTime("abc", out tm);
                Console.WriteLine("TryParseTime ok={0}", okT);
            }

            // Spara med säkerhetskopia
            {
                string sd = Path.Combine(dir, "savetest"); if (Directory.Exists(sd)) Directory.Delete(sd, true);
                Directory.CreateDirectory(sd);
                string orig = Path.Combine(sd, "Film.sv.srt");
                var a = new List<Cue> { new Cue(1000, 2000, "original 1"), new Cue(3000, 4000, "original 2") };
                var b = new List<Cue> { new Cue(1500, 2500, "synkad 1") };
                Srt.Save(orig, a);
                string bk = MainForm.SaveWithBackup(orig, orig, b);
                bool ok1 = bk != null && Srt.Load(bk).Count == 2 && Srt.Load(orig).Count == 1;
                // andra sparningen får inte skriva över kopian av originalet
                MainForm.SaveWithBackup(orig, orig, new List<Cue> { new Cue(0, 1, "x"), new Cue(2, 3, "y"), new Cue(4, 5, "z") });
                bool ok2 = Srt.Load(bk).Count == 2 && Srt.Load(bk)[0].Text == "original 1";
                // annan fil: ingen kopia
                string otherFile = Path.Combine(sd, "Annan.srt");
                bool ok3 = MainForm.SaveWithBackup(otherFile, orig, b) == null && File.Exists(otherFile);
                bool noTmp = Directory.GetFiles(sd, "*.textsync-tmp").Length == 0;
                Console.WriteLine("Save+backup: first={0} keepsOriginal={1} noBackupForOther={2} noTempLeft={3}", ok1, ok2, ok3, noTmp);
            }

            // MKV
            foreach (var cfg in new[] { new[] { false, false }, new[] { true, false }, new[] { false, true } })
            {
                string mp = Path.Combine(dir, "t.mkv");
                WriteMkv(mp, refc, cfg[0], cfg[1]);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var f = new MkvFile(mp))
                {
                    var tr = f.Tracks.Where(t => t.IsSubtitle && t.IsText).ToList();
                    var subs = f.ReadSubtitles(tr[0], x => { }, null);
                    bool ok = subs.Count == refc.Count && subs.Zip(refc, (a, b) => Math.Abs(a.Start - Math.Round(b.Start)) <= 1 && a.Text == b.Text).All(x => x);
                    Console.WriteLine("MKV zlib={0} unknownCluster={1}: tracks={2} '{3}' subs={4}/{5} ok={6} size={7}MB {8}ms",
                        cfg[0], cfg[1], f.Tracks.Count, tr[0], subs.Count, refc.Count, ok, new FileInfo(mp).Length / 1048576, sw.ElapsedMilliseconds);
                }
            }
            // Avkortad MKV (t.ex. halvt nedladdad): ska ge det som gick att läsa + flagga
            {
                string mp = Path.Combine(dir, "trunc.mkv");
                WriteMkv(mp, refc, false, false);
                var bytes = File.ReadAllBytes(mp);
                File.WriteAllBytes(mp, bytes.Take((int)(bytes.Length * 0.6)).ToArray());
                using (var f = new MkvFile(mp))
                {
                    var subs = f.ReadSubtitles(f.Tracks[2], x => { }, null);
                    Console.WriteLine("Truncated MKV: truncated={0} read {1}/{2} cues", f.Truncated, subs.Count, refc.Count);
                }
                try { File.WriteAllBytes(mp, bytes.Take(30).ToArray()); using (var f = new MkvFile(mp)) { } Console.WriteLine("Tiny MKV: no error?!"); }
                catch (InvalidDataException ex) { Console.WriteLine("Tiny MKV: '" + ex.Message + "'"); }
            }

            // Lägg in i MKV med riktig mkvmerge
            {
                string exe = MainForm.FindMkvMergePublic();
                if (exe == null) Console.WriteLine("Mux: mkvmerge saknas – hoppar över");
                else
                {
                    string src = Path.Combine(dir, "muxsrc.mkv"), outp = Path.Combine(dir, "muxout.mkv"), srt = Path.Combine(dir, "mux.srt");
                    var few = refc.Take(60).ToList();
                    WriteMkv(src, few, false, false);
                    Srt.Save(srt, few.Select(c => new Cue(c.Start, c.End, "Svensk " + c.Text)).ToList());
                    List<MkvTrack> tr; using (var f = new MkvFile(src)) tr = f.Tracks;
                    if (File.Exists(outp)) File.Delete(outp);
                    var psi = new System.Diagnostics.ProcessStartInfo(exe, MainForm.BuildMuxArgs(tr, src, outp, "sv", srt)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    int code; string outText;
                    using (var proc = System.Diagnostics.Process.Start(psi)) { outText = proc.StandardOutput.ReadToEnd(); proc.WaitForExit(); code = proc.ExitCode; }
                    if (code > 1) Console.WriteLine("Mux FAILED code " + code + ": " + outText);
                    else using (var f = new MkvFile(outp))
                    {
                        var subsT = f.Tracks.Where(t => t.IsSubtitle).ToList();
                        var nt = subsT.Last();
                        var cues = f.ReadSubtitles(nt, x => { }, null);
                        Console.WriteLine("Mux: code={0} subtitleTracks={1} old.default={2} new='{3}' new.default={4} cues={5}/{6} firstText='{7}'",
                            code, subsT.Count, subsT[0].Default, nt, nt.Default, cues.Count, few.Count, cues[0].Text);
                    }
                }
            }

            {
                string mp = Path.Combine(dir, "nolang.mkv");
                WriteMkv(mp, refc.Take(10).ToList(), false, false, true);
                using (var f = new MkvFile(mp))
                    Console.WriteLine("MKV without language element: '{0}'  audio: '{1}'", f.Tracks[2], MkvTrack.LanguageName(f.Tracks[1].Language));
            }
            return 0;
        }
    }
}
