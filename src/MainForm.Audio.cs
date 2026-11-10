// Text Sync - användargränssnitt: ljudspår, grafikkort, verktyg för ljudsynk, spara igenkänd text
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
        // ------------------------------------------------------------ ljudspår
        // Fyller listan Ljudspår. Den visas bara när filmen har mer än ett ljudspår och referensen använder ljudet.
        void FillAudio(List<Audio.AudioStream> audio)
        {
            audioFilling = true;
            audioCombo.Items.Clear();
            for (int i = 0; i < audio.Count; i++)
            {
                var s = audio[i];
                audioCombo.Items.Add("Ljudspår " + (i + 1) + ": " + MkvTrack.LanguageName(s.Language.Length > 0 ? s.Language : "und") +
                    (s.Name.Length > 0 ? " – " + s.Name : "") + (s.Default ? " (standard)" : ""));
            }
            int pick = audio.Count > 0 ? Audio.PickAudio(audio) : 0;
            if (audio.Count > 0) audioCombo.SelectedIndex = pick;
            audioFilling = false;
            SetAudioIndex(pick);
            if (audio.Count > 1) Log.Info("Ljudspår: " + audio.Count + " st, förvalt " + audioCombo.SelectedItem);
        }

        // Valt ljudspår gäller alla val i referenslistan som använder ljudet
        void SetAudioIndex(int idx)
        {
            bool changed = idx != refAudioIndex;
            refAudioIndex = idx;
            foreach (var it in trackCombo.Items)
            {
                if (it is AudioRefOption) ((AudioRefOption)it).AudioIndex = idx;
                else if (it is TextCheckOption) ((TextCheckOption)it).AudioIndex = idx;
                else if (it is OrigSrtOption) ((OrigSrtOption)it).AudioIndex = idx;
            }
            if (changed && !busy) { ClearResult(); Log.Info("Ljudspår valt: " + audioCombo.SelectedItem); }
            UpdateGpuVisibility();
        }

        // ------------------------------------------------------------ grafikkort
        void UpdateGpuVisibility()
        {
            var a = trackCombo.SelectedItem as AudioRefOption;
            bool whisper = (a != null && a.Mode != AudioMode.Speech) || trackCombo.SelectedItem is TextCheckOption || trackCombo.SelectedItem is OrigSrtOption;
            if (whisper && gpuDevices == null) FillGpus();
            gpuCombo.Visible = whisper;
            audioRow.Visible = audioCombo.Items.Count > 1 && (a != null || whisper);
        }

        // NVIDIA-kort (CUDA) + processorn. Intel/AMD-grafik kan inte användas av Whisper-versionen och visas inte.
        // Sökningen görs i bakgrunden (Tools.GpuScan). Är den inte klar visas "letar…" och listan fylls när den är klar.
        void FillGpus()
        {
            var scan = Tools.GpuScan();
            if (!scan.IsCompleted)
            {
                if (gpuPending) return;
                gpuPending = true;
                gpuFilling = true;
                gpuCombo.Items.Clear(); gpuCombo.Items.Add("Grafikkort: letar…"); gpuCombo.SelectedIndex = 0; gpuCombo.Enabled = false;
                gpuFilling = false;
                scan.ContinueWith(t => { gpuPending = false; if (IsDisposed) return; gpuCombo.Enabled = !busy; FillGpus(); }, TaskScheduler.FromCurrentSynchronizationContext());
                return;
            }
            gpuFilling = true;
            gpuDevices = new List<int>();
            gpuCombo.Items.Clear();
            foreach (var g in scan.Result) { gpuDevices.Add(g.Key); gpuCombo.Items.Add("Grafikkort: " + g.Value); }
            gpuDevices.Add(-1); gpuCombo.Items.Add("Grafikkort: inget – processorn (långsamt)");
            string saved = Tools.GetSetting("gpu", "");
            int parsed; int want = saved == "cpu" ? -1 : (int.TryParse(saved, out parsed) ? parsed : gpuDevices[0]);
            int idx = gpuDevices.IndexOf(want); if (idx < 0) idx = 0;
            gpuCombo.SelectedIndex = idx;
            Audio.WhisperDevice = gpuDevices[idx];
            gpuFilling = false;
        }

        // Whisper på processorn tar lång tid: fråga först (inte om resultatet redan finns i cachen)
        bool ConfirmSlowCpu(int audioIndex, bool words)
        {
            if (Audio.WhisperDevice >= 0 || mkvPath == null || Audio.HasWhisperCache(mkvPath, audioIndex, words)) return true;
            var a = MessageBox.Show(this,
                "Taligenkänningen (Whisper) körs på processorn, eftersom inget NVIDIA-grafikkort är valt.\n\n" +
                "Det tar ungefär lika lång tid som filmen, för en film på 2 timmar alltså 1–2 timmar. Du kan avbryta när som helst, och nästa gång går det snabbt.\n\n" +
                "Vill du fortsätta?",
                Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
            Log.Info("Varning för Whisper på processorn: " + (a == DialogResult.Yes ? "fortsatte" : "avbröt"));
            return a == DialogResult.Yes;
        }

        // ------------------------------------------------------------ verktyg för ljudsynk
        // Frågar en gång och laddar ner det som saknas (kontrolleras mot fasta kontrollsummor).
        async Task<bool> EnsureTools(bool whisper, CancellationToken token)
        {
            var need = new List<ToolPackage>();
            if (!Tools.IsInstalled(Tools.Ffmpeg)) need.Add(Tools.Ffmpeg);
            // bara det som behövs för valt grafikkort (eller processorn), inte allt som skulle kunna användas
            if (whisper) need.AddRange(Tools.WhisperNeeds(Audio.WhisperDevice >= 0));
            if (need.Count == 0) return true;
            long total = need.Sum(p => p.Size);
            long free = Tools.FreeBytes(), want = Tools.SpaceNeeded(need);
            if (free >= 0 && free < want)
            {
                Log.Info("För lite diskutrymme för verktygen: " + free / 1048576 + " MB ledigt, behöver " + want / 1048576 + " MB");
                MessageBox.Show(this,
                    "Det finns för lite ledigt utrymme på disken " + Path.GetPathRoot(Path.GetFullPath(Tools.Root)).TrimEnd('\\') + " för verktygen som synk mot ljudet behöver.\n\n" +
                    "De behöver cirka " + Fmt(want / 1073741824.0, "0.0") + " GB under nedladdningen, men bara " + Fmt(free / 1073741824.0, "0.0") + " GB är ledigt.\n\n" +
                    "Frigör utrymme, till exempel genom att tömma Papperskorgen, och försök igen. Du kan också synka mot en undertext (textspår eller SRT) i stället" +
                    (whisper ? ", eller välja \"Ljud A – taldetektering\", som behöver mycket mindre." : "."),
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var a = MessageBox.Show(this,
                "Synk mot ljudet behöver några gratisverktyg som inte finns på datorn ännu:\n\n" +
                string.Join("\n", need.Select(p => "•  " + p.Name + "  (" + Fmt(p.Size / 1048576.0, "0") + " MB)")) +
                "\n\nTotalt cirka " + Fmt(total / 1048576.0, "0") + " MB. De laddas ner en gång till\n" + Tools.Root +
                "\noch kontrolleras så att de inte har ändrats.\n\nVill du ladda ner dem nu?",
                Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (a != DialogResult.Yes) return false;
            long doneBefore = 0;
            foreach (var p in need)
            {
                var pkg = p; long before = doneBefore;
                await Task.Run(() => Tools.Download(pkg, (d, t) => Progress((int)(100 * (before + d) / Math.Max(1, total)),
                    "Laddar ner " + pkg.Name + "… " + Fmt((before + d) / 1048576.0, "0") + " av " + Fmt(total / 1048576.0, "0") + " MB"), token));
                doneBefore += p.Size;
            }
            // ffprobe finns först nu: läs ljudspåren i en film som inte är MKV, så att rätt spår väljs redan första gången
            if (need.Contains(Tools.Ffmpeg) && mkvPath != null && !IsMkv(mkvPath) && audioCombo.Items.Count == 0)
            {
                string p0 = mkvPath;
                var streams = await Task.Run(() => Audio.ListAudioStreams(p0));
                if (streams.Count > 1 && mkvPath == p0) FillAudio(streams);
            }
            return true;
        }

        // Originaltexten (t.ex. engelska) efter synk mot ljudet: sparas som Film.en.srt bredvid filmen
        void SaveOrig()
        {
            string lang = Norm(GuessLang(origUsed.Path)) ?? (Regex.IsMatch(origLang, "^[a-z]{2,3}$") ? Norm(origLang) : null) ?? "en";
            string dir = Path.GetDirectoryName(mkvPath);
            using (var d = new SaveFileDialog { Title = "Spara synkad originaltext", Filter = "SRT-undertext (*.srt)|*.srt", FileName = Path.GetFileNameWithoutExtension(mkvPath) + "." + lang + ".srt", InitialDirectory = dir, OverwritePrompt = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string backup = SaveWithBackup(d.FileName, origUsed.Path, origSynced);
                    lastOutput = d.FileName;
                    SetStatus("✔ Originaltext sparad: " + Path.GetFileName(d.FileName) + "." + (backup != null ? "  Originalet finns kvar i mappen \"" + BackupFolder + "\"." : ""), true);
                    openLink.Visible = true;
                    Log.Info("Sparade synkad originaltext: " + d.FileName + (backup != null ? " (kopia: " + backup + ")" : ""));
                }
                catch (Exception ex)
                {
                    Log.Error("Kunde inte spara originaltext", ex);
                    MessageBox.Show(this, "Kunde inte spara: " + Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void SaveTranscript()
        {
            if (origSynced != null && origUsed != null) { SaveOrig(); return; }
            if (transcript == null || transcript.Count == 0) return;
            string lang = transcriptLang.Length > 0 ? transcriptLang : "und";
            string dir = Path.GetDirectoryName(mkvPath ?? tgtLoadedPath);
            string name = Path.GetFileNameWithoutExtension(mkvPath ?? tgtLoadedPath) + ".whisper." + lang + ".srt";   // eget namn: skriver aldrig över en riktig undertext
            using (var d = new SaveFileDialog { Title = "Spara igenkänd text (automatisk textning)", Filter = "SRT-undertext (*.srt)|*.srt", FileName = name, InitialDirectory = dir, OverwritePrompt = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // utan "(tal)"-rader; bara det Whisper faktiskt hörde
                    SaveWithBackup(d.FileName, tgtLoadedPath, transcript);
                    lastOutput = d.FileName;
                    SetStatus("✔ Igenkänd text sparad: " + Path.GetFileName(d.FileName) + " (automatisk – kan innehålla fel).", true);
                    openLink.Visible = true;
                    Log.Info("Sparade Whisper-text: " + d.FileName);
                }
                catch (Exception ex)
                {
                    Log.Error("Kunde inte spara igenkänd text", ex);
                    MessageBox.Show(this, "Kunde inte spara: " + Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
