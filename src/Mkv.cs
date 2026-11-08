// Text Sync - minimal Matroska (MKV) läsare för textundertexter
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextSync
{
    public class MkvTrack
    {
        public ulong Number;
        public int Type;
        public string Codec = "";
        public string Language = "und";
        public string Name = "";
        public bool Forced;
        public int CompAlgo = -1;
        public byte[] CompSettings;

        public bool IsSubtitle { get { return Type == 0x11; } }
        public bool IsText
        {
            get
            {
                return Codec == "S_TEXT/UTF8" || Codec == "S_TEXT/ASCII" || Codec == "S_TEXT/ASS"
                    || Codec == "S_TEXT/SSA" || Codec == "S_TEXT/WEBVTT" || Codec == "S_ASS" || Codec == "S_SSA";
            }
        }
        public bool IsAss { get { return Codec.Contains("ASS") || Codec.Contains("SSA"); } }

        public override string ToString()
        {
            string fmt = Codec.Replace("S_TEXT/", "").Replace("UTF8", "SRT");
            string s = "Spår " + Number + ": " + Language;
            if (Name.Length > 0) s += " – " + Name;
            s += " (" + fmt + ")";
            if (Forced) s += " [tvingad]";
            return s;
        }
    }

    public class MkvFile : IDisposable
    {
        const uint ID_EBML = 0x1A45DFA3, ID_SEGMENT = 0x18538067, ID_INFO = 0x1549A966,
            ID_TCSCALE = 0x2AD7B1, ID_TRACKS = 0x1654AE6B, ID_TRACKENTRY = 0xAE,
            ID_TRACKNUM = 0xD7, ID_TRACKTYPE = 0x83, ID_CODEC = 0x86, ID_LANG = 0x22B59C,
            ID_LANGBCP = 0x22B59D, ID_NAME = 0x536E, ID_FORCED = 0x55AA,
            ID_ENCODINGS = 0x6D80, ID_ENCODING = 0x6240, ID_COMPRESSION = 0x5034,
            ID_COMPALGO = 0x4254, ID_COMPSET = 0x4255,
            ID_CLUSTER = 0x1F43B675, ID_TIMECODE = 0xE7, ID_SIMPLEBLOCK = 0xA3,
            ID_BLOCKGROUP = 0xA0, ID_BLOCK = 0xA1, ID_BLOCKDUR = 0x9B;

        static readonly HashSet<uint> TopLevel = new HashSet<uint> {
            0x1F43B675, 0x1C53BB6B, 0x1254C367, 0x1043A770, 0x1941A469, 0x114D9B74, 0x1549A966, 0x1654AE6B };

        FileStream fs;
        long length;
        long segEnd;
        long firstCluster = -1;
        public double TimecodeScale = 1000000; // ns per enhet
        public List<MkvTrack> Tracks = new List<MkvTrack>();
        public bool Truncated; // filen tog slut/var trasig innan allt kunde läsas
        public string Path;

        public MkvFile(string path)
        {
            Path = path;
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
            try
            {
                length = fs.Length;
                ReadHeader();
            }
            catch (EndOfStreamException)
            {
                fs.Dispose();
                throw new InvalidDataException("MKV-filen är ofullständig eller trasig.");
            }
            catch
            {
                fs.Dispose();
                throw;
            }
        }

        public void Dispose() { if (fs != null) fs.Dispose(); fs = null; }

        // ---------- EBML-primitiver
        int RB() { int b = fs.ReadByte(); if (b < 0) throw new EndOfStreamException(); return b; }

        uint ReadId()
        {
            int b = RB(); int len;
            if ((b & 0x80) != 0) len = 1; else if ((b & 0x40) != 0) len = 2;
            else if ((b & 0x20) != 0) len = 3; else if ((b & 0x10) != 0) len = 4;
            else throw new InvalidDataException("Ogiltigt EBML-ID");
            uint v = (uint)b;
            for (int i = 1; i < len; i++) v = (v << 8) + (uint)RB();
            return v;
        }

        // returnerar -1 för okänd storlek
        long ReadSize()
        {
            int b = RB(); int len = 1; int mask = 0x80;
            while (len <= 8 && (b & mask) == 0) { len++; mask >>= 1; }
            if (len > 8) throw new InvalidDataException("Ogiltig EBML-storlek");
            long v = b & (mask - 1); bool allOnes = v == mask - 1;
            for (int i = 1; i < len; i++) { int x = RB(); v = (v << 8) + x; if (x != 0xFF) allOnes = false; }
            return allOnes ? -1 : v;
        }

        byte[] ReadBytes(long n)
        {
            if (n < 0 || n > 64 * 1024 * 1024) throw new InvalidDataException("Orimlig elementstorlek");
            var buf = new byte[n]; int off = 0;
            while (off < n) { int r = fs.Read(buf, off, (int)n - off); if (r <= 0) throw new EndOfStreamException(); off += r; }
            return buf;
        }

        ulong ReadUInt(long n) { ulong v = 0; for (int i = 0; i < n; i++) v = (v << 8) + (ulong)RB(); return v; }

        string ReadString(long n) { return Encoding.UTF8.GetString(ReadBytes(n)).TrimEnd('\0'); }

        static int VintLen(byte b) { int len = 1, mask = 0x80; while (len <= 8 && (b & mask) == 0) { len++; mask >>= 1; } return len; }

        // ---------- huvud + spår
        void ReadHeader()
        {
            if (ReadId() != ID_EBML) throw new InvalidDataException("Filen är inte en MKV/Matroska-fil.");
            long sz = ReadSize(); fs.Position += sz;
            while (true)
            {
                uint id = ReadId(); long s = ReadSize();
                if (id == ID_SEGMENT) { segEnd = s < 0 ? length : Math.Min(length, fs.Position + s); break; }
                if (s < 0) throw new InvalidDataException("Hittar inget Segment");
                fs.Position += s; // Void m.m.
            }
            while (fs.Position < segEnd)
            {
                long elStart = fs.Position;
                uint id; long s;
                try { id = ReadId(); s = ReadSize(); } catch (EndOfStreamException) { break; }
                long dataStart = fs.Position;
                if (id == ID_CLUSTER)
                {
                    if (firstCluster < 0) firstCluster = elStart;
                    if (Tracks.Count > 0) break;
                    if (s < 0) break;
                    fs.Position = dataStart + s; continue;
                }
                if (s < 0) break;
                long end = dataStart + s;
                if (id == ID_INFO) ParseInfo(end);
                else if (id == ID_TRACKS) ParseTracks(end);
                fs.Position = end;
            }
        }

        void ParseInfo(long end)
        {
            while (fs.Position < end)
            {
                uint id = ReadId(); long s = ReadSize(); long p = fs.Position;
                if (id == ID_TCSCALE) TimecodeScale = ReadUInt(s);
                fs.Position = p + s;
            }
        }

        void ParseTracks(long end)
        {
            while (fs.Position < end)
            {
                uint id = ReadId(); long s = ReadSize(); long p = fs.Position;
                if (id == ID_TRACKENTRY) Tracks.Add(ParseTrack(p + s));
                fs.Position = p + s;
            }
        }

        MkvTrack ParseTrack(long end)
        {
            var t = new MkvTrack(); string bcp = null;
            while (fs.Position < end)
            {
                uint id = ReadId(); long s = ReadSize(); long p = fs.Position;
                switch (id)
                {
                    case ID_TRACKNUM: t.Number = ReadUInt(s); break;
                    case ID_TRACKTYPE: t.Type = (int)ReadUInt(s); break;
                    case ID_CODEC: t.Codec = ReadString(s); break;
                    case ID_LANG: t.Language = ReadString(s); break;
                    case ID_LANGBCP: bcp = ReadString(s); break;
                    case ID_NAME: t.Name = ReadString(s); break;
                    case ID_FORCED: t.Forced = ReadUInt(s) != 0; break;
                    case ID_ENCODINGS: ParseEncodings(t, p + s); break;
                }
                fs.Position = p + s;
            }
            if (!string.IsNullOrEmpty(bcp)) t.Language = bcp;
            return t;
        }

        void ParseEncodings(MkvTrack t, long end)
        {
            while (fs.Position < end)
            {
                uint id = ReadId(); long s = ReadSize(); long p = fs.Position;
                if (id == ID_ENCODING || id == ID_COMPRESSION)
                {
                    if (id == ID_COMPRESSION && t.CompAlgo < 0) t.CompAlgo = 0; // standard = zlib
                    ParseEncodings(t, p + s);
                }
                else if (id == ID_COMPALGO) t.CompAlgo = (int)ReadUInt(s);
                else if (id == ID_COMPSET) t.CompSettings = ReadBytes(s);
                fs.Position = p + s;
            }
        }

        // ---------- läs undertexter
        public List<Cue> ReadSubtitles(MkvTrack track, Action<int> progress, Func<bool> cancel)
        {
            var cues = new List<Cue>();
            if (firstCluster < 0) return cues;
            double toMs = TimecodeScale / 1000000.0;
            fs.Position = firstCluster;
            long nextReport = 0;
            ulong clusterTc = 0;
            while (fs.Position < segEnd)
            {
                if (fs.Position >= nextReport)
                {
                    progress((int)(100.0 * fs.Position / length));
                    nextReport = fs.Position + 8 * 1024 * 1024;
                    if (cancel != null && cancel()) throw new OperationCanceledException();
                }
                uint id; long s;
                try { id = ReadId(); s = ReadSize(); }
                catch (EndOfStreamException) { Truncated = true; break; }
                catch (InvalidDataException) { Truncated = true; break; }
                long p = fs.Position;
                if (id == ID_CLUSTER)
                {
                    long end = s < 0 ? segEnd : p + s;
                    // läs klustrets barn
                    while (fs.Position < end)
                    {
                        long cs = fs.Position;
                        uint cid; long csz;
                        try { cid = ReadId(); csz = ReadSize(); }
                        catch (EndOfStreamException) { Truncated = true; end = 0; break; }
                        catch (InvalidDataException) { Truncated = true; end = 0; break; }
                        if (s < 0 && TopLevel.Contains(cid)) { fs.Position = cs; break; }
                        long cp = fs.Position;
                        if (csz < 0) { end = 0; break; }
                        if (cp + csz > length) { Truncated = true; end = 0; break; }
                        try
                        {
                            if (cid == ID_TIMECODE) clusterTc = ReadUInt(csz);
                            else if (cid == ID_SIMPLEBLOCK) ReadBlock(track, cp, csz, clusterTc, -1, toMs, cues);
                            else if (cid == ID_BLOCKGROUP) ReadGroup(track, cp + csz, clusterTc, toMs, cues);
                        }
                        catch (EndOfStreamException) { Truncated = true; end = 0; break; }
                        catch (InvalidDataException) { Truncated = true; end = 0; break; }
                        fs.Position = cp + csz;
                    }
                    if (end == 0) break;
                    if (s >= 0) fs.Position = p + s;
                    continue;
                }
                if (s < 0) break;
                fs.Position = p + s;
            }
            progress(100);

            // saknad längd → fram till nästa replik (max 5 s)
            cues.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = 0; i < cues.Count; i++)
                if (cues[i].End <= cues[i].Start)
                {
                    double next = i + 1 < cues.Count ? cues[i + 1].Start : cues[i].Start + 3000;
                    cues[i].End = Math.Min(cues[i].Start + 5000, Math.Max(cues[i].Start + 500, next));
                }
            return cues.Where(c => c.Text.Trim().Length > 0).ToList();
        }

        void ReadGroup(MkvTrack track, long end, ulong clusterTc, double toMs, List<Cue> cues)
        {
            long blockPos = -1, blockSize = 0; long dur = -1;
            while (fs.Position < end)
            {
                uint id = ReadId(); long s = ReadSize(); long p = fs.Position;
                if (id == ID_BLOCK) { blockPos = p; blockSize = s; }
                else if (id == ID_BLOCKDUR) dur = (long)ReadUInt(s);
                fs.Position = p + s;
            }
            if (blockPos >= 0) ReadBlock(track, blockPos, blockSize, clusterTc, dur, toMs, cues);
        }

        void ReadBlock(MkvTrack track, long pos, long size, ulong clusterTc, long dur, double toMs, List<Cue> cues)
        {
            fs.Position = pos;
            int b = RB(); int len = VintLen((byte)b);
            ulong tn = (ulong)(b & ((0x80 >> (len - 1)) - 1));
            for (int i = 1; i < len; i++) tn = (tn << 8) + (ulong)RB();
            if (tn != track.Number) return;
            short rel = (short)((RB() << 8) | RB());
            RB(); // flaggor
            long hdr = len + 3;
            if (size - hdr <= 0) return;
            byte[] data = ReadBytes(size - hdr);
            data = Decode(track, data);
            string txt = Encoding.UTF8.GetString(data).TrimEnd('\0');
            if (track.IsAss) txt = AssToText(txt);
            double start = ((double)clusterTc + rel) * toMs;
            double end = dur >= 0 ? start + dur * toMs : start;
            cues.Add(new Cue(start, end, txt.Trim()));
        }

        static byte[] Decode(MkvTrack t, byte[] d)
        {
            if (t.CompAlgo == 3 && t.CompSettings != null)
            {
                var r = new byte[t.CompSettings.Length + d.Length];
                Buffer.BlockCopy(t.CompSettings, 0, r, 0, t.CompSettings.Length);
                Buffer.BlockCopy(d, 0, r, t.CompSettings.Length, d.Length);
                return r;
            }
            if (t.CompAlgo == 0 && d.Length > 2)
            {
                try
                {
                    using (var ms = new MemoryStream(d, 2, d.Length - 2))
                    using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var o = new MemoryStream()) { ds.CopyTo(o); return o.ToArray(); }
                }
                catch { return d; }
            }
            return d;
        }

        static readonly Regex AssTags = new Regex(@"\{[^}]*\}");
        public static string AssToText(string s)
        {
            // ReadOrder, Layer, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            var parts = s.Split(new[] { ',' }, 9);
            string t = parts.Length == 9 ? parts[8] : s;
            t = AssTags.Replace(t, "");
            return t.Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ");
        }
    }
}
