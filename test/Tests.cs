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

        // Testerna avgör själva om de gick igenom: varje kontroll räknas, och Main returnerar 1 om någon misslyckades.
        static int checks = 0, failed = 0;
        static void Check(string what, bool ok)
        {
            checks++;
            if (ok) return;
            failed++;
            Console.WriteLine("    FEL: " + what);
        }

        // require = false: resultatet skrivs ut men bedöms inte (Simple klarar inte klipp, eftersom den bara synkar linjärt)
        static void Eval(string name, List<Cue> refc, List<Cue> tgt, SyncMode mode, bool require = true)
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
            if (require) Check(name + " (" + mode + "): alla rader ska ligga inom 300 ms", errs.All(e => e < 300));
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
                bool cutsOk = m != SyncMode.Simple;   // Simple får vara sämre i scenarierna med klipp (C, F, G)
                Eval("C cuts (+2s, +10s@40m, +5s@70m)", refc, Distort(other, t => t + 2000 + (t > 2400000 ? 8000 : 0) - (t > 4200000 ? 5000 : 0)), m, cutsOk);
                Eval("D drift 0.08% + 600ms", refc, Distort(other, t => t * 1.0008 + 600), m);
                Eval("E big offset -95s", refc, Distort(other, t => t - 95000), m);
                Eval("F fps + cut", refc, Distort(other, t => t * 25 / 23.976 + 1500 + (t > 3000000 ? 12000 : 0)), m, cutsOk);
                // korta klipp: +1 s i bara 60 s, och ett avsnitt på 20 s före nästa hopp (som Blacklist S04E01 03:29 och 07:18)
                Eval("G korta klipp (60 s, 20 s)", refc, Distort(other, t => t + 2000 + (t > 600000 && t < 660000 ? 1000 : 0) + (t > 1200000 ? 1800 : 0) + (t > 1220000 ? 4600 : 0)), m, cutsOk);
            }
            {   // tiden före synk följer med varje rad (visas i listan), även efter extra förskjutning
                var inp = Distort(other, t => t * 25 / 23.976 + 1500 + (t > 3000000 ? 12000 : 0));
                var outp = SyncEngine.ApplyExtra(SyncEngine.Sync(refc, inp, new SyncOptions(), null).Output, 250);
                var want = inp.Select(c => c.Start).OrderBy(x => x).ToList();
                var got = outp.Select(c => c.OrigStart).OrderBy(x => x).ToList();
                bool sameText = outp.All(c => inp.Any(q => q.Text == c.Text && q.Start == c.OrigStart));
                Console.WriteLine("OrigStart: count={0} allSet={1} same={2} textMatch={3}", got.Count, !got.Any(double.IsNaN), want.SequenceEqual(got), sameText);
                Check("OrigStart: varje rad ska behålla sin tid före synk", !got.Any(double.IsNaN) && want.SequenceEqual(got) && sameText);
            }

            int n = 0;
            Srt.Save(Path.Combine(dir, "Film.sv.srt"), Distort(other, t => t * 23.976 / 25 + 2000 + (t > 2400000 ? 8000 : 0)).Select(c => new Cue(c.Start, c.End, "Svensk rad " + (++n))).ToList());

            // SRT-runda
            string p = Path.Combine(dir, "t.srt");
            var withText = refc.Take(20).Select(c => new Cue(c.Start, c.End, "Hej åäö\nrad 2")).ToList();
            Srt.Save(p, withText);
            var back = Srt.Load(p);
            bool timeOk = back.Zip(withText, (a, b) => Math.Abs(a.Start - Math.Round(b.Start)) < 1.01).All(x => x);
            Console.WriteLine("SRT roundtrip: {0} cues, first '{1}', time ok {2}", back.Count, back[0].Text.Replace("\n", "/"), timeOk);
            Check("SRT sparad och inläst igen: 20 rader, samma text och tider", back.Count == 20 && back[0].Text == "Hej åäö\nrad 2" && timeOk);
            File.WriteAllText(p, "1\r\n00:00:01,500 --> 00:00:03,000\r\nRad ett\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\nRad två\r\n", Encoding.GetEncoding(1252));
            back = Srt.Load(p);
            Console.WriteLine("Sloppy 1252 SRT: {0} cues, '{1}' '{2}'", back.Count, back[0].Text, back[1].Text);
            Check("SRT utan tomrader i Windows-1252: 2 rader med rätt text", back.Count == 2 && back[0].Text == "Rad ett" && back[1].Text == "Rad två");

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
                Check("Dra-och-släpp med en eller två filer: rätt fil ska bli referens och mål", r1 && r2 && r3 && r4 && r5 && r6);
                // film + två SRT: den svenska synkas, den andra blir originaltext som kontrolleras mot ljudet
                string of;
                MainForm.PlanRoute(new[] { @"C:\a\Film.en.srt", @"C:\a\Film.mkv", @"C:\a\Film.sv.srt" }, "sv", out rf, out tf, out of, out note);
                bool o1 = rf == @"C:\a\Film.mkv" && tf == @"C:\a\Film.sv.srt" && of == @"C:\a\Film.en.srt" && note == null;
                MainForm.PlanRoute(new[] { @"C:\a\Film.mkv", @"C:\a\svensk.srt", @"C:\a\Film.eng.srt" }, "sv", out rf, out tf, out of, out note);
                bool o2 = tf == @"C:\a\svensk.srt" && of == @"C:\a\Film.eng.srt" && note == null;
                MainForm.PlanRoute(new[] { @"C:\a\Film.mkv", @"C:\a\A.srt", @"C:\a\B.srt" }, "sv", out rf, out tf, out of, out note);
                bool o3 = tf == @"C:\a\B.srt" && of == @"C:\a\A.srt" && note != null;
                MainForm.PlanRoute(new[] { @"C:\a\Film.mkv", @"C:\a\Film.sv.srt" }, "sv", out rf, out tf, out of, out note);
                bool o4 = of == null && tf == @"C:\a\Film.sv.srt";
                Console.WriteLine("Route3: mkv+en+sv={0} oneUnknown={1} bothUnknown={2} noOrig={3}", o1, o2, o3, o4);
                Check("Dra-och-släpp med film och två SRT: rätt fil ska bli originaltext", o1 && o2 && o3 && o4);
            }

            // Whispers ord → fraser (referens för originaltextens grova synk)
            {
                var w = new List<Cue> { new Cue(1000, 1300, "Hello"), new Cue(1400, 1700, "there."), new Cue(3000, 3300, "Bye"), new Cue(3350, 3360, " "), new Cue(9000, 9200, "Again") };
                var ph = TextCheck.Phrases(w);
                bool ok = ph.Count == 3 && ph[0].Text == "Hello there." && ph[0].End == 1700 && ph[1].Text == "Bye" && ph[2].Start == 9000;
                Console.WriteLine("Phrases: {0}", ok);
                Check("Whispers ord ska slås ihop till fraser", ok);
            }

            // SRT-specialfall och teckenkodningar
            {
                var c1 = Srt.Parse("1\n00:00:01.500 --> 00:00:02.000\nPunkt som decimal\n2\n00:00:03,000 --> 00:00:04,000\nUtan tomrad\n\n3\n99999999999:00:00,000 --> 99999999999:00:01,000\nOrimlig\n");
                Console.WriteLine("SRT edge: count={0} firstStart={1} secondText='{2}'", c1.Count, c1[0].Start, c1.Count > 1 ? c1[1].Text : "");
                Check("SRT-specialfall: punkt som decimal, saknad tomrad, orimlig tid hoppas över", c1.Count == 2 && c1[0].Start == 1500 && c1[1].Text == "Utan tomrad");
                string p1 = Path.Combine(dir, "ru.srt");
                File.WriteAllBytes(p1, Encoding.GetEncoding(1251).GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nПривет, как дела? Это проверка кириллицы.\r\n"));
                string p2 = Path.Combine(dir, "pl.srt");
                File.WriteAllBytes(p2, Encoding.GetEncoding(1250).GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nZażółć gęślą jaźń. Łódź, źródło, świat, będę.\r\n"));
                string p3 = Path.Combine(dir, "bom.srt");
                File.WriteAllBytes(p3, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nÅäö med BOM\n")).ToArray());
                string eRu = Srt.Load(p1)[0].Text.Substring(0, 6), ePl = Srt.Load(p2)[0].Text.Substring(0, 12), eBom = Srt.Load(p3)[0].Text, eForced = Srt.Load(p1, 1251)[0].Text.Substring(0, 6);
                Console.WriteLine("Encodings: ru='{0}' pl='{1}' bom='{2}' forced1251='{3}'", eRu, ePl, eBom, eForced);
                Check("Teckenkodningar: kyrillisk, centraleuropeisk, UTF-8 med BOM och manuellt vald", eRu == "Привет" && ePl == "Zażółć gęślą" && eBom == "Åäö med BOM" && eForced == "Привет");
                double tm; bool okT = Srt.TryParseTime("00:01:23,456", out tm) && tm == 83456 && !Srt.TryParseTime("1:99:00,000", out tm) && !Srt.TryParseTime("abc", out tm);
                Console.WriteLine("TryParseTime ok={0}", okT);
                Check("Tolkning av tider: giltig tid godkänns, ogiltiga avvisas", okT);
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
                Check("Spara med säkerhetskopia: originalet kopieras en gång och skrivs aldrig över", ok1 && ok2 && ok3 && noTmp);
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
                    Check("MKV (zlib=" + cfg[0] + ", okänd klusterstorlek=" + cfg[1] + "): 3 spår och alla undertextrader lästa", ok && f.Tracks.Count == 3);
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
                    Check("Avkortad MKV: flaggas som avkortad och ger de rader som gick att läsa", f.Truncated && subs.Count > 0 && subs.Count < refc.Count);
                }
                try { File.WriteAllBytes(mp, bytes.Take(30).ToArray()); using (var f = new MkvFile(mp)) { } Console.WriteLine("Tiny MKV: no error?!"); Check("Trasig MKV på 30 byte ska ge ett fel", false); }
                catch (InvalidDataException ex) { Console.WriteLine("Tiny MKV: '" + ex.Message + "'"); Check("Trasig MKV på 30 byte ska ge ett fel", true); }
            }
            bool muxSkipped = false;

            // Lägg in i MKV med riktig mkvmerge
            {
                string exe = MainForm.FindMkvMergePublic();
                if (exe == null) { muxSkipped = true; Console.WriteLine("Mux: mkvmerge saknas – hoppar över"); }
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
                    if (code > 1) { Console.WriteLine("Mux FAILED code " + code + ": " + outText); Check("Lägg in i MKV: mkvmerge ska lyckas (kod 0 eller 1)", false); }
                    else using (var f = new MkvFile(outp))
                    {
                        var subsT = f.Tracks.Where(t => t.IsSubtitle).ToList();
                        var nt = subsT.Last();
                        var cues = f.ReadSubtitles(nt, x => { }, null);
                        Console.WriteLine("Mux: code={0} subtitleTracks={1} old.default={2} new='{3}' new.default={4} cues={5}/{6} firstText='{7}'",
                            code, subsT.Count, subsT[0].Default, nt, nt.Default, cues.Count, few.Count, cues[0].Text);
                        Check("Lägg in i MKV: den nya svenska undertexten är standardspår, den gamla är det inte, alla rader finns med",
                            subsT.Count == 2 && !subsT[0].Default && nt.Default && nt.ToString() == "Spår 2: svenska – Svenska (SRT)" && cues.Count == few.Count && cues[0].Text == "Svensk Ref line 0");
                    }
                }
            }

            {
                string mp = Path.Combine(dir, "nolang.mkv");
                WriteMkv(mp, refc.Take(10).ToList(), false, false, true);
                using (var f = new MkvFile(mp))
                {
                    Console.WriteLine("MKV without language element: '{0}'  audio: '{1}'", f.Tracks[2], MkvTrack.LanguageName(f.Tracks[1].Language));
                    Check("MKV-spår utan språk ska tolkas som engelska", f.Tracks[2].ToString() == "Spår 3: engelska (SRT)" && MkvTrack.LanguageName(f.Tracks[1].Language) == "svenska");
                }
            }

            // Matcha raderna mot referensen: slå ihop 2 ↔ 1 och dela 1 ↔ 2
            {
                var er = new List<Cue> {
                    new Cue(1000, 3000, "Hello there."),
                    new Cue(10000, 12000, "Where were you?"),
                    new Cue(12200, 14000, "At the office."),
                    new Cue(20000, 24000, "I told you I'd be late, so don't wait up."),
                    new Cue(30000, 32000, "Okay."),
                    new Cue(32100, 34000, "Fine."),
                    new Cue(40000, 42000, "- Where are you?\n- Home."),
                    new Cue(50000, 52000, "(tal)") };
                var sv = new List<Cue> {
                    new Cue(1050, 2950, "Hej på dig."),
                    new Cue(10100, 14000, "- Var var du?\n- På kontoret."),
                    new Cue(20000, 22000, "Jag sa att jag skulle bli sen,"),
                    new Cue(22100, 24000, "så vänta inte uppe."),
                    new Cue(30000, 34000, "okej då bra då"),
                    new Cue(40000, 41000, "Var är du?"),
                    new Cue(41100, 42000, "Hemma."),
                    new Cue(50000, 52000, "Hallå?") };
                var props = LineAligner.Propose(sv, er);
                foreach (var q in props) Console.WriteLine("Matchning: " + q.Describe() + "  [" + Srt.FormatTime(q.NewCues[0].Start) + "–" + Srt.FormatTime(q.NewCues[q.NewCues.Count - 1].End) + "]");
                Check("Matchning: tre förslag (en delning, två sammanslagningar)", props.Count == 3);
                var sp = props.FirstOrDefault(q => !q.IsMerge);
                Check("Matchning: \"- Var var du? / - På kontoret.\" delas vid frågetecknet och får referensens tider",
                    sp != null && sp.First == 1 && sp.NewCues[0].Text == "Var var du?" && sp.NewCues[1].Text == "På kontoret." &&
                    sp.NewCues[0].Start == 10000 && sp.NewCues[0].End == 12000 && sp.NewCues[1].Start == 12200 && sp.NewCues[1].End == 14000);
                var mg = props.FirstOrDefault(q => q.IsMerge && q.First == 2);
                Check("Matchning: två svenska rader slås ihop till en med den engelska radens tid",
                    mg != null && mg.NewCues[0].Text == "Jag sa att jag skulle bli sen,\nså vänta inte uppe." && mg.NewCues[0].Start == 20000 && mg.NewCues[0].End == 24000);
                var dl = props.FirstOrDefault(q => q.IsMerge && q.First == 5);
                Check("Matchning: replikstreck läggs till när referensen visar två personer",
                    dl != null && dl.NewCues[0].Text == "- Var är du?\n- Hemma.");
                Check("Matchning: en rad utan skiljetecken eller radbrytning delas inte", !props.Any(q => q.First == 4));
                Check("Matchning: rader som redan motsvarar en referensrad rörs inte", !props.Any(q => q.First == 0 || q.First == 7));

                Check("Matchning: delar inte mitt i en mening när referensens första rad slutar med punkt",
                    LineAligner.SplitText("Jag iscensatte min död för att få ett\nnormalt liv, men jag hålls som fånge.",
                        "I faked my death to have a normal life.", "There's nothing normal about being held prisoner.") == null);
                var mid = LineAligner.SplitText("Det är så ofta folk överskattar sig\nsjälva och missbrukar sina förmågor.",
                    "So often people overestimate themselves...", "...misapply their gifts.");
                Check("Matchning: delar mitt i en mening när referensen också gör det (\"...\")",
                    mid != null && mid[0] == "Det är så ofta folk överskattar sig" && mid[1] == "själva och missbrukar sina förmågor.");
                Check("Matchning: inga förslag mot en gles referens (t.ex. bara tvingad text)",
                    LineAligner.Propose(sv, er.Take(3).ToList()).Count == 0);

                string lay = LineAligner.Layout("Det här är en ganska lång mening som inte får plats på en enda rad alls");
                Console.WriteLine("Matchning, radbrytning: " + (lay ?? "(null)").Replace("\n", " / "));
                Check("Matchning: lång text bryts på två rader med högst 42 tecken",
                    lay != null && lay.Split('\n').Length == 2 && lay.Split('\n').All(l => l.Length <= LineAligner.MaxChars));
                Check("Matchning: sammanslagning som blir längre än två rader à 42 tecken föreslås inte",
                    LineAligner.MergeText("Det här är en rad som är ganska så lång,\noch här kommer en till som är lång", "och sedan en tredje rad som inte får plats.", "x") == null);
            }

            // ---- grafikkort, ljudspår, Whisper-kontroll, sökvägar (product-check 2026-11-09)
            {
                var g = Tools.ParseNvidiaSmi("0, NVIDIA RTX A2000 Laptop GPU, 8.6, 560.94\r\n1, NVIDIA GeForce GTX 680, 3.0, 470.10\r\n2, Quadro M2000, 5.2, 470.05\r\nField \"compute_cap\" is not a valid field to query.\r\n");
                Check("Grafikkort: bara kort med compute capability 5.0 och drivrutin 527 eller senare listas", g.Count == 1 && g[0].Key == 0 && g[0].Value == "NVIDIA RTX A2000 Laptop GPU");
                Check("Grafikkort: gammal drivrutin utan compute_cap ger inga kort (Whisper körs på processorn)", Tools.ParseNvidiaSmi("Field \"compute_cap\" is not a valid field to query.").Count == 0);

                Func<string, string, bool, Audio.AudioStream> A = (l, nm, d) => new Audio.AudioStream { Language = l, Name = nm, Default = d, Commentary = Audio.LooksLikeCommentary(nm) };
                Check("Ljudspår: engelska väljs före svensk dubbning som är standard",
                    Audio.PickAudio(new List<Audio.AudioStream> { A("swe", "", true), A("eng", "Director's Commentary", false), A("eng", "", false) }) == 2);
                Check("Ljudspår: utan engelska väljs standardspåret",
                    Audio.PickAudio(new List<Audio.AudioStream> { A("fre", "", false), A("ger", "", true) }) == 1);
                Check("Ljudspår: kommentarspår och syntolkning väljs inte, även om de är standard",
                    Audio.PickAudio(new List<Audio.AudioStream> { A("eng", "Commentary", true), A("swe", "Syntolkning", false), A("swe", "", false) }) == 2);

                // Whisper-kontrollen: antalet rättade rader i rapporten ska stämma med det som faktiskt ändrades
                bool reportOk = true; int scen = 0;
                foreach (var shift in new[] { new[] { 40, 60, 3000 }, new[] { 30, 33, 2500 }, new[] { 50, 52, 4000 }, new[] { 10, 90, -1500 } })
                    for (int rep = 0; rep < 5; rep++)
                    {
                        var track = new List<Cue>(); var heard = new List<Cue>();
                        for (int i = 0; i < 120; i++)
                        {
                            double t = 5000 + i * 4000 + rnd.NextDouble() * 500;
                            string txt = "ordet" + i + " andra" + i + " tredje" + i;
                            track.Add(new Cue(t, t + 2000, txt));
                            double d = (i >= shift[0] && i < shift[1] ? shift[2] : 0) + N(rep * 250);
                            heard.Add(new Cue(t + d, t + d + 2000, txt));
                        }
                        var cr = TextCheck.Check(track, heard);
                        int changed = cr.Corrected.Where((c, i) => Math.Abs(c.Start - track[i].Start) > 0.5).Count();
                        int reported = cr.Regions.Sum(r => r.Lines);
                        if (changed != reported) { reportOk = false; Console.WriteLine("Whisper-kontroll, scenario " + scen + ": " + changed + " rader ändrade men " + reported + " i rapporten"); }
                        scen++;
                    }
                Check("Whisper-kontroll: rapporten visar alla rader som rättades", reportOk);

                string uni = Path.Combine(dir, "Жора 测试");
                Directory.CreateDirectory(uni);
                string wav = Path.Combine(uni, "TextSync_x.wav");
                File.WriteAllBytes(wav, new byte[] { 1 });
                string safe = Audio.AnsiSafePath(wav), safeOut = Audio.AnsiSafePath(Path.Combine(uni, "TextSync_w_abc"));
                Console.WriteLine("Sökväg för Whisper: " + safe + "  |  " + safeOut);
                Check("Sökvägar: västeuropeiska tecken (åäö) behöver inte skrivas om", Audio.IsAnsi(@"C:\Users\Åsa\Film.mkv") && Audio.AnsiSafePath(dir) == dir);
                Check("Sökvägar: kyrilliska och kinesiska tecken skrivs om till korta namn som pekar på samma fil",
                    !Audio.IsAnsi(wav) && Audio.IsAnsi(safe) && File.Exists(safe) && Audio.IsAnsi(safeOut) && safeOut.EndsWith("TextSync_w_abc"));
                Directory.Delete(uni, true);

                long need = Tools.SpaceNeeded(new[] { Tools.WhisperGpu, Tools.ModelTurbo });
                Check("Diskutrymme: nedladdningen kräver mer än paketens storlek (zip + uppackat)", need > Tools.WhisperGpu.Size * 2 + Tools.ModelTurbo.Size);
            }

            // ---- varning för fel avsnitt och besked om hur säker synken är
            {
                Func<string, string> ep = s => { var e = EpisodeId.Parse(s); return e == null ? "-" : e.Season + "/" + e.Episode; };
                Check("Avsnittsnummer: S04E01, s4e1, 4x02 och Säsong 2 Avsnitt 5 tolkas",
                    ep(@"C:\Film\The.Blacklist.S04E01.Esteban.2160p.NF.WEBRip.DDP5.1.x264-NTb.mkv") == "4/1" && ep("The blacklist - S04E01.srt") == "4/1" &&
                    ep("serie s4e1.srt") == "4/1" && ep("Serie - 4x02 - Titel.srt") == "4/2" && ep("Serie Säsong 2 Avsnitt 5.srt") == "2/5" && ep("Show.S12E103.mkv") == "12/103");
                Check("Avsnittsnummer: upplösning, kodek och årtal tolkas inte som avsnitt",
                    ep("Film.1920x1080.x264.mkv") == "-" && ep("Film.2019.2160p.DDP5.1.x265.mkv") == "-" && ep("Filmen.srt") == "-" && ep("Glass 2x4 special.srt") == "-");
                Check("Fel avsnitt: S04E02.srt mot S04E01.mkv ger en varning som säger vad som inte stämmer",
                    EpisodeId.Mismatch("Serie.S04E01.mkv", "Serie.S04E02.srt", "Texten", "filmen") == "Texten verkar höra till avsnitt 2, men filmen är avsnitt 1.");
                Check("Fel avsnitt: annan säsong nämner säsongen",
                    EpisodeId.Mismatch("Serie.S04E01.mkv", "Serie.S03E01.srt", "Texten", "filmen") == "Texten verkar höra till säsong 3, avsnitt 1, men filmen är säsong 4, avsnitt 1.");
                Check("Fel avsnitt: samma avsnitt, eller filnamn utan avsnittsnummer, ger ingen varning",
                    EpisodeId.Mismatch("Serie.S04E01.mkv", "serie 4x01 sv.srt", "Texten", "filmen") == null && EpisodeId.Mismatch("Film.mkv", "Serie.S04E02.srt", "Texten", "filmen") == null);

                var r1 = MakeRef(100); var o1 = MakeOther(r1);
                var r2 = MakeRef(100); var o2 = MakeOther(r2);   // en helt annan "film"
                Check("Olika längd: en text på 45 minuter mot en referens på 100 minuter ger en varning",
                    EpisodeId.LengthMismatch(r1, o1.Where(c => c.Start < 45 * 60000).ToList(), "referensen") != null);
                Check("Olika längd: annan bildfrekvens, förskjutning eller 8 minuter eftertexter ger ingen varning",
                    EpisodeId.LengthMismatch(r1, Distort(o1, t => t * 25 / 23.976 + 5000), "referensen") == null &&
                    EpisodeId.LengthMismatch(r1, o1.Where(c => c.Start < 92 * 60000).ToList(), "referensen") == null);

                Func<string, List<Cue>, List<Cue>, SyncMode, QualityResult> Q = (name, rf, tg, mode) =>
                {
                    var sr = SyncEngine.Sync(rf, tg, new SyncOptions { Mode = mode }, null);
                    var q = Quality.Assess(rf, sr.Output, sr, true);
                    Console.WriteLine("Besked, {0,-38} {1}", name + ":", q.LogText());
                    return q;
                };
                var qa = Q("rätt text (+3,7 s)", r1, Distort(o1, t => t + 3700), SyncMode.Smart);
                var qf = Q("rätt text (bildfrekvens + klipp)", r1, Distort(o1, t => t * 25 / 23.976 + 1500 + (t > 3000000 ? 12000 : 0)), SyncMode.Smart);
                Check("Besked: rätt text som synkas rätt ger Bra, utan svaga ställen", qa.Verdict == Verdict.Good && qf.Verdict == Verdict.Good && qa.Weak.Count == 0 && qf.Weak.Count == 0);
                var qw = Q("text från en annan film", r1, o2, SyncMode.Smart);
                var qw2 = Q("text från en annan film, avancerad", r1, Distort(o2, t => t + 20000), SyncMode.Advanced);
                Check("Besked: text från en annan film ger Osäkert", qw.Verdict == Verdict.Unsure && qw2.Verdict == Verdict.Unsure);
                var sparse = r1.Where((c, i) => i % 12 == 0).ToList();   // som ett spår med bara tvingad text
                var qs = Quality.Assess(sparse, o1, null, true);
                Check("Besked: mot en gles referens blir beskedet Kontrollera (för lite text att bedöma), inte Osäkert", qs.Sparse && qs.Verdict == Verdict.Check && qs.Weak.Count == 0);
                var speech = r1.Select(c => new Cue(c.Start - 200, c.End + 300, "(tal)")).ToList();
                var srA = SyncEngine.Sync(speech, Distort(o1, t => t + 3700), new SyncOptions(), null);
                var qAudio = Quality.Assess(speech, srA.Output, srA, false);
                Console.WriteLine("Besked, mot ljud (taldetektering):         " + qAudio.LogText());
                Check("Besked: mot talavsnitt ur ljudet (syntetiska) ger rätt text Bra", qAudio.Verdict == Verdict.Good);
                Check("Besked: tomma listor och rader före filmens början ger inget fel",
                    Quality.Assess(r1, new List<Cue>(), null, true).Verdict == Verdict.Unsure && Quality.Assess(new List<Cue>(), o1, null, true).Verdict == Verdict.Unsure &&
                    Quality.Assess(r1, o1.Select(c => new Cue(c.Start - 9000000, c.End - 9000000, c.Text)).ToList(), null, true).Verdict == Verdict.Unsure);
                // ett klipp på 10 s efter 40 minuter som den enkla metoden inte kan rätta
                var qc = Q("klipp som enkel synk missar", r1, Distort(o1, t => t + 2000 + (t > 2400000 ? 10000 : 0)), SyncMode.Simple);
                Check("Besked: ett klipp som synken missar ger Kontrollera, med svaga ställen att hoppa till",
                    qc.Verdict == Verdict.Check && qc.Weak.Count > 0);
            }

            // ---- spela upp filmen runt en rad
            {
                string pa = MainForm.BuildClipArgs(@"C:\Film\Min film.mkv", "TextSync_play_abc.srt", "TextSync_play_abc.mkv", 61500, 13250, 0, 1440);
                Console.WriteLine("Uppspelning: ffmpeg " + pa);
                Check("Uppspelning: klippet får start och längd i sekunder med punkt före filmen, texten inbränd och första ljudspåret",
                    pa.Contains(" -ss 61.500 -t 13.250 -i \"C:\\Film\\Min film.mkv\" ") && pa.Contains("-vf \"scale=1440:-2,subtitles=TextSync_play_abc.srt\"") &&
                    pa.Contains(" -map 0:v:0 -map 0:a:0? ") && pa.EndsWith(" TextSync_play_abc.mkv"));
                string pb = MainForm.BuildClipArgs(@"C:\Film\a.mkv", null, "k.mkv", 0, 12000, 2, 960);
                Check("Uppspelning: valt ljudspår används, och utan rader i fönstret används inget textfilter",
                    pb.Contains(" -map 0:a:2? ") && pb.Contains("-vf \"scale=960:-2\"") && !pb.Contains("subtitles"));
                string pp = MainForm.BuildPlayArgs("TextSync_play_abc.mkv", "Text Sync – 00:01:06 (Esc stänger)");
                Check("Uppspelning: spelaren stänger sig själv när klippet är slut", pp.Contains("-autoexit") && pp.EndsWith(" TextSync_play_abc.mkv") && pp.Contains("-window_title \"Text Sync – 00:01:06 (Esc stänger)\""));
                var pc = new List<Cue> { new Cue(1000, 3000, "ett"), new Cue(9000, 11000, "två"), new Cue(20000, 22000, "tre"), new Cue(40000, 41000, "fyra"), new Cue(12000, 13000, " ") };
                var win = MainForm.PlayCues(pc, 9000 - MainForm.PlayMarginMs, 11000 + MainForm.PlayMarginMs);
                Check("Uppspelning: fönstret är 5 s före och efter raden, bara rader med text följer med, och tiderna räknas från klippets början",
                    MainForm.PlayMarginMs == 5000 && win.Count == 1 && win[0].Text == "två" && win[0].Start == 5000 && win[0].End == 7000 && MainForm.PlayCues(pc, 0, 16000).Count == 2);
                var cut = MainForm.PlayCues(pc, 2000, 10000);
                Check("Uppspelning: rader som sticker ut ur klippet kapas vid klippets början och slut",
                    cut.Count == 2 && cut[0].Start == 0 && cut[0].End == 1000 && cut[1].Start == 7000 && cut[1].End == 8000);
                Check("Uppspelning: fönstret blir tre fjärdedelar av skärmens bredd, minst 960 och högst 1920",
                    MainForm.PlayWidth(1920) == 1440 && MainForm.PlayWidth(3840) == 1920 && MainForm.PlayWidth(1366) == 1024 && MainForm.PlayWidth(1024) == 960 && MainForm.PlayWidth(1601) % 2 == 0);
                var vids = new[] { @"C:\F\Serie.S04E01.1080p.mkv", @"C:\F\Serie.S04E02.1080p.mkv", @"C:\F\Annan film.mp4" };
                Check("Uppspelning: filmen med samma avsnittsnummer som texten väljs",
                    MainForm.PickMovie(vids, @"C:\F\Serie.S04E02.en.srt", @"C:\F\serie 4x02 sv.srt") == vids[1]);
                Check("Uppspelning: en ensam film i mappen väljs, och filmen med samma namn som texten",
                    MainForm.PickMovie(new[] { vids[2] }, @"C:\F\ref.srt", @"C:\F\text.srt") == vids[2] &&
                    MainForm.PickMovie(new[] { vids[2], @"C:\F\Tredje.mkv" }, @"C:\F\ref.srt", @"C:\F\Annan film.sv.srt") == vids[2]);
                Check("Uppspelning: går det inte att avgöra väljs ingen film (användaren får frågan)",
                    MainForm.PickMovie(new[] { vids[2], @"C:\F\Tredje.mkv" }, @"C:\F\ref.srt", @"C:\F\text.srt") == null &&
                    MainForm.PickMovie(new[] { vids[0] }, @"C:\F\ref.srt", @"C:\F\Serie.S04E02.srt") == null);
            }

            // ---- text för hörselskadade
            {
                Func<string, string> H = s => Sdh.Clean(s) ?? "(borttagen)";
                Check("Hörselskadade: hela rader med [MUSIK], (skrattar) och ♪ tas bort",
                    H("[MUSIK]") == "(borttagen)" && H("(skrattar)") == "(borttagen)" && H("♪ ♪") == "(borttagen)" && H("<i>[DÖRR SLÅR IGEN]</i>") == "(borttagen)" && H("- [SUCKAR]\n- (HOSTAR)") == "(borttagen)");
                Check("Hörselskadade: ljudbeskrivning inuti en rad tas bort och resten står kvar",
                    H("[suckar] Jag vet inte.") == "Jag vet inte." && H("(skrattar) Det var kul.") == "Det var kul." && H("Vänta! (springer)") == "Vänta!" &&
                    H("Jag hör (DÖRR SLÅR IGEN) något.") == "Jag hör något." && H("<i>[radio] Nyheterna.</i>") == "<i>Nyheterna.</i>");
                Check("Hörselskadade: talarnamn i versaler först på raden tas bort",
                    H("JOHN: Kom hit.") == "Kom hit." && H("- MARIA: Nej.\n- JOHN: Jo.") == "- Nej.\n- Jo." && H("MAN 2: Stanna!") == "Stanna!" && H("JOHN: (suckar) Jaha.") == "Jaha." && H("DR. ÅKE:\nHur mår du?") == "Hur mår du?");
                Check("Hörselskadade: replikstrecket tas bort när bara en talare blir kvar",
                    H("- [SKRATTAR]\n- Vad sa du?") == "Vad sa du?" && H("- Hej.\n- Hej då.") == "- Hej.\n- Hej då." && H("- Vänta lite.") == "- Vänta lite.");
                Check("Hörselskadade: vanlig dialog med parenteser, versaler, klockslag och sångtext förstörs inte",
                    H("Han (min bror) kom hem.") == "Han (min bror) kom hem." && H("FBI är här.") == "FBI är här." && H("Vi ses 12:30.") == "Vi ses 12:30." &&
                    H("Klockan: tolv.") == "Klockan: tolv." && H("♪ Ja, må hon leva ♪") == "♪ Ja, må hon leva ♪" && H("{\\an8}Stockholm, 1987") == "{\\an8}Stockholm, 1987" && H("NEJ!") == "NEJ!");
                int hc, hr;
                var hl = Sdh.Apply(new List<Cue> { new Cue(0, 1000, "[MUSIK]"), new Cue(2000, 3000, "ANNA: Hej."), new Cue(4000, 5000, "Hej."), new Cue(6000, 7000, "♪"),
                    new Cue(8000, 9000, "ANNA: Kom."), new Cue(10000, 11000, "USA: 3, Sverige: 2"), new Cue(12000, 13000, "Han sa <i>nej </i>till mig.") }, out hc, out hr);
                Check("Hörselskadade: antalet ändrade och borttagna rader räknas, och tiderna behålls",
                    hc == 2 && hr == 2 && hl.Count == 5 && hl[0].Text == "Hej." && hl[0].Start == 2000 && Sdh.Summary(hc, hr) == "Text för hörselskadade borttagen: 2 rader ändrade, 2 rader borttagna.");
                Check("Hörselskadade: ett namn i versaler som bara står en gång i filen räknas inte som talarnamn, och orörda rader ändras inte alls",
                    hl[3].Text == "USA: 3, Sverige: 2" && hl[4].Text == "Han sa <i>nej </i>till mig.");
                Check("Hörselskadade: förkortningar och rubriker före kolon står kvar",
                    H("OBS: Dörren är låst.") == "OBS: Dörren är låst." && H("PS: Ring mamma.") == "PS: Ring mamma." && H("DAG 3: Ingen mat kvar.") == "DAG 3: Ingen mat kvar.");
                Check("Hörselskadade: parentes först på andra raden mitt i en mening står kvar, liksom förkortningar i parentes och rader utan bokstäver",
                    H("Jag träffade Anna\n(min syster) i går.") == "Jag träffade Anna\n(min syster) i går." && H("Världshälsoorganisationen (WHO) varnar.") == "Världshälsoorganisationen (WHO) varnar." &&
                    H("Plan (B) gäller.") == "Plan (B) gäller." && H("?!") == "?!" && H("…") == "…" && H("Kom hit.\n(suckar) Nu.") == "Kom hit.\nNu.");
                Check("Hörselskadade: beskrivningar över flera rader, efter kursiv text och mellan namn och kolon tas bort",
                    H("[MUSIK SPELAR\nI BAKGRUNDEN]") == "(borttagen)" && H("(skrattar\nhögt)") == "(borttagen)" && H("<i>Vänta!</i> (springer)") == "<i>Vänta!</i>" &&
                    H("JOHN (skrattar): Hej.") == "Hej." && H("Hej. [MUSIK\nTONAR UT] Vi går.") == "Hej. Vi går.");
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                H(string.Concat(Enumerable.Repeat("<->", 60)) + "x"); H(new string('(', 5000) + "x"); H(string.Concat(Enumerable.Repeat("- ", 3000)) + "(");
                Check("Hörselskadade: konstig indata hänger inte programmet", sw2.ElapsedMilliseconds < 2000);
            }

            // ---- kodade tecken (&amp;) och taggar (<i>, <b>, <u>, <s>, <font>)
            {
                Func<string, string> F = Markup.Fix;
                Func<string, string> S0 = Markup.Strip;
                Check("Tecken: &amp; &lt; &gt; &quot; &#39; och numeriska koder blir riktiga tecken",
                    F("Tom &amp; Jerry") == "Tom & Jerry" && F("3 &lt; 5 &gt; 2") == "3 < 5 > 2" && F("&quot;Hej&quot;") == "\"Hej\"" && F("Det&#39;s") == "Det's" &&
                    F("caf&#233; caf&#xE9; caf&eacute;") == "café café café" && F("Vänta&hellip;") == "Vänta…");
                Check("Tecken: &nbsp; blir ett vanligt blanksteg, och dubbelt kodade tecken avkodas helt",
                    F("Hej&nbsp;du") == "Hej du" && F("Tom &amp;amp; Jerry") == "Tom & Jerry" && F("&amp;lt;i&amp;gt;Hej&amp;lt;/i&amp;gt;") == "<i>Hej</i>");
                Check("Tecken: vanliga &-tecken och okända koder står kvar",
                    F("AT&T; nu") == "AT&T; nu" && F("Q&A") == "Q&A" && F("Tom & Jerry; sedan") == "Tom & Jerry; sedan" && F("&abc; &;") == "&abc; &;" && F("&#0; &#13;") == "&#0; &#13;");
                Check("Taggar: hela taggar lämnas exakt som de är (kursiv, fetstil, understruken, genomstruken, färg, flera i varandra, över flera rader)",
                    F("<i>Hej</i>") == "<i>Hej</i>" && F("<b>Hej</b> och <u>hej</u> och <s>hej</s>") == "<b>Hej</b> och <u>hej</u> och <s>hej</s>" &&
                    F("<font color=\"#ffff00\">Gul</font>") == "<font color=\"#ffff00\">Gul</font>" && F("<b><i>Båda</i></b>") == "<b><i>Båda</i></b>" &&
                    F("<i>Rad ett\nrad två</i>") == "<i>Rad ett\nrad två</i>" && F("- <i>Hej.</i>\n- <i>Hej då.</i>") == "- <i>Hej.</i>\n- <i>Hej då.</i>" &&
                    F("{\\an8}<i>Överst</i>") == "{\\an8}<i>Överst</i>" && F("Han sa <i>nej </i>till mig.") == "Han sa <i>nej </i>till mig.");
                Check("Taggar: tecken som inte är taggar står kvar",
                    F("3 < 5 och 7 > 6") == "3 < 5 och 7 > 6" && F("I <3 you") == "I <3 you" && F("<island> <bra>") == "<island> <bra>" && Markup.Strip("3 < 5 och 7 > 6") == "3 < 5 och 7 > 6");
                Check("Taggar: stora bokstäver, blanksteg och <em>/<strong> rättas",
                    F("<I>Hej</I>") == "<i>Hej</i>" && F("<i >Hej</ i >") == "<i>Hej</i>" && F("<B>Hej</B>") == "<b>Hej</b>" && F("<em>Hej</em> <strong>du</strong>") == "<i>Hej</i> <b>du</b>" &&
                    F("<FONT COLOR=\"red\">Röd</FONT>") == "<font COLOR=\"red\">Röd</font>");
                Check("Taggar: en tagg som inte stängs stängs sist, och en som inte öppnas öppnas först",
                    F("<i>Hej") == "<i>Hej</i>" && F("Hej</i>") == "<i>Hej</i>" && F("<b>Rad ett\nrad två") == "<b>Rad ett\nrad två</b>" && F("{\\an8}Hej</i>") == "{\\an8}<i>Hej</i>" &&
                    F("<font color=\"red\">Röd") == "<font color=\"red\">Röd</font>" && F("<i>Hej<i/>") == "<i>Hej</i>");
                Check("Taggar: fel ordning, dubbla och tomma taggar rättas (flera sorter samtidigt)",
                    F("<i><b>Hej</i></b>") == "<i><b>Hej</b></i>" && F("<i><i>Hej</i>") == "<i>Hej</i>" && F("<i><i>Hej</i></i>") == "<i>Hej</i>" && F("Hej <i></i>du") == "Hej du" &&
                    F("<b>Fet <i>båda</b> kursiv</i>") == "<b>Fet <i>båda</i></b><i> kursiv</i>" && F("<b><u>Hej") == "<b><u>Hej</u></b>");
                Check("Taggar: rester från WebVTT tas bort", F("<c.yellow>Hej</c> <v Anna>du</v>") == "Hej du" && F("Hej <00:01:02.500>du") == "Hej du");
                bool plainOk = true;
                foreach (string t in new[] { "<I morgon åker vi.>", "<I am your father>", "<I love you>\nsa hon.", "Skylten: <S Bahn>", "<U turn>", "<V for Vendetta>", "vitamin <C> och <K>",
                    "a < b > c", "if a<b and c>d", "x < s och y > 2", "<font size>", "< i >" })
                    if (F(t) != t || S0(t) != t) { plainOk = false; Console.WriteLine("    ändrades: " + t.Replace("\n", " / ") + "  →  " + F(t).Replace("\n", " / ")); }
                Check("Taggar: vanlig text inom vinkelparenteser som börjar med I, B, U, S, C eller V är inte taggar", plainOk);
                Check("Taggar: en stängning för mycket tas bort, och en lagad text ändras inte om den lagas igen",
                    F("<i>A</i> B</i>") == "<i>A</i> B" && new[] { "<i>A</i> B</i>", "Hej</i>", "<b>Fet <i>båda</b> kursiv</i>", "<i><b>Hej</i></b>", "x</i> y</b>", "<b>a</i> b</b>" }.All(t => F(F(t)) == F(t)));
                Check("Tecken: tecken utanför grundplanet, stora bokstäver i koden och radbrytningar",
                    F("Ja &#128512;") == "Ja 😀" && F("&#55357;") == "&#55357;" && F("Tom &AMP; Jerry &Amp; co") == "Tom & Jerry & co" && F("Rad&#10;&#10;två") == "Rad\ntvå" && F("Hej&nbsp;") == "Hej");
                Func<string, string> S = Markup.Strip;
                Check("Ta bort formatering: alla sorter tas bort och texten står kvar",
                    S("<i>Hej</i>") == "Hej" && S("<b>Fet</b>, <u>understruken</u>, <s>struken</s>") == "Fet, understruken, struken" && S("<font color=\"#ffff00\" size=\"20\">Gul</font>") == "Gul" &&
                    S("<b><i>Båda</i></b>") == "Båda" && S("{\\an8}<i>Överst</i>") == "Överst" && S("- <i>Hej.</i>\n- <b>Hej då.</b>") == "- Hej.\n- Hej då." && S("<I>Hej") == "Hej");
                Check("Ta bort formatering: text utan formatering ändras inte, och klamrar utan kod står kvar",
                    S("Hej  du ") == "Hej  du " && S("{text}") == "{text}" && S("♪ La la ♪") == "♪ La la ♪");
                Check("Taggar: <br> blir en radbrytning, och <strike>/<del> blir <s>",
                    F("Rad ett<br>rad två") == "Rad ett\nrad två" && F("Rad ett<BR />rad två") == "Rad ett\nrad två" && F("Rad ett <br/> rad två") == "Rad ett\nrad två" &&
                    F("<i>Rad ett<br>rad två</i>") == "<i>Rad ett\nrad två</i>" && F("<strike>Fel</strike> <del>fel</del>") == "<s>Fel</s> <s>fel</s>" && F("<bra>") == "<bra>");
                Check("Koder från ASS: \\N blir en radbrytning, och {\\i1} {\\b1} {\\u1} {\\s1} blir <i> <b> <u> <s>",
                    F("Rad ett\\NRad två") == "Rad ett\nRad två" && F("{\\i1}Hej{\\i0}") == "<i>Hej</i>" && F("{\\b1}Fet{\\b0} och {\\i1}kursiv") == "<b>Fet</b> och <i>kursiv</i>" &&
                    F("{\\an8\\i1}Överst") == "{\\an8}<i>Överst</i>" && F("{\\u1}Under{\\u0} {\\s1}struken{\\s0}") == "<u>Under</u> <s>struken</s>" &&
                    F("{\\an8}Orörd") == "{\\an8}Orörd" && F("{\\bord2\\shad1\\b700}Orörd") == "{\\bord2\\shad1\\b700}Orörd" && F("C:\\nytt och 1\\2") == "C:\\nytt och 1\\2");
                Check("Taggar från webbsidor: <p> och <div> blir rader, <span> <sup> <sub> <small> och länkar tas bort och texten står kvar",
                    F("<p>Rad ett</p><p>Rad två</p>") == "Rad ett\nRad två" && F("<div class=\"x\">Hej</div>") == "Hej" && F("<span style=\"color:red\">Hej</span> du") == "Hej du" &&
                    F("m<sup>2</sup> H<sub>2</sub>O <small>liten</small>") == "m2 H2O liten" && F("Text från <a href=\"http://exempel.se\">exempel.se</a>") == "Text från exempel.se" &&
                    F("<span style='font-style:italic'><i>Hej</i></span>") == "<i>Hej</i>");
                bool htmlOk = true;
                foreach (string t in new[] { "Kör in på <P>", "<a>", "</a>", "<p och q>", "<span av tid>", "<A href>", "<small talk>", "<sub zero>", "<div by zero>", "{y:i}Hej" })
                    if (F(t) != t) { htmlOk = false; Console.WriteLine("    ändrades: " + t + "  →  " + F(t)); }
                Check("Taggar från webbsidor: vanlig text inom vinkelparenteser står kvar", htmlOk);
                Check("Ta bort formatering: koder från MicroDVD och ASS tas bort",
                    S0("{y:i}Hej") == "Hej" && S0("{c:$0000ff}{y:b}Blå och fet") == "Blå och fet" && S0("{\\pos(10,20)}Hej") == "Hej" && S0("{klammer: text som är längre än fyrtio tecken står kvar}") == "{klammer: text som är längre än fyrtio tecken står kvar}" && S0("{x:i}Hej") == "{x:i}Hej");
                int fe, ft, fs, fg;
                var fl0 = Markup.FixAll(new List<Cue> { new Cue(0, 1000, "<i> </i>"), new Cue(2000, 3000, "Hej &amp; hå"), new Cue(4000, 5000, "&nbsp;"), new Cue(6000, 7000, "<br>"), new Cue(8000, 9000, "Orörd"), new Cue(10000, 11000, "") }, out fe, out ft, out fg);
                Check("Tecken och taggar: rader där ingen text blir kvar tas bort, och en rättad rad minns vad som stod i filen",
                    fg == 3 && fe == 1 && ft == 0 && fl0.Count == 3 && fl0[0].Text == "Hej & hå" && fl0[0].Raw == "Hej &amp; hå" && fl0[0].Clone().Raw == "Hej &amp; hå" && fl0[1].Raw == null && fl0[2].Text == "" &&
                    Markup.Summary(fe, ft, fg) == "Rättat: kodade tecken på 1 rad. 3 rader utan text borttagna." && Markup.Summary(0, 0, 1) == "1 rad utan text borttagen.");
                var fsync = SyncEngine.Sync(refc, Markup.FixAll(Distort(other, t => t + 3700).Select((c, i) => i == 5 ? new Cue(c.Start, c.End, c.Text + " &amp; mer") : c).ToList()), new SyncOptions(), null);
                Check("Tecken och taggar: märkningen av rättade rader följer med genom synken och den extra justeringen",
                    fsync.Output.Count(c => c.Raw != null) == 1 && SyncEngine.ApplyExtra(fsync.Output, 100).Count(c => c.Raw != null && c.Raw.EndsWith("&amp; mer") && c.Text.EndsWith("& mer")) == 1);
                Check("SRT: en rad utan text sparas inte",
                    Srt.ToSrt(new List<Cue> { new Cue(0, 1000, "Ett"), new Cue(2000, 3000, " "), new Cue(4000, 5000, "Två") }) == "1\r\n00:00:00,000 --> 00:00:01,000\r\nEtt\r\n\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\nTvå\r\n\r\n");
                var fl = Markup.FixAll(new List<Cue> { new Cue(0, 1000, "Tom &amp; Jerry"), new Cue(2000, 3000, "<i>Hej"), new Cue(4000, 5000, "&lt;I&gt;Hej &amp; hå"), new Cue(6000, 7000, "<i>Orörd</i>") }, out fe, out ft, out fg);
                var fp = Markup.StripAll(fl, out fs);
                Check("Tecken och taggar: antalet rättade rader räknas, och antalet rader och tiderna behålls",
                    fe == 2 && ft == 2 && fl.Count == 4 && fl[2].Text == "<i>Hej & hå</i>" && fl[1].Start == 2000 && fs == 3 && fp.Count == 4 && fp[3].Text == "Orörd" && fl[3].Text == "<i>Orörd</i>" &&
                    Markup.Summary(fe, ft) == "Rättat: kodade tecken på 2 rader, formatering på 2 rader." && Markup.Summary(0, 0) == "" && Markup.Summary(1, 0) == "Rättat: kodade tecken på 1 rad.");
                var fpath = Path.Combine(dir, "markup.srt");
                File.WriteAllText(fpath, "1\r\n00:00:01,000 --> 00:00:02,000\r\n<i>Tom &amp; Jerry\r\n\r\n", new UTF8Encoding(false));
                var fsrt = Srt.ToSrt(Markup.FixAll(Srt.Load(fpath)));
                Check("Tecken och taggar: en SRT-fil som läses, rättas och skrivs får riktiga tecken och hela taggar", fsrt.Contains("<i>Tom & Jerry</i>\r\n"));
                var sw3 = System.Diagnostics.Stopwatch.StartNew();
                F(string.Concat(Enumerable.Repeat("<i>", 3000)) + "x"); F(string.Concat(Enumerable.Repeat("</b>", 3000))); F(new string('<', 20000) + "i"); F(string.Concat(Enumerable.Repeat("&amp;", 5000))); S(string.Concat(Enumerable.Repeat("{\\", 5000)));
                F("<p" + new string(' ', 20000) + "x"); F(string.Concat(Enumerable.Repeat("<a b=", 5000))); F(string.Concat(Enumerable.Repeat("{\\i1", 5000))); F(string.Concat(Enumerable.Repeat("<span a=\"", 5000)));
                F("<" + new string(' ', 20000) + "x"); F(string.Concat(Enumerable.Repeat("<" + new string('\t', 5000) + "x", 20)));
                int big = F(string.Concat(Enumerable.Repeat("<font color=\"red\">", 2000)) + string.Concat(Enumerable.Repeat("</i>", 2000))).Length;
                Check("Tecken och taggar: orimligt djupa taggar blåser inte upp texten", big < 200000);
                Check("Tecken och taggar: konstig indata hänger inte programmet", sw3.ElapsedMilliseconds < 2000);
            }

            Console.WriteLine();
            if (muxSkipped) Console.WriteLine("OBS: testet av \"Lägg in i MKV\" hoppades över, eftersom mkvmerge (MKVToolNix) inte är installerat.");
            if (failed == 0) Console.WriteLine("RESULTAT: alla {0} kontroller gick igenom.", checks);
            else Console.WriteLine("RESULTAT: {0} av {1} kontroller MISSLYCKADES. Se raderna som börjar med FEL ovan.", failed, checks);
            return failed == 0 ? 0 : 1;
        }
    }
}
