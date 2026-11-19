// Text Sync - packar upp .tar.bz2 med egen kod, utan hjälp av Windows.
// tar.exe i Windows 10 och i tidiga Windows 11 kan inte bzip2 (felet "unable to run program bzip2 -d"), och .NET Framework kan det inte heller.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TextSync
{
    // Läser en bzip2-ström (bara uppackning). Kontrollsumman för varje block och för hela strömmen kontrolleras,
    // och trasiga eller avkortade data ger InvalidDataException.
    public sealed class BZip2Stream : Stream
    {
        const int MaxSelectors = 18002, MaxAlpha = 258, MaxCodeLen = 20;
        static readonly uint[] crcTable = MakeCrcTable();

        readonly Stream src;
        readonly byte[] inBuf = new byte[1 << 16];
        int inPos, inLen;
        ulong bitBuf; int bitCount;

        bool inStream, anyStream, finished;
        int blockMax;                 // största blockstorlek enligt huvudet (100 000–900 000)
        uint[] tt;                    // blockets byte i de låga 8 bitarna, pekaren till nästa byte i de höga
        int nblock, nUsed; uint tPos;
        uint storedBlockCrc, blockCrc, combinedCrc;
        bool blockOpen;
        int runLen, repeat; byte last;

        readonly byte[] seqToUnseq = new byte[256];
        readonly byte[] selector = new byte[MaxSelectors];
        readonly byte[] mtf = new byte[256];
        readonly int[] unzftab = new int[256];
        readonly int[] cftab = new int[257];
        readonly byte[][] len = NewTable<byte>(6, MaxAlpha);
        readonly int[][] limit = NewTable<int>(6, MaxCodeLen + 3);
        readonly int[][] bas = NewTable<int>(6, MaxCodeLen + 3);
        readonly int[][] perm = NewTable<int>(6, MaxAlpha);
        readonly int[] minLens = new int[6];

        public BZip2Stream(Stream source) { src = source; }

        static T[][] NewTable<T>(int n, int m) { var a = new T[n][]; for (int i = 0; i < n; i++) a[i] = new T[m]; return a; }

        static uint[] MakeCrcTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 24;
                for (int k = 0; k < 8; k++) c = (c & 0x80000000u) != 0 ? (c << 1) ^ 0x04C11DB7u : c << 1;
                t[i] = c;
            }
            return t;
        }

        static InvalidDataException Bad(string why) { return new InvalidDataException("Arkivet är trasigt (" + why + ")."); }

        // ------------------------------------------------------------ bitar
        bool Fill()
        {
            inLen = src.Read(inBuf, 0, inBuf.Length);
            inPos = 0;
            return inLen > 0;
        }

        int Bits(int n)   // 1–24 bitar, mest signifikanta först
        {
            while (bitCount < n)
            {
                if (inPos == inLen && !Fill()) throw Bad("det tar slut för tidigt");
                bitBuf = (bitBuf << 8) | inBuf[inPos++];
                bitCount += 8;
            }
            bitCount -= n;
            return (int)((bitBuf >> bitCount) & ((1u << n) - 1));
        }

        uint U32() { uint hi = (uint)Bits(16); return (hi << 16) | (uint)Bits(16); }

        bool AtEnd()
        {
            if (bitCount >= 8 || inPos < inLen) return false;
            return !Fill();
        }

        // ------------------------------------------------------------ block
        // Läser nästa block till tt. Returnerar false när alla strömmar i filen är slut.
        bool ReadBlock()
        {
            for (;;)
            {
                if (!inStream)
                {
                    if (anyStream && AtEnd()) return false;
                    // en fil kan bestå av flera bzip2-strömmar efter varandra; annat skräp efter den första strömmen hoppas över
                    bool ok = Bits(8) == 'B' && Bits(8) == 'Z' && Bits(8) == 'h';
                    int level = ok ? Bits(8) - '0' : 0;
                    if (!ok || level < 1 || level > 9)
                    {
                        if (anyStream) return false;
                        throw Bad("det är inte bzip2");
                    }
                    blockMax = level * 100000;
                    if (tt == null || tt.Length < blockMax) tt = new uint[blockMax];
                    combinedCrc = 0; inStream = true; anyStream = true;
                }
                int m1 = Bits(24), m2 = Bits(24);
                if (m1 == 0x177245 && m2 == 0x385090)
                {
                    if (U32() != combinedCrc) throw Bad("kontrollsumman för hela arkivet stämmer inte");
                    bitCount -= bitCount & 7;   // strömmen slutar på en hel byte
                    inStream = false;
                    continue;
                }
                if (m1 != 0x314159 || m2 != 0x265359) throw Bad("ett block börjar fel");
                break;
            }

            storedBlockCrc = U32();
            if (Bits(1) != 0) throw Bad("blocket är skrivet av en mycket gammal bzip2-version som inte stöds");
            int origPtr = Bits(24);

            // vilka bytevärden som förekommer
            int used16 = Bits(16), nInUse = 0;
            for (int i = 0; i < 16; i++)
            {
                if ((used16 & (0x8000 >> i)) == 0) continue;
                int b = Bits(16);
                for (int j = 0; j < 16; j++) if ((b & (0x8000 >> j)) != 0) seqToUnseq[nInUse++] = (byte)(i * 16 + j);
            }
            if (nInUse == 0) throw Bad("ett block saknar innehåll");
            int alphaSize = nInUse + 2, eob = nInUse + 1;

            // vilken kodtabell som gäller för varje grupp om 50 symboler
            int nGroups = Bits(3);
            if (nGroups < 2 || nGroups > 6) throw Bad("fel antal kodtabeller");
            int nSelectors = Bits(15);
            if (nSelectors < 1) throw Bad("kodtabellernas ordning saknas");
            var pos = new byte[6];
            for (int i = 0; i < nGroups; i++) pos[i] = (byte)i;
            for (int i = 0; i < nSelectors; i++)
            {
                int j = 0;
                while (Bits(1) == 1) { j++; if (j >= nGroups) throw Bad("kodtabellernas ordning är fel"); }
                if (i >= MaxSelectors) continue;   // fler än som kan användas: läses förbi, som bzip2 gör
                byte tmp = pos[j];
                for (; j > 0; j--) pos[j] = pos[j - 1];
                pos[0] = tmp;
                selector[i] = tmp;
            }
            if (nSelectors > MaxSelectors) nSelectors = MaxSelectors;

            // kodlängderna och tabellerna som avkodar dem
            for (int t = 0; t < nGroups; t++)
            {
                int curr = Bits(5);
                for (int i = 0; i < alphaSize; i++)
                {
                    for (;;)
                    {
                        if (curr < 1 || curr > MaxCodeLen) throw Bad("en kodlängd är fel");
                        if (Bits(1) == 0) break;
                        if (Bits(1) == 0) curr++; else curr--;
                    }
                    len[t][i] = (byte)curr;
                }
                int minLen = 32, maxLen = 0;
                for (int i = 0; i < alphaSize; i++) { if (len[t][i] > maxLen) maxLen = len[t][i]; if (len[t][i] < minLen) minLen = len[t][i]; }
                MakeDecodeTables(limit[t], bas[t], perm[t], len[t], minLen, maxLen, alphaSize);
                minLens[t] = minLen;
            }

            // symbolerna: "flytta längst fram" och löplängder av det främsta värdet
            for (int i = 0; i < 256; i++) { mtf[i] = (byte)i; unzftab[i] = 0; }
            int groupNo = -1, groupPos = 0;
            int[] gLimit = null, gBase = null, gPerm = null; int gMin = 0;
            nblock = 0;
            int run = 0, runBit = 1;   // pågående löplängd
            for (;;)
            {
                if (groupPos == 0)
                {
                    if (++groupNo >= nSelectors) throw Bad("kodtabellerna räcker inte till");
                    groupPos = 50;
                    int g = selector[groupNo];
                    gLimit = limit[g]; gBase = bas[g]; gPerm = perm[g]; gMin = minLens[g];
                }
                groupPos--;
                int zn = gMin, zvec = Bits(zn);
                while (zvec > gLimit[zn])
                {
                    if (++zn > MaxCodeLen) throw Bad("en kod är fel");
                    zvec = (zvec << 1) | Bits(1);
                }
                int idx = zvec - gBase[zn];
                if (idx < 0 || idx >= alphaSize) throw Bad("en kod är fel");
                int sym = gPerm[idx];

                if (sym <= 1)
                {
                    // RUNA (0) och RUNB (1) bygger tillsammans upp ett antal, minst signifikant först
                    if (runBit > 2 * 1024 * 1024) throw Bad("en löplängd är för lång");
                    run += sym == 0 ? runBit : 2 * runBit;
                    runBit <<= 1;
                    continue;
                }
                if (run > 0)
                {
                    byte uc = seqToUnseq[mtf[0]];
                    if (nblock + run > blockMax) throw Bad("ett block är för stort");
                    unzftab[uc] += run;
                    for (int end = nblock + run; nblock < end; nblock++) tt[nblock] = uc;
                    run = 0; runBit = 1;
                }
                if (sym == eob) break;
                {
                    if (nblock >= blockMax) throw Bad("ett block är för stort");
                    int nn = sym - 1;
                    byte tmp = mtf[nn];
                    for (; nn > 0; nn--) mtf[nn] = mtf[nn - 1];
                    mtf[0] = tmp;
                    byte uc = seqToUnseq[tmp];
                    unzftab[uc]++;
                    tt[nblock++] = uc;
                }
            }
            if (nblock == 0 || origPtr >= nblock) throw Bad("ett block pekar fel");

            // Burrows–Wheeler baklänges: bygg kedjan av pekare
            cftab[0] = 0;
            for (int i = 0; i < 256; i++) cftab[i + 1] = cftab[i] + unzftab[i];
            for (int i = 0; i < nblock; i++)
            {
                int uc = (int)(tt[i] & 0xFF);
                tt[cftab[uc]++] |= (uint)i << 8;
            }
            tPos = tt[origPtr] >> 8;
            nUsed = 0; runLen = 0; repeat = 0;
            blockCrc = 0xFFFFFFFFu;
            blockOpen = true;
            return true;
        }

        static void MakeDecodeTables(int[] limit, int[] bas, int[] perm, byte[] length, int minLen, int maxLen, int alphaSize)
        {
            int pp = 0;
            for (int i = minLen; i <= maxLen; i++)
                for (int j = 0; j < alphaSize; j++) if (length[j] == i) perm[pp++] = j;
            for (int i = 0; i < bas.Length; i++) bas[i] = 0;
            for (int i = 0; i < alphaSize; i++) bas[length[i] + 1]++;
            for (int i = 1; i < bas.Length; i++) bas[i] += bas[i - 1];
            // längder som inte finns får gränsen -1: avkodningen går då vidare och stannar med fel efter den längsta tillåtna koden
            for (int i = 0; i < limit.Length; i++) limit[i] = -1;
            int vec = 0;
            for (int i = minLen; i <= maxLen; i++)
            {
                vec += bas[i + 1] - bas[i];
                limit[i] = vec - 1;
                vec <<= 1;
            }
            for (int i = minLen + 1; i <= maxLen; i++) bas[i] = ((limit[i - 1] + 1) << 1) - bas[i];
        }

        // ------------------------------------------------------------ Stream
        public override int Read(byte[] buffer, int offset, int count)
        {
            int done = 0;
            while (done < count && !finished)
            {
                if (!blockOpen) { if (!ReadBlock()) { finished = true; break; } }
                // lokala kopior: slingan körs en gång per byte
                uint[] t = tt; uint p = tPos, crc = blockCrc;
                int used = nUsed, n = nblock, rl = runLen, rep = repeat; byte la = last;
                while (done < count)
                {
                    if (rep > 0)
                    {
                        buffer[offset + done++] = la;
                        crc = (crc << 8) ^ crcTable[(crc >> 24) ^ la];
                        rep--;
                        continue;
                    }
                    if (used == n) break;
                    p = t[p];
                    byte ch = (byte)p;
                    p >>= 8;
                    used++;
                    if (rl == 4) { rep = ch; rl = 0; continue; }   // efter fyra lika byte står hur många till som följer
                    if (rl > 0 && ch == la) rl++; else { la = ch; rl = 1; }
                    buffer[offset + done++] = ch;
                    crc = (crc << 8) ^ crcTable[(crc >> 24) ^ ch];
                }
                tPos = p; blockCrc = crc; nUsed = used; runLen = rl; repeat = rep; last = la;
                if (used == n && rep == 0)
                {
                    if (~blockCrc != storedBlockCrc) throw Bad("kontrollsumman för ett block stämmer inte");
                    combinedCrc = ((combinedCrc << 1) | (combinedCrc >> 31)) ^ storedBlockCrc;
                    blockOpen = false;
                }
            }
            return done;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    public static class TarBz2
    {
        // Packar upp ett .tar.bz2-arkiv till mappen dir. progress får 0–100 efter hur mycket av arkivet som är läst.
        public static void Extract(string archive, string dir, CancellationToken ct, Action<int> progress = null)
        {
            using (var fs = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var bz = new BZip2Stream(fs))
            {
                long total = Math.Max(1, fs.Length); int lastPct = -1;
                ExtractTar(bz, dir, ct, progress == null ? (Action)null : () =>
                {
                    int pct = (int)(fs.Position * 100 / total);
                    if (pct != lastPct) { lastPct = pct; progress(pct); }
                });
                // läs resten också (utfyllnaden efter sista filen), så att kontrollsumman för hela arkivet kontrolleras
                var rest = new byte[1 << 16];
                while (bz.Read(rest, 0, rest.Length) > 0) ct.ThrowIfCancellationRequested();
                if (progress != null && lastPct != 100) progress(100);
            }
        }

        // Packar upp en tar-ström: filer och mappar. Länkar och annat hoppas över.
        // En post som pekar utanför dir (.., enhetsbokstav eller rot) avvisas med InvalidDataException.
        public static void ExtractTar(Stream tar, string dir, CancellationToken ct, Action tick = null)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            Directory.CreateDirectory(root);
            var h = new byte[512];
            var buf = new byte[1 << 16];
            string longName = null, paxPath = null; long paxSize = -1;
            for (;;)
            {
                ct.ThrowIfCancellationRequested();
                int got = ReadFull(tar, h, 512);
                if (got == 0) break;
                if (got < 512) throw Bad("det tar slut mitt i ett huvud");
                bool zero = true;
                for (int i = 0; i < 512 && zero; i++) zero = h[i] == 0;
                if (zero) break;   // slutmarkeringen

                long sum = 0;
                for (int i = 0; i < 512; i++) sum += i >= 148 && i < 156 ? (byte)' ' : h[i];
                if (sum != Number(h, 148, 8)) throw Bad("ett huvud har fel kontrollsumma");

                long size = Number(h, 124, 12);
                char type = (char)h[156];
                string name = Text(h, 0, 100);
                if (Text(h, 257, 5) == "ustar")
                {
                    string prefix = Text(h, 345, 155);
                    if (prefix.Length > 0) name = prefix + "/" + name;
                }

                if (type == 'L' || type == 'x' || type == 'g' || type == 'K')
                {
                    // tillägg som gäller nästa post: långt namn (GNU) eller pax-uppgifter
                    if (size < 0 || size > 1 << 20) throw Bad("ett tillägg är för stort");
                    var data = new byte[(int)size];
                    if (ReadFull(tar, data, data.Length) < data.Length) throw Bad("det tar slut mitt i ett tillägg");
                    Skip(tar, Pad(size), buf);
                    if (type == 'L') longName = Encoding.UTF8.GetString(data).TrimEnd('\0');
                    else if (type == 'x') ReadPax(data, ref paxPath, ref paxSize);
                    continue;
                }
                if (paxPath != null) name = paxPath; else if (longName != null) name = longName;
                if (paxSize >= 0) size = paxSize;
                longName = null; paxPath = null; paxSize = -1;
                if (size < 0) throw Bad("en fil har fel storlek");

                bool isDir = type == '5' || name.EndsWith("/");
                bool isFile = !isDir && (type == '0' || type == '\0' || type == '7');
                string path = isDir || isFile ? SafePath(root, name) : null;
                if (path == null) { Skip(tar, size + Pad(size), buf); }
                else if (isDir) { Directory.CreateDirectory(path); Skip(tar, size + Pad(size), buf); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var o = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        long left = size;
                        while (left > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            int n = tar.Read(buf, 0, (int)Math.Min(buf.Length, left));
                            if (n <= 0) throw Bad("det tar slut mitt i en fil");
                            o.Write(buf, 0, n);
                            left -= n;
                            if (tick != null) tick();
                        }
                    }
                    Skip(tar, Pad(size), buf);
                }
                if (tick != null) tick();
            }
        }

        static InvalidDataException Bad(string why) { return new InvalidDataException("Arkivet är trasigt (" + why + ")."); }

        static long Pad(long size) { return (512 - size % 512) % 512; }

        static int ReadFull(Stream s, byte[] b, int n)
        {
            int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) break; got += r; }
            return got;
        }

        static void Skip(Stream s, long n, byte[] buf)
        {
            while (n > 0)
            {
                int r = s.Read(buf, 0, (int)Math.Min(buf.Length, n));
                if (r <= 0) throw Bad("det tar slut för tidigt");
                n -= r;
            }
        }

        static string Text(byte[] h, int off, int len)
        {
            int n = 0;
            while (n < len && h[off + n] != 0) n++;
            return Encoding.UTF8.GetString(h, off, n);
        }

        // Tal i huvudet: oktala siffror, eller binärt (första biten satt) för filer över 8 GB
        static long Number(byte[] h, int off, int len)
        {
            if ((h[off] & 0x80) != 0)
            {
                long v = h[off] & 0x7F;
                for (int i = 1; i < len; i++)
                {
                    if (v > (long.MaxValue >> 8)) throw Bad("ett tal är för stort");
                    v = (v << 8) | h[off + i];
                }
                return v;
            }
            long r = 0; bool any = false;
            for (int i = 0; i < len; i++)
            {
                byte c = h[off + i];
                if (c == 0 || c == ' ') { if (any) break; continue; }
                if (c < '0' || c > '7') throw Bad("ett tal i ett huvud är fel");
                r = r * 8 + (c - '0'); any = true;
            }
            return r;
        }

        // pax-poster: "<längd> <nyckel>=<värde>\n", där längden gäller hela posten
        static void ReadPax(byte[] d, ref string path, ref long size)
        {
            int p = 0;
            while (p < d.Length)
            {
                int sp = Array.IndexOf(d, (byte)' ', p);
                int n;
                if (sp < 0 || !int.TryParse(Encoding.ASCII.GetString(d, p, sp - p), out n) || n <= sp - p + 1 || p + n > d.Length) throw Bad("ett tillägg går inte att läsa");
                string rec = Encoding.UTF8.GetString(d, sp + 1, p + n - sp - 1).TrimEnd('\n');
                int eq = rec.IndexOf('=');
                if (eq > 0)
                {
                    string key = rec.Substring(0, eq), val = rec.Substring(eq + 1);
                    long v;
                    if (key == "path") path = val;
                    else if (key == "size" && long.TryParse(val, out v)) size = v;
                }
                p += n;
            }
        }

        // Postens fulla sökväg under root, eller null för själva roten ("./"). Kastar om posten pekar utanför root.
        static string SafePath(string root, string name)
        {
            var parts = new List<string>();
            foreach (string part in name.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == ".." || part.IndexOf(':') >= 0) throw Bad("en fil pekar utanför mappen: " + name);
                parts.Add(part);
            }
            if (name.StartsWith("/") || name.StartsWith("\\")) throw Bad("en fil pekar utanför mappen: " + name);
            if (parts.Count == 0) return null;
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, string.Join("\\", parts))); }
            catch (Exception ex)
            {
                if (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) throw Bad("ett filnamn går inte att använda i Windows: " + name);
                throw;
            }
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw Bad("en fil pekar utanför mappen: " + name);
            return full;
        }
    }
}
