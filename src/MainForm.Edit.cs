// Text Sync - användargränssnitt: redigera rader, ångra, förslag
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
        // ------------------------------------------------------------ redigera rader (med ångra)
        // Markerade rader som index i synced (grå referensrader hoppas över)
        List<int> SelectedCues()
        {
            var r = new List<int>();
            if (rowCue == null) return r;
            foreach (int row in list.SelectedIndices)
                if (row < rowCue.Length && rowCue[row] >= 0) r.Add(rowCue[row]);
            r.Sort();
            return r;
        }

        // Markera synkad rad nummer ci (index i synced)
        void SelectRow(int ci)
        {
            list.SelectedIndices.Clear();
            if (cueRow == null || ci < 0 || ci >= cueRow.Length) return;
            int i = cueRow[ci];
            if (i < 0 || i >= list.Items.Count) return;
            list.Items[i].Selected = true; list.Items[i].Focused = true; list.EnsureVisible(i);
            list.Focus();
        }

        // Varje ändring sparar först en kopia av hela listan, så att den kan ångras
        void Changed(List<Cue> newOutput, Cue select, string status, string log)
        {
            undo.Push(result.Output);
            result.Output = newOutput.OrderBy(c => c.Start).ToList();
            unsaved = true;
            list.SelectedIndices.Clear();
            RefreshPreview();
            int i = select != null ? result.Output.IndexOf(select) : -1;
            if (i >= 0) SelectRow(i);
            SetStatus(status, false);
            openLink.Visible = false;
            undoLink.Visible = true;
            Log.Info(log);
        }

        void DeleteSelected()
        {
            if (busy || result == null || list.SelectedIndices.Count == 0) return;
            var idx = SelectedCues();
            if (idx.Count == 0)
            {
                bool prop = rowProp != null && list.SelectedIndices.Cast<int>().Any(r => r < rowProp.Length && rowProp[r] >= 0);
                SetStatus(prop ? "Raden visar hur förslaget blir. Högerklicka för att avvisa förslaget." : "Grå rader kommer från referensen och sparas inte – de kan inte tas bort.", false);
                return;
            }
            if (idx.Count == result.Output.Count)
            {
                SetStatus("Minst en rad måste finnas kvar.", false);
                return;
            }
            var del = new HashSet<int>(idx);
            var keep = result.Output.Where((c, i) => !del.Contains(i)).ToList();
            int next = Math.Min(idx[0], keep.Count - 1);
            Changed(keep, keep[next], idx.Count == 1 ? "1 rad borttagen." : idx.Count + " rader borttagna.", "Tog bort " + idx.Count + " rad(er)");
        }

        void EditSelected()
        {
            if (busy || result == null || list.SelectedIndices.Count != 1) return;
            int row = list.SelectedIndices[0];
            if (rowCue == null || row >= rowCue.Length) return;
            if (rowProp != null && row < rowProp.Length && rowProp[row] >= 0)
            {
                SetStatus("Raden visar hur förslaget blir. Högerklicka för att godkänna eller avvisa det.", false);
                return;
            }
            if (rowCue[row] < 0) { AddRow(rowRef[row]); return; }   // grå referensrad: lägg till en egen rad på samma tid
            int i = rowCue[row];
            var shown = synced[i];
            double extra = (double)extraNum.Value;
            using (var d = new CueDialog("Ändra rad " + (i + 1), shown.Start, shown.End, shown.Text))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                // Orörd text (bara tiden ändrades) behåller formateringen som rutan "Ta bort kursiv och annan formatering" döljer.
                // Ny text lagas som vid inläsningen (kodade tecken, taggar som inte stängs).
                string text = d.CueText == shown.Text ? result.Output[i].Text : Markup.Fix(d.CueText);
                var nc = new Cue(d.StartMs - extra, d.EndMs - extra, text) { OrigStart = result.Output[i].OrigStart, Raw = d.CueText == shown.Text ? result.Output[i].Raw : null };
                var list2 = new List<Cue>(result.Output);
                list2[i] = nc;
                Changed(list2, nc, "Rad " + (i + 1) + " ändrad.", "Ändrade rad " + (i + 1));
            }
        }

        void AddRow() { AddRow(null); }

        // at = grå referensrad att ta tiden från, annars hamnar raden efter den markerade
        void AddRow(Cue at)
        {
            if (busy || result == null) return;
            double extra = (double)extraNum.Value;
            double start = 0;
            var selCues = SelectedCues();
            if (at != null) start = at.Start;
            else if (selCues.Count > 0) start = synced[selCues[selCues.Count - 1]].End + 200;
            else if (synced.Count > 0) start = synced[synced.Count - 1].End + 200;
            double end = at != null ? at.End : start + 2000;
            using (var d = new CueDialog("Lägg till rad", start, end, ""))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var nc = new Cue(d.StartMs - extra, d.EndMs - extra, Markup.Fix(d.CueText));
                var list2 = new List<Cue>(result.Output); list2.Add(nc);
                Changed(list2, nc, "Rad tillagd.", "Lade till rad vid " + Srt.FormatTime(d.StartMs));
            }
        }

        // Förslag som berör de markerade raderna
        List<AlignProposal> SelectedProposals()
        {
            var r = new List<AlignProposal>();
            if (cueProp == null) return r;
            foreach (int ci in SelectedCues())
                if (ci < cueProp.Length && cueProp[ci] >= 0 && !r.Contains(proposals[cueProp[ci]])) r.Add(proposals[cueProp[ci]]);
            // raderna "→ blir" hör också till sitt förslag
            if (rowProp != null)
                foreach (int row in list.SelectedIndices)
                    if (row < rowProp.Length && rowProp[row] >= 0 && rowProp[row] < proposals.Count && !r.Contains(proposals[rowProp[row]])) r.Add(proposals[rowProp[row]]);
            return r;
        }

        // Slå ihop eller dela raderna enligt förslagen. Ett enda steg att ångra, även för alla förslag på en gång.
        void AcceptProposals(List<AlignProposal> ps)
        {
            if (busy || result == null || ps == null || ps.Count == 0) return;
            ps = new List<AlignProposal>(ps);
            double extra = (double)extraNum.Value;
            var replaced = new HashSet<int>(); var added = new List<Cue>();
            foreach (var p in ps)
            {
                for (int k = 0; k < p.Count; k++) replaced.Add(p.First + k);
                // en delad rad kan få halva taggar ("<i>Rad ett." och "Rad två.</i>"): de lagas
                foreach (var c in p.NewCues) added.Add(new Cue(c.Start - extra, c.End - extra, Markup.Fix(c.Text)) { OrigStart = c.OrigStart });
            }
            var list2 = result.Output.Where((c, i) => !replaced.Contains(i)).Concat(added).ToList();
            int merged = ps.Count(p => p.IsMerge), splits = ps.Count - merged;
            var parts = new List<string>();
            if (merged > 0) parts.Add(merged == 1 ? "1 sammanslagning" : merged + " sammanslagningar");
            if (splits > 0) parts.Add(splits == 1 ? "1 delning" : splits + " delningar");
            string what = string.Join(" och ", parts);
            Changed(list2, added[0], "Godkände " + what + ". Ctrl+Z ångrar.", "Godkände förslag: " + what);
        }

        // Avvisade förslag visas inte igen förrän nästa synk
        void RejectProposals(List<AlignProposal> ps)
        {
            if (busy || result == null || ps == null || ps.Count == 0) return;
            foreach (var p in ps) rejected.Add(p.Key);
            RefreshPreview();
            SetStatus(ps.Count == 1 ? "Förslaget avvisat." : ps.Count + " förslag avvisade.", false);
        }

        void UndoChange()
        {
            if (busy || result == null || undo.Count == 0) return;
            result.Output = undo.Pop();
            if (hiUndoDepth > 0 && undo.Count < hiUndoDepth)
            {
                // rensningen av text för hörselskadade ångrades: rutan ska stämma med texten
                hiUndoDepth = 0; hiInfo = null;
                hiQuiet = true; hiCheck.Checked = false; hiQuiet = false;
            }
            unsaved = true;
            list.SelectedIndices.Clear();
            RefreshPreview();
            SetStatus("Ändringen ångrades.", false);
            undoLink.Visible = undo.Count > 0;
        }

        // Hoppa till nästa rad som saknar referenstext samtidigt (börjar om från toppen)
        void NextUnmatched()
        {
            if (rowMatched == null || rowMatched.Length == 0) return;
            int from = 0;
            if (list.SelectedIndices.Count > 0 && rowCue != null)
            {
                // första synkade raden efter den markerade listraden
                int row = list.SelectedIndices[0];
                for (int r = row + 1; r < rowCue.Length; r++) if (rowCue[r] >= 0) { from = rowCue[r]; break; }
            }
            for (int k = 0; k < rowMatched.Length; k++)
            {
                int i = (from + k) % rowMatched.Length;
                if (!rowMatched[i])
                {
                    // den röda raden är dold av valet i Visa: visa alla rader först
                    if (cueRow != null && i < cueRow.Length && cueRow[i] < 0) viewCombo.SelectedIndex = ViewAll;
                    SelectRow(i); return;
                }
            }
            SetStatus("Alla rader har referenstext samtidigt.", true);
        }

        // Hoppa till föregående (-1) eller nästa (+1) svaga ställe. Börjar om från andra änden.
        void JumpWeak(int dir)
        {
            if (weakStops == null || weakStops.Count == 0 || synced == null) return;
            int cur = -1;
            if (list.SelectedIndices.Count > 0 && rowCue != null)
                for (int r = Math.Min(list.SelectedIndices[0], rowCue.Length - 1); r >= 0; r--) if (rowCue[r] >= 0) { cur = rowCue[r]; break; }
            int idx = -1;
            if (dir > 0) { for (int k = 0; k < weakStops.Count; k++) if (weakStops[k] > cur) { idx = k; break; } if (idx < 0) idx = 0; }
            else { for (int k = weakStops.Count - 1; k >= 0; k--) if (weakStops[k] < cur) { idx = k; break; } if (idx < 0) idx = weakStops.Count - 1; }
            int i = weakStops[idx];
            // raden är dold av valet i Visa: visa alla rader först
            if (cueRow != null && i < cueRow.Length && cueRow[i] < 0) viewCombo.SelectedIndex = ViewAll;
            SelectRow(i);
        }

        // Sök i texten (synkad text och referens) bland raderna som listan visar.
        // dir: 1 = nästa träff, -1 = föregående, 0 = första träffen från den markerade raden (medan man skriver)
        void Find(int dir)
        {
            string q = searchBox.Text.Trim();
            searchInfo.ForeColor = Muted;
            if (q.Length == 0) { searchInfo.Text = ""; return; }
            if (synced == null || list.Items.Count == 0) { searchInfo.Text = synced == null ? "Synka först, så går det att söka i texten." : "Inga rader att söka i."; return; }
            var hits = new List<int>();
            for (int i = 0; i < list.Items.Count; i++)
            {
                var si = list.Items[i].SubItems;
                if (si[6].Text.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || si[8].Text.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) hits.Add(i);
            }
            if (hits.Count == 0)
            {
                searchInfo.ForeColor = ErrColor;
                searchInfo.Text = viewCombo.SelectedIndex == ViewAll ? "Inga träffar." : "Inga träffar bland raderna som visas. Välj Alla rader i Visa.";
                return;
            }
            int cur = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : -1;
            int k = -1;
            if (dir < 0) { for (int j = hits.Count - 1; j >= 0; j--) if (hits[j] < cur) { k = j; break; } if (k < 0) k = hits.Count - 1; }
            else { for (int j = 0; j < hits.Count; j++) if (dir == 0 ? hits[j] >= cur : hits[j] > cur) { k = j; break; } if (k < 0) k = 0; }
            list.SelectedIndices.Clear();
            var it = list.Items[hits[k]];
            it.Selected = true; it.Focused = true; list.EnsureVisible(hits[k]);   // fokus stannar i sökrutan
            searchInfo.Text = "Träff " + (k + 1) + " av " + hits.Count + ". Enter = nästa.";
        }

        // Under 95 % matchade rader: fråga innan sparning
        bool ConfirmLowMatch()
        {
            if (rowMatched == null || rowMatched.Length == 0) return true;
            int ok = rowMatched.Count(m => m), n = rowMatched.Length;
            if (ok >= MatchThreshold * n) return true;
            var a = MessageBox.Show(this,
                "Bara " + ok + " av " + n + " rader (" + Fmt(100.0 * ok / n, "0") + " %) har en referensrad samtidigt.\n\n" +
                "Det kan betyda att synken är fel, men också att texten har rader som inte finns i referensen (t.ex. utrop eller textarens namn).\n\n" +
                "Vill du spara ändå?\n\nVälj Nej för att granska de röda raderna – F3 hoppar till nästa.",
                Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (a == DialogResult.Yes) return true;
            list.SelectedIndices.Clear();
            NextUnmatched();
            return false;
        }
    }
}
