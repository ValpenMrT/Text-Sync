// Text Sync - användargränssnitt: inläsning av referens och undertext
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TextSync
{
    public partial class MainForm
    {
        // ------------------------------------------------------------ inläsning
        void BrowseRef()
        {
            using (var d = new OpenFileDialog { Title = "Välj referens (MKV eller SRT)", Filter = "Film eller undertext|*.mkv;*.mks;*.mka;*.mp4;*.m4v;*.mov;*.avi;*.ts;*.m2ts;*.wmv;*.webm;*.mpg;*.srt|Alla filer (*.*)|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) LoadReference(d.FileName);
        }

        void BrowseTarget()
        {
            using (var d = new OpenFileDialog { Title = "Välj undertexten som ska synkas", Filter = "SRT-undertext (*.srt)|*.srt|Alla filer (*.*)|*.*" })
            {
                if (mkvPath != null) d.InitialDirectory = Path.GetDirectoryName(mkvPath);
                if (d.ShowDialog(this) == DialogResult.OK) LoadTarget(d.FileName);
            }
        }

        void LoadReference(string path)
        {
            if (busy) return;
            path = (path ?? "").Trim().Trim('"');
            refBox.Text = path; refLoadedPath = path; playMovie = null;
            mkvPath = null; refSrtCues = null;
            trackCombo.Items.Clear(); trackCombo.Visible = trackLabel.Visible = false;
            audioFilling = true; audioCombo.Items.Clear(); audioFilling = false; audioRow.Visible = false; refAudioIndex = 0;
            origLink.Visible = false; origLink.Text = "Lägg till undertext på filmens originalspråk (SRT)…"; refInfoDefault = "";
            ClearResult();
            if (path.Length == 0) { refInfo.Text = ""; return; }
            if (!File.Exists(path)) { refInfo.Text = "Filen finns inte."; refInfo.ForeColor = ErrColor; UpdateButtons(); return; }
            refInfo.ForeColor = Muted;
            try
            {
                bool video = IsMkv(path) || Audio.IsVideo(path);
                if (!video && LooksLikeText(path)) // MKV börjar alltid med byte 0x1A
                {
                    refSrtCues = Markup.FixAll(Srt.Load(path));
                    if (refSrtCues.Count == 0) { refSrtCues = null; throw new InvalidDataException("Hittade inga repliker – är det en SRT-fil?"); }
                    refInfo.Text = refSrtCues.Count + " repliker, " + Duration(refSrtCues);
                }
                else
                {
                    var tracks = new List<MkvTrack>();
                    if (IsMkv(path) || !Audio.IsVideo(path))
                        using (var f = new MkvFile(path)) tracks = f.Tracks; // stäng direkt så filmen inte låses
                    mkvPath = path;
                    var subs = tracks.Where(t => t.IsSubtitle).ToList();
                    var text = subs.Where(t => t.IsText).ToList();
                    foreach (var t in text) trackCombo.Items.Add(t);
                    // ljudsynk: alltid möjlig när ljud finns. Har filmen flera ljudspår väljs ett i listan Ljudspår.
                    var audio = tracks.Where(t => t.Type == 2).Select(t => new Audio.AudioStream
                    { Language = t.Language, Name = t.Name, Default = t.Default, Commentary = Audio.LooksLikeCommentary(t.Name) }).ToList();
                    foreach (var t in text.Where(x => !x.Forced))
                        trackCombo.Items.Add(new TextCheckOption { Track = t });
                    int firstAudio = trackCombo.Items.Count;
                    foreach (AudioMode m in new[] { AudioMode.Speech, AudioMode.Whisper, AudioMode.Combined })
                        trackCombo.Items.Add(new AudioRefOption { Mode = m });
                    trackCombo.Visible = trackLabel.Visible = true;
                    FillAudio(audio);
                    // andra format än MKV: ljudspåren läses med ffprobe i bakgrunden (om ffmpeg redan är nedladdat)
                    if (!IsMkv(path) && Audio.IsVideo(path) && Tools.FindFfprobe() != null)
                    {
                        string p0 = path;
                        Task.Run(() => Audio.ListAudioStreams(p0)).ContinueWith(t =>
                        {
                            if (!IsDisposed && !t.IsFaulted && t.Result.Count > 1 && refLoadedPath == p0 && mkvPath == p0 && !busy) FillAudio(t.Result);
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                    origLink.Visible = audio.Count > 0 || Audio.IsVideo(path);
                    if (text.Count == 0)
                    {
                        // ingen textundertext: synka mot ljudet. A+B om Whisper redan finns, annars A (snabbt, ingen stor nedladdning)
                        bool whisperReady = Tools.WhisperReady();
                        trackCombo.SelectedIndex = firstAudio + (whisperReady ? 2 : 0);
                        refInfo.Text = subs.Count > 0
                            ? "Bara bildbaserade undertexter (PGS/VobSub) – synkar mot filmens ljud i stället."
                            : "Ingen textundertext i filmen – synkar mot filmens ljud.";
                    }
                    else
                    {
                        // förval: engelska (eller okänt språk), inte "forced" och inte SDH/hörselskadade
                        Func<MkvTrack, bool> plain = t => !t.Forced && !Regex.IsMatch(t.Name, @"\b(SDH|CC|HI|forced)\b", RegexOptions.IgnoreCase);
                        int pick = text.FindIndex(t => plain(t) && t.Language.StartsWith("en"));
                        if (pick < 0) pick = text.FindIndex(t => plain(t) && t.Language == "und");
                        if (pick < 0) pick = text.FindIndex(t => t.Language.StartsWith("en") && !t.Forced);
                        if (pick < 0) pick = text.FindIndex(t => !t.Forced);
                        trackCombo.SelectedIndex = Math.Max(0, pick);
                        refInfo.Text = text.Count + " textspår hittades – eller välj synk mot ljudet i listan";
                    }
                    refInfoDefault = refInfo.Text;
                }
            }
            catch (Exception ex)
            {
                refInfoDefault = "";
                Log.Error("Kunde inte läsa referens " + path, ex);
                refInfo.ForeColor = ErrColor;
                refInfo.Text = "Kunde inte läsa filen: " + Friendly(ex);
                mkvPath = null; refSrtCues = null;
            }
            UpdateButtons();
        }

        void BrowseOrig()
        {
            if (busy || mkvPath == null) return;
            using (var d = new OpenFileDialog { Title = "Välj undertexten på filmens originalspråk (t.ex. engelska)", Filter = "SRT-undertext (*.srt)|*.srt|Alla filer (*.*)|*.*", InitialDirectory = Path.GetDirectoryName(mkvPath) })
                if (d.ShowDialog(this) == DialogResult.OK) AddOrig(d.FileName);
        }

        // Lägger in "Separat SRT + Whisper-kontroll" i referenslistan och väljer det
        void AddOrig(string path)
        {
            if (busy || mkvPath == null) return;
            path = (path ?? "").Trim().Trim('"');
            List<Cue> cues;
            try
            {
                if (IsMkv(path) || Audio.IsVideo(path) || !LooksLikeText(path)) throw new InvalidDataException("Det måste vara en undertext (SRT).");
                if (SamePath(path, tgtLoadedPath)) throw new InvalidDataException("Det är samma fil som undertexten som ska synkas i fält 2. Välj undertexten på filmens originalspråk (t.ex. engelska).");
                cues = Markup.FixAll(Srt.Load(path));   // originaltexten kan också sparas, så den rättas på samma sätt
                if (cues.Count < 5) throw new InvalidDataException("Hittade nästan inga repliker (" + cues.Count + ") – är det en SRT-fil?");
            }
            catch (Exception ex)
            {
                Log.Error("Kunde inte läsa originaltext " + path, ex);
                refInfo.ForeColor = ErrColor; refInfo.Text = "Undertexten på originalspråket kunde inte läsas: " + Friendly(ex);
                return;
            }
            for (int i = trackCombo.Items.Count - 1; i >= 0; i--)
                if (trackCombo.Items[i] is OrigSrtOption) trackCombo.Items.RemoveAt(i);
            int at = 0;
            while (at < trackCombo.Items.Count && !(trackCombo.Items[at] is AudioRefOption)) at++;
            var opt = new OrigSrtOption { Path = path, Cues = cues, AudioIndex = refAudioIndex };
            trackCombo.Items.Insert(at, opt);
            refInfo.ForeColor = Muted;
            trackCombo.SelectedIndex = at;   // info-raden uppdateras av SelectedIndexChanged
            origLink.Text = "Byt undertext på originalspråket…";
            Log.Info("Originaltext vald: " + path);
            UpdateButtons();
        }

        static bool LooksLikeText(string path)
        {
            try { using (var f = File.OpenRead(path)) { int b = f.ReadByte(); return b != 0x1A; } } catch { return false; }
        }

        void LoadTarget(string path)
        {
            if (busy) return;
            path = (path ?? "").Trim().Trim('"');
            if (!SamePath(path, tgtLoadedPath) && encCombo.SelectedIndex != 0) { encReset = true; encCombo.SelectedIndex = 0; encReset = false; } // ny fil → automatisk igen
            tgtBox.Text = path; tgtLoadedPath = path; playMovie = null;
            tgtCues = null; ClearResult();
            if (path.Length == 0) { tgtInfo.Text = ""; return; }
            try
            {
                if (IsMkv(path) || Audio.IsVideo(path)) throw new InvalidDataException("Här ska undertexten (SRT) som ska synkas ligga. Lägg filmen i fält 1.");
                // kodade tecken (&amp;) och trasiga taggar rättas alltid, redan vid inläsningen
                int fixedEnt, fixedTags, fixedGone;
                tgtCues = Markup.FixAll(Srt.Load(path, (int)Srt.Encodings[encCombo.SelectedIndex][0]), out fixedEnt, out fixedTags, out fixedGone);
                string fixedInfo = Markup.Summary(fixedEnt, fixedTags, fixedGone);
                if (fixedInfo.Length > 0) Log.Info(fixedInfo + " (" + path + ")");
                tgtInfo.ForeColor = Muted;
                tgtInfo.Text = tgtCues.Count + " repliker, " + Duration(tgtCues) + (fixedInfo.Length > 0 ? ".  " + fixedInfo : "");
                if (tgtCues.Count == 0) { tgtInfo.ForeColor = ErrColor; tgtInfo.Text = "Hittade inga repliker – är det en SRT-fil?"; tgtCues = null; }
                string guess = GuessLang(path);
                if (guess != null) langBox.Text = guess;
            }
            catch (Exception ex)
            {
                Log.Error("Kunde inte läsa mål " + path, ex);
                tgtInfo.ForeColor = ErrColor; tgtInfo.Text = "Kunde inte läsa filen: " + Friendly(ex);
            }
            UpdateButtons();
        }

        static string GuessLang(string path)
        {
            var m = Regex.Match(Path.GetFileNameWithoutExtension(path), @"[._ -]([a-z]{2,3})(?:[._ -](?:forced|sdh|cc|hi))?$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string c = m.Groups[1].Value.ToLowerInvariant();
            string[] known = { "sv", "swe", "en", "eng", "no", "nor", "nob", "da", "dan", "fi", "fin", "de", "ger", "deu", "fr", "fre", "fra", "es", "spa", "it", "ita", "nl", "dut", "nld", "pl", "pol" };
            return known.Contains(c) ? c : null;
        }

        static string Duration(List<Cue> c)
        {
            if (c.Count == 0) return "";
            return "slutar " + Srt.FormatTime(c[c.Count - 1].End).Substring(0, 8);
        }

        void ClearResult()
        {
            result = null; synced = null; refCuesUsed = null; unsaved = false;
            if (list != null) list.Items.Clear();
            rowCue = null; cueRow = null; rowRef = null; rowProp = null; refOnlyTotal = 0;
            if (searchInfo != null) searchInfo.Text = "";
            proposals = new List<AlignProposal>(); cueProp = null; rejected.Clear();
            if (viewInfo != null) viewInfo.Text = "";
            if (acceptAllLink != null) acceptAllLink.Visible = false;
            if (resBox != null) resBox.Visible = false;
            StopPlay();
            quality = null; weakStops = new List<int>(); cueWeak = null; hiInfo = null; hiUndoDepth = 0;
            if (openLink != null) openLink.Visible = false;
            undo.Clear(); rowMatched = null; transcript = null; origSynced = null; origUsed = null; if (saveTextBtn != null) saveTextBtn.Visible = false;
            if (undoLink != null) undoLink.Visible = false;
            if (timeline != null) { timeline.Ref = null; timeline.Orig = null; timeline.Synced = null; timeline.Highlight = null; timeline.Invalidate(); }
            UpdateButtons();
        }
    }
}
