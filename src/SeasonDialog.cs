// Text Sync - fönstret "Hel säsong": parar ihop filmer och undertexter, synkar dem i kö, sparar varje undertext bredvid sitt avsnitt
// och kan återställa allt efteråt (SeasonUndo i Season.cs). Själva synken av ett avsnitt finns i SeasonSync.cs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TextSync
{
    public class SeasonDialog : Form
    {
        class Row
        {
            public SeasonItem Item;
            public EpisodeOutcome Out = new EpisodeOutcome();
            public ListViewItem View;
            public bool Edited;   // användaren har bytt undertext eller valt Hoppa över
            public UndoRun Run;   // omgången som avsnittets sparade fil hör till (för Återställ avsnittet)
            public string Tip;    // beskedet med förklaring, visas under listan när avsnittet är markerat
        }

        readonly MainForm main;
        readonly List<Row> rows = new List<Row>();
        readonly List<string> files = new List<string>();   // alla filer som har lagts till, i den ordning de kom
        readonly HashSet<string> fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000 };
        public SeasonOptions Options = new SeasonOptions();
        public string OpenRef, OpenTarget;      // satta när användaren valde "Öppna i huvudfönstret"
        public readonly bool BuiltDark = Theme.Dark;

        ListView list;
        Label intro, info, status;
        ProgressBar progress;
        ComboBox refCombo;
        CheckBox mkvCheck, unsureCheck;
        Button addFolderBtn, addFilesBtn, clearBtn, swapBtn, skipBtn, openBtn, undoOneBtn, runBtn, restoreBtn, closeBtn;
        readonly float k;

        UndoRun run;            // den här omgångens sparade filer (skapas vid första synken)
        string runFolder;       // mappen som omgången gäller
        readonly HashSet<string> triedRuns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // omgångar där Återställ… redan är gjord (det som är kvar där gick inte att återställa)
        UndoRun restorable;     // det som Återställ… gäller: den här omgången, annars den senaste på disken
        bool busy, inQueue, stopAfter;
        int busySince;
        CancellationTokenSource cts;

        public SeasonDialog(MainForm owner)
        {
            main = owner;
            SuspendLayout();
            Text = "Hel säsong – " + MainForm.Caption;
            Font = new Font("Segoe UI", 9.5F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Theme.Card; ForeColor = Theme.Fg;
            ClientSize = new Size(1040, 620);
            MinimumSize = new Size(760, 440);
            try { Icon = owner.Icon; } catch { }
            float dpi = 96f;
            try { using (var gr = owner.CreateGraphics()) dpi = gr.DpiX; } catch { }   // som i huvudfönstret: kolumnbredderna skalas inte av Windows
            k = dpi / 96f;
            AllowDrop = true;
            DragEnter += (s, e) => e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) BeginInvoke(new Action(() => AddPaths(f))); };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16, 12, 16, 12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // förklaring
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // lägg till
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // listan
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // knappar för markerat avsnitt
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // filer som inte användes
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // förlopp och Synka alla
            Controls.Add(root);

            intro = new Label { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseMnemonic = false, Margin = new Padding(0, 0, 0, 8) };
            MainForm.FitLabelHeight(intro);
            root.Controls.Add(intro, 0, 0);

            // --- lägg till filer, och vad som används som referens
            var top = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 6) };
            addFolderBtn = Btn("Lägg till &mapp…", 140, false); addFolderBtn.Click += (s, e) => BrowseFolder();
            addFilesBtn = Btn("Lägg till &filer…", 130, false); addFilesBtn.Click += (s, e) => BrowseFiles();
            clearBtn = Btn("&Töm listan", 110, false); clearBtn.Click += (s, e) => ClearList();
            top.Controls.Add(addFolderBtn); top.Controls.Add(addFilesBtn); top.Controls.Add(clearBtn);
            top.Controls.Add(new Label { Text = "&Referens:", AutoSize = true, Margin = new Padding(14, 9, 6, 0) });
            refCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380, Margin = new Padding(0, 5, 0, 0), AccessibleName = "Referens för varje avsnitt" };
            refCombo.Items.AddRange(new object[] {
                "Automatiskt – filmens textspår, annars filmens ljud",
                "Filmens ljud A – taldetektering (snabb)",
                "Filmens ljud A+B – med Whisper (säkrast mot ljud)" });
            int rm; if (!int.TryParse(Tools.GetSetting("seasonref", "0"), out rm) || rm < 0 || rm > 2) rm = 0;
            refCombo.SelectedIndex = rm;
            refCombo.SelectedIndexChanged += (s, e) =>
            {
                Tools.SetSetting("seasonref", refCombo.SelectedIndex.ToString());
                // en annan referens kan ge ett annat besked: osäkra avsnitt får en ny chans
                foreach (var r in rows) if (r.Out.State == EpisodeState.Unsure && !r.Out.Saved) { r.Out = new EpisodeOutcome(); UpdateRow(r); }
                UpdateButtons();
            };
            top.Controls.Add(refCombo);
            top.SetFlowBreak(refCombo, true);
            // två val som sparas till nästa gång. Båda är av från början.
            mkvCheck = new CheckBox { Text = "Lägg också in undertexten i en &kopia av filmen (MKV)", AutoSize = true, Margin = new Padding(0, 8, 24, 0), Checked = Tools.GetSetting("seasonmkv", "0") == "1" };
            mkvCheck.CheckedChanged += (s, e) => { Tools.SetSetting("seasonmkv", mkvCheck.Checked ? "1" : "0"); UpdateIntro(); };
            tips.SetToolTip(mkvCheck, "Utöver SRT-filen skapas Filmnamn (Text Sync).mkv bredvid varje film, med undertexten inlagd. Originalfilmen ändras inte.\nKopian är lika stor som filmen, så en säsong behöver mycket plats. Kräver gratisprogrammet MKVToolNix.");
            unsureCheck = new CheckBox { Text = "Spara även &osäkra avsnitt", AutoSize = true, Margin = new Padding(0, 8, 0, 0), Checked = Tools.GetSetting("seasonunsure", "0") == "1" };
            unsureCheck.CheckedChanged += (s, e) =>
            {
                Tools.SetSetting("seasonunsure", unsureCheck.Checked ? "1" : "0");
                UpdateIntro();
                // osäkra avsnitt som inte sparades får en ny chans
                if (unsureCheck.Checked) foreach (var r in rows) if (r.Out.State == EpisodeState.Unsure && !r.Out.Saved) { r.Out = new EpisodeOutcome(); UpdateRow(r); }
                UpdateButtons();
            };
            tips.SetToolTip(unsureCheck, "Osäkert betyder att texten inte verkar passa filmen. Utan kryss sparas ingenting för de avsnitten.\nMed kryss sparas de ändå, och du kan ångra med Återställ avsnittet.");
            top.Controls.Add(mkvCheck); top.Controls.Add(unsureCheck);
            root.Controls.Add(top, 0, 1);

            // --- listan: ett avsnitt per rad
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, ShowItemToolTips = true,
                BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 6), AccessibleName = "Avsnitt" };
            list.Columns.Add("Avsnitt", (int)(90 * k));
            list.Columns.Add("Film eller referens", (int)(300 * k));
            list.Columns.Add("Undertext att synka", (int)(300 * k));
            list.Columns.Add("Besked", (int)(340 * k));
            list.ClientSizeChanged += (s, e) =>
            {
                int rest = list.ClientSize.Width - list.Columns[0].Width - list.Columns[3].Width - 2;
                int w = Math.Max((int)(140 * k), rest / 2);
                list.Columns[1].Width = w; list.Columns[2].Width = w;
            };
            // Mörkt läge: rubrikerna och den markerade raden ritas av programmet (Windows ritar dem grå på grå). Annars ritar Windows allt.
            list.OwnerDraw = !Theme.HC;
            list.DrawColumnHeader += (s, e) =>
            {
                if (!Theme.Dark) { e.DrawDefault = true; return; }
                using (var bg = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(bg, e.Bounds);
                using (var pen = new Pen(Theme.BtnBorder)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height), Theme.Title,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.Left);
            };
            list.DrawItem += (s, e) => { if (!Theme.Dark) e.DrawDefault = true; };
            list.DrawSubItem += (s, e) =>
            {
                if (!Theme.Dark) { e.DrawDefault = true; return; }
                bool sel = e.Item.Selected;
                var b = e.ColumnIndex == 0 ? new Rectangle(e.Bounds.X, e.Bounds.Y, e.Header.Width, e.Bounds.Height) : e.Bounds;
                using (var bg = new SolidBrush(sel ? Theme.SelBg : list.BackColor)) e.Graphics.FillRectangle(bg, b);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, new Rectangle(b.X + 4, b.Y, b.Width - 7, b.Height), sel ? Color.White : e.SubItem.ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            };
            list.SelectedIndexChanged += (s, e) => { UpdateInfo(); UpdateButtons(); };
            list.DoubleClick += (s, e) => { if (swapBtn.Enabled) SwapTarget(); };
            root.Controls.Add(list, 0, 2);

            // --- det markerade avsnittet
            var rowBtns = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            rowBtns.Controls.Add(new Label { Text = "Markerat avsnitt:", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            swapBtn = Btn("&Byt undertext…", 140, false); swapBtn.Click += (s, e) => SwapTarget();
            skipBtn = Btn("&Hoppa över", 110, false); skipBtn.Click += (s, e) => ToggleSkip();
            openBtn = Btn("&Öppna i huvudfönstret", 190, false); openBtn.Click += (s, e) => OpenInMain();
            undoOneBtn = Btn("Återställ a&vsnittet", 160, false); undoOneBtn.Click += (s, e) => RestoreOne();
            tips.SetToolTip(openBtn, "Läser in avsnittets film och osynkade undertext i huvudfönstret och synkar, så att du kan granska raderna och spela upp ställen i filmen.");
            tips.SetToolTip(undoOneBtn, "Tar tillbaka filen som fanns före synken, eller tar bort den nya undertexten om ingen fil fanns där.");
            tips.SetToolTip(swapBtn, "Välj en annan undertext för avsnittet (eller dubbelklicka på raden).");
            rowBtns.Controls.AddRange(new Control[] { swapBtn, skipBtn, openBtn, undoOneBtn });
            root.Controls.Add(rowBtns, 0, 3);

            info = new Label { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseMnemonic = false, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 6) };
            MainForm.FitLabelHeight(info);
            root.Controls.Add(info, 0, 4);

            // --- förlopp till vänster, Synka alla / Återställ / Stäng till höger
            var bottom = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 4, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            progress = new ProgressBar { Width = 160, Height = 10, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0), Visible = false };
            status = new Label { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseMnemonic = false, AutoEllipsis = true, Height = 34, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 10, 0) };
            runBtn = Btn("&Synka alla", 170, true); runBtn.Height = 34; runBtn.Margin = new Padding(0, 0, 8, 0);
            runBtn.Click += (s, e) => RunClicked();
            restoreBtn = Btn("Åt&erställ…", 120, false); restoreBtn.Height = 34; restoreBtn.Margin = new Padding(0, 0, 8, 0);
            restoreBtn.Click += (s, e) => RestoreAll();
            closeBtn = Btn("Stäng", 100, false); closeBtn.Height = 34; closeBtn.Margin = new Padding(0);
            closeBtn.Click += (s, e) => Close();
            bottom.Controls.Add(progress, 0, 0); bottom.Controls.Add(status, 1, 0); bottom.Controls.Add(runBtn, 2, 0); bottom.Controls.Add(restoreBtn, 3, 0); bottom.Controls.Add(closeBtn, 4, 0);
            root.Controls.Add(bottom, 0, 5);

            Theme.Apply(this, null);
            // skalningen slås på sist, som i huvudfönstret
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
            UpdateIntro();
            UpdateButtons();
        }

        Button Btn(string text, int width, bool primary)
        {
            var b = main.MakeButton(text, primary);
            b.Width = width; b.Margin = new Padding(0, 0, 8, 0);
            return b;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // aldrig större än skärmen
            var wa = Screen.FromControl(Owner ?? this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width), Math.Min(MinimumSize.Height, wa.Height));
            Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
        }

        // Anropas av huvudfönstret före varje visning (samma fönster används igen, så att listan står kvar).
        // Inställningarna följer huvudfönstret: språkkod, metod, bildfrekvensfel, text för hörselskadade och formatering.
        // paths = mappar eller filer som läggs till när fönstret har visats (null = inga)
        public void Prepare(SeasonOptions o, string[] paths)
        {
            if (busy) return;
            pendingPaths = paths != null && paths.Length > 0 ? paths : null;
            bool newLang = o.Lang != Options.Lang;
            o.Ref = Options.Ref;
            Options = o;
            OpenRef = null; OpenTarget = null;
            UpdateIntro();
            if (newLang && files.Count > 0) Replan();   // en annan språkkod kan ge andra undertexter och andra filnamn
            else foreach (var r in rows) UpdateRow(r);
            RefreshRestorable();
            if (!busy && restorable != null && (run == null || restorable != run) && rows.Count == 0)
                SetStatus("Senaste säsongssynken (" + Describe(restorable) + ") går att ångra med Återställ….", false);
            UpdateButtons();
        }

        string[] pendingPaths;
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible || pendingPaths == null) return;
            var p = pendingPaths; pendingPaths = null;
            BeginInvoke(new Action(() => AddPaths(p)));
        }

        void UpdateIntro()
        {
            intro.Text = "Dra in mappen med säsongens filmer och undertexter hit, eller klicka Lägg till mapp…. Programmet parar ihop dem efter avsnittsnumret i filnamnen (S04E01, 4x01). " +
                "Kontrollera listan och klicka Synka alla. Varje undertext sparas som Filmnamn." + Options.Lang + ".srt bredvid sitt avsnitt, så att spelaren hittar den" +
                (mkvCheck != null && mkvCheck.Checked ? ", och läggs också in i en kopia av filmen (Filmnamn (Text Sync).mkv). " : ". ") +
                (unsureCheck != null && unsureCheck.Checked ? "Osäkra avsnitt sparas också. " : "Osäkra avsnitt sparas inte. ") + "Allt går att ångra efteråt med Återställ….";
        }

        // ------------------------------------------------------------ lägga till filer
        void BrowseFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Välj mappen med säsongens filmer och undertexter", ShowNewFolderButton = false })
            {
                string last = Tools.GetSetting("seasondir", "");
                if (last.Length > 0 && Directory.Exists(last)) d.SelectedPath = last;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Tools.SetSetting("seasondir", d.SelectedPath);
                AddPaths(new[] { d.SelectedPath });
            }
        }

        void BrowseFiles()
        {
            using (var d = new OpenFileDialog { Title = "Välj filmer och undertexter", Multiselect = true, Filter = "Filmer och undertexter|*.mkv;*.mp4;*.m4v;*.mov;*.avi;*.ts;*.m2ts;*.wmv;*.webm;*.mpg;*.srt|Alla filer (*.*)|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames);
        }

        // Lägger till mappar och filer och parar om. Det som redan är valt eller synkat för ett avsnitt behålls.
        public async void AddPaths(string[] paths)
        {
            if (busy || paths == null || paths.Length == 0) return;
            // upptaget medan mappen läses (kan ta tid på USB eller nätverk), så att kön inte kan startas mot en lista som strax byts ut
            SetBusy(true);
            progress.Visible = false;
            SetStatus("Letar efter filmer och undertexter…", false);
            List<string> found;
            try { found = await Task.Run(() => SeasonPlan.Collect(paths)); }
            catch (Exception ex) { Log.Error("Kunde inte läsa mappen", ex); found = new List<string>(); }
            if (IsDisposed) return;
            SetBusy(false);
            int before = files.Count;
            foreach (var f in found) if (fileSet.Add(f)) files.Add(f);
            Replan();
            if (found.Count == 0) SetStatus("Hittade inga filmer eller undertexter (SRT) där.", false);
            else if (files.Count == before) SetStatus("Filerna finns redan i listan.", false);
            else SetStatus("", false);
        }

        void Replan()
        {
            List<string> unused;
            var items = SeasonPlan.Build(files, Options.Lang, out unused);
            // nämn bara undertexter som hade kunnat synkas: en engelsk text bredvid filmen är inget att påpeka
            string want = MainForm.Norm(Options.Lang) ?? "sv";
            Log.Info("Säsong: " + items.Count + " avsnitt, " + unused.Count + " filer används inte" + (unused.Count > 0 ? ": " + string.Join("; ", unused) : ""));
            unused = unused.Where(f => MainForm.IsSrt(f) && (MainForm.GuessLang(f) == null || MainForm.Norm(MainForm.GuessLang(f)) == want)).ToList();
            // behåll det som användaren har ändrat och det som redan är synkat (samma film, eller samma referens)
            var old = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows) if (r.Item.RefPath != null) old[r.Item.RefPath] = r;
            rows.Clear();
            foreach (var it in items)
            {
                Row r;
                if (it.RefPath != null && old.TryGetValue(it.RefPath, out r) && (r.Edited || r.Out.State != EpisodeState.Waiting)) { rows.Add(r); continue; }
                rows.Add(new Row { Item = it });
            }
            MarkAlreadySynced();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var r in rows)
            {
                r.View = new ListViewItem(new[] { "", "", "", "" }) { UseItemStyleForSubItems = false, Tag = r };
                list.Items.Add(r.View);
                UpdateRow(r);
            }
            list.EndUpdate();
            int noSub = rows.Count(r => r.Item.TargetPath == null);
            int noNumber = rows.Count(r => r.Item.TargetPath == null && r.Item.Id == null);
            int done = rows.Count(r => r.Out.State == EpisodeState.Done);
            int overwrite = rows.Count(r => Pending(r) && File.Exists(r.Item.OutputPath(Options.Lang) ?? ""));
            int otherLang = files.Count(f => MainForm.IsSrt(f) && MainForm.GuessLang(f) != null && MainForm.Norm(MainForm.GuessLang(f)) != want);
            var parts = new List<string>();
            if (done > 0) parts.Add((done == 1 ? "1 avsnitt är redan synkat" : done + " avsnitt är redan synkade") + " och synkas inte igen.");
            if (noNumber > 0) parts.Add("Filnamn utan avsnittsnummer (t.ex. S04E01) kan inte paras ihop automatiskt. Markera filmen och klicka Byt undertext….");
            else if (noSub > 0) parts.Add(noSub == 1 ? "1 avsnitt saknar undertext. Markera det och klicka Byt undertext… om filen finns någon annanstans." : noSub + " avsnitt saknar undertext. Markera ett avsnitt och klicka Byt undertext… om filen finns någon annanstans.");
            if (noSub > 0 && otherLang > 0) parts.Add(otherLang + (otherLang == 1 ? " undertext har" : " undertexter har") + " en annan språkkod än den som står i huvudfönstret (" + Options.Lang + ") och synkas inte.");
            if (overwrite > 0) parts.Add((overwrite == 1 ? "1 undertext som redan finns skrivs över." : overwrite + " undertexter som redan finns skrivs över.") + " De gamla går att få tillbaka med Återställ….");
            if (unused.Count > 0)
                parts.Add((unused.Count == 1 ? "1 undertext används inte: " : unused.Count + " undertexter används inte: ") +
                    string.Join(", ", unused.Take(4).Select(f => Path.GetFileName(f))) + (unused.Count > 4 ? " med flera" : "") + ".");
            planInfo = string.Join("  ", parts);
            if (files.Count > 0 && rows.Count == 0) planInfo = "Hittade undertexter men inga filmer att para ihop dem med. Lägg till mappen där filmerna ligger.";
            UpdateInfo();
            UpdateButtons();
        }

        // Känner igen undertexter som programmet självt sparade i en tidigare säsongssynk (samma innehåll som då, inte återställda).
        // De synkas inte en gång till när mappen dras in igen, och väljs inte som undertext att synka när den osynkade filen finns kvar.
        void MarkAlreadySynced()
        {
            var own = new Dictionary<string, KeyValuePair<UndoRun, UndoEntry>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var runs = SeasonUndo.List();
                for (int i = runs.Count - 1; i >= 0; i--)   // äldst först, så att den nyaste gäller
                    foreach (var en in runs[i].Entries) if (!en.Restored && en.Path != null) own[en.Path] = new KeyValuePair<UndoRun, UndoEntry>(runs[i], en);
            }
            catch (Exception ex) { Log.Error("Kunde inte läsa tidigare säsongssynkar", ex); return; }
            if (own.Count == 0) return;
            foreach (var r in rows)
            {
                if (r.Edited || r.Out.State != EpisodeState.Waiting || r.Item.RefPath == null) continue;
                string outPath = r.Item.OutputPath(Options.Lang);
                KeyValuePair<UndoRun, UndoEntry> kv;
                if (outPath == null || !own.TryGetValue(outPath, out kv)) continue;
                try
                {
                    if (!File.Exists(outPath) || !string.Equals(SeasonUndo.HashBytes(File.ReadAllBytes(outPath)), kv.Value.Hash, StringComparison.OrdinalIgnoreCase)) continue;
                }
                catch { continue; }
                var others = r.Item.Candidates.Where(c => !MainForm.SamePath(c, outPath)).ToList();
                if (MainForm.SamePath(r.Item.TargetPath, outPath) && others.Count > 0) r.Item.TargetPath = others[0];   // den osynkade filen, inte programmets egen
                if (others.Count <= 1 && r.Item.Note != null)
                {
                    string note = r.Item.Note.Replace(SeasonPlan.NoteSeveral, "").Trim();
                    r.Item.Note = note.Length > 0 ? note : null;
                }
                r.Out = new EpisodeOutcome { State = EpisodeState.Done, Text = "✔ Redan synkad " + kv.Key.When.ToString("yyyy-MM-dd"),
                    Detail = "Undertexten sparades av säsongssynken " + kv.Key.When.ToString("yyyy-MM-dd HH:mm") + ": " + outPath +
                        "\nKlicka Synka igen om avsnittet ska synkas en gång till. Återställ… ångrar säsongssynken." };
            }
        }

        string planInfo = "";

        // Den grå raden under knapparna: det markerade avsnittets besked med förklaring, annars det som gäller hela listan
        void UpdateInfo()
        {
            var sel = Selected;
            string t = sel != null ? sel.Tip : null;
            info.Text = !string.IsNullOrEmpty(t) ? sel.Item.Title + ": " + t.Replace("\n", "  ") : planInfo;
        }

        void ClearList()
        {
            if (busy) return;
            files.Clear(); fileSet.Clear(); rows.Clear(); list.Items.Clear(); planInfo = "";
            run = null; runFolder = null;   // nästa synk blir en egen omgång, som återställs för sig
            UpdateInfo();
            SetStatus("", false);
            UpdateButtons();
        }

        // ------------------------------------------------------------ raderna
        static bool Paired(SeasonItem it) { return it.RefPath != null && it.TargetPath != null; }

        // ska synkas när man klickar Synka alla: med i kön, och inte redan sparad eller bedömd som osäker
        static bool Pending(Row r)
        {
            return Paired(r.Item) && r.Item.Include && (r.Out.State == EpisodeState.Waiting || r.Out.State == EpisodeState.Failed || r.Out.State == EpisodeState.Restored);
        }

        void UpdateRow(Row r)
        {
            var it = r.Item; var v = r.View;
            string text, tip; Color col = Theme.Fg;
            if (r.Out.State != EpisodeState.Waiting)
            {
                text = r.Out.Text; tip = r.Out.Detail;
                if (r.Out.State == EpisodeState.Good || r.Out.State == EpisodeState.Done) col = Theme.Ok;
                else if (r.Out.State == EpisodeState.Check) col = Theme.Warn;
                else if (r.Out.State == EpisodeState.Unsure || r.Out.State == EpisodeState.Failed) col = Theme.Err;
                else if (r.Out.State == EpisodeState.Restored) col = Theme.Muted;
            }
            else if (!Paired(it))
            {
                text = it.TargetPath == null ? "Saknar undertext" : "Saknar film";
                tip = (it.Note ?? (it.TargetPath == null ? "Ingen undertext hittades för avsnittet." : "Ingen film hittades för avsnittet.")) +
                    (it.TargetPath == null ? "\nKlicka Byt undertext… för att välja en fil." : ""); col = Theme.Muted;
            }
            else if (!it.Include) { text = "Hoppas över"; tip = "Avsnittet synkas inte. Klicka Ta med för att synka det."; col = Theme.Muted; }
            else
            {
                string outPath = it.OutputPath(Options.Lang);
                bool exists = outPath != null && File.Exists(outPath);
                text = it.Note != null ? "Väntar. " + it.Note : exists ? "Väntar – skriver över filen som finns" : "Väntar";
                tip = "Sparas som " + outPath + "." + (exists ? "\nFilen finns redan och skrivs över. Den gamla går att få tillbaka med Återställ…." : "");
                if (it.Note != null) { tip = it.Note + "\n" + tip; col = Theme.Warn; }
            }
            v.SubItems[0].Text = it.Title;
            v.SubItems[1].Text = it.RefPath != null ? Path.GetFileName(it.RefPath) : "— (ingen film)";
            v.SubItems[2].Text = it.TargetPath != null ? Path.GetFileName(it.TargetPath) : "— (ingen undertext)";
            v.SubItems[3].Text = text;
            bool dim = !Paired(it) || (!it.Include && r.Out.State == EpisodeState.Waiting);
            for (int i = 0; i < 3; i++) v.SubItems[i].ForeColor = dim ? Theme.Muted : Theme.Fg;
            v.SubItems[3].ForeColor = Theme.HC ? SystemColors.WindowText : col;
            v.ToolTipText = (it.RefPath != null ? "Film eller referens: " + it.RefPath + "\n" : "") + (it.TargetPath != null ? "Undertext: " + it.TargetPath + "\n" : "") + tip;
            r.Tip = text + (string.IsNullOrEmpty(tip) || tip == text ? "" : "\n" + tip);
            if (r == Selected) UpdateInfo();
        }

        Row Selected { get { return list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as Row : null; } }

        void UpdateButtons()
        {
            if (runBtn == null) return;
            var sel = Selected;
            int pending = rows.Count(Pending);
            bool anyDone = rows.Any(r => r.Out.State != EpisodeState.Waiting);
            foreach (Control c in new Control[] { addFolderBtn, addFilesBtn, refCombo, mkvCheck, unsureCheck }) c.Enabled = !busy;
            clearBtn.Enabled = !busy && rows.Count > 0;
            swapBtn.Enabled = !busy && sel != null && !sel.Out.Saved;
            skipBtn.Enabled = !busy && sel != null && Paired(sel.Item) && !sel.Out.Saved;
            skipBtn.Text = sel != null && sel.Out.State == EpisodeState.Done ? "Synka &igen" : sel != null && Paired(sel.Item) && !sel.Item.Include ? "T&a med" : "&Hoppa över";
            openBtn.Enabled = !busy && sel != null && Paired(sel.Item);
            undoOneBtn.Enabled = !busy && sel != null && sel.Out.Saved;
            if (busy && cts == null) runBtn.Enabled = false;   // återställning pågår: knappen behåller sin text
            else if (busy)
            {
                // samma knapp är Avbryt medan kön går: första klicket stannar efter avsnittet som pågår, andra klicket avbryter direkt
                runBtn.Text = !inQueue || stopAfter ? "■  &Avbryt nu" : "■  &Avbryt";
                runBtn.Enabled = cts != null && !cts.IsCancellationRequested;
            }
            else
            {
                runBtn.Text = (anyDone ? "&Synka resten (" : "&Synka alla (") + pending + ")";
                runBtn.Enabled = pending > 0;
            }
            if (!Theme.HC)
            {
                bool cancel = busy && cts != null;
                runBtn.Tag = cancel ? "secondary" : "primary";
                Theme.Apply(runBtn, null);
                runBtn.ForeColor = cancel ? Theme.Fg : Color.White;
            }
            restoreBtn.Enabled = !busy && restorable != null;
            // inte medan kön går: då skriver en annan tråd i listan över sparade filer
            if (!busy) tips.SetToolTip(restoreBtn, restorable != null ? "Ångrar säsongssynken (" + Describe(restorable) + "): filerna som fanns före kommer tillbaka och nya undertexter tas bort." : "Det finns ingen säsongssynk att ångra.");
            closeBtn.Enabled = !busy;
        }

        void SetStatus(string text, bool ok)
        {
            status.Text = text ?? "";
            status.ForeColor = ok ? Theme.Ok : Theme.Muted;
            tips.SetToolTip(status, text != null && text.Length > 80 ? text : null);
        }

        void Progress(int p, string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<int, string>(Progress), p, s); return; }
            progress.Value = Math.Max(0, Math.Min(100, p));
            if (s != null && busy) status.Text = (stopAfter ? "Stannar efter det här avsnittet – " : "") + s;
        }

        void SetBusy(bool b)
        {
            if (b && !busy) busySince = Environment.TickCount;
            busy = b;
            progress.Visible = b; progress.Value = 0;
            status.ForeColor = Theme.Muted;
            UpdateButtons();
        }

        // ------------------------------------------------------------ det markerade avsnittet
        void SwapTarget()
        {
            var r = Selected;
            if (busy || r == null || r.Out.Saved) return;
            // flera undertexter hör till avsnittet: välj bland dem i en meny, annars direkt i en dialog
            var others = r.Item.Candidates.Where(c => !MainForm.SamePath(c, r.Item.TargetPath)).ToList();
            if (others.Count == 0) { PickTargetFile(r); return; }
            var menu = new ContextMenuStrip();
            foreach (var c in others)
            {
                string path = c;
                menu.Items.Add(new ToolStripMenuItem(Path.GetFileName(path), null, (s, e) => SetTarget(r, path)) { ToolTipText = path });
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Välj en annan fil…", null, (s, e) => PickTargetFile(r)));
            Theme.ApplyMenu(menu);
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(swapBtn, new Point(0, swapBtn.Height));
        }

        void PickTargetFile(Row r)
        {
            using (var d = new OpenFileDialog { Title = "Välj undertexten för " + r.Item.Title, Filter = "SRT-undertext (*.srt)|*.srt|Alla filer (*.*)|*.*" })
            {
                string near = r.Item.TargetPath ?? r.Item.RefPath;
                try { if (near != null) d.InitialDirectory = Path.GetDirectoryName(near); } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) SetTarget(r, d.FileName);
            }
        }

        void SetTarget(Row r, string path)
        {
            if (busy) return;
            if (MainForm.IsMkv(path) || Audio.IsVideo(path)) { SetStatus("Det måste vara en undertext (SRT), inte en film.", false); return; }
            if (MainForm.SamePath(path, r.Item.RefPath)) { SetStatus("Det är samma fil som referensen.", false); return; }
            r.Item.TargetPath = path;
            if (!r.Item.Candidates.Any(c => MainForm.SamePath(c, path))) r.Item.Candidates.Insert(0, path);
            r.Item.Note = null;
            r.Item.Include = Paired(r.Item);
            r.Edited = true;
            r.Out = new EpisodeOutcome();
            UpdateRow(r);
            SetStatus("", false);
            UpdateButtons();
        }

        void ToggleSkip()
        {
            var r = Selected;
            if (busy || r == null || !Paired(r.Item) || r.Out.Saved) return;
            r.Edited = true;
            // "Synka igen" på ett avsnitt som redan är synkat: tillbaka i kön
            if (r.Out.State == EpisodeState.Done) { r.Out = new EpisodeOutcome(); r.Item.Include = true; UpdateRow(r); UpdateButtons(); return; }
            r.Item.Include = !r.Item.Include;
            if (r.Item.Include && r.Out.State == EpisodeState.Unsure) r.Out = new EpisodeOutcome();
            UpdateRow(r);
            UpdateButtons();
        }

        // Huvudfönstret läser in avsnittets film och den osynkade undertexten och synkar, så att raderna går att granska.
        // Skrevs den osynkade filen över av säsongssynken används kopian som sparades före.
        void OpenInMain()
        {
            var r = Selected;
            if (busy || r == null || !Paired(r.Item)) return;
            string target = r.Item.TargetPath;
            var en = r.Out.Entry;
            if (en != null && !en.Restored && en.Kept != null && MainForm.SamePath(en.Path, target) && File.Exists(en.Kept)) target = en.Kept;
            OpenRef = r.Item.RefPath; OpenTarget = target;
            DialogResult = DialogResult.OK;
        }

        // ------------------------------------------------------------ kön
        void RunClicked()
        {
            if (!busy) { RunAll(); return; }
            if (unchecked(Environment.TickCount - busySince) <= 700 || cts == null) return;   // skydd mot dubbelklick, som Synka i huvudfönstret
            if (inQueue && !stopAfter)
            {
                stopAfter = true;
                busySince = Environment.TickCount;   // ett dubbelklick på Avbryt ska inte bli Avbryt nu
                SetStatus("Stannar när avsnittet som pågår är klart. Klicka Avbryt nu för att avbryta direkt.", false);
            }
            else
            {
                cts.Cancel();
                SetStatus("Avbryter…", false);
            }
            UpdateButtons();
        }

        async void RunAll()
        {
            var todo = rows.Where(Pending).ToList();
            if (busy || todo.Count == 0) return;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            stopAfter = false; inQueue = false;
            SetBusy(true);
            SetStatus("Förbereder…", false);
            string endNote = null;
            try
            {
                Options.Ref = (SeasonRefMode)refCombo.SelectedIndex;
                var films = todo.Where(r => r.Item.RefIsVideo).Select(r => r.Item.RefPath).ToList();
                if (films.Count > 0)
                {
                    // grafikkortet avgör om Whisper används, och verktygen för ljudet hämtas en gång före kön
                    await main.PrepareGpu();
                    Options.WhisperOk = Tools.WhisperReady() && Audio.WhisperDevice >= 0;
                    var mode = Options.Ref; bool wok = Options.WhisperOk;
                    bool needAudio = false, needWhisper = false; int nAudio = 0;
                    await Task.Run(() =>
                    {
                        foreach (var f in films)
                        {
                            if (token.IsCancellationRequested) break;
                            try
                            {
                                var c = SeasonSync.ChooseRef(f, mode, wok);
                                if (c.Audio) { needAudio = true; nAudio++; if (c.Mode != AudioMode.Speech) needWhisper = true; }
                            }
                            catch { }   // en film som inte går att läsa får sitt felbesked i kön
                        }
                    });
                    token.ThrowIfCancellationRequested();
                    // Filmer utan textspår synkas mot ljudet. Finns inte Whisper (eller inget NVIDIA-kort) väljer användaren själv:
                    // A+B, som kan behöva en nedladdning eller ta lång tid, eller A som går fort (ägarens svar 2026-11-11).
                    bool asked = false;
                    if (mode == SeasonRefMode.Auto && needAudio && !wok)
                    {
                        bool ready = Tools.WhisperReady(), gpu = Audio.WhisperDevice >= 0;
                        int pick = ConfirmDialog.Choose(this, MainForm.Caption,
                            (nAudio == 1 ? "1 avsnitt har" : nAudio + " avsnitt har") + " inget engelskt textspår och synkas mot filmens ljud. Välj hur:\n\n" +
                            "•  Ljud A+B (säkrast): taligenkänningen Whisper hör vad som sägs." +
                            (ready ? "" : " Verktygen behöver laddas ner först. Programmet visar hur stort det är och frågar innan något hämtas.") +
                            (gpu ? " Tar ungefär en minut per avsnitt." : " Utan NVIDIA-grafikkort tar det ungefär lika lång tid som att se avsnitten.") + "\n\n" +
                            "•  Ljud A (snabbt): hittar var det pratas. Tar ungefär en minut per avsnitt" + (Tools.IsInstalled(Tools.Ffmpeg) ? "." : ", och behöver bara det lilla verktyget ffmpeg."),
                            ready ? "Använd A+B" : "Ladda ner och använd A+B", "Använd A", "Avbryt");
                        if (pick == 0) { endNote = "Avbrutet."; return; }
                        if (pick == 1) { Options.WhisperOk = true; needWhisper = true; asked = true; }
                        Log.Info("Säsong: " + nAudio + " avsnitt utan textspår, valt " + (pick == 1 ? "A+B" : "A"));
                    }
                    // frågan kommer före nedladdningen, som i huvudfönstret
                    if (!asked && needWhisper && Audio.WhisperDevice < 0 && !ConfirmDialog.Ask(this, MainForm.Caption,
                        "Taligenkänningen (Whisper) körs på processorn, eftersom inget NVIDIA-grafikkort är valt.\n\n" +
                        "Det tar ungefär lika lång tid som att se avsnitten (" + films.Count + " st). Välj \"Filmens ljud A – taldetektering\" i listan Referens om det ska gå fort.",
                        "Fortsätt", "Avbryt")) { endNote = "Avbrutet."; return; }
                    if (needAudio && !await main.EnsureTools(needWhisper, token, this, Progress)) { endNote = "Avbrutet – verktygen för synk mot ljudet hämtades inte."; return; }
                }
                // MKV-kopior: MKVToolNix måste finnas, och kopiorna är lika stora som filmerna
                Options.SaveUnsure = unsureCheck.Checked;
                Options.MakeMkv = mkvCheck.Checked && films.Count > 0;
                if (Options.MakeMkv)
                {
                    if (MainForm.FindMkvMergePublic() == null)
                    {
                        var a = MessageBox.Show(this,
                            "För att lägga in undertexten i en kopia av filmen behövs det kostnadsfria programmet MKVToolNix.\n\n" +
                            "Vill du öppna nedladdningssidan?\n\n(Kryssa ur rutan \"Lägg också in undertexten i en kopia av filmen\" om det räcker med SRT-filerna. De flesta spelare hittar dem automatiskt.)",
                            MainForm.Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (a == DialogResult.Yes) { try { System.Diagnostics.Process.Start("https://mkvtoolnix.download/downloads.html#windows"); } catch { } }
                        endNote = "Avbrutet – MKVToolNix behövs för MKV-kopiorna. Installera det och klicka Synka alla igen, eller kryssa ur rutan.";
                        return;
                    }
                    long need = 0, free = -1; string drv = "";
                    try
                    {
                        foreach (var f in films) if (!File.Exists(SeasonSync.MkvCopyPath(f))) need += new FileInfo(f).Length;
                        var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(films[0])));
                        if (d.IsReady) { free = d.AvailableFreeSpace; drv = d.Name; }
                    }
                    catch { }   // t.ex. nätverkssökväg: kontrollen görs då bara per avsnitt
                    if (free >= 0 && free < need && !ConfirmDialog.Ask(this, MainForm.Caption,
                        "Kopiorna av filmerna behöver cirka " + MainForm.Fmt(need / 1073741824.0, "0.0") + " GB, men bara " + MainForm.Fmt(free / 1073741824.0, "0.0") + " GB är ledigt på " + drv + "\n\n" +
                        "Alla avsnitt får sin SRT-fil. De avsnitt som inte får plats får ingen MKV-kopia, och det står då på raden.",
                        "Fortsätt", "Avbryt")) { endNote = "Avbrutet."; return; }
                }
                // En omgång per mapp: en annan mapp (en annan säsong) blir en egen omgång, som återställs för sig
                string folder = "";
                try { folder = Path.GetDirectoryName(todo[0].Item.RefPath); } catch { }
                // Omgången skapas först när något sparas, så att försök utan sparade filer inte tränger undan äldre omgångar.
                Func<UndoRun> getRun = () =>
                {
                    if (run == null || !string.Equals(folder, runFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        SeasonUndo.PruneFinished(null);   // omgångar utan något kvar att återställa räknas inte
                        SeasonUndo.Prune(9);              // de tio senaste säsongssynkarna går att återställa
                        run = SeasonUndo.Begin(folder);
                        runFolder = folder;
                    }
                    return run;
                };
                inQueue = true;
                UpdateButtons();
                for (int i = 0; i < todo.Count; i++)
                {
                    if (stopAfter || token.IsCancellationRequested) break;
                    var row = todo[i];
                    row.Out = new EpisodeOutcome { State = EpisodeState.Working, Text = "Synkar…" };
                    UpdateRow(row);
                    list.EnsureVisible(row.View.Index);
                    int idx = i, n = todo.Count; var item = row.Item; var o = Options;
                    string lead = item.Title + " (" + (i + 1) + " av " + n + "): ";
                    try
                    {
                        row.Out = await Task.Run(() => SeasonSync.SyncOne(item, o, getRun, (p, s) => Progress((idx * 100 + p) / n, lead + s), token));
                        if (row.Out.Run != null) row.Run = row.Out.Run;
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException || token.IsCancellationRequested) { row.Out = new EpisodeOutcome(); UpdateRow(row); break; }
                        Log.Error("Säsong: " + item.Title + " misslyckades", ex);
                        row.Out = new EpisodeOutcome { State = EpisodeState.Failed, Text = "✖ Misslyckades: " + MainForm.Friendly(ex), Detail = "Ingenting har sparats för avsnittet.\n" + MainForm.Friendly(ex) };
                    }
                    UpdateRow(row);
                }
            }
            catch (OperationCanceledException) { endNote = "Avbrutet."; }
            catch (Exception ex)
            {
                Log.Error("Säsongssynken misslyckades", ex);
                endNote = "Säsongssynken avbröts: " + MainForm.Friendly(ex);
                MessageBox.Show(this, endNote, MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                cts = null; inQueue = false; stopAfter = false;
                if (run != null) triedRuns.Remove(run.Dir);   // omgången kan ha fått nya filer
                SetBusy(false);
                RefreshRestorable();
                if (endNote != null) SetStatus(endNote, false); else Summarize(todo);
                UpdateButtons();
            }
        }

        void Summarize(List<Row> todo)
        {
            int good = todo.Count(r => r.Out.State == EpisodeState.Good), check = todo.Count(r => r.Out.State == EpisodeState.Check);
            int unsure = todo.Count(r => r.Out.State == EpisodeState.Unsure && !r.Out.Saved), failed = todo.Count(r => r.Out.State == EpisodeState.Failed);
            int kept = todo.Count(r => r.Out.State == EpisodeState.Unsure && r.Out.Saved);   // osäkra som sparades ändå (ägarens val)
            int mkv = todo.Count(r => r.Out.MkvEntry != null);
            int left = todo.Count(r => r.Out.State == EpisodeState.Waiting);
            var parts = new List<string>();
            check += kept;
            if (good + check > 0) parts.Add(good + check + (good + check == 1 ? " undertext sparad" : " undertexter sparade") +
                (check > 0 ? " (" + check + " bör kontrolleras" + (kept > 0 ? ", varav " + kept + (kept == 1 ? " osäker" : " osäkra") : "") + ")" : ""));
            if (Options.MakeMkv) parts.Add(mkv + (mkv == 1 ? " MKV-kopia skapad" : " MKV-kopior skapade"));
            if (unsure > 0) parts.Add(unsure + (unsure == 1 ? " osäker och inte sparad" : " osäkra och inte sparade"));
            if (failed > 0) parts.Add(failed + " misslyckades");
            if (left > 0) parts.Add(left + " inte synkade (avbrutet)");
            string text = (left > 0 ? "Avbrutet: " : "Klart: ") + string.Join(", ", parts) + "." +
                (check + unsure + failed > 0 ? "  Markera ett avsnitt för att se varför." : "") + (good + check > 0 ? "  Ångra med Återställ…." : "");
            SetStatus(text, left == 0 && unsure == 0 && failed == 0 && check == 0);
            Log.Info("Säsongssynk: " + text);
        }

        // ------------------------------------------------------------ återställa
        static string Describe(UndoRun r)
        {
            return r.When.ToString("yyyy-MM-dd HH:mm") + (string.IsNullOrEmpty(r.Title) ? "" : ", " + r.Title) + ", " + (r.Pending == 1 ? "1 fil" : r.Pending + " filer");
        }

        void RefreshRestorable()
        {
            // den här omgången först, annars den nyaste på disken som har något kvar. En omgång som redan är återställd så långt det gick
            // (resten har ändrats efter synken) hoppas över, så att äldre omgångar går att nå.
            if (run != null && run.Pending > 0 && !triedRuns.Contains(run.Dir)) { restorable = run; return; }
            try { restorable = SeasonUndo.List().FirstOrDefault(u => u.Pending > 0 && !triedRuns.Contains(u.Dir)); }
            catch (Exception ex) { Log.Error("Kunde inte läsa sparade säsongssynkar", ex); restorable = null; }
        }

        void RestoreOne()
        {
            var r = Selected;
            if (busy || r == null || !r.Out.Saved || r.Run == null) return;
            // MKV-kopian först, om avsnittet har en
            string mkvNote = "";
            if (r.Out.MkvEntry != null && !r.Out.MkvEntry.Restored)
            {
                var mr = SeasonUndo.Restore(r.Run, r.Out.MkvEntry);
                Log.Info("Säsong: återställning av " + r.Out.MkvEntry.Path + " → " + mr.Outcome);
                mkvNote = "  " + (mr.Message ?? "");
            }
            var res = SeasonUndo.Restore(r.Run, r.Out.Entry);
            res.Message = (res.Message ?? "") + mkvNote;
            Log.Info("Säsong: återställning av " + r.Out.Entry.Path + " → " + res.Outcome + (res.Message != null ? " (" + res.Message + ")" : ""));
            if (res.Outcome == RestoreOutcome.Restored || res.Outcome == RestoreOutcome.Removed) MarkRestored(r, res);
            SetStatus(res.Message ?? "", false);
            RefreshRestorable();
            UpdateButtons();
        }

        void MarkRestored(Row r, RestoreResult res)
        {
            r.Out = new EpisodeOutcome { State = EpisodeState.Restored, Entry = r.Out.Entry,
                Text = res.Outcome == RestoreOutcome.Restored ? "↩ Återställd – filen som fanns före är tillbaka" : "↩ Återställd – den nya undertexten är borttagen",
                Detail = res.Message ?? "" };
            UpdateRow(r);
        }

        async void RestoreAll()
        {
            RefreshRestorable();
            var target = restorable;
            if (busy || target == null) { UpdateButtons(); return; }
            var todo = target.Entries.Where(en => !en.Restored).ToList();
            int back = todo.Count(en => en.Kept != null);
            int copies = todo.Count(en => en.Kept == null && en.Path != null && en.Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
            int gone = todo.Count - back - copies;
            string msg = "Vill du ångra säsongssynken från " + target.When.ToString("yyyy-MM-dd HH:mm") + "?\n" + (string.IsNullOrEmpty(target.Title) ? "" : target.Title + "\n") + "\n" +
                (back > 0 ? "•  " + back + (back == 1 ? " undertext byts" : " undertexter byts") + " tillbaka mot filen som fanns före synken.\n" : "") +
                (gone > 0 ? "•  " + gone + (gone == 1 ? " ny undertext tas bort" : " nya undertexter tas bort") + " (det fanns ingen fil där före synken).\n" : "") +
                (copies > 0 ? "•  " + copies + (copies == 1 ? " kopia av en film" : " kopior av filmer") + " med undertexten inlagd tas bort (… (Text Sync).mkv).\n" : "") +
                "\nOriginalfilmerna rörs inte. En fil som du har ändrat efter synken rörs inte heller.";
            if (!ConfirmDialog.Ask(this, MainForm.Caption, msg, "Återställ", "Avbryt")) return;
            SetBusy(true);
            runBtn.Enabled = false;
            SetStatus("Återställer…", false);
            List<RestoreResult> res;
            try { res = await Task.Run(() => SeasonUndo.RestoreAll(target)); }
            catch (Exception ex)
            {
                Log.Error("Återställningen misslyckades", ex);
                res = new List<RestoreResult>();
                MessageBox.Show(this, "Återställningen misslyckades: " + MainForm.Friendly(ex), MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            SetBusy(false);
            int ok = 0;
            var problems = new List<string>();
            foreach (var x in res)
            {
                Log.Info("Säsong: återställning av " + x.Entry.Path + " → " + x.Outcome + (x.Message != null ? " (" + x.Message + ")" : ""));
                bool done = x.Outcome == RestoreOutcome.Restored || x.Outcome == RestoreOutcome.Removed;
                if (done) ok++; else problems.Add(x.Message ?? Path.GetFileName(x.Entry.Path));
                if (!done) continue;
                foreach (var r in rows)
                    if (r.Out.Entry != null && r.Out.State != EpisodeState.Restored && MainForm.SamePath(r.Out.Entry.Path, x.Entry.Path)) { r.Out.Entry.Restored = true; MarkRestored(r, x); }
            }
            triedRuns.Add(target.Dir);
            RefreshRestorable();
            SetStatus("Återställt: " + ok + (ok == 1 ? " fil." : " filer.") + (problems.Count > 0 ? "  " + problems.Count + " kunde inte återställas." : "") +
                (restorable != null ? "  En äldre säsongssynk (" + Describe(restorable) + ") går också att återställa med Återställ…." : ""), problems.Count == 0 && ok > 0);
            UpdateButtons();
            if (problems.Count > 0)
                MessageBox.Show(this, (problems.Count == 1 ? "En fil återställdes inte:" : problems.Count + " filer återställdes inte:") + "\n\n" +
                    string.Join("\n", problems.Take(10).Select(p => "•  " + p)) + (problems.Count > 10 ? "\n… och " + (problems.Count - 10) + " till (se loggen)." : ""),
                    MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------ stänga
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                DialogResult = DialogResult.None;
                SetStatus("Säsongssynken pågår. Klicka Avbryt först om du vill stänga.", false);
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
