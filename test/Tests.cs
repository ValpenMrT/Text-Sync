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

                // ---- hur SRT-filen sparas: UTF-8 med BOM, UTF-8 utan BOM, eller Windows-teckentabell för mycket gamla spelare
                int cp, rep;
                string sv = "1\r\n00:00:01,000 --> 00:00:02,000\r\nÅsa sjöng: ”Hej då…” – ♪ la la ♪\r\n\r\n";
                byte[] e0 = Srt.Encode(sv, Srt.FormatUtf8Bom, out cp, out rep);
                Check("Spara, UTF-8 med BOM: filen börjar med BOM och inga tecken byts ut", e0[0] == 0xEF && e0[1] == 0xBB && e0[2] == 0xBF && cp == 65001 && rep == 0 && Encoding.UTF8.GetString(e0, 3, e0.Length - 3) == sv);
                byte[] e1 = Srt.Encode(sv, Srt.FormatUtf8, out cp, out rep);
                Check("Spara, UTF-8 utan BOM: filen börjar med texten", e1[0] == (byte)'1' && cp == 65001 && rep == 0 && Encoding.UTF8.GetString(e1) == sv);
                byte[] e2 = Srt.Encode(sv, Srt.FormatLegacy, out cp, out rep);
                string legacyText = Encoding.GetEncoding(1252).GetString(e2);
                Console.WriteLine("Spara för gamla spelare: Windows-" + cp + ", " + rep + " tecken utbytta: " + legacyText.Split('\n')[2].Trim());
                Check("Spara, gamla spelare: svensk text sparas i Windows-1252 med å, ä och ö som en byte var",
                    cp == 1252 && e2.Length == legacyText.Length && legacyText.Contains("Åsa sjöng") && Array.IndexOf(e2, (byte)0xC5) > 0 && Array.IndexOf(e2, (byte)0xF6) > 0);
                Check("Spara, gamla spelare: tecken som finns i tabellen behålls (” … –), och ♪ byts mot # och räknas",
                    rep == 2 && legacyText.Contains("”Hej då…” – # la la #"));
                Srt.Encode("Привет, как дела? ♪", Srt.FormatLegacy, out cp, out rep);
                Check("Spara, gamla spelare: rysk text ger kyrillisk teckentabell (1251)", cp == 1251 && rep == 1);
                Srt.Encode("Zażółć gęślą jaźń", Srt.FormatLegacy, out cp, out rep);
                Check("Spara, gamla spelare: polsk text ger centraleuropeisk teckentabell (1250) utan utbytta tecken", cp == 1250 && rep == 0);
                byte[] e3 = Srt.Encode("Åsa och 中", Srt.FormatLegacy, out cp, out rep);
                Check("Spara, gamla spelare: a + ring blir å, och ett tecken utan motsvarighet blir ?", Encoding.GetEncoding(1252).GetString(e3) == "Åsa och ?" && rep == 1);
                // hela vägen: inställningen styr filen som sparas, och den går att läsa in igen
                Srt.SaveFormat = Srt.FormatLegacy;
                string legacyFile = Path.Combine(sd, "Gammal.srt");
                MainForm.SaveWithBackup(legacyFile, orig, new List<Cue> { new Cue(1000, 2000, "Räksmörgås på en ö ♪") });
                byte[] lf = File.ReadAllBytes(legacyFile);
                Check("Spara, gamla spelare: filen har ingen BOM, läses in rätt igen och beskedet säger vad som hände",
                    lf[0] == (byte)'1' && Srt.Load(legacyFile)[0].Text == "Räksmörgås på en ö #" && MainForm.LastSaveNote.Contains("Windows-1252") && MainForm.LastSaveNote.Contains("1 tecken"));
                Srt.SaveFormat = Srt.FormatUtf8Bom;
                MainForm.SaveWithBackup(legacyFile, orig, new List<Cue> { new Cue(1000, 2000, "Räksmörgås ♪") });
                Check("Spara, förvalt: UTF-8 med BOM, texten oförändrad och inget extra besked",
                    File.ReadAllBytes(legacyFile)[0] == 0xEF && Srt.Load(legacyFile)[0].Text == "Räksmörgås ♪" && MainForm.LastSaveNote == "");
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

                // inställningen Godkänn förslag om radindelning automatiskt
                string did;
                var auto = LineAligner.AcceptAll(sv, er, null, out did);
                Console.WriteLine("Matchning, automatiskt: " + did);
                Check("Matchning: alla förslag godkänns på en gång (8 rader blir 7, med referensens tider)",
                    did == "2 sammanslagningar och 1 delning" && auto.Count == 7 && sv.Count == 8 &&
                    auto.Any(c => c.Text == "Var var du?" && c.Start == 10000) && auto.Any(c => c.Text == "På kontoret." && c.Start == 12200) &&
                    auto.Any(c => c.Text == "- Var är du?\n- Hemma." && c.Start == 40000 && c.End == 42000) &&
                    auto.Zip(auto.Skip(1), (a, b) => a.Start <= b.Start).All(x => x));
                string none;
                var same = LineAligner.AcceptAll(auto, er, null, out none);
                Check("Matchning: utan förslag lämnas texten orörd", none == null && same == auto);
            }

            // Text ur ljudet: Whispers ord (ett ord per rad med egen tid) blir undertexter
            {
                // ord: text, start (ms), längd 200 ms. "-" för sig = en ny talare börjar.
                Func<string, List<Cue>> mk = spec =>
                {
                    var l = new List<Cue>();
                    foreach (var part in spec.Split(' '))
                    {
                        int at = part.LastIndexOf('@');
                        double t = double.Parse(part.Substring(at + 1));
                        l.Add(new Cue(t, t + 200, " " + part.Substring(0, at)));
                    }
                    return l;
                };
                var subs = Subtitler.FromWords(mk("-@1000 Where@1000 were@1300 you?@1600 -@2000 At@2000 the@2300 office.@2600 " +
                    "I@5000 told@5300 you@5600 I'd@5900 be@6200 late,@6500 so@6800 don't@7100 wait@7400 up.@7700 Okay.@9000 [MUSIC]@10000 Fine.@12000"));
                foreach (var c in subs) Console.WriteLine("Text ur ljudet: " + Srt.FormatTime(c.Start) + " → " + Srt.FormatTime(c.End) + "  " + c.Text.Replace("\n", " / "));
                Check("Text ur ljudet: två talare i följd får var sin rad med replikstreck",
                    subs.Count > 0 && subs[0].Text == "- Where were you?\n- At the office.");
                Check("Text ur ljudet: en ny undertext börjar efter en lång paus och vid meningsslut",
                    subs.Count == 4 && subs[1].Text.StartsWith("I told you") && subs[1].Text.Replace("\n", " ").EndsWith("wait up.") && subs[2].Text == "Okay." && subs[3].Text == "Fine.");
                Check("Text ur ljudet: ljudbeskrivningar som [MUSIC] tas bort", !subs.Any(c => c.Text.Contains("MUSIC")));
                Check("Text ur ljudet: texten visas strax före första ordet och går aldrig in i nästa undertext",
                    subs.Count == 4 && subs[0].Start == 1000 - Subtitler.LeadMs && subs[1].Start == 5000 - Subtitler.LeadMs &&
                    subs.Zip(subs.Skip(1), (a, b) => a.End < b.Start).All(x => x) && subs.All(c => c.End > c.Start));
                Check("Text ur ljudet: en ensam undertext står kvar minst en sekund", subs.Count == 4 && subs[3].End - subs[3].Start >= 1000);
                // en lång mening utan paus delas, och ingen undertext blir längre än två rader à 42 tecken
                var longWords = new List<Cue>();
                for (int k = 0; k < 60; k++) longWords.Add(new Cue(1000 + k * 300, 1200 + k * 300, " " + (k % 9 == 8 ? "ending," : "wording")));
                var longSubs = Subtitler.FromWords(longWords);
                Check("Text ur ljudet: en lång mening delas i undertexter om högst två rader à 42 tecken och högst sex sekunder",
                    longSubs.Count >= 5 && longSubs.All(c => c.Text.Split('\n').Length <= 2 && c.Text.Split('\n').All(l => l.Length <= LineAligner.MaxChars)) &&
                    longSubs.All(c => c.End - c.Start <= Subtitler.MaxDurMs + Subtitler.LeadMs + Subtitler.LingerMs) &&
                    string.Join(" ", longSubs.Select(c => c.Text.Replace("\n", " "))).Split(' ').Length == 60);
                Check("Text ur ljudet: inga ord ger ingen text", Subtitler.FromWords(new List<Cue>()).Count == 0);

                // Ljud C använder texten som användaren har rättat och sparat (2.4.14)
                string fakeFilm = Path.Combine(dir, "film för sparad text.mkv"), savedSrt = Path.Combine(dir, "film för sparad text.en.srt");
                File.WriteAllText(fakeFilm, "x"); Srt.Save(savedSrt, subs);
                Audio.ForgetSavedText(fakeFilm, 0);
                bool noneFirst = Audio.SavedTextPath(fakeFilm, 0) == null;
                Audio.RememberSavedText(fakeFilm, 0, savedSrt);
                bool found = Audio.SavedTextPath(fakeFilm, 0) == savedSrt && Audio.SavedTextPath(fakeFilm, 1) == null;
                File.Delete(savedSrt);
                bool goneAfterDelete = Audio.SavedTextPath(fakeFilm, 0) == null;
                Audio.ForgetSavedText(fakeFilm, 0);
                Check("Text ur ljudet: den sparade texten koms ihåg per film och ljudspår, och glöms när filen är borttagen", noneFirst && found && goneAfterDelete);

                // en text ur ljudet skriver aldrig över en fil: namnet som föreslås är ledigt (2.4.15)
                string fdir = Path.Combine(dir, "ledigt namn"); if (Directory.Exists(fdir)) Directory.Delete(fdir, true);
                Directory.CreateDirectory(fdir);
                string ffilm = Path.Combine(fdir, "Film.S01E01.mkv");
                string free1 = MainForm.FreeTextPath(ffilm, "en");
                File.WriteAllText(free1, "x");
                string free2 = MainForm.FreeTextPath(ffilm, "en");
                File.WriteAllText(free2, "x");
                string free3 = MainForm.FreeTextPath(ffilm, "en");
                Check("Text ur ljudet: filnamnet som föreslås är ledigt, och språkkoden står sist",
                    Path.GetFileName(free1) == "Film.S01E01.en.srt" && Path.GetFileName(free2) == "Film.S01E01.whisper.en.srt" && Path.GetFileName(free3) == "Film.S01E01.whisper2.en.srt" && !File.Exists(free3));

                // rättningar efter mätningen mot en riktig film (2.4.13)
                var inOrder = Srt.Parse("1\n00:00:02,000 --> 00:00:02,100\n What's\n\n2\n00:00:02,000 --> 00:00:02,100\n wrong\n\n3\n00:00:01,950 --> 00:00:02,400\n with\n\n4\n00:00:02,400 --> 00:00:02,900\n you?\n", true);
                Check("Text ur ljudet: ordfilen läses i filens ordning, också när tiderna är lika eller går omlott",
                    string.Join("", inOrder.Select(c => c.Text)) == " What's wrong with you?" && Subtitler.FromWords(inOrder)[0].Text == "What's wrong with you?");
                var stretched = Subtitler.FromWords(new List<Cue> { new Cue(1000, 1300, " Who"), new Cue(1300, 60000, " knows."), new Cue(60000, 60400, " Next.") });
                Check("Text ur ljudet: sista ordet före en tystnad drar inte ut undertexten över tystnaden",
                    stretched.Count == 2 && stretched[0].End < 4000 && stretched[1].Text == "Next.");
                // Whisper lägger "Is it true?" mitt i tystnaden. Talet börjar 30 000 enligt taldetekteringen.
                var early = new List<Cue> { new Cue(10000, 10300, " Fine."), new Cue(24850, 25100, " Is"), new Cue(25100, 25300, " it"), new Cue(25300, 25700, " true?"), new Cue(31250, 31600, " She's"), new Cue(31600, 32000, " alive?") };
                var speechIv = new List<Cue> { new Cue(9900, 10500, "(tal)"), new Cue(30000, 32200, "(tal)") };
                var snapped = Subtitler.FromWords(early, speechIv);
                Check("Text ur ljudet: ord som Whisper lägger i en tystnad flyttas till nästa tal från taldetekteringen, i samma ordning",
                    snapped.Count >= 2 && snapped[1].Text.StartsWith("Is it true?") && snapped[1].Start == 30000 - Subtitler.LeadMs &&
                    string.Join(" ", snapped.Select(c => c.Text.Replace("\n", " "))) == "Fine. Is it true? She's alive?");
                var dangling = Subtitler.FromWords(new List<Cue> { new Cue(1000, 1300, " He's"), new Cue(1300, 1700, " gone."), new Cue(1750, 1900, " As"), new Cue(8000, 8200, " you"), new Cue(8200, 8500, " know.") });
                Check("Text ur ljudet: ett ord som börjar en mening före en lång paus hör till raden efter pausen",
                    dangling.Count == 2 && dangling[0].Text == "He's gone." && dangling[1].Text == "As you know." && dangling[1].Start > 7000);
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

            // ---- fönstrets storlek och plats sparas till nästa gång
            {
                var R = new Func<int, int, int, int, System.Drawing.Rectangle>((x, y, w, h) => new System.Drawing.Rectangle(x, y, w, h));
                var min = new System.Drawing.Size(940, 600);
                var main = R(0, 0, 1920, 1040); var ext = R(1920, 0, 2560, 1400);
                System.Drawing.Rectangle b, area; bool mx;
                Check("Fönster: sparad plats, storlek och maximerat läge läses tillbaka",
                    Tools.ParseWindow(Tools.FormatWindow(R(-1800, 40, 1200, 900), true), out b, out mx) && b == R(-1800, 40, 1200, 900) && mx);
                Check("Fönster: tom eller trasig inställning används inte",
                    !Tools.ParseWindow("", out b, out mx) && !Tools.ParseWindow("10,10,abc,500,0", out b, out mx) && !Tools.ParseWindow("10,10,0,500,0", out b, out mx) && !Tools.ParseWindow(null, out b, out mx));
                Check("Fönster: en plats som ryms på skärmen behålls oförändrad",
                    Tools.FitWindow(R(2000, 100, 1500, 1000), new[] { main, ext }, main, min, out area) == R(2000, 100, 1500, 1000) && area == ext);
                Check("Fönster: när den externa skärmen är borta flyttas fönstret in på skärmen som finns kvar och krymps till den",
                    Tools.FitWindow(R(2000, 100, 2400, 1300), new[] { main }, main, min, out area) == R(0, 0, 1920, 1040) && area == main);
                Check("Fönster: en sparad plats helt utanför alla skärmar ger ett fönster mitt på skärmen",
                    Tools.FitWindow(R(5000, 5000, 1000, 700), new[] { main }, main, min, out area) == R(460, 170, 1000, 700));
                Check("Fönster: ett fönster som sticker ut utanför kanten flyttas in helt",
                    Tools.FitWindow(R(1500, 800, 1000, 700), new[] { main }, main, min, out area) == R(920, 340, 1000, 700));
                Check("Fönster: blir aldrig mindre än minsta storleken, men heller aldrig större än en liten skärm",
                    Tools.FitWindow(R(10, 10, 300, 200), new[] { main }, main, min, out area).Size == min &&
                    Tools.FitWindow(R(0, 0, 300, 200), new[] { R(0, 0, 800, 560) }, R(0, 0, 800, 560), min, out area) == R(0, 0, 800, 560));
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

            // ------------------------------------------------------------ hel säsong: parning och återställning
            {
                Console.WriteLine("Säsong: parning och återställning");
                List<string> un;
                Func<string, string> P = fileName => @"C:\S\" + fileName;

                // S04E01–E03: två sätt att skriva avsnittet, och en film utan text
                var s1 = SeasonPlan.Build(new[] { P("Show.S04E03.1080p.mkv"), P("Show.S04E01.1080p.mkv"), P(@"Subs\show - 4x01 - titel.sv.srt"), P("Show.S04E02.srt"), P("Show.S04E02.1080p.mkv") }, "sv", out un);
                Check("Säsong: tre filmer blir tre poster i avsnittsordning",
                    s1.Count == 3 && s1[0].Title == "S04E01" && s1[1].Title == "S04E02" && s1[2].Title == "S04E03" && s1.All(i => i.RefIsVideo));
                Check("Säsong: texten paras med filmen på avsnittsnummer, också när det skrivs 4x01",
                    s1.Count == 3 && s1[0].RefPath == P("Show.S04E01.1080p.mkv") && s1[0].TargetPath == P(@"Subs\show - 4x01 - titel.sv.srt") && s1[0].Include && s1[0].Note == null &&
                    s1[1].TargetPath == P("Show.S04E02.srt") && s1[1].Include);
                Check("Säsong: film utan undertext finns med, utan bock och med en anmärkning",
                    s1.Count == 3 && s1[2].TargetPath == null && !s1[2].Include && s1[2].Note == "Ingen undertext hittades för avsnittet." && s1[2].Candidates.Count == 0 && s1[2].OutputPath("sv") == null);
                Check("Säsong: inga filer blir över när alla hör till ett avsnitt", un.Count == 0);
                Check("Säsong: resultatet sparas bredvid filmen med filmens namn och språkkoden",
                    s1.Count == 3 && s1[0].OutputPath("sv") == P("Show.S04E01.1080p.sv.srt") && s1[0].OutputPath("no") == P("Show.S04E01.1080p.no.srt") && s1[0].OutputPath(null) == P("Show.S04E01.1080p.sv.srt"));

                // annat språk blir aldrig mål
                var s2 = SeasonPlan.Build(new[] { P("A.S01E01.mkv"), P("A.S01E01.en.srt"), P("A.S01E01.sv.srt") }, "sv", out un);
                Check("Säsong: med svensk och engelsk text är den svenska målet, och den engelska finns inte bland kandidaterna",
                    s2.Count == 1 && s2[0].TargetPath == P("A.S01E01.sv.srt") && s2[0].Candidates.Count == 1 && s2[0].Candidates[0] == P("A.S01E01.sv.srt") && s2[0].Note == null &&
                    un.Count == 1 && un[0] == P("A.S01E01.en.srt"));
                s2 = SeasonPlan.Build(new[] { P("A.S01E01.mkv"), P("A.S01E01.en.srt") }, "sv", out un);
                Check("Säsong: en text på ett annat språk blir aldrig mål", s2.Count == 1 && s2[0].TargetPath == null && !s2[0].Include && un.Count == 1);
                s2 = SeasonPlan.Build(new[] { P("A.S01E01.mkv"), P("A.S01E01.en.srt"), P("a 1x01.srt"), P("A.S01E01.swe.srt") }, "swe", out un);
                Check("Säsong: text med önskat språk går före text utan språkkod, och swe räknas som sv",
                    s2.Count == 1 && s2[0].TargetPath == P("A.S01E01.swe.srt") && s2[0].Note == null && s2[0].Candidates.Count == 2 && s2[0].Candidates[1] == P("a 1x01.srt"));
                s2 = SeasonPlan.Build(new[] { P("A.S01E01.mkv"), P("A.S01E01.en.srt"), P("a 1x01.srt") }, null, out un);
                Check("Säsong: saknas text med önskat språk väljs den utan språkkod", s2.Count == 1 && s2[0].TargetPath == P("a 1x01.srt") && s2[0].Include);

                // utan film: texten på ett annat språk blir referens
                var s3 = SeasonPlan.Build(new[] { P("X.S01E01.en.srt"), P("X.S01E01.sv.srt"), P("Y.S01E02.srt"), P("y - 1x02.srt"), P("Z.S01E03.sv.srt") }, "sv", out un);
                Check("Säsong: utan film blir texten på det andra språket referens",
                    s3.Count == 1 && s3[0].RefPath == P("X.S01E01.en.srt") && s3[0].TargetPath == P("X.S01E01.sv.srt") && !s3[0].RefIsVideo && s3[0].Include && s3[0].Title == "S01E01");
                Check("Säsong: mot en referenstext sparas resultatet som .synkad.srt, aldrig över referensen",
                    s3.Count == 1 && s3[0].OutputPath("sv") == P("X.S01E01.sv.synkad.srt") && s3[0].OutputPath("sv") != s3[0].RefPath);
                Check("Säsong: två texter utan språkkod paras inte, och en ensam text blir över",
                    un.Count == 3 && un[0] == P("Y.S01E02.srt") && un[1] == P("y - 1x02.srt") && un[2] == P("Z.S01E03.sv.srt"));

                // programmets egna filer
                var s4 = SeasonPlan.Build(new[] { P("B.S01E01.mkv"), P("B.S01E01 (Text Sync).mkv"), P(@"Text Sync original\B.S01E01.sv.srt"), P("B.S01E01.whisper.en.srt"),
                    P("B.S01E01.whisper.sv.srt"), P("B.S01E01.sv.srt"), P("B.S01E01.sv.synkad.srt"), P("anteckning.txt") }, "sv", out un);
                Check("Säsong: kopior i Text Sync original, igenkända texter (.whisper.) och MKV-kopian (Text Sync) hoppas över",
                    s4.Count == 1 && s4[0].RefPath == P("B.S01E01.mkv") && s4[0].TargetPath == P("B.S01E01.sv.srt") && s4[0].Candidates.Count == 1 && s4[0].Note == null);
                Check("Säsong: bara främmande filer räknas som överblivna, inte programmets egna", un.Count == 1 && un[0] == P("anteckning.txt"));
                s4 = SeasonPlan.Build(new[] { P("B.S01E01 (Text Sync).mkv"), P("b 1x01.sv.synkad.srt") }, "sv", out un);
                Check("Säsong: en ensam MKV-kopia och en ensam synkad text används när inget annat finns",
                    s4.Count == 1 && s4[0].Include && s4[0].TargetPath == P("b 1x01.sv.synkad.srt"));

                // flera kandidater
                var s5 = SeasonPlan.Build(new[] { P(@"Subs\C.S02E01.sv.srt"), P("c 2x01.sv.srt"), P("C.S02E01.mkv") }, "sv", out un);
                Check("Säsong: flera texter som passar ger en anmärkning, och den i filmens mapp väljs",
                    s5.Count == 1 && s5[0].TargetPath == P("c 2x01.sv.srt") && s5[0].Note == "Flera undertexter passar. Kontrollera att rätt fil är vald." &&
                    s5[0].Candidates.Count == 2 && s5[0].Candidates[0] == s5[0].TargetPath && s5[0].Candidates[1] == P(@"Subs\C.S02E01.sv.srt") && un.Count == 0);
                s5 = SeasonPlan.Build(new[] { P(@"Subs\c.s02e01 b.sv.srt"), P(@"Subs\C.S02E01 a.sv.srt"), P("C.S02E01.mkv") }, "sv", out un);
                Check("Säsong: ligger ingen i filmens mapp väljs den första i bokstavsordning",
                    s5.Count == 1 && s5[0].TargetPath == P(@"Subs\C.S02E01 a.sv.srt") && s5[0].Note != null);
                s5 = SeasonPlan.Build(new[] { P("C.S02E01.mkv"), P("C.S02E01.720p.mkv"), P("C.S02E01.720p.sv.srt"), P("c 2x01.sv.srt") }, "sv", out un);
                Check("Säsong: samma avsnitt två gånger blir två poster med en anmärkning, och filnamnet går före avsnittsnumret",
                    s5.Count == 2 && s5[0].RefPath == P("C.S02E01.720p.mkv") && s5[0].TargetPath == P("C.S02E01.720p.sv.srt") && s5[0].Note.Contains("två gånger") &&
                    s5[1].RefPath == P("C.S02E01.mkv") && s5[1].TargetPath == P("c 2x01.sv.srt") && s5[1].Note.Contains("två gånger") && un.Count == 0);

                // utan avsnittsnummer: bara på filnamnet
                var s6 = SeasonPlan.Build(new[] { P("Min film.mkv"), P("min film.sv.srt"), P("Annan text.sv.srt"), P("D.S01E02.mkv"), P("D.S01E02.srt"), P("Bio.mp4") }, "sv", out un);
                Check("Säsong: filer utan avsnittsnummer paras på filnamnet och hamnar sist",
                    s6.Count == 3 && s6[0].Title == "S01E02" && s6[1].Title == "Bio" && s6[1].Id == null && !s6[1].Include &&
                    s6[2].Title == "Min film" && s6[2].TargetPath == P("min film.sv.srt") && s6[2].Include && s6[2].OutputPath("sv") == P("Min film.sv.srt"));
                Check("Säsong: en text som inte hör till någon film blir över", un.Count == 1 && un[0] == P("Annan text.sv.srt"));
                s6 = SeasonPlan.Build(null, "sv", out un);
                Check("Säsong: ingen fil alls ger en tom lista", s6.Count == 0 && un.Count == 0);

                // riktiga filer bara i testmappen
                string sdir = Path.Combine(dir, "säsong test");
                string oldRoot = SeasonUndo.Root;
                bool oldLogging = SeasonUndo.Logging;
                try
                {
                    SeasonUndo.Logging = false;   // de avsiktliga felen nedan ska inte hamna i den riktiga loggfilen
                    if (Directory.Exists(sdir)) Directory.Delete(sdir, true);
                    string col = Path.Combine(sdir, "Serien S01");
                    Directory.CreateDirectory(Path.Combine(col, "Subs"));
                    Directory.CreateDirectory(Path.Combine(col, MainForm.BackupFolder));
                    Directory.CreateDirectory(Path.Combine(col, @"1\2\3\4"));
                    foreach (var f in new[] { "E.S01E01.mkv", @"Subs\E.S01E01.sv.srt", MainForm.BackupFolder + @"\E.S01E01.sv.srt", "läs mig.txt", @"1\2\3\tre.srt", @"1\2\3\4\fyra.srt" })
                        File.WriteAllBytes(Path.Combine(col, f), new byte[0]);
                    string lone = Path.Combine(sdir, "ensam.srt"), loneTxt = Path.Combine(sdir, "ensam.txt");
                    File.WriteAllBytes(lone, new byte[0]); File.WriteAllBytes(loneTxt, new byte[0]);
                    var got = SeasonPlan.Collect(new[] { col, lone, loneTxt, Path.Combine(sdir, "finns inte"), col });
                    Check("Säsong: en mapp görs om till sina filmer och texter, också i undermappar",
                        got.Count == 4 && got.Contains(Path.Combine(col, "E.S01E01.mkv")) && got.Contains(Path.Combine(col, @"Subs\E.S01E01.sv.srt")) && got.Contains(lone));
                    Check("Säsong: mappen Text Sync original och andra filtyper tas inte med",
                        !got.Any(f => f.Contains(MainForm.BackupFolder)) && !got.Any(f => f.EndsWith(".txt")));
                    Check("Säsong: undermappar tas med högst tre nivåer ner",
                        got.Contains(Path.Combine(col, @"1\2\3\tre.srt")) && !got.Contains(Path.Combine(col, @"1\2\3\4\fyra.srt")));
                    var s7 = SeasonPlan.Build(got, "sv", out un);
                    Check("Säsong: filerna ur mappen paras ihop", s7.Count == 1 && s7[0].Include && s7[0].TargetPath == Path.Combine(col, @"Subs\E.S01E01.sv.srt") && un.Count == 2);

                    // ---- återställning
                    SeasonUndo.Root = Path.Combine(sdir, "ångra");
                    string work = Path.Combine(sdir, "Filmer åäö & mera = bra");
                    Directory.CreateDirectory(work);
                    Func<string, byte[]> B = t => Encoding.UTF8.GetBytes(t);
                    Func<string, string> Rd = f => File.Exists(f) ? Encoding.UTF8.GetString(File.ReadAllBytes(f)) : null;
                    Check("Återställning: utan körningar finns inget att återställa", SeasonUndo.List().Count == 0 && SeasonUndo.Latest() == null);

                    var run = SeasonUndo.Begin("Serien åäö S01");
                    string fNew = Path.Combine(work, "Ny fil S01E01.sv.srt"), fOld = Path.Combine(work, "Gammal S01E02.sv.srt"), fChg = Path.Combine(work, "Ändrad S01E03.sv.srt"),
                        fTwo = Path.Combine(work, "Två gånger S01E04.sv.srt"), fGone = Path.Combine(work, "Borta S01E05.sv.srt"), fLock = Path.Combine(work, "Låst S01E06.sv.srt");
                    byte[] orig = { 0xEF, 0xBB, 0xBF, 0x31, 0x0D, 0x0A, 0xC3, 0xA5, 0x00, 0xFF };
                    File.WriteAllBytes(fOld, orig); File.WriteAllText(fChg, "original 3"); File.WriteAllText(fTwo, "original 4"); File.WriteAllText(fGone, "original 5"); File.WriteAllText(fLock, "original 6");
                    var eNew = SeasonUndo.Write(run, fNew, B("synkad 1"), "S01E01");
                    var eOld = SeasonUndo.Write(run, fOld, B("synkad 2"), "S01E02");
                    var eChg = SeasonUndo.Write(run, fChg, B("synkad 3"), "S01E03");
                    var eTwo = SeasonUndo.Write(run, fTwo, B("synkad 4a"), "S01E04");
                    var eTwo2 = SeasonUndo.Write(run, fTwo, B("synkad 4b"), "S01E04");
                    var eGone = SeasonUndo.Write(run, fGone, B("synkad 5"), "S01E05");
                    var eLock = SeasonUndo.Write(run, fLock, B("synkad 6"), "S01E06");
                    Check("Återställning: filerna skrivs, och ingen temporär fil blir kvar",
                        Rd(fNew) == "synkad 1" && Rd(fOld) == "synkad 2" && Rd(fTwo) == "synkad 4b" && Directory.GetFiles(work, "*.textsync-tmp").Length == 0);
                    Check("Återställning: filen som fanns före sparas som kopia med samma filnamn, och en ny fil har ingen kopia",
                        eNew.Kept == null && eOld.Kept != null && Path.GetFileName(eOld.Kept) == Path.GetFileName(fOld) && eOld.Kept.StartsWith(run.Dir) && File.ReadAllBytes(eOld.Kept).SequenceEqual(orig));
                    Check("Återställning: samma fil skriven två gånger blir en post som behåller det verkliga originalet",
                        ReferenceEquals(eTwo, eTwo2) && run.Entries.Count == 6 && run.Pending == 6 && Rd(eTwo.Kept) == "original 4" && eTwo.Hash == SeasonUndo.HashBytes(B("synkad 4b")));

                    var listed = SeasonUndo.List();
                    var fromDisk = listed.Count == 1 ? listed[0] : new UndoRun();
                    Check("Återställning: körningen läses tillbaka från disken med rätt poster, också med å/ä/ö och blanksteg i sökvägen",
                        listed.Count == 1 && !ReferenceEquals(fromDisk, run) && fromDisk.Title == "Serien åäö S01" && fromDisk.When == run.When && fromDisk.Dir == run.Dir && fromDisk.Entries.Count == 6 && fromDisk.Pending == 6 &&
                        Enumerable.Range(0, Math.Min(6, fromDisk.Entries.Count)).All(ix => fromDisk.Entries[ix].Path == run.Entries[ix].Path && fromDisk.Entries[ix].Kept == run.Entries[ix].Kept &&
                            fromDisk.Entries[ix].Hash == run.Entries[ix].Hash && fromDisk.Entries[ix].Label == run.Entries[ix].Label && !fromDisk.Entries[ix].Restored));
                    var latest = SeasonUndo.Latest();
                    Check("Återställning: den nyaste körningen med något kvar hittas", latest != null && latest.Dir == run.Dir && !ReferenceEquals(latest, run));

                    var rNew = SeasonUndo.Restore(run, eNew);
                    Check("Återställning: en fil som inte fanns före tas bort", rNew.Outcome == RestoreOutcome.Removed && !File.Exists(fNew) && eNew.Restored && rNew.Entry == eNew && !string.IsNullOrEmpty(rNew.Message));
                    var rOld = SeasonUndo.Restore(run, eOld);
                    Check("Återställning: originalet kommer tillbaka byte för byte", rOld.Outcome == RestoreOutcome.Restored && File.ReadAllBytes(fOld).SequenceEqual(orig) && eOld.Restored);
                    File.WriteAllText(fOld, "användarens nya text");
                    var rOld2 = SeasonUndo.Restore(run, eOld);
                    Check("Återställning: en redan återställd post rörs inte en gång till", rOld2.Outcome == RestoreOutcome.Restored && Rd(fOld) == "användarens nya text");
                    File.WriteAllText(fChg, "ändrad av användaren");
                    var rChg = SeasonUndo.Restore(run, eChg);
                    Check("Återställning: en fil som har ändrats efter synken rörs inte",
                        rChg.Outcome == RestoreOutcome.Changed && Rd(fChg) == "ändrad av användaren" && !eChg.Restored && rChg.Message.Contains("Ändrad S01E03.sv.srt") && rChg.Message.Contains("rördes inte"));
                    File.Delete(eGone.Kept);
                    var rGone = SeasonUndo.Restore(run, eGone);
                    Check("Återställning: saknas kopian av originalet misslyckas återställningen och filen rörs inte",
                        rGone.Outcome == RestoreOutcome.Failed && Rd(fGone) == "synkad 5" && !eGone.Restored && rGone.Message.Contains("Kopian"));
                    RestoreResult rLock;
                    using (new FileStream(fLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        rLock = SeasonUndo.Restore(run, eLock);
                    Check("Återställning: en låst fil ger ett begripligt fel i stället för ett undantag",
                        rLock.Outcome == RestoreOutcome.Failed && !eLock.Restored && rLock.Message.Contains("öppen i ett annat program") && Rd(fLock) == "synkad 6");

                    // från disken, som efter en omstart
                    var again = SeasonUndo.Latest();
                    Check("Återställning: det som är återställt minns programmet till nästa start",
                        again != null && again.Pending == 4 && again.Entries[0].Restored && again.Entries[1].Restored && !again.Entries[2].Restored);
                    var all = again != null ? SeasonUndo.RestoreAll(again) : new List<RestoreResult>();
                    Check("Återställning: Återställ alla hoppar över det som redan är återställt",
                        all.Count == 4 && all.Count(r => r.Outcome == RestoreOutcome.Restored) == 2 && all.Count(r => r.Outcome == RestoreOutcome.Changed) == 1 && all.Count(r => r.Outcome == RestoreOutcome.Failed) == 1);
                    Check("Återställning: efter två skrivningar av samma fil kommer det verkliga originalet tillbaka",
                        Rd(fTwo) == "original 4" && Rd(fLock) == "original 6" && Rd(fChg) == "ändrad av användaren" && Rd(fGone) == "synkad 5" && Rd(fOld) == "användarens nya text" && !File.Exists(fNew));
                    Check("Återställning: inga temporära filer blir kvar efter återställningen", Directory.GetFiles(work, "*.textsync-tmp").Length == 0 && again != null && again.Pending == 2);

                    // filen borta, originalet redan på plats, och en post från en annan körning
                    var run2 = SeasonUndo.Begin(null);
                    string fDel = Path.Combine(work, "Raderad.srt"), fDel2 = Path.Combine(work, "Raderad ny.srt"), fSame = Path.Combine(work, "Redan original.srt");
                    File.WriteAllText(fDel, "original 7"); File.WriteAllText(fSame, "original 8");
                    var eDel = SeasonUndo.Write(run2, fDel, B("synkad 7"), null);
                    var eDel2 = SeasonUndo.Write(run2, fDel2, B("synkad 7b"), "x");
                    var eSame = SeasonUndo.Write(run2, fSame, B("synkad 8"), "y");
                    File.Delete(fDel); File.Delete(fDel2); File.WriteAllText(fSame, "original 8");
                    var rDel = SeasonUndo.Restore(run2, eDel);
                    var rDel2 = SeasonUndo.Restore(run2, eDel2);
                    var rSame = SeasonUndo.Restore(run2, eSame);
                    Check("Återställning: är den synkade filen borta läggs originalet tillbaka, och fanns inget original görs ingenting",
                        rDel.Outcome == RestoreOutcome.Restored && Rd(fDel) == "original 7" && rDel2.Outcome == RestoreOutcome.Removed && !File.Exists(fDel2));
                    Check("Återställning: ligger originalet redan på plats räknas posten som återställd", rSame.Outcome == RestoreOutcome.Restored && Rd(fSame) == "original 8" && run2.Pending == 0);
                    Check("Återställning: en post från en annan körning återställs inte", SeasonUndo.Restore(run2, eChg).Outcome == RestoreOutcome.Failed && Rd(fChg) == "ändrad av användaren");
                    Check("Återställning: en körning utan namn får mappens namn, och två körningar samma sekund får var sin mapp",
                        run2.Title == Path.GetFileName(run2.Dir) && run2.Dir != run.Dir && Directory.Exists(run2.Dir));
                    Check("Återställning: en körning där allt är återställt är inte längre den senaste", SeasonUndo.Latest() != null && SeasonUndo.Latest().Dir == run.Dir && SeasonUndo.List().Count == 2 && SeasonUndo.List()[0].Dir == run2.Dir);
                    var eRe = SeasonUndo.Write(run2, fDel, B("synkad igen"), "z");
                    Check("Återställning: en fil som skrivs igen efter återställning får en ny post med en ny kopia",
                        !ReferenceEquals(eRe, eDel) && run2.Pending == 1 && Rd(eRe.Kept) == "original 7" && SeasonUndo.Restore(run2, eRe).Outcome == RestoreOutcome.Restored && Rd(fDel) == "original 7");

                    // skrivfel: ingen post blir kvar
                    bool threw = false;
                    int before = run2.Entries.Count;
                    try { using (new FileStream(fSame, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) SeasonUndo.Write(run2, fSame, B("går inte"), "q"); }
                    catch (IOException) { threw = true; }
                    Check("Återställning: går filen inte att skriva blir ingen post kvar", threw && run2.Entries.Count == before && Rd(fSame) == "original 8" && SeasonUndo.List()[0].Entries.Count == before);

                    // trasigt manifest och rensning
                    string broken = Path.Combine(SeasonUndo.Root, "20000101-000000");
                    Directory.CreateDirectory(broken);
                    File.WriteAllText(Path.Combine(broken, "manifest.txt"), "inte ett manifest\r\n");
                    string foreign = Path.Combine(SeasonUndo.Root, "Användarens mapp");
                    Directory.CreateDirectory(foreign);
                    File.WriteAllText(Path.Combine(foreign, "viktigt.txt"), "rör ej");
                    Check("Återställning: ett trasigt manifest hoppas över", SeasonUndo.List().Count == 2);
                    var run3 = SeasonUndo.Begin("nyast");
                    SeasonUndo.Prune(1);
                    Check("Återställning: rensningen lämnar bara den nyaste körningen",
                        Directory.Exists(run3.Dir) && !Directory.Exists(run2.Dir) && !Directory.Exists(run.Dir) && !Directory.Exists(broken) && SeasonUndo.List().Count == 1);
                    Check("Återställning: rensningen rör inte en mapp utan manifest, och inte användarens filer",
                        File.Exists(Path.Combine(foreign, "viktigt.txt")) && Rd(fChg) == "ändrad av användaren" && Rd(fDel) == "original 7");
                    SeasonUndo.Prune(0);
                    Check("Återställning: rensning av allt tar bort körningarna men inget annat", !Directory.Exists(run3.Dir) && Directory.Exists(foreign) && SeasonUndo.Latest() == null);
                }
                finally
                {
                    SeasonUndo.Root = oldRoot;
                    SeasonUndo.Logging = oldLogging;
                    try { if (Directory.Exists(sdir)) Directory.Delete(sdir, true); } catch { }
                }
            }

            // Hel säsong från början till slut: mapp → parning → synk av varje avsnitt → sparat → återställt
            {
                string sd = Path.Combine(dir, "säsong kö"); if (Directory.Exists(sd)) Directory.Delete(sd, true);
                Directory.CreateDirectory(Path.Combine(sd, "Subs"));
                string oldRoot = SeasonUndo.Root; bool oldLogging = SeasonUndo.Logging;
                SeasonUndo.Root = Path.Combine(sd, "ångra"); SeasonUndo.Logging = false;
                try
                {
                    var r1 = MakeRef(20); var o1 = MakeOther(r1);
                    var r2 = MakeRef(20); var o2 = MakeOther(r2);
                    var r3 = MakeRef(20); var wrong = MakeOther(MakeRef(20));   // E03: en text som hör till något helt annat
                    WriteMkv(Path.Combine(sd, "Serie.S01E01.mkv"), r1, false, false);
                    WriteMkv(Path.Combine(sd, "Serie.S01E02.mkv"), r2, false, false);
                    WriteMkv(Path.Combine(sd, "Serie.S01E03.mkv"), r3, false, false);
                    string t1 = Path.Combine(sd, "Serie.S01E01.sv.srt"), t2 = Path.Combine(sd, @"Subs\serie 1x02.srt"), t3 = Path.Combine(sd, "Serie.S01E03.sv.srt");
                    Srt.Save(t1, Distort(o1, t => t + 2500));                    // ligger redan där resultatet ska sparas: skrivs över
                    Srt.Save(t2, Distort(o2, t => t * 25 / 23.976 - 1200));      // i en undermapp: resultatet blir en ny fil bredvid filmen
                    Srt.Save(t3, wrong);
                    byte[] orig1 = File.ReadAllBytes(t1), orig3 = File.ReadAllBytes(t3);
                    string out2 = Path.Combine(sd, "Serie.S01E02.sv.srt");

                    List<string> un;
                    var plan = SeasonPlan.Build(SeasonPlan.Collect(new[] { sd }), "sv", out un);
                    Check("Hel säsong: tre avsnitt paras ihop ur mappen", plan.Count == 3 && plan.All(pl => pl.Include && pl.RefIsVideo) &&
                        plan[0].TargetPath == t1 && plan[1].TargetPath == t2 && plan[2].TargetPath == t3 && plan[1].OutputPath("sv") == out2);
                    Check("Hel säsong: en mapp eller två filmer räknas som en säsong, en film och en text gör det inte",
                        MainForm.LooksLikeSeason(new[] { sd }) && MainForm.LooksLikeSeason(new[] { @"C:\a\A.S01E01.mkv", @"C:\a\A.S01E02.mp4", @"C:\a\A.srt" }) &&
                        !MainForm.LooksLikeSeason(new[] { @"C:\a\Film.mkv", @"C:\a\Film.sv.srt" }) && !MainForm.LooksLikeSeason(new string[0]));
                    var ch = SeasonSync.ChooseRef(plan[0].RefPath, SeasonRefMode.Auto, false);
                    var chA = SeasonSync.ChooseRef(plan[0].RefPath, SeasonRefMode.Speech, true);
                    Check("Hel säsong: filmens textspår väljs som referens, och ljudet när det är valt",
                        ch.Track != null && !ch.Audio && chA.Audio && chA.Mode == AudioMode.Speech && SeasonSync.ChooseRef(plan[0].RefPath, SeasonRefMode.Combined, false).Mode == AudioMode.Combined);

                    var run = SeasonUndo.Begin("Serie");
                    var opt = new SeasonOptions { Lang = "sv" };
                    var res = new List<EpisodeOutcome>();
                    foreach (var pl in plan) res.Add(SeasonSync.SyncOne(pl, opt, () => run, (a, b) => { }, System.Threading.CancellationToken.None));
                    Console.WriteLine("Hel säsong: {0} | {1} | {2}", res[0].Text, res[1].Text, res[2].Text);
                    Check("Hel säsong: avsnitt 1 och 2 synkas och sparas bredvid filmen", res[0].Saved && res[1].Saved &&
                        (res[0].State == EpisodeState.Good || res[0].State == EpisodeState.Check) && (res[1].State == EpisodeState.Good || res[1].State == EpisodeState.Check) && File.Exists(out2));
                    // facit: texten på varje rad är den sanna starttiden
                    Func<string, bool> inSync = f => { var c = Srt.Load(f).Where(x => x.Text.StartsWith("T|")).ToList(); return c.Count > 100 && c.Count(x => Math.Abs(x.Start - double.Parse(x.Text.Substring(2))) < 300) > 0.97 * c.Count; };
                    Check("Hel säsong: de sparade undertexterna går i takt (förskjutning och fel bildfrekvens)", inSync(t1) && inSync(out2));
                    Check("Hel säsong: en text som inte passar filmen blir Osäkert och sparas inte",
                        res[2].State == EpisodeState.Unsure && !res[2].Saved && File.ReadAllBytes(t3).SequenceEqual(orig3));
                    Check("Hel säsong: den överskrivna filen finns också i mappen Text Sync original, och den osynkade i undermappen är orörd",
                        File.ReadAllBytes(Path.Combine(sd, MainForm.BackupFolder, "Serie.S01E01.sv.srt")).SequenceEqual(orig1) && !inSync(t2));
                    Check("Hel säsong: beskedet säger var filen sparades och vilken referens som användes",
                        res[1].Detail.Contains(out2) && res[1].Detail.Contains("textspår") && res[2].Detail.Contains("Ingenting har sparats"));

                    // inställningarna följer med: formatering tas bort ur det som sparas
                    string t4 = Path.Combine(sd, "Subs", "serie 1x02 kursiv.srt");
                    Srt.Save(t4, Distort(o2, t => t + 900).Select(c => new Cue(c.Start, c.End, "<i>" + c.Text + "</i>")).ToList());
                    var it4 = new SeasonItem { RefPath = plan[1].RefPath, RefIsVideo = true, TargetPath = t4, Include = true };
                    var r4 = SeasonSync.SyncOne(it4, new SeasonOptions { Lang = "sv", StripFormatting = true }, () => run, (a, b) => { }, System.Threading.CancellationToken.None);
                    Check("Hel säsong: Ta bort formatering gäller det som sparas", r4.Saved && !File.ReadAllText(out2).Contains("<i>") && inSync(out2));

                    // inställningen Godkänn förslag om radindelning automatiskt gäller också det som sparas i kön
                    SeasonSync.SyncOne(plan[1], opt, () => run, (a, b) => { }, System.Threading.CancellationToken.None);
                    int rowsPlain = Srt.Load(out2).Count;
                    var rA = SeasonSync.SyncOne(plan[1], new SeasonOptions { Lang = "sv", AutoAccept = true }, () => run, (a, b) => { }, System.Threading.CancellationToken.None);
                    // en sammanslagen rad har två rader text ("T|a" och "T|b"): den första är radens sanna starttid
                    var autoCues = Srt.Load(out2).Where(x => x.Text.StartsWith("T|")).ToList();
                    bool autoInSync = autoCues.Count > 100 && autoCues.Count(x => Math.Abs(x.Start - double.Parse(x.Text.Split('\n')[0].Substring(2))) < 300) > 0.97 * autoCues.Count;
                    int rowsAuto = Srt.Load(out2).Count;
                    Console.WriteLine("Hel säsong, radindelning: {0} rader utan inställningen, {1} med", rowsPlain, rowsAuto);
                    Check("Hel säsong: förslag om radindelning godkänns automatiskt när inställningen är på, och beskedet säger det",
                        rA.Saved && rowsAuto < rowsPlain && rA.Detail.Contains("Radindelning som referensen:") && rA.Detail.Contains("sammanslag") && autoInSync);

                    // avbrott: inget sparas för avsnittet som pågick
                    var cts = new System.Threading.CancellationTokenSource(); cts.Cancel();
                    bool cancelled = false; byte[] before = File.ReadAllBytes(t1);
                    try { SeasonSync.SyncOne(plan[0], opt, () => run, (a, b) => { }, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
                    Check("Hel säsong: ett avbrutet avsnitt sparar ingenting", cancelled && File.ReadAllBytes(t1).SequenceEqual(before));

                    // återställning: det som fanns före kommer tillbaka, och den nya filen tas bort
                    var undone = SeasonUndo.RestoreAll(SeasonUndo.Latest());
                    Check("Hel säsong: Återställ lägger tillbaka den osynkade filen och tar bort den nya",
                        undone.Count == 2 && undone.All(x => x.Outcome == RestoreOutcome.Restored || x.Outcome == RestoreOutcome.Removed) &&
                        File.ReadAllBytes(t1).SequenceEqual(orig1) && !File.Exists(out2) && File.Exists(t2) && File.Exists(t3) && SeasonUndo.Latest() == null);
                    Check("Hel säsong: filmerna rörs aldrig", new[] { "Serie.S01E01.mkv", "Serie.S01E02.mkv", "Serie.S01E03.mkv" }.All(f => File.Exists(Path.Combine(sd, f))));

                    // rättningar efter granskningen
                    List<string> un2;
                    var pk = SeasonPlan.Build(new[] { @"C:\s\F.S01E01.mkv", @"C:\s\F.S01E01.sv.sdh.srt", @"C:\s\F.S01E01.sv.forced.srt", @"C:\s\F.S01E01.sv.srt" }, "sv", out un2);
                    Check("Hel säsong: den vanliga texten väljs före text för hörselskadade och tvingad text", pk.Count == 1 && pk[0].TargetPath == @"C:\s\F.S01E01.sv.srt");
                    Func<string, byte[]> u8 = s => Encoding.UTF8.GetBytes(s);
                    var runB = SeasonUndo.Begin("b"); string fb = Path.Combine(sd, "b.srt");
                    File.WriteAllText(fb, "original");
                    SeasonUndo.Write(runB, fb, u8("synk 1"), "x");
                    File.WriteAllText(fb, "ändrad för hand");
                    SeasonUndo.Write(runB, fb, u8("synk 2"), "x");
                    SeasonUndo.RestoreAll(runB);
                    Check("Återställning: en fil som ändrades för hand mellan två synkar får en egen kopia och kommer tillbaka",
                        runB.Entries.Count == 2 && File.ReadAllText(fb) == "ändrad för hand" && File.ReadAllText(runB.Entries[0].Kept) == "original");
                    // ägarens val 2026-11-11: osäkra avsnitt kan sparas ändå, och undertexten kan också läggas in i en kopia av filmen
                    var runE = SeasonUndo.Begin("val");
                    var rU = SeasonSync.SyncOne(plan[2], new SeasonOptions { Lang = "sv", SaveUnsure = true }, () => runE, (a, b) => { }, System.Threading.CancellationToken.None);
                    Check("Hel säsong: med Spara även osäkra avsnitt sparas ett osäkert avsnitt, med ett besked som säger det",
                        rU.State == EpisodeState.Unsure && rU.Saved && rU.Text.Contains("sparad ändå") && !File.ReadAllBytes(t3).SequenceEqual(orig3));
                    Check("Hel säsong: det osäkra avsnittet går att återställa", SeasonUndo.Restore(runE, rU.Entry).Outcome == RestoreOutcome.Restored && File.ReadAllBytes(t3).SequenceEqual(orig3));
                    string big = Path.Combine(sd, "Serie.S01E09 (Text Sync).mkv");
                    File.WriteAllText(big, "kopia av filmen");
                    var eBig = SeasonUndo.RegisterCreated(runE, big, "S01E09");
                    var reread = SeasonUndo.List().First(x => x.Dir == runE.Dir);
                    Check("Återställning: en skapad kopia av filmen känns igen på storlek och tid, och står i listan på disken",
                        eBig.Hash.StartsWith("size-") && reread.Entries.Any(x => x.Path == big && x.Hash == eBig.Hash && x.Kept == null));
                    File.AppendAllText(big, " som någon har ändrat");
                    Check("Återställning: en kopia av filmen som har ändrats tas inte bort", SeasonUndo.Restore(runE, eBig).Outcome == RestoreOutcome.Changed && File.Exists(big));
                    string big2 = Path.Combine(sd, "Serie.S01E08 (Text Sync).mkv");
                    File.WriteAllText(big2, "kopia av filmen");
                    var eBig2 = SeasonUndo.RegisterCreated(runE, big2, "S01E08");
                    Check("Återställning: en orörd kopia av filmen tas bort", SeasonUndo.Restore(runE, eBig2).Outcome == RestoreOutcome.Removed && !File.Exists(big2));
                    if (MainForm.FindMkvMergePublic() != null)
                    {
                        byte[] film1 = File.ReadAllBytes(plan[0].RefPath);
                        var rM = SeasonSync.SyncOne(plan[0], new SeasonOptions { Lang = "sv", MakeMkv = true }, () => runE, (a, b) => { }, System.Threading.CancellationToken.None);
                        string copy = SeasonSync.MkvCopyPath(plan[0].RefPath);
                        bool hasTrack = false;
                        if (File.Exists(copy)) using (var mf = new MkvFile(copy)) hasTrack = mf.Tracks.Count(x => x.IsSubtitle) == 2;
                        Console.WriteLine("Hel säsong MKV: {0}", rM.Text);
                        Check("Hel säsong: MKV-kopian skapas bredvid filmen med undertexten inlagd, och originalfilmen är orörd",
                            rM.MkvEntry != null && hasTrack && rM.Text.Contains("SRT och MKV") && File.ReadAllBytes(plan[0].RefPath).SequenceEqual(film1));
                        var rM2 = SeasonSync.SyncOne(plan[0], new SeasonOptions { Lang = "sv", MakeMkv = true }, () => runE, (a, b) => { }, System.Threading.CancellationToken.None);
                        Check("Hel säsong: en MKV-kopia som redan finns skrivs inte över, och raden säger det", rM2.MkvEntry == null && rM2.State == EpisodeState.Check && rM2.Detail.Contains("finns redan"));
                        SeasonUndo.RestoreAll(runE);
                        Check("Hel säsong: Återställ tar bort MKV-kopian och lägger tillbaka undertexten", !File.Exists(copy) && File.ReadAllBytes(t1).SequenceEqual(orig1) && File.Exists(plan[0].RefPath));
                    }
                    // knappen Lägg in i MKV…: en redan sparad undertext läggs in i efterhand
                    if (MainForm.FindMkvMergePublic() != null)
                    {
                        string copy = SeasonSync.MkvCopyPath(plan[0].RefPath);
                        var rS = SeasonSync.SyncOne(plan[0], new SeasonOptions { Lang = "sv" }, () => runE, (a, b) => { }, System.Threading.CancellationToken.None);
                        UndoEntry me, me2; UndoRun mr, mr2;
                        string merr = SeasonSync.MuxSaved(plan[0].RefPath, rS.Entry.Path, "sv", "S01E01", () => runE, (a, b) => { }, System.Threading.CancellationToken.None, out me, out mr);
                        bool two = false;
                        if (File.Exists(copy)) using (var mf = new MkvFile(copy)) two = mf.Tracks.Count(x => x.IsSubtitle) == 2;
                        Check("Hel säsong: Lägg in i MKV… lägger in den sparade undertexten i en kopia av filmen", merr == null && me != null && mr == runE && two && rS.MkvEntry == null);
                        string merr2 = SeasonSync.MuxSaved(plan[0].RefPath, rS.Entry.Path, "sv", "S01E01", () => runE, (a, b) => { }, System.Threading.CancellationToken.None, out me2, out mr2);
                        Check("Hel säsong: Lägg in i MKV… skriver inte över en kopia som finns", merr2 != null && merr2.Contains("finns redan") && me2 == null);
                        SeasonUndo.RestoreAll(runE);
                        Check("Hel säsong: Återställ tar bort kopian som knappen skapade", !File.Exists(copy) && File.ReadAllBytes(t1).SequenceEqual(orig1));
                    }
                    else Console.WriteLine("OBS: testet av MKV-kopior i Hel säsong hoppades över, eftersom mkvmerge (MKVToolNix) inte är installerat.");
                    var runC = SeasonUndo.Begin("tom");
                    SeasonUndo.PruneFinished(null);
                    Check("Återställning: omgångar utan något kvar att återställa rensas, de med något kvar står kvar",
                        !Directory.Exists(runC.Dir) && !Directory.Exists(run.Dir) && Directory.Exists(runB.Dir) && File.ReadAllText(fb) == "ändrad för hand");
                }
                finally
                {
                    SeasonUndo.Root = oldRoot; SeasonUndo.Logging = oldLogging;
                    try { Directory.Delete(sd, true); } catch { }
                }
            }

            // ---------------- betydelsejämförelsen (Meaning.cs): påhittade vektorer i stället för språkmodellen
            {
                var mr = new Random(7);
                Func<int, float[]> rndVec = dim => { var v = new float[dim]; for (int i = 0; i < dim; i++) v[i] = (float)(mr.NextDouble() * 2 - 1); Meaning.Normalize(v); return v; };
                Func<float[], double, float[]> near = (v, noise) => { var w = new float[v.Length]; var z = rndVec(v.Length); for (int i = 0; i < v.Length; i++) w[i] = (float)(v[i] + noise * z[i]); Meaning.Normalize(w); return w; };
                // 300 referensrader, en var fjärde sekund. Texten har samma rader med lite andra tider ("översättningen").
                var mref = new List<Cue>(); var mrefV = new List<float[]>();
                for (int i = 0; i < 300; i++) { mref.Add(new Cue(10000 + i * 4000, 12500 + i * 4000, "Reference line number " + i)); mrefV.Add(rndVec(256)); }
                var truth = new List<double>(); var mtgt = new List<Cue>(); var mtgtV = new List<float[]>();
                for (int i = 0; i < 300; i++)
                {
                    if (i % 11 == 5) continue;   // några rader saknas i översättningen
                    double s = mref[i].Start + (mr.NextDouble() * 300 - 150);
                    truth.Add(s); mtgt.Add(new Cue(s, s + 2400, "Det här är rad nummer " + i)); mtgtV.Add(near(mrefV[i], 0.35));
                }
                MeaningReport rep;
                // a) synken är redan rätt: ingenting ska flyttas
                var same = Meaning.Refine(mref, mtgt, mrefV.ToArray(), mtgtV.ToArray(), out rep);
                Console.WriteLine("Betydelse, rätt synk: " + rep.LogText());
                Check("Betydelse: en text som redan går i takt ändras inte", rep.Moved == 0 && !rep.TooFew && rep.Anchors > 200 && same.Zip(mtgt, (x, y) => x.Start == y.Start && x.End == y.End).All(x => x));
                // b) synkmotorn missade ett kort klipp: rad 100–119 ligger 1 s för tidigt
                var off = mtgt.Select(c => c.Clone()).ToList();
                for (int i = 100; i < 120; i++) { off[i].Start -= 1000; off[i].End -= 1000; off[i].OrigStart = 123; off[i].Raw = "rå"; }
                var fixedUp = Meaning.Refine(mref, off, mrefV.ToArray(), mtgtV.ToArray(), out rep);
                Console.WriteLine("Betydelse, missat klipp: " + rep.LogText());
                bool inside = true, outside = true;
                for (int i = 0; i < off.Count; i++)
                {
                    if (i >= 100 && i < 120) inside &= Math.Abs(fixedUp[i].Start - truth[i]) < 300;
                    else outside &= fixedUp[i].Start == off[i].Start;
                }
                Check("Betydelse: rader som ligger 1 s fel flyttas rätt, och inga andra rader rörs", inside && outside && rep.Moved == 20 && rep.Places == 1);
                Check("Betydelse: raden behåller längd, tid före synk och ursprunglig text", fixedUp[105].End - fixedUp[105].Start == off[105].End - off[105].Start && fixedUp[105].OrigStart == 123 && fixedUp[105].Raw == "rå");
                Check("Betydelse: raderna byter aldrig ordning", Enumerable.Range(1, fixedUp.Count - 1).All(i => fixedUp[i].Start >= fixedUp[i - 1].Start));
                Check("Betydelse: en flyttad rad överlappar inte raden före mer än den gjorde innan",
                    Enumerable.Range(1, fixedUp.Count - 1).All(i => fixedUp[i].Start >= fixedUp[i - 1].End - Math.Max(0, off[i - 1].End - off[i].Start) - 0.001));
                // växla mellan den vanliga synken och synken med betydelsejämförelsen
                Meaning.MarkMoved(off, fixedUp);
                var plainAgain = Meaning.Switch(fixedUp);
                var withAgain = Meaning.Switch(plainAgain);
                Check("Betydelse: de flyttade raderna märks, och listan visar först synken med betydelsejämförelsen", Meaning.MovedCount(fixedUp) == 20 && !Meaning.ShowsPlain(fixedUp));
                Check("Betydelse: växling ger den vanliga synken tillbaka, med samma längd på raderna",
                    Meaning.ShowsPlain(plainAgain) && Meaning.MovedCount(plainAgain) == 20 &&
                    plainAgain.Zip(off, (x, y) => Math.Abs(x.Start - y.Start) < 0.001 && Math.Abs(x.End - y.End) < 0.001).All(x => x));
                Check("Betydelse: växling en gång till ger synken med betydelsejämförelsen igen",
                    !Meaning.ShowsPlain(withAgain) && withAgain.Zip(fixedUp, (x, y) => Math.Abs(x.Start - y.Start) < 0.001).All(x => x));
                var extraShift = SyncEngine.ApplyExtra(fixedUp, 250);
                Check("Betydelse: märket och den andra tiden följer med vid extra justering", Meaning.MovedCount(extraShift) == 20 && Math.Abs(extraShift[105].MeaningAlt - (off[105].Start + 250)) < 0.001);
                var edited = fixedUp.Select(c => c.Clone()).ToList();
                edited[105] = new Cue(edited[105].Start + 40, edited[105].End, "Ändrad för hand");
                Check("Betydelse: en rad som har ändrats för hand rörs inte av växlingen", Meaning.Switch(edited).Any(c => c.Text == "Ändrad för hand" && c.Start == edited[105].Start));
                // rader som ligger längre än 10 s från sin referensrad flyttas inte (en scen som upprepas, eller fel avsnitt)
                var far = mtgt.Select(c => c.Clone()).ToList();
                for (int i = 200; i < far.Count; i++) { far[i].Start += 30000; far[i].End += 30000; }
                var farOut = Meaning.Refine(mref, far, mrefV.ToArray(), mtgtV.ToArray(), out rep);
                Check("Betydelse: rader flyttas aldrig längre än 10 s", rep.Moved == 0 && farOut.Zip(far, (x, y) => x.Start == y.Start).All(x => x));
                // c) enstaka rader som paras med grannraden (t.ex. en rad som svenskan har slagit ihop) flyttar ingenting
                var mixV = mtgtV.ToArray();
                foreach (int i in new[] { 30, 90, 150, 210 }) mixV[i] = near(mrefV[int.Parse(mtgt[i].Text.Substring(mtgt[i].Text.LastIndexOf(' ') + 1)) + 1], 0.35);
                Meaning.Refine(mref, mtgt, mrefV.ToArray(), mixV, out rep);
                Check("Betydelse: enstaka par som pekar på grannraden flyttar ingenting", rep.Moved == 0);
                // d) en annan text (inga rader betyder samma sak): ingenting ändras
                var otherV = mtgt.Select(c => rndVec(256)).ToArray();
                var unrelated = Meaning.Refine(mref, off, mrefV.ToArray(), otherV, out rep);
                Console.WriteLine("Betydelse, annan text: " + rep.LogText());
                Check("Betydelse: en text som inte hör till referensen ändras inte", rep.Moved == 0 && unrelated.Zip(off, (x, y) => x.Start == y.Start).All(x => x));
                // e) korta rader jämförs inte, och rader utan vektor följer med sitt avsnitt
                Check("Betydelse: korta rader jämförs inte, taggar och replikstreck tas bort", !Meaning.Usable(Meaning.Clean("- Ja.\n- Okej.")) && Meaning.Clean("<i>- Var är\nhan?</i>") == "Var är han?" && Meaning.Usable("Var är han någonstans?"));
                var holes = mtgtV.ToArray(); for (int i = 0; i < holes.Length; i += 3) holes[i] = null;
                var withHoles = Meaning.Refine(mref, off, mrefV.ToArray(), holes, out rep);
                Check("Betydelse: rader som inte jämförs flyttas med raderna runt omkring", Enumerable.Range(100, 20).All(i => Math.Abs(withHoles[i].Start - truth[i]) < 300) && withHoles[50].Start == off[50].Start && rep.Moved == 20);

                // f) översättningen slår ihop två referensrader till en: med grupper mäts avvikelsen mot den första raden,
                //    och då hittas också ett kort fel (7 rader) som annars drunknar i bruset
                {
                    var gref = new List<Cue>(); var grefV = new List<float[]>();
                    for (int i = 0; i < 300; i++) { gref.Add(new Cue(10000 + i * 2500, 12000 + i * 2500, "Reference line number " + i)); grefV.Add(rndVec(256)); }
                    var pairV = new float[300][];
                    for (int i = 0; i + 1 < 300; i++) { pairV[i] = new float[256]; for (int k = 0; k < 256; k++) pairV[i][k] = grefV[i][k] + grefV[i + 1][k]; Meaning.Normalize(pairV[i]); }
                    var gt = new List<Cue>(); var gtV = new List<float[]>(); var gTruth = new List<double>();
                    for (int i = 0; i + 1 < 300; i += 2)
                    {
                        double s = gref[i].Start + (mr.NextDouble() * 200 - 100);
                        gTruth.Add(s); gt.Add(new Cue(s, s + 4200, "Två rader i en, nummer " + i)); gtV.Add(near(pairV[i], 0.35));
                    }
                    for (int i = 60; i < 67; i++) { gt[i].Start += 1000; gt[i].End += 1000; }
                    var gOut = Meaning.Refine(gref, gt, grefV.ToArray(), gtV.ToArray(), out rep, new[] { pairV });
                    Console.WriteLine("Betydelse, två rader i en: " + rep.LogText());
                    Check("Betydelse: en rad som motsvarar två referensrader paras med båda, och ett kort fel på 7 rader rättas",
                        rep.Grouped > 120 && rep.Moved == 7 && Enumerable.Range(0, gt.Count).All(i => Math.Abs(gOut[i].Start - gTruth[i]) < 300));
                    var gtexts = Meaning.GroupTexts(new List<Cue> { new Cue(0, 1000, "Hej."), new Cue(1500, 2500, "<i>Var är du?</i>"), new Cue(9000, 9500, "Här.") }, 2);
                    Check("Betydelse: bara referensrader som följer tätt på varandra bildar en grupp", gtexts[0] == "Hej. Var är du?" && gtexts[1] == "" && gtexts[2] == "");
                }

                // g) repliker: "- Han är polis." står sist i en rad, men referensen har den först i nästa rad → ett förslag om att flytta.
                //    Och en enskild rad som ligger 0,6 s före sin referensrad läggs på referensradens starttid.
                {
                    var uref = new List<Cue>(); var utgt = new List<Cue>();
                    var vecOf = new Dictionary<string, float[]>();
                    Func<string, float[]> vec = t => { float[] v; if (!vecOf.TryGetValue(t, out v)) { v = rndVec(256); vecOf[t] = v; } return v; };
                    for (int i = 0; i < 30; i++)
                    {
                        double s = 10000 + i * 4000;
                        string en = "Reference sentence number " + i + ".", sv = "Svensk mening nummer " + i + ".";
                        if (i == 10) { en = "He worked for a man who abducted a friend."; sv = "- Hans chef kidnappade en vän.\n- Han är polis."; }
                        if (i == 11) { en = "-He's a cop.\n-Who's his rabbi?"; sv = "- Vem är hans rabbin?\n- Jag vet inte, men jag vet vem."; }
                        if (i == 12) { en = "I don't know, but I know who might."; sv = null; }
                        uref.Add(new Cue(s, s + 2500, en));
                        if (sv != null) utgt.Add(new Cue(i == 20 ? s - 600 : s, s + (i == 11 ? 6500 : 2500), sv) { OrigStart = 1000 + i });
                    }
                    var uR = Meaning.Units(uref); var uT = Meaning.Units(utgt);
                    Check("Betydelse: en rad med två replikstreck blir två repliker, och en rad utan streck blir en",
                        uT.Count == utgt.Count + 2 && uR.Count == uref.Count + 1 && uT.First(x => x.Row == 10 && x.Part == 1).Text == "Han är polis." && uR.First(x => x.Row == 11).Parts == 2);
                    // varje replik i texten får en vektor nära sin replik i referensen
                    var pairOf = new Dictionary<string, string> {
                        { "Hans chef kidnappade en vän.", "He worked for a man who abducted a friend." }, { "Han är polis.", "He's a cop." },
                        { "Vem är hans rabbin?", "Who's his rabbi?" }, { "Jag vet inte, men jag vet vem.", "I don't know, but I know who might." } };
                    for (int i = 0; i < 30; i++) pairOf["Svensk mening nummer " + i + "."] = "Reference sentence number " + i + ".";
                    var uRv = uR.Select(x => vec(x.Text)).ToArray();
                    var uTv = uT.Select(x => near(vec(pairOf[x.Text]), 0.35)).ToArray();
                    var urep = new MeaningReport();
                    for (int i = 0; i < utgt.Count; i++) urep.Pairs.Add(new MeaningPair { Line = i, Ref = i < 12 ? i : i + 1, Size = 1, Z = 10 });   // parningen rad för rad
                    var uOut = utgt.Select(c => c.Clone()).ToList();
                    Meaning.RefineUnits(uref, uOut, uT, uTv, uR, uRv, new float[0][][], urep);
                    Console.WriteLine("Betydelse, repliker: " + urep.Summary());
                    bool oneMove = urep.Moves.Count == 1 && urep.Moves[0].NewCues.Count == 3 && urep.Moves[0].Texts.Length == 2;
                    Check("Betydelse: repliker som står i fel rad jämfört med referensen blir ett förslag med referensens indelning och tider",
                        oneMove && urep.Moves[0].NewCues[0].Text == "Hans chef kidnappade en vän." && urep.Moves[0].NewCues[1].Text == "- Han är polis.\n- Vem är hans rabbin?" &&
                        urep.Moves[0].NewCues[2].Text == "Jag vet inte, men jag vet vem." && urep.Moves[0].NewCues[1].Start == uref[11].Start && urep.Moves[0].NewCues[2].Start == uref[12].Start &&
                        urep.Moves[0].OrigStarts[0] == 1010 && urep.Moves[0].NewCues[0].OrigStart == 1010 && double.IsNaN(urep.Moves[0].NewCues[1].OrigStart));
                    int sn = utgt.FindIndex(c => c.OrigStart == 1020);
                    Check("Betydelse: en enskild rad som ligger 0,6 s före sin referensrad läggs på referensradens starttid (slutet följer med), och inga andra rader rörs",
                        urep.Snapped == 1 && uOut[sn].Start == uref[20].Start && uOut[sn].End == utgt[sn].End + 600 &&Enumerable.Range(0, uOut.Count).All(i => i == sn || uOut[i].Start == utgt[i].Start));
                    // växlingen lägger tillbaka både start och slut
                    Meaning.MarkMoved(utgt, uOut);
                    var uBack = Meaning.Switch(uOut);
                    Check("Betydelse: växlingen lägger tillbaka både start och slut för en enskild rad", uBack[sn].Start == utgt[sn].Start && uBack[sn].End == utgt[sn].End && Meaning.MovedCount(uOut) == 1);
                }

                // frågan till och svaret från llama-server
                string reqJson = LlamaServer.BuildRequest(new List<string> { "Var är \"han\"?", "Hallå å\\ä 😀" });
                var parsed = Json.Parse(reqJson) as Dictionary<string, object>;
                var inp = parsed != null ? parsed["input"] as List<object> : null;
                Check("llama-server: frågan är giltig JSON i ren ASCII och texten kommer fram oförändrad",
                    inp != null && inp.Count == 2 && (string)inp[0] == "Var är \"han\"?" && (string)inp[1] == "Hallå å\\ä 😀" && reqJson.All(ch => ch < 127));
                var vecs = LlamaServer.ParseResponse("{\"object\":\"list\",\"data\":[{\"index\":1,\"object\":\"embedding\",\"embedding\":[0.5,-1e-1]},{\"embedding\":[1,2.5],\"index\":0}],\"model\":\"x\"}", 2);
                Check("llama-server: vektorerna läggs efter index, inte efter ordningen i svaret", vecs[0][0] == 1f && vecs[0][1] == 2.5f && vecs[1][0] == 0.5f && Math.Abs(vecs[1][1] + 0.1f) < 1e-6);
                bool threw = false;
                try { LlamaServer.ParseResponse("{\"data\":[{\"index\":0,\"embedding\":[1]}]}", 2); } catch (InvalidDataException) { threw = true; }
                Check("llama-server: ett svar med för få vektorer avvisas", threw);
                // cachen
                string embFile = Path.Combine(dir, "test.emb");
                Embedder.WriteCache(embFile, mrefV.Take(5).ToList());
                var readBack = Embedder.ReadCache(embFile, 5);
                Check("Betydelse: vektorerna sparas och läses tillbaka, och en fil med fel antal rader används inte",
                    readBack != null && readBack.Length == 5 && readBack[4].SequenceEqual(mrefV[4]) && Embedder.ReadCache(embFile, 6) == null);
                // utan verktyg: synken lämnas som den var, och rapporten säger varför
                if (!Tools.MeaningReady())
                {
                    var sr = new SyncResult { Output = off.Select(c => c.Clone()).ToList() };
                    var er = Meaning.Apply(mref, sr, (a, b) => { }, System.Threading.CancellationToken.None);
                    Check("Betydelse: utan verktyg lämnas synken som den var och felet står i rapporten", er.Error != null && sr.Meaning == er && sr.Output.Zip(off, (x, y) => x.Start == y.Start).All(x => x) && er.Summary().Contains("som vanligt"));
                }
            }

            Console.WriteLine();
            if (muxSkipped) Console.WriteLine("OBS: testet av \"Lägg in i MKV\" hoppades över, eftersom mkvmerge (MKVToolNix) inte är installerat.");
            if (failed == 0) Console.WriteLine("RESULTAT: alla {0} kontroller gick igenom.", checks);
            else Console.WriteLine("RESULTAT: {0} av {1} kontroller MISSLYCKADES. Se raderna som börjar med FEL ovan.", failed, checks);
            return failed == 0 ? 0 : 1;
        }
    }
}
