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

        // Dela den markerade raden i två, där användaren ställer markören i texten (Dela rad… i menyn, Ctrl+D)
        void SplitSelected()
        {
            if (busy || result == null) return;
            if (list.SelectedIndices.Count != 1) { SetStatus("Markera en rad för att dela den.", false); return; }
            int row = list.SelectedIndices[0];
            if (rowCue == null || row >= rowCue.Length) return;
            if (rowProp != null && row < rowProp.Length && rowProp[row] >= 0)
            {
                SetStatus("Raden visar hur förslaget blir. Högerklicka för att godkänna eller avvisa det.", false);
                return;
            }
            if (rowCue[row] < 0) { SetStatus("Grå rader kommer från referensen och sparas inte – de kan inte delas.", false); return; }
            int i = rowCue[row];
            var shown = synced[i];
            if (LineAligner.DefaultSplit(shown.Text) < 0) { SetStatus("Raden har bara ett ord och kan inte delas.", false); return; }
            double extra = (double)extraNum.Value;
            using (var d = new SplitDialog("Dela rad " + (i + 1), shown.Start, shown.End, shown.Text))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Cue first;
                var list2 = LineAligner.SplitManually(result.Output, i, d.PartA, d.PartB, d.MidMs - extra, out first);
                Changed(list2, first, "Rad " + (i + 1) + " delad i två. Ctrl+Z ångrar.", "Delade rad " + (i + 1) + " för hand vid " + Srt.FormatTime(d.MidMs));
            }
        }

        // Synka om bara de markerade raderna (menyn och Ctrl+R): varje stycke markerade rader i följd flyttas så att det passar referensen,
        // och mot en text delas raderna sedan och slås ihop som referensen. Ett steg att ångra. Reglerna ligger i LineAligner.Resync.
        void ResyncSelected()
        {
            if (busy || result == null) return;
            var idx = SelectedCues();
            if (idx.Count == 0)
            {
                SetStatus(list.SelectedIndices.Count == 0 ? "Markera raderna som ska synkas om." : "Grå rader kommer från referensen och kan inte synkas om. Markera egna rader.", false);
                return;
            }
            var refc = refCuesUsed ?? new List<Cue>();
            // den extra justeringen visas men ligger inte i texten: referensen flyttas åt andra hållet i stället
            double extra = (double)extraNum.Value;
            IList<Cue> rc = extra == 0 ? refc : refc.Select(r => new Cue(r.Start - extra, r.End - extra, r.Text)).ToList();
            List<double> shifts; string what; Cue first;
            var list2 = LineAligner.Resync(result.Output, idx, rc, refIsText, result.Meaning != null ? result.Meaning.Moves : null, out shifts, out what, out first);
            string rows = idx.Count == 1 ? "1 rad" : idx.Count + " rader";
            int movedRuns = shifts.Count(d => d != 0);
            if (movedRuns == 0 && what == null)
            {
                SetStatus((idx.Count == 1 ? "Raden ligger" : "De " + idx.Count + " raderna ligger") + " redan så bra det går mot referensen" +
                    (refIsText ? ", och radindelningen går inte att göra mer lik referensens" : "") + ". Inget ändrades.", false);
                return;
            }
            double big = shifts.OrderByDescending(d => Math.Abs(d)).First();
            string moved = movedRuns == 0 ? "tiderna låg redan rätt"
                : shifts.Count == 1 ? "flyttade " + FmtOffset(big)
                : movedRuns + " av " + shifts.Count + " stycken flyttade (som mest " + FmtOffset(big) + ")";
            Changed(list2, first, "Synkade om " + rows + ": " + moved + (what != null ? ", och " + what + " som referensen" : "") + ". Ctrl+Z ångrar.",
                "Synkade om " + idx.Count + " markerade rader: förskjutning per stycke " + string.Join(", ", shifts.Select(d => FmtOffset(d))) + (what != null ? ", radindelning: " + what : ""));
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
            if (busy) return;
            AcceptProposals(ps, false);
        }

        // auto = inställningen "Godkänn förslag om radindelning automatiskt" (anropas också i slutet av synken, medan busy är satt).
        // Returnerar texten till statusraden, null = inget att godkänna.
        string AcceptProposals(List<AlignProposal> ps, bool auto)
        {
            if (result == null || ps == null || ps.Count == 0) return null;
            ps = new List<AlignProposal>(ps);
            List<Cue> added;
            var list2 = LineAligner.Apply(result.Output, ps, (double)extraNum.Value, out added);
            string what = LineAligner.CountText(ps);
            string status = auto ? "Radindelning som referensen: " + what + " gjordes automatiskt (Inställningar). Ctrl+Z ångrar."
                                 : "Godkände " + what + ". Ctrl+Z ångrar.";
            Changed(list2, added[0], status, (auto ? "Godkände förslag automatiskt: " : "Godkände förslag: ") + what);
            return status;
        }

        // Godkänner alla förslag som visas, om inställningen är på. Görs en gång efter synken och när inställningen slås på,
        // inte efter varje ändring: efter Ctrl+Z står förslagen kvar som förslag.
        string AutoAccept()
        {
            return autoAccept ? AcceptProposals(proposals, true) : null;
        }

        // Avvisade förslag visas inte igen förrän nästa synk
        void RejectProposals(List<AlignProposal> ps)
        {
            if (busy || result == null || ps == null || ps.Count == 0) return;
            foreach (var p in ps) rejected.Add(p.Key);
            RefreshPreview();
            SetStatus(ps.Count == 1 ? "Förslaget avvisat." : ps.Count + " förslag avvisade.", false);
        }

        // Byter mellan den vanliga synken och synken med betydelsejämförelsen. Det som visas är det som sparas.
        // Bytet är en ändring som alla andra: Ctrl+Z ångrar, och rader som har ändrats för hand rörs inte.
        void SwitchMeaning()
        {
            if (busy || result == null) return;
            int n = Meaning.MovedCount(result.Output);
            if (n == 0) return;
            bool toPlain = !Meaning.ShowsPlain(result.Output);
            string rows = n == 1 ? "1 rad" : n + " rader";
            Changed(Meaning.Switch(result.Output), null,
                toPlain ? "Nu visas den vanliga synken (" + rows + " ligger som före betydelsejämförelsen). Det du ser är det som sparas."
                        : "Nu visas synken med betydelsejämförelsen (" + rows + " flyttade). Det du ser är det som sparas.",
                "Växlade till " + (toPlain ? "den vanliga synken" : "synken med betydelsejämförelsen") + " (" + n + " rader)");
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
