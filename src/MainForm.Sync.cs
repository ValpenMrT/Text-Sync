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
            var opt = new SyncOptions { Mode = (SyncMode)modeCombo.SelectedIndex, DetectFramerate = fpsCheck.Checked };
            var tgt = tgtCues;
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
                        refc = res.Item1; truncated = res.Item2;
                        if (!truncated) trackCache[key] = refc;
                    }
                    if (refc.Count < 5) throw new InvalidDataException("Spåret innehåller nästan inga repliker (" + refc.Count + "). Välj ett annat spår.");
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
                ShowResult();
                RefreshPreview();
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

        void ShowResult()
        {
            var r = result;
            var sb = new StringBuilder();
            bool good = true;
            sb.Append(r.Method);
            if (rowMatched != null && rowMatched.Length > 0)
            {
                int ok = rowMatched.Count(m => m), n = rowMatched.Length;
                good = ok >= MatchThreshold * n;
                var ao = trackCombo.SelectedItem as AudioRefOption;
                sb.Append((ao != null && ao.Mode != AudioMode.Whisper ? "     Rader som ligger på tal i ljudet: " : "     Rader med referenstext samtidigt: ") + ok + " av " + n + " (" + Fmt(Math.Floor(1000.0 * ok / n) / 10, "0.#") + " %)");
            }
            sb.Insert(0, good ? "✔  " : "⚠  ");
            sb.Append("\n");
            if (r.Segments.Count > 1)
            {
                sb.Append("Avsnitt: ");
                var parts = r.Segments.Take(8).Select(s => Srt.FormatTime(s.FromMs).Substring(0, 8) + " " + FmtOffset(s.OffsetMs));
                sb.Append(string.Join("   ·   ", parts));
                if (r.Segments.Count > 8) sb.Append("   … (" + r.Segments.Count + " st)");
                sb.Append("\n");
            }
            else
            {
                sb.Append("Förskjutning: " + FmtOffset(r.OffsetMs));
                if (Math.Abs(r.DriftMsPerHour) >= 1) sb.Append("     Drift: " + Fmt(r.DriftMsPerHour, "0") + " ms/timme");
                sb.Append("\n");
            }
            if (r.FpsText.Length > 0) sb.Append(r.FpsText + "\n");
            foreach (var n in r.Notes) sb.Append("⚠  " + n + "\n");
            if (!good) sb.Append("Under 95 % av raderna har referenstext samtidigt – granska de röda raderna (F3 hoppar till nästa).\n");
            if (proposals.Count > 0)
                sb.Append(proposals.Count + " förslag om att slå ihop eller dela rader så att de följer referensens radindelning och tider (blå i listan). " +
                    "Högerklicka för att godkänna eller avvisa.\n");
            if (refOnlyTotal > 0) sb.Append(refOnlyTotal + " grå rader finns bara i referensen och sparas inte" + (viewCombo.SelectedIndex == ViewAll ? "" : viewCombo.SelectedIndex == ViewProposals ? " (de flesta dolda just nu, se Visa ovanför listan)" : " (dolda just nu, se Visa ovanför listan)") + ".\n");
            sb.Append("Redigera i listan: dubbelklicka för att ändra text och tid, Insert lägger till, Delete tar bort, Ctrl+Z ångrar.");
            resultLabel.Text = sb.ToString().TrimEnd();
            resultLabel.ForeColor = good ? OkColor : ErrColor;
            resultLabel.MaximumSize = new Size(Math.Max(200, root.ClientSize.Width - 40), 0);
            resultLabel.Visible = true;
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

            // 2) referensrader som inte redan står bredvid en synkad rad får en egen grå rad i tidsordning,
            //    så att listan visar alla rader i båda texterna. "(tal)" från taldetekteringen är ingen text och visas inte.
            var extraRef = refc.Where(r => !shownRef.Contains(r) && r.Text != "(tal)").OrderBy(r => r.Start).ToList();
            refOnlyTotal = extraRef.Count;

            // 3) förslag på att slå ihop eller dela rader så att de följer referensens radindelning
            proposals = refIsText && proposeCheck.Checked ? LineAligner.Propose(synced, refc).Where(p => !rejected.Contains(p.Key)).ToList() : new List<AlignProposal>();
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

            int view = viewCombo.SelectedIndex;
            int total = synced.Count + extraRef.Count;
            rowCue = new int[total]; rowRef = new Cue[total]; cueRow = new int[synced.Count];
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
                    var it = new ListViewItem(new[] { "", "", "", "", "", "",
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
                    // förslagsrader märks med text också, inte bara med färg
                    string mark = cueProp[i] < 0 ? "" : proposals[cueProp[i]].IsMerge ? "Ihop?  " : "Dela?  ";
                    var it = new ListViewItem(new[] { mark + (i + 1),
                        double.IsNaN(c.OrigStart) ? "— (ny rad)" : Srt.FormatTime(c.OrigStart),
                        Srt.FormatTime(c.Start), Srt.FormatTime(c.End),
                        // flyttad efter bildfrekvensomräkning – samma tal som "Avsnitt" i resultatet
                        double.IsNaN(c.OrigStart) ? "" : Fmt((c.Start - c.OrigStart * result.Scale) / 1000, "+0.00;-0.00;0.00") + " s",
                        c.Text.Replace("\n", " / "),
                        bc != null ? Srt.FormatTime(bc.Start) : "—",
                        bc != null ? bc.Text.Replace("\n", " / ") : "— (ingen referenstext samtidigt)" });
                    if (bc == null) it.ForeColor = RedRow;
                    if (cueProp[i] >= 0)
                    {
                        it.ForeColor = PropRow;
                        it.ToolTipText = proposals[cueProp[i]].Describe() + "\nHögerklicka för att godkänna eller avvisa.";
                    }
                    rowCue[row] = i; rowRef[row] = bc; cueRow[i] = row;
                    items.Add(it);
                }
            }
            Array.Resize(ref rowCue, items.Count); Array.Resize(ref rowRef, items.Count);
            if (view == ViewAll) viewInfo.Text = "";
            else if (items.Count > 0) viewInfo.Text = "Visar " + items.Count + " av " + total + " rader.";
            // tom lista: säg varför, så att det inte ser ut som att synken misslyckades
            else if (view == ViewOnlyRed) viewInfo.Text = "Inga röda rader. Välj Alla rader i Visa för att se listan.";
            else if (view == ViewProposals && !proposeCheck.Checked) viewInfo.Text = "Förslag är avstängda. Kryssa i rutan bredvid, eller välj Alla rader.";
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
