// Text Sync - användargränssnitt: synk, resultat och förhandsvisning
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
        // ------------------------------------------------------------ synk
        async Task DoSync()
        {
            if (busy || tgtCues == null) return;
            // varna före synken om filnamnen pekar på olika avsnitt, eller om texterna är mycket olika långa
            var warn = new List<string>();
            warn.Add(EpisodeId.Mismatch(mkvPath ?? refLoadedPath, tgtLoadedPath, "Texten", mkvPath != null ? "filmen" : "referensen"));
            var oo0 = mkvPath != null && refSrtCues == null ? trackCombo.SelectedItem as OrigSrtOption : null;
            if (oo0 != null) warn.Add(EpisodeId.Mismatch(mkvPath, oo0.Path, "Undertexten på originalspråket", "filmen"));
            if (refSrtCues != null) warn.Add(EpisodeId.LengthMismatch(refSrtCues, tgtCues, "referensen"));
            bool warned = warn.Any(x => x != null);
            if (!ConfirmWrongFile(warn)) return;
            var opt = new SyncOptions { Mode = (SyncMode)modeCombo.SelectedIndex, DetectFramerate = fpsCheck.Checked };
            var tgt = tgtCues;
            hiInfo = null;
            if (hiCheck.Checked)
            {
                // tas bort före synken: ljudbeskrivningar motsvarar inte tal och kan störa synken mot ljudet
                int ch, rm;
                tgt = Sdh.Apply(tgtCues, out ch, out rm);
                if (tgt.Count < 5)
                {
                    MessageBox.Show(this, "Nästan allt i texten ser ut som text för hörselskadade (" + (tgtCues.Count - tgt.Count) + " av " + tgtCues.Count + " rader skulle tas bort).\n\n" +
                        "Kryssa ur \"Ta bort text för hörselskadade\" i Inställningar och synka igen.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // rensningen före synken går inte att ångra med Ctrl+Z: säg hur man får tillbaka texten
                hiInfo = ch + rm == 0 ? Sdh.Summary(0, 0) : Sdh.Summary(ch, rm) + " Kryssa ur rutan i Inställningar och synka igen för att få tillbaka den.";
                hiUndoDepth = 0;
                Log.Info("Text för hörselskadade: " + ch + " rader ändrade, " + rm + " borttagna av " + tgtCues.Count);
            }
            cts = new CancellationTokenSource();
            var token = cts.Token;
            SetBusy(true, "Förbereder…");
            string finalStatus = "";
            try
            {
                List<Cue> refc = refSrtCues;
                bool truncated = false;
                string audioInfo = null;
                transcript = null; origSynced = null; origUsed = null; saveTextBtn.Visible = false;
                // Whisper behöver veta vilket grafikkort som gäller: vänta på sökningen om den inte är klar (oftast är den det)
                if (gpuCombo.Visible && gpuDevices == null)
                {
                    statusLabel.Text = "Letar efter grafikkort…";
                    await Tools.GpuScan();
                    FillGpus();
                }
                var aopt = refc == null ? trackCombo.SelectedItem as AudioRefOption : null;
                var oopt = refc == null ? trackCombo.SelectedItem as OrigSrtOption : null;
                if (oopt != null)
                {
                    // 1) originaltexten (t.ex. engelska) synkas mot ljudet och kontrolleras ord för ord, 2) målet synkas mot den
                    if (!ConfirmSlowCpu(oopt.AudioIndex, true) || !await EnsureTools(true, token)) { finalStatus = "Avbrutet."; return; }
                    string path = mkvPath; var oo = oopt;
                    SyncResult coarse = null;
                    var chk = await Task.Run(() =>
                    {
                        var sp = Audio.GetReference(path, new AudioRefOption { Mode = AudioMode.Speech, AudioIndex = oo.AudioIndex },
                            (p, s) => { token.ThrowIfCancellationRequested(); Progress(p * 20 / 100, s); }, token);
                        string wl;
                        var words = Audio.GetWords(path, oo.AudioIndex, (p, s) => { token.ThrowIfCancellationRequested(); Progress(20 + p * 70 / 100, s); }, token, out wl);
                        origLang = wl ?? "";
                        Progress(90, "Synkar originaltexten mot ljudet…");
                        return TextCheck.AlignToAudio(oo.Cues, words, sp.Cues.Select(c => new Iv(c.Start, c.End)).ToList(), new SyncOptions { Mode = SyncMode.Smart, DetectFramerate = true },
                            (p, s) => { token.ThrowIfCancellationRequested(); Progress(90 + p * 8 / 100, s); }, out coarse);
                    });
                    refc = chk.Corrected;
                    origSynced = chk.Corrected; origUsed = oo;
                    audioInfo = "originaltexten synkad mot ljudet (" + coarse.Method.ToLowerInvariant() + ", " + FmtOffset(coarse.OffsetMs) + "). " + chk.Summary("originaltexten");
                    Log.Info(audioInfo);
                }
                else if (aopt != null)
                {
                    if ((aopt.Mode != AudioMode.Speech && !ConfirmSlowCpu(aopt.AudioIndex, false)) || !await EnsureTools(aopt.Mode != AudioMode.Speech, token)) { finalStatus = "Avbrutet."; return; }
                    string path = mkvPath;
                    var ar = await Task.Run(() => Audio.GetReference(path, aopt, (p, s) => { token.ThrowIfCancellationRequested(); Progress(p, s); }, token));
                    refc = ar.Cues; audioInfo = ar.Info;
                    transcript = ar.Transcript; transcriptLang = ar.Language;
                    if (refc.Count < 5) throw new InvalidDataException("Det gick nästan inte att hitta något tal i ljudet. Prova en annan metod i listan Referens, eller en undertext (SRT) som referens.");
                }
                else if (refc == null)
                {
                    var tco = trackCombo.SelectedItem as TextCheckOption;
                    var track = tco != null ? tco.Track : (MkvTrack)trackCombo.SelectedItem;
                    string key = mkvPath + "|" + track.Number + "|" + File.GetLastWriteTimeUtc(mkvPath).Ticks;
                    if (!trackCache.TryGetValue(key, out refc))
                    {
                        string path = mkvPath;
                        var res = await Task.Run(() =>
                        {
                            using (var f = new MkvFile(path))
                            {
                                var t = f.Tracks.First(x => x.Number == track.Number);
                                var cues = f.ReadSubtitles(t, p => Progress(p, "Läser undertextspår ur MKV… " + p + " %"), () => token.IsCancellationRequested);
                                return Tuple.Create(cues, f.Truncated);
                            }
                        });
                        refc = Markup.FixAll(res.Item1); truncated = res.Item2;   // WebVTT-spår i MKV har ofta &amp;
                        if (!truncated) trackCache[key] = refc;
                    }
                    if (refc.Count < 5) throw new InvalidDataException("Spåret innehåller nästan inga repliker (" + refc.Count + "). Välj ett annat spår.");
                    // inte om filmen är ofullständig (då är det filmen som är kort), och inte om man redan har valt "Synka ändå"
                    if (!truncated && !warned && !ConfirmWrongFile(new List<string> { EpisodeId.LengthMismatch(refc, tgt, "filmens text") })) { finalStatus = "Avbrutet."; return; }
                    if (tco != null)
                    {
                        // Whisper lyssnar ord för ord och rättar de delar av textspåret som inte går i takt med ljudet
                        if (!ConfirmSlowCpu(tco.AudioIndex, true) || !await EnsureTools(true, token)) { finalStatus = "Avbrutet."; return; }
                        string path2 = mkvPath; var refIn = refc; int aIdx2 = tco.AudioIndex;
                        var chk = await Task.Run(() =>
                        {
                            string wl;
                            var words = Audio.GetWords(path2, aIdx2, (p, s) => { token.ThrowIfCancellationRequested(); Progress(p, s); }, token, out wl);
                            Progress(97, "Jämför textspåret med ljudet…");
                            return TextCheck.Check(refIn, words);
                        });
                        refc = chk.Corrected;
                        audioInfo = chk.Summary();
                        Log.Info(audioInfo);
                    }
                }
                Action<int, string> prog = (p, s) => { token.ThrowIfCancellationRequested(); Progress(p, s); };
                var r = await Task.Run(() => SyncEngine.Sync(refc, tgt, opt, prog));
                // Betydelsejämförelsen (Inställningar): bara mot riktig text, inte mot ljud eller Whispers egen text.
                // Verktygen hämtas vid behov. Går något fel står det i resultatet, och synken är kvar som den var.
                // Avbryt under nedladdningen eller jämförelsen hoppar över jämförelsen: synken är redan klar och visas.
                if (meaningCheck.Checked && aopt == null)
                {
                    try
                    {
                        if (await EnsureMeaningTools(token))
                        {
                            var refM = refc;
                            await Task.Run(() => Meaning.Apply(refM, r, prog, token));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        r.Meaning = new MeaningReport { Canceled = true };
                        Log.Info("Betydelsejämförelsen avbröts, synken visas som den var");
                    }
                }
                if (truncated) r.Notes.Insert(0, "MKV-filen verkar vara ofullständig – bara en del av referensen kunde läsas.");
                if (audioInfo != null) r.FpsText = (r.FpsText.Length > 0 ? r.FpsText + " · " : "") + "Referens: " + audioInfo;
                saveTextBtn.Text = origSynced != null ? "Spara synkad originaltext…" : "Spara igenkänd text…";
                saveTextBtn.Visible = transcript != null || origSynced != null;
                refCuesUsed = refc;
                // raderna kan bara matchas mot riktig text: textspår, SRT eller originaltext, inte ljudet
                refIsText = mkvPath == null || !(trackCombo.SelectedItem is AudioRefOption);
                rejected.Clear();
                result = r;
                undo.Clear();
                unsaved = true;
                extraNum.Value = 0;
                timeline.Highlight = null;
                RefreshPreview();   // räknar beskedet och visar resultatet
                if (quality != null) Log.Info("Synkens kvalitet: " + quality.LogText() + (r.Meaning != null && r.Meaning.Moved > 0 ? " (efter betydelsejämförelsen)" : ""));
                Log.Info("Synk: ref=" + (mkvPath ?? refLoadedPath) + (mkvPath != null ? " " + trackCombo.SelectedItem : "") +
                    ", mål=" + tgtLoadedPath + ", metod=" + opt.Mode + " → " + r.Method + ", matchning " + Fmt(r.ScoreBefore * 100, "0") + "→" + Fmt(r.ScoreAfter * 100, "0") + " %");
            }
            catch (OperationCanceledException)
            {
                finalStatus = "Avbrutet.";
            }
            catch (Exception ex)
            {
                Log.Error("Synk misslyckades", ex);
                MessageBox.Show(this, "Synken misslyckades: " + Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                cts = null;
                SetBusy(false, finalStatus);
            }
        }

        // Varning för fel avsnitt eller fel version. Stoppar inte synken: man väljer "Synka ändå" eller "Avbryt".
        // reasons får innehålla null (= ingen anmärkning). Returnerar true om synken ska fortsätta.
        bool ConfirmWrongFile(List<string> reasons)
        {
            var r = reasons.Where(x => x != null).ToList();
            if (r.Count == 0) return true;
            string names = "\n\nReferens: " + Path.GetFileName(mkvPath ?? refLoadedPath ?? "") + "\nText: " + Path.GetFileName(tgtLoadedPath ?? "");
            bool go = ConfirmDialog.Ask(this, Caption, string.Join("\n", r) + names +
                "\n\nSynken kan bli fel om texten hör till ett annat avsnitt eller en annan version av filmen. Kontrollera att du har valt rätt filer.",
                "Synka ändå", "Avbryt");
            Log.Info("Varning för fel avsnitt: " + string.Join(" ", r) + " → " + (go ? "synkade ändå" : "avbröt"));
            return go;
        }

        // Resultatrutan. Beskedet och en mening på första raden, siffrorna på nästa, varningar under dem.
        // Allt annat (hur synken gjordes, vad färgerna betyder, hur man redigerar) står under Detaljer.
        void ShowResult()
        {
            var r = result;
            var v = quality != null ? quality.Verdict : Verdict.Check;
            bool anyRed = rowMatched != null && rowMatched.Any(m => !m);
            bool sparse = quality != null && quality.Sparse;
            bool weak = quality != null && quality.Weak.Count > 0;
            string more = null;   // fortsättningen på beskedet, under Detaljer
            if (v == Verdict.Good)
            {
                verdictLabel.Text = "✔  Bra"; verdictLabel.ForeColor = OkColor;
                summaryLabel.Text = refIsText ? "Texten går i takt med referensen." : "Raderna ligger på tal i filmens ljud. Mot ljud är beskedet mindre säkert än mot en text, så kontrollera några rader.";
            }
            else if (v == Verdict.Check)
            {
                verdictLabel.Text = weakStops.Count > 0 && !sparse ? "⚠  Kontrollera de markerade ställena" : "⚠  Kontrollera resultatet"; verdictLabel.ForeColor = Theme.Warn;
                if (sparse) summaryLabel.Text = "Referensen har för lite text för att synken ska kunna bedömas. Kontrollera några rader på olika ställen i filmen.";
                else if (weak)
                {
                    summaryLabel.Text = (refIsText ? "På några ställen börjar få rader samtidigt som referensen" : "På några ställen ligger få rader på tal i ljudet") + " (märkta ⚠ i listan). Kontrollera att texten stämmer där.";
                    more = "F4 hoppar till nästa ställe, Skift+F4 till föregående.";
                }
                else if (anyRed)
                {
                    summaryLabel.Text = "Några rader saknar " + (refIsText ? "referenstext samtidigt" : "tal i ljudet samtidigt") + " (röda i listan). Det är ofta i sin ordning, t.ex. utrop.";
                    more = "F4 hoppar till nästa röda rad, Skift+F4 till föregående.";
                }
                else summaryLabel.Text = "Synken är inte helt säker. Kontrollera några rader i början, mitten och slutet av filmen.";
            }
            else
            {
                verdictLabel.Text = "✖  Osäkert"; verdictLabel.ForeColor = ErrColor;
                summaryLabel.Text = "Texten verkar inte passa referensen. Kontrollera att du har valt rätt filer.";
                more = "Texten kan höra till ett annat avsnitt eller en annan version av filmen.";
            }
            summaryLabel.ForeColor = Theme.Fg;
            weakLinks = v != Verdict.Good && weakStops.Count > 0 && !sparse;
            prevWeakLink.Visible = nextWeakLink.Visible = weakLinks;

            // ---- siffrorna
            var ao = trackCombo.SelectedItem as AudioRefOption;
            bool speech = ao != null && ao.Mode != AudioMode.Whisper;   // referensen är tal i ljudet, inte text
            int n = rowMatched != null ? rowMatched.Length : 0, ok = 0, red = 0;
            bool good = true;
            for (int i = 0; i < n; i++)
            {
                if (rowMatched[i]) ok++;
                else if (cueProp == null || i >= cueProp.Length || cueProp[i] < 0) red++;   // en rad med förslag visas blå, inte röd
            }
            chipPct.Visible = n > 0;
            if (n > 0)
            {
                good = ok >= MatchThreshold * n;
                pctFrac = (double)ok / n; pctGood = good;
                chipPct.Text = Fmt(Math.Floor(1000.0 * ok / n) / 10, "0.#") + " % av raderna " + (speech ? "ligger på tal i ljudet" : "har referenstext samtidigt") + " (" + ok + " av " + n + ")";
                chipPct.Invalidate();
                tips.SetToolTip(chipPct, "En lyckad synk brukar ge 98–100 %." + (good ? "" : " Nu är det under 95 %: granska de röda raderna (F3 hoppar till nästa)."));
            }
            string redWhy = speech ? "Röda rader ligger inte på tal i ljudet." : "Röda rader har ingen referenstext samtidigt.";
            chipRed.Visible = red > 0;
            chipRed.Text = red == 1 ? "1 röd rad" : red + " röda rader";
            tips.SetToolTip(chipRed, redWhy + " F3 hoppar till nästa.");
            chipProp.Visible = proposals.Count > 0;
            chipProp.Text = proposals.Count + " förslag";
            chipGrey.Visible = refOnlyTotal > 0;
            chipGrey.Text = refOnlyTotal + " bara i referensen";
            int fixedRows = r.Output.Count(c => c.Raw != null);
            chipFixed.Visible = fixedRows > 0;
            chipFixed.Text = "✎ " + (fixedRows == 1 ? "1 rättad rad" : fixedRows + " rättade rader");
            bool drift = Math.Abs(r.DriftMsPerHour) >= 1;
            chipSeg.Visible = true;
            // "avsnitt" kan läsas som avsnitt av en tv-serie, därför "delar" här
            chipSeg.Text = r.Segments.Count > 1 ? "Flyttad olika mycket i " + r.Segments.Count + " delar" : "Förskjutning " + FmtOffset(r.OffsetMs) + (drift ? ", drift " + Fmt(r.DriftMsPerHour, "0") + " ms/timme" : "");
            tips.SetToolTip(chipSeg, r.Segments.Count > 1 ? "Texten har flyttats olika mycket i olika delar av filmen, t.ex. vid klipp eller reklampauser. Se Detaljer." : null);
            // text som programmet har tagit bort ska synas utan klick: hela beskedet står i tipsrutan och under Detaljer
            bool hiDone = hiInfo != null && !hiInfo.StartsWith("Ingen");
            bool plainDone = plainCheck.Checked && plainCount > 0;
            chipHi.Visible = hiDone || plainDone;
            chipHi.Text = hiDone && plainDone ? "Text för hörselskadade och formatering borttagen" : hiDone ? "Text för hörselskadade borttagen" : "Formatering borttagen på " + plainCount + (plainCount == 1 ? " rad" : " rader");
            tips.SetToolTip(chipHi, (hiDone ? hiInfo + " " : "") + (plainDone ? "Kursiv och annan formatering borttagen på " + plainCount + (plainCount == 1 ? " rad" : " rader") + ". Kryssa ur rutan i Inställningar för att få tillbaka den." : ""));
            // betydelsejämförelsen: vad den gjorde syns utan klick, hela meningen står i tipsrutan och under Detaljer
            // nMoved och plainShown följer listan som visas just nu, så att rutan stämmer efter en växling eller Ctrl+Z
            int nMoved = Meaning.MovedCount(r.Output);
            bool plainShown = Meaning.ShowsPlain(r.Output);
            string meaningText = null;
            chipMeaning.Visible = r.Meaning != null;
            if (r.Meaning != null)
            {
                string rows = nMoved == 1 ? "1 rad" : nMoved + " rader";
                string cmp = r.Meaning.Compare();
                chipMeaning.Text = nMoved == 0 ? r.Meaning.Chip() : plainShown ? "Betydelse: vanlig synk visas (" + rows + " kan flyttas)" : "Betydelse: " + rows + " flyttade" + (cmp != null ? " – " + cmp.Substring(0, cmp.IndexOf('.')).ToLowerInvariant() : "");
                meaningText = r.Meaning.Summary() + (nMoved > 0 && cmp != null ? "\n" + cmp + "\n" +
                    (plainShown ? "Nu visas den vanliga synken." : "Nu visas synken med betydelsejämförelsen.") + " Länken ovanför listan växlar mellan dem, och raderna som skiljer sig är märkta ⇄." : "");
                tips.SetToolTip(chipMeaning, meaningText);
            }
            foreach (Control c in chips.Controls) c.ForeColor = Theme.Fg;

            // ---- varningar: syns alltid
            notesLabel.Text = string.Join("\n", r.Notes.Select(x => "⚠  " + x));
            notesLabel.ForeColor = Theme.Warn;
            notesLabel.Visible = r.Notes.Count > 0;

            // ---- Detaljer, kolumn 1: så gjordes synken
            var d = new StringBuilder("Metod: " + r.Method + "\n");
            if (r.Segments.Count > 1)
            {
                d.Append("Varje avsnitt har flyttats för sig:\n");
                var parts = r.Segments.Take(8).Select(s => Srt.FormatTime(s.FromMs).Substring(0, 8) + "   " + FmtOffset(s.OffsetMs)).ToList();
                // två i bredd: första halvan till vänster, andra till höger
                int half = (parts.Count + 1) / 2;
                for (int i = 0; i < half; i++) d.Append(parts[i] + (i + half < parts.Count ? "          " + parts[i + half] : "") + "\n");
                if (r.Segments.Count > 8) d.Append("… (" + r.Segments.Count + " st)\n");
            }
            else d.Append("Förskjutning: " + FmtOffset(r.OffsetMs) + (drift ? "     Drift: " + Fmt(r.DriftMsPerHour, "0") + " ms/timme" : "") + "\n");
            if (r.FpsText.Length > 0) d.Append(r.FpsText + "\n");
            if (meaningText != null) d.Append(meaningText + "\n");
            if (more != null) d.Append(more + "\n");
            if (hiInfo != null) d.Append(hiInfo + "\n");
            if (plainCheck.Checked && plainCount > 0) d.Append("Kursiv och annan formatering borttagen på " + plainCount + (plainCount == 1 ? " rad" : " rader") + ". Kryssa ur rutan i Inställningar för att få tillbaka den.\n");
            detailBody[0].Text = d.ToString().TrimEnd();

            // ---- kolumn 2: färgerna och märkena som finns i listan just nu
            d = new StringBuilder();
            if (red > 0 || !good) d.Append("Röd – " + (speech ? "ligger inte på tal i ljudet" : "ingen referenstext samtidigt") + ". " +
                (good ? "F3 hoppar till nästa." : "Under 95 % av raderna " + (speech ? "ligger på tal" : "har referenstext samtidigt") + ", så granska de röda raderna. F3 hoppar till nästa.") + "\n");
            if (proposals.Count > 0) d.Append("Blå – förslag om att slå ihop eller dela rader, eller flytta repliker mellan rader (Flytta?), så att de följer referensens radindelning och tider. Under förslaget står hur raden blir (→ blir). Högerklicka för att godkänna eller avvisa.\n");
            if (refOnlyTotal > 0) d.Append("Grå – finns bara i referensen och sparas inte" + (viewCombo.SelectedIndex == ViewAll ? "" : viewCombo.SelectedIndex == ViewProposals ? " (de flesta dolda just nu, se Visa ovanför listan)" : " (dolda just nu, se Visa ovanför listan)") + ".\n");
            if (fixedRows > 0) d.Append("✎ – rättades vid inläsningen. Håll muspekaren över raden för att se vad som stod i filen, eller välj Bara rättade rader i Visa.\n");
            if (nMoved > 0) d.Append("⇄ – raden ligger olika i den vanliga synken och med betydelsejämförelsen. Håll muspekaren över raden för att se den andra tiden.\n");
            if (weak && !sparse) d.Append("⚠ – svagt ställe: få rader " + (refIsText ? "börjar samtidigt som referensen" : "ligger på tal i ljudet") + ".\n");
            if (d.Length == 0) d.Append("Inga rader är märkta: " + (speech ? "alla ligger på tal i ljudet." : "alla har referenstext samtidigt."));
            detailBody[1].Text = d.ToString().TrimEnd();
            for (int i = 0; i < 3; i++) { detailHead[i].ForeColor = Theme.Fg; detailBody[i].ForeColor = Theme.Fg; }
            detailsSep.BackColor = HC ? SystemColors.WindowText : Theme.BtnBorder;

            resBox.Visible = true;
            LayoutResult();
        }

        void RefreshPreview()
        {
            if (result == null) return;
            // behåll markeringen: synkad rad, annars den grå referensraden
            int selCue = -1; Cue selRef = null;
            if (list.SelectedIndices.Count > 0 && rowCue != null && list.SelectedIndices[0] < rowCue.Length)
            {
                int row = list.SelectedIndices[0];
                selCue = rowCue[row]; if (selCue < 0) selRef = rowRef[row];
            }
            synced = SyncEngine.ApplyExtra(result.Output, (double)extraNum.Value);
            // formateringen tas bort ur det som visas, spelas upp och sparas. result.Output behåller den, så att den kommer tillbaka när rutan kryssas ur.
            plainCount = 0;
            if (plainCheck.Checked) synced = Markup.StripAll(synced, out plainCount);
            var refc = refCuesUsed ?? new List<Cue>();
            int ri = 0, lost = 0;
            rowMatched = new bool[synced.Count];

            // 1) den referensrad som överlappar mest, för varje synkad rad
            var best = new Cue[synced.Count];
            var shownRef = new HashSet<Cue>();
            for (int i = 0; i < synced.Count; i++)
            {
                var c = synced[i];
                if (c.End <= 0) lost++;
                while (ri < refc.Count && refc[ri].End < c.Start) ri++;
                double bestOv = 0;
                for (int j = Math.Max(0, ri - 2); j < refc.Count && refc[j].Start < c.End; j++)
                {
                    double ov = Math.Min(c.End, refc[j].End) - Math.Max(c.Start, refc[j].Start);
                    if (ov > bestOv) { bestOv = ov; best[i] = refc[j]; }
                }
                rowMatched[i] = best[i] != null;
                if (best[i] != null) shownRef.Add(best[i]);
            }

            // besked om hur säker synken är, och var de svaga ställena finns (räknas om efter varje ändring)
            quality = Quality.Assess(refc, synced, result, refIsText);
            cueWeak = new bool[synced.Count];
            weakStops = new List<int>();
            foreach (var w in quality.Weak)
            {
                bool first = true;
                for (int i = 0; i < synced.Count; i++)
                    if (synced[i].Start >= w.S && synced[i].Start <= w.E) { cueWeak[i] = true; if (first) { weakStops.Add(i); first = false; } }
            }
            // utan svaga ställen hoppar länkarna mellan de röda raderna
            if (weakStops.Count == 0 && quality.Verdict != Verdict.Good && !quality.Sparse)
                for (int i = 0; i < synced.Count; i++) if (!rowMatched[i]) weakStops.Add(i);
            weakStops.Sort();

            // 2) referensrader som inte redan står bredvid en synkad rad får en egen grå rad i tidsordning,
            //    så att listan visar alla rader i båda texterna. "(tal)" från taldetekteringen är ingen text och visas inte.
            var extraRef = refc.Where(r => !shownRef.Contains(r) && r.Text != "(tal)").OrderBy(r => r.Start).ToList();
            refOnlyTotal = extraRef.Count;

            // 3) förslag på att slå ihop eller dela rader så att de följer referensens radindelning
            proposals = refIsText && proposeCheck.Checked ? LineAligner.Propose(synced, refc).Where(p => !rejected.Contains(p.Key)).ToList() : new List<AlignProposal>();
            // förslag från betydelsejämförelsen om att flytta repliker mellan rader. De visas även när rutan för radindelning inte är ikryssad,
            // och går före förslagen som bara bygger på tider. Raderna känns igen på tid före synk och text.
            if (refIsText && result.Meaning != null && result.Meaning.Moves.Count > 0)
            {
                double extraMs = (double)extraNum.Value;
                foreach (var mv in result.Meaning.Moves)
                {
                    int first = -1;
                    for (int i = 0; i + mv.Texts.Length <= synced.Count && first < 0; i++)
                    {
                        bool same = true;
                        for (int k = 0; k < mv.Texts.Length && same; k++)
                            same = synced[i + k].OrigStart == mv.OrigStarts[k] && (synced[i + k].Text == mv.Texts[k] || (plainCheck.Checked && synced[i + k].Text == Markup.Strip(mv.Texts[k])));
                        if (same) first = i;
                    }
                    string key = "V|" + mv.OrigStarts[0] + "|" + string.Join("|", mv.Texts);
                    if (first < 0 || rejected.Contains(key)) continue;
                    int f = first, cnt = mv.Texts.Length;
                    proposals.RemoveAll(p => p.First < f + cnt && f < p.First + p.Count);
                    proposals.Add(new AlignProposal
                    {
                        IsMove = true, First = f, Count = cnt, Refs = mv.Refs, Key = key,
                        NewCues = mv.NewCues.Select(c => new Cue(c.Start + extraMs, c.End + extraMs, c.Text) { OrigStart = c.OrigStart }).ToList()
                    });
                }
                proposals = proposals.OrderBy(p => p.First).ToList();
            }
            cueProp = new int[synced.Count];
            for (int i = 0; i < cueProp.Length; i++) cueProp[i] = -1;
            var propRefs = new HashSet<Cue>();
            for (int k = 0; k < proposals.Count; k++)
            {
                for (int t = 0; t < proposals[k].Count; t++) cueProp[proposals[k].First + t] = k;
                foreach (var r in proposals[k].Refs) propRefs.Add(r);
            }
            acceptAllLink.Text = "Godkänn alla förslag (" + proposals.Count + ")";
            acceptAllLink.Visible = proposals.Count > 0;
            int movedRows = Meaning.MovedCount(result.Output);
            meaningLink.Visible = movedRows > 0;
            meaningLink.Text = Meaning.ShowsPlain(result.Output) ? "Visa synken med betydelsejämförelsen" : "Visa den vanliga synken (utan betydelsejämförelsen)";

            int view = viewCombo.SelectedIndex;
            int total = synced.Count + extraRef.Count;
            // under varje förslag visas en eller två rader "→ blir" med texten och tiderna som raden får om förslaget godkänns
            int cap = total + proposals.Sum(p => p.NewCues.Count), previewRows = 0;
            rowCue = new int[cap]; rowRef = new Cue[cap]; rowProp = new int[cap]; cueRow = new int[synced.Count];
            for (int i = 0; i < cap; i++) rowProp[i] = -1;
            if (propFont == null || propFont.Size != list.Font.Size) propFont = new Font(list.Font, FontStyle.Italic);
            for (int i = 0; i < cueRow.Length; i++) cueRow[i] = -1;   // -1 = raden är dold av valet i Visa
            var items = new List<ListViewItem>(total);
            int si = 0, ei = 0;
            while (si < synced.Count || ei < extraRef.Count)
            {
                bool takeRef = ei < extraRef.Count && (si >= synced.Count || extraRef[ei].Start < synced[si].Start);
                int row = items.Count;
                if (takeRef)
                {
                    var r = extraRef[ei++];
                    // i "Bara förslag" visas de grå rader som ingår i ett förslag, så att man ser båda referensraderna
                    if (view != ViewAll && !(view == ViewProposals && propRefs.Contains(r))) continue;
                    var it = new ListViewItem(new[] { HC ? "▶" : "Spela referensraden", "", "", "", "", "", "",
                        Srt.FormatTime(r.Start), r.Text.Replace("\n", " / ") });
                    it.ForeColor = RefOnlyRow;
                    it.ToolTipText = "Referensrad utan egen synkad rad – sparas inte. Dubbelklicka för att lägga till en rad på samma tid.";
                    rowCue[row] = -1; rowRef[row] = r;
                    items.Add(it);
                }
                else
                {
                    int i = si++;
                    var c = synced[i]; var bc = best[i];
                    if (view == ViewOnlyRed && bc != null) continue;
                    if (view == ViewProposals && cueProp[i] < 0) continue;
                    if (view == ViewFixed && c.Raw == null) continue;
                    bool alt = !double.IsNaN(c.MeaningAlt);
                    if (view == ViewMoved && !alt) continue;
                    // förslagsrader märks med text också, inte bara med färg
                    string mark = (cueWeak[i] ? "⚠ " : "") + (alt ? "⇄ " : "") + (c.Raw != null ? "✎ " : "") + (cueProp[i] < 0 ? "" : proposals[cueProp[i]].IsMove ? "Flytta?  " : proposals[cueProp[i]].IsMerge ? "Ihop?  " : "Dela?  ");
                    var it = new ListViewItem(new[] { HC ? "▶" : "Spela rad " + (i + 1), mark + (i + 1),
                        double.IsNaN(c.OrigStart) ? "— (ny rad)" : Srt.FormatTime(c.OrigStart),
                        Srt.FormatTime(c.Start), Srt.FormatTime(c.End),
                        // flyttad efter bildfrekvensomräkning – samma tal som "Avsnitt" i resultatet
                        double.IsNaN(c.OrigStart) ? "" : Fmt((c.Start - c.OrigStart * result.Scale) / 1000, "+0.00;-0.00;0.00") + " s",
                        c.Text.Replace("\n", " / "),
                        bc != null ? Srt.FormatTime(bc.Start) : "—",
                        bc != null ? bc.Text.Replace("\n", " / ") : "— (ingen referenstext samtidigt)" });
                    if (bc == null) it.ForeColor = RedRow;
                    if (cueWeak[i]) it.ToolTipText = "Svagt ställe: få rader här " + (refIsText ? "börjar samtidigt som referensen" : "ligger på tal i ljudet") + ". Kontrollera att texten stämmer med det som sägs.";
                    if (cueProp[i] >= 0)
                    {
                        it.ForeColor = PropRow;
                        it.ToolTipText = proposals[cueProp[i]].Describe() + "\nHögerklicka för att godkänna eller avvisa.";
                    }
                    // betydelsejämförelsen: visa radens tid i den andra varianten
                    if (alt)
                    {
                        string diff = Fmt(Math.Abs(c.MeaningAlt - c.Start) / 1000, "0.00") + " s " + ((c.MeaningAlt < c.Start) == c.MeaningOff ? "tidigare" : "senare");
                        string tip = c.MeaningOff
                            ? "⇄ Vanlig synk visas. Betydelsejämförelsen flyttar raden " + diff + ", till " + Srt.FormatTime(c.MeaningAlt) + "."
                            : "⇄ Betydelsejämförelsen flyttade raden " + diff + ". I den vanliga synken börjar den " + Srt.FormatTime(c.MeaningAlt) + ".";
                        it.ToolTipText = (string.IsNullOrEmpty(it.ToolTipText) ? "" : it.ToolTipText + "\n\n") + tip + "\nLänken ovanför listan växlar mellan de två.";
                    }
                    // rättad vid inläsningen (kodade tecken, trasig formatering): visa vad som stod i filen
                    if (c.Raw != null) it.ToolTipText = (string.IsNullOrEmpty(it.ToolTipText) ? "" : it.ToolTipText + "\n\n") + "✎ Rättad vid inläsningen. I filen stod:\n" + (c.Raw.Length > 400 ? c.Raw.Substring(0, 400) + "…" : c.Raw);
                    rowCue[row] = i; rowRef[row] = bc; cueRow[i] = row;
                    items.Add(it);
                    int pk = cueProp[i];
                    if (pk >= 0 && i == proposals[pk].First + proposals[pk].Count - 1)
                    {
                        var p = proposals[pk];
                        for (int q = 0; q < p.NewCues.Count; q++)
                        {
                            var nc = p.NewCues[q]; var pr = q < p.Refs.Length ? p.Refs[q] : null;
                            var pit = new ListViewItem(new[] { HC ? "▶" : "Spela stället", "→ blir", "", Srt.FormatTime(nc.Start), Srt.FormatTime(nc.End), "",
                                nc.Text.Replace("\n", " / "), pr != null ? Srt.FormatTime(pr.Start) : "", pr != null ? pr.Text.Replace("\n", " / ") : "" });
                            pit.ForeColor = PropRow; pit.Font = propFont;
                            if (!HC) pit.BackColor = Theme.PropBg;
                            pit.ToolTipText = "Så här blir raden om du godkänner förslaget ovanför. Högerklicka för att godkänna eller avvisa.";
                            int prow = items.Count;
                            rowCue[prow] = -1; rowRef[prow] = null; rowProp[prow] = pk;
                            items.Add(pit); previewRows++;
                        }
                    }
                }
            }
            Array.Resize(ref rowCue, items.Count); Array.Resize(ref rowRef, items.Count); Array.Resize(ref rowProp, items.Count);
            if (view == ViewAll) viewInfo.Text = "";
            else if (items.Count > 0) viewInfo.Text = "Visar " + (items.Count - previewRows) + " av " + total + " rader.";
            // tom lista: säg varför, så att det inte ser ut som att synken misslyckades
            else if (view == ViewOnlyRed) viewInfo.Text = "Inga röda rader. Välj Alla rader i Visa för att se listan.";
            else if (view == ViewProposals && !proposeCheck.Checked) viewInfo.Text = "Förslag är avstängda. Kryssa i rutan bredvid, eller välj Alla rader.";
            else if (view == ViewFixed) viewInfo.Text = "Inga rader rättades vid inläsningen. Välj Alla rader i Visa för att se listan.";
            else if (view == ViewMoved) viewInfo.Text = (meaningCheck.Checked ? "Betydelsejämförelsen flyttade inga rader." : "Betydelsejämförelsen är avstängd i Inställningar.") + " Välj Alla rader i Visa för att se listan.";
            else if (view == ViewProposals) viewInfo.Text = "Inga förslag" + (refIsText ? "" : " (görs bara mot text, inte mot ljud)") + ". Välj Alla rader i Visa.";
            else viewInfo.Text = "Inga rader att visa. Välj Alla rader i Visa.";
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(items.ToArray());
            int selRow = -1;
            if (selCue >= 0 && selCue < cueRow.Length) selRow = cueRow[selCue];
            else if (selRef != null) selRow = Array.IndexOf(rowRef, selRef);
            if (selRow >= 0 && selRow < list.Items.Count) { list.Items[selRow].Selected = true; list.EnsureVisible(selRow); }
            list.EndUpdate();
            int sel = selRow >= 0 ? rowCue[selRow] : -1;

            timeline.Ref = refc; timeline.Orig = tgtCues; timeline.Synced = synced;
            if (sel >= 0 && sel < synced.Count) timeline.Highlight = synced[sel];
            else if (selRow >= 0 && selRef != null) { timeline.Highlight = null; timeline.Center = (selRef.Start + selRef.End) / 2; }
            else if (synced.Count > 0) { timeline.Highlight = null; timeline.Center = synced[0].Start + 20000; }
            timeline.Invalidate();
            if (lost > 0) SetStatus(lost + " repliker hamnar före filmens början och tas bort när du sparar.", false);
            ShowResult();
            UpdateButtons();
        }
    }
}
