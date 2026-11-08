// Text Sync - användargränssnitt
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

namespace TextSync
{
    public class MainForm : Form
    {
        // Högkontrastläge i Windows: använd systemets färger i stället för egna
        public static readonly bool HC = SystemInformation.HighContrast;
        static readonly Color Accent = HC ? SystemColors.Highlight : Color.FromArgb(0, 103, 192);
        static readonly Color Bg = HC ? SystemColors.Control : Color.FromArgb(243, 243, 243);
        static readonly Color Card = HC ? SystemColors.Window : Color.White;
        static readonly Color Muted = HC ? SystemColors.WindowText : Color.FromArgb(96, 96, 96);
        static readonly Color ErrColor = HC ? SystemColors.WindowText : Color.Firebrick;
        static readonly Color OkColor = HC ? SystemColors.WindowText : Color.FromArgb(16, 110, 40);
        static readonly Color RedRow = HC ? SystemColors.GrayText : Color.FromArgb(170, 60, 60);
        static readonly Color TitleColor = HC ? SystemColors.WindowText : Color.FromArgb(28, 28, 28);
        public const string BackupFolder = "Text Sync original";
        static readonly string Caption = "Text Sync " + Program.Version;
        readonly Stack<List<Cue>> undo = new Stack<List<Cue>>(); // för att ångra ändringar av rader
        const double MatchThreshold = 0.95;   // andel rader som ska ha referenstext samtidigt
        bool[] rowMatched;
        LinkLabel undoLink;

        TextBox refBox, tgtBox, langBox;
        ComboBox trackCombo, modeCombo, encCombo;
        Label refInfo, tgtInfo, statusLabel, resultLabel, trackLabel;
        LinkLabel openLink, helpLink;
        CheckBox fpsCheck;
        Button syncBtn, saveBtn, muxBtn, cancelBtn, refBtn, tgtBtn;
        ProgressBar progress;
        ListView list;
        TimelinePanel timeline;
        NumericUpDown extraNum;
        TableLayoutPanel root;

        string mkvPath;                // referens-MKV (filen hålls inte öppen)
        List<Cue> refSrtCues;          // om referensen är en SRT
        List<Cue> tgtCues;
        List<Cue> refCuesUsed;         // referens som användes vid senaste synk
        SyncResult result;
        List<Cue> synced;              // result + extra justering
        readonly Dictionary<string, List<Cue>> trackCache = new Dictionary<string, List<Cue>>();
        string refLoadedPath, tgtLoadedPath, lastOutput;
        bool busy, unsaved, encReset;
        CancellationTokenSource cts;
        Process muxProc;
        string muxPart, muxTmp;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Text Sync " + Program.Version;
            Font = new Font("Segoe UI", 9.5F);
            BackColor = Bg;
            MinimumSize = new Size(940, 600);
            Size = new Size(1080, 800);
            StartPosition = FormStartPosition.Manual;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) RouteFiles(f, false); };

            root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16, 12, 16, 18) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // rubrik
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // referens
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // mål
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // inställningar
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // resultat
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // förhandsvisning
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // spara
            Controls.Add(root);

            // --- rubrik
            var head = new Panel { Dock = DockStyle.Top, Height = 56, Margin = new Padding(0, 0, 0, 6) };
            var title = new Label { Text = "Text Sync", Font = new Font("Segoe UI Semibold", 20F), AutoSize = true, Location = new Point(0, 0), ForeColor = TitleColor };
            var sub = new Label { Text = "Synka en undertext mot en annan undertext (annat språk) som redan går i takt med filmen.", AutoSize = true, Location = new Point(3, 36), ForeColor = Muted };
            var ver = new Label { Text = "version " + Program.Version, AutoSize = true, ForeColor = Muted, Location = new Point(title.PreferredWidth + 8, 14) };
            head.Controls.Add(title); head.Controls.Add(ver); head.Controls.Add(sub);
            head.Layout += (s, e) => { ver.Left = title.Right + 6; ver.Top = title.Bottom - ver.Height - (int)(title.Height * 0.12); };
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Z && !(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown) && undo.Count > 0)
                { UndoChange(); e.Handled = true; }
                else if (e.KeyCode == Keys.F3 && synced != null) { NextUnmatched(); e.Handled = true; }
            };
            root.Controls.Add(head, 0, 0);

            // --- 1. referens
            refBox = MakePathBox(); refBox.Leave += (s, e) => { if (!busy && refBox.Text != (mkvPath ?? refLoadedPath)) LoadReference(refBox.Text); };
            refBtn = MakeButton("Bläddra…", false);
            refBtn.Click += (s, e) => BrowseRef();
            trackLabel = new Label { Text = "Spår:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Visible = false };
            trackCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, Visible = false, Margin = new Padding(0, 3, 12, 0) };
            trackCombo.SelectedIndexChanged += (s, e) => { if (!busy) ClearResult(); };
            refInfo = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 7, 0, 0), Text = "Dra in en MKV- eller SRT-fil hit, eller klicka Bläddra." };
            var trow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            trow.Controls.Add(trackLabel); trow.Controls.Add(trackCombo); trow.Controls.Add(refInfo);
            root.Controls.Add(MakeCard("1.  Referens – undertext som redan är i synk (MKV-fil med textspår, eller SRT)", refBox, refBtn, trow), 0, 1);

            // --- 2. mål
            tgtBox = MakePathBox(); tgtBox.Leave += (s, e) => { if (!busy && tgtBox.Text != tgtLoadedPath) LoadTarget(tgtBox.Text); };
            tgtBtn = MakeButton("Bläddra…", false);
            tgtBtn.Click += (s, e) => BrowseTarget();
            tgtInfo = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 7, 0, 0), Text = "Dra in SRT-filen hit." };
            // teckenkodning: automatisk räcker nästan alltid; manuellt val om å/ä/ö eller t.ex. kyrilliska tecken blir fel
            encCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(0, 3, 0, 0), AccessibleName = "Teckenkodning" };
            foreach (var en in Srt.Encodings) encCombo.Items.Add(en[1]);
            encCombo.SelectedIndex = 0;
            encCombo.SelectedIndexChanged += (s, e) => { if (!busy && !encReset && !string.IsNullOrEmpty(tgtLoadedPath)) LoadTarget(tgtLoadedPath); };
            var tinfo = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, RowCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0) };
            tinfo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tinfo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tinfo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tinfo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tgtInfo.Anchor = AnchorStyles.Left; tgtInfo.Margin = new Padding(0);
            tinfo.Controls.Add(tgtInfo, 0, 0);
            tinfo.Controls.Add(new Label { Text = "&Teckenkodning:", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 6, 0) }, 2, 0);
            tinfo.Controls.Add(encCombo, 3, 0);
            root.Controls.Add(MakeCard("2.  Undertext att synka (SRT som inte går i takt)", tgtBox, tgtBtn, tinfo), 0, 2);

            // --- 3. inställningar + synka
            var opts = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
            opts.Controls.Add(new Label { Text = "Metod:", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
            modeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, Margin = new Padding(0, 5, 16, 0) };
            modeCombo.Items.AddRange(new object[] {
                "Smart (rekommenderas)",
                "Enkel – bara förskjutning/bildfrekvens",
                "Avancerad – även klipp och reklampauser" });
            modeCombo.SelectedIndex = 0;
            opts.Controls.Add(modeCombo);
            fpsCheck = new CheckBox { Text = "Upptäck bildfrekvensfel (25 ↔ 23,976 fps m.fl.)", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 16, 0) };
            opts.Controls.Add(fpsCheck);
            syncBtn = MakeButton("&Synka", true);
            syncBtn.Width = 120; syncBtn.Height = 34; syncBtn.Margin = new Padding(0, 2, 12, 0);
            syncBtn.Click += async (s, e) => await DoSync();
            opts.Controls.Add(syncBtn);
            progress = new ProgressBar { Width = 160, Height = 10, Margin = new Padding(0, 14, 10, 0), Visible = false };
            opts.Controls.Add(progress);
            cancelBtn = MakeButton("&Avbryt", false); cancelBtn.Visible = false; cancelBtn.Margin = new Padding(0, 4, 10, 0);
            cancelBtn.Click += (s, e) => CancelWork();
            opts.Controls.Add(cancelBtn);
            statusLabel = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 9, 6, 0) };
            opts.Controls.Add(statusLabel);
            openLink = new LinkLabel { Text = "Visa i mappen", AutoSize = true, Margin = new Padding(0, 9, 0, 0), Visible = false, LinkColor = Accent };
            openLink.LinkClicked += (s, e) => ShowInFolder(lastOutput);
            opts.Controls.Add(openLink);
            undoLink = new LinkLabel { Text = "Ångra", AutoSize = true, Margin = new Padding(0, 9, 0, 0), Visible = false, LinkColor = Accent };
            undoLink.LinkClicked += (s, e) => UndoChange();
            opts.Controls.Add(undoLink);
            root.Controls.Add(opts, 0, 3);

            // --- resultat
            resultLabel = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 2, 0, 6), BackColor = Card, Visible = false, MaximumSize = new Size(4000, 0) };
            root.Controls.Add(resultLabel, 0, 4);
            root.Resize += (s, e) => { if (resultLabel.Visible) resultLabel.MaximumSize = new Size(Math.Max(200, root.ClientSize.Width - 40), 0); };

            // --- förhandsvisning
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Bg };
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false, BorderStyle = BorderStyle.None };
            float k = 1f;
            using (var gr = CreateGraphics()) k = gr.DpiX / 96f;
            list.Columns.Add("#", (int)(50 * k), HorizontalAlignment.Right);
            list.Columns.Add("Start", (int)(95 * k));
            list.Columns.Add("Slut", (int)(95 * k));
            list.Columns.Add("Synkad text", (int)(380 * k));
            list.Columns.Add("Referens (samtidigt)", (int)(380 * k));
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedIndices.Count > 0 && synced != null)
                {
                    var c = synced[list.SelectedIndices[0]];
                    timeline.Center = (c.Start + c.End) / 2; timeline.Highlight = c; timeline.Invalidate();
                }
            };
            // redigera rader: dubbelklick/Enter = ändra, Insert = lägg till, Delete = ta bort, F3 = nästa röda rad, Ctrl+Z = ångra
            list.MultiSelect = true;
            list.DoubleClick += (s, e) => EditSelected();
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Return) { EditSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Insert) { AddRow(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in list.Items) it.Selected = true; e.Handled = true; }
            };
            var menu = new ContextMenuStrip();
            var miEdit = new ToolStripMenuItem("Ändra rad…", null, (s, e) => EditSelected()) { ShortcutKeyDisplayString = "Enter" };
            var miAdd = new ToolStripMenuItem("Lägg till rad…", null, (s, e) => AddRow()) { ShortcutKeyDisplayString = "Insert" };
            var miDel = new ToolStripMenuItem("Ta bort markerade rader", null, (s, e) => DeleteSelected()) { ShortcutKeyDisplayString = "Delete" };
            var miNext = new ToolStripMenuItem("Gå till nästa röda rad", null, (s, e) => NextUnmatched()) { ShortcutKeyDisplayString = "F3" };
            var miUndo = new ToolStripMenuItem("Ångra", null, (s, e) => UndoChange()) { ShortcutKeyDisplayString = "Ctrl+Z" };
            menu.Items.AddRange(new ToolStripItem[] { miEdit, miAdd, miDel, new ToolStripSeparator(), miNext, miUndo });
            menu.Opening += (s, e) =>
            {
                if (synced == null || busy) { e.Cancel = true; return; }
                int n = list.SelectedIndices.Count;
                miEdit.Enabled = n == 1;
                miDel.Enabled = n > 0;
                miDel.Text = n > 1 ? "Ta bort " + n + " markerade rader" : "Ta bort markerad rad";
                miNext.Enabled = rowMatched != null && rowMatched.Any(m => !m);
                miUndo.Enabled = undo.Count > 0;
            };
            list.ContextMenuStrip = menu;
            split.Panel1.Controls.Add(list);
            timeline = new TimelinePanel { Dock = DockStyle.Fill };
            split.Panel2.Controls.Add(timeline);
            split.FixedPanel = FixedPanel.Panel2;
            root.Controls.Add(split, 0, 5);
            Shown += (s, e) => BeginInvoke(new Action(() =>
            {
                try { split.SplitterDistance = Math.Max(80, split.Height - (int)(105 * k)); } catch { }
            }));

            // --- spara
            var ctrls = new List<Control>();
            ctrls.Add(new Label { Text = "Extra justering (ms):", AutoSize = true, Margin = new Padding(0, 0, 6, 0) });
            extraNum = new NumericUpDown { Minimum = -600000, Maximum = 600000, Increment = 100, Width = 90, Margin = new Padding(0, 0, 4, 0) };
            extraNum.ValueChanged += (s, e) => { RefreshPreview(); if (result != null) unsaved = true; };
            ctrls.Add(extraNum);
            ctrls.Add(new Label { Text = "(+ = senare, − = tidigare)", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 20, 0) });
            ctrls.Add(new Label { Text = "Språkkod:", AutoSize = true, Margin = new Padding(0, 0, 6, 0) });
            langBox = new TextBox { Text = "sv", Width = 50, Margin = new Padding(0, 0, 20, 0) };
            ctrls.Add(langBox);
            saveBtn = MakeButton("&Spara SRT…", true); saveBtn.Enabled = false; saveBtn.Width = 130; saveBtn.Height = 34; saveBtn.Margin = new Padding(0, 0, 6, 0);
            saveBtn.Click += (s, e) => SaveSrt();
            ctrls.Add(saveBtn);
            muxBtn = MakeButton("Lägg in i &MKV…", false); muxBtn.Enabled = false; muxBtn.Width = 140; muxBtn.Height = 34;
            muxBtn.Click += async (s, e) => await MuxIntoMkv();
            ctrls.Add(muxBtn);
            // tabell (FlowLayoutPanel placerar NumericUpDown fel vid hög DPI)
            var brow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = ctrls.Count + 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            for (int i = 0; i < ctrls.Count; i++)
            {
                brow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                ctrls[i].Anchor = AnchorStyles.Left;
                brow.Controls.Add(ctrls[i], i, 0);
            }
            brow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            brow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            helpLink = new LinkLabel { Text = "Hjälp", AutoSize = true, Anchor = AnchorStyles.Right, LinkColor = Accent };
            helpLink.LinkClicked += (s, e) => ShowHelp();
            brow.Controls.Add(helpLink, ctrls.Count + 1, 0);
            root.Controls.Add(brow, 0, 6);

            ResumeLayout(true);
        }

        // Fönstret får aldrig bli större än skärmen (t.ex. laptop med 150 % skalning)
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width), Math.Min(MinimumSize.Height, wa.Height));
            Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        }

        // ------------------------------------------------------------ UI-hjälp
        TableLayoutPanel MakeCard(string caption, TextBox box, Button btn, Control info)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 3,
                BackColor = Card, Padding = new Padding(12, 8, 12, 10), Margin = new Padding(0, 0, 0, 8)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var cap = new Label { Text = caption, AutoSize = true, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0, 0, 0, 6) };
            t.Controls.Add(cap, 0, 0); t.SetColumnSpan(cap, 2);
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            t.Controls.Add(box, 0, 1); t.Controls.Add(btn, 1, 1);
            t.Controls.Add(info, 0, 2); t.SetColumnSpan(info, 2);
            return t;
        }

        TextBox MakePathBox()
        {
            var b = new TextBox { AllowDrop = true, Margin = new Padding(0, 4, 8, 0) };
            b.DragEnter += OnDragEnter;
            b.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                if (files.Length > 1) { RouteFiles(files, false); return; }
                if (s == refBox) LoadReference(files[0]); else LoadTarget(files[0]);
            };
            return b;
        }

        Button MakeButton(string text, bool primary)
        {
            var b = new Button { Text = text, AutoSize = false, Width = 100, Height = 30, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            if (HC) { b.FlatStyle = FlatStyle.System; b.UseVisualStyleBackColor = true; return b; }
            b.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                b.BackColor = Accent; b.ForeColor = Color.White; b.FlatAppearance.BorderColor = Accent;
                b.Font = new Font("Segoe UI Semibold", 9.5F);
                b.EnabledChanged += (s, e) => { b.BackColor = b.Enabled ? Accent : Color.FromArgb(200, 200, 200); };
            }
            else
            {
                b.BackColor = Color.White; b.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            }
            return b;
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        static bool IsMkv(string f) { string x = Path.GetExtension(f).ToLowerInvariant(); return x == ".mkv" || x == ".mks" || x == ".mka"; }
        static bool IsSrt(string f) { return Path.GetExtension(f).ToLowerInvariant() == ".srt"; }

        static readonly Dictionary<string, string> Iso3 = new Dictionary<string, string> {
            {"swe","sv"},{"eng","en"},{"nor","no"},{"nob","no"},{"dan","da"},{"fin","fi"},{"ger","de"},{"deu","de"},
            {"fre","fr"},{"fra","fr"},{"spa","es"},{"ita","it"},{"dut","nl"},{"nld","nl"},{"pol","pl"} };
        static string Norm(string l)
        {
            if (string.IsNullOrEmpty(l)) return null;
            l = l.ToLowerInvariant(); string two;
            return Iso3.TryGetValue(l, out two) ? two : l;
        }

        // Fördela filer: MKV → referens. Två SRT → den med "rätt" språkkod (t.ex. .sv.srt) synkas,
        // den andra blir referens. Oklart → säg det tydligt.
        // Bestäm vilken fil som blir referens och vilken som synkas. ref/target = null betyder "rör inte".
        public static void PlanRoute(string[] files, string wantLang, out string refFile, out string targetFile, out string note)
        {
            refFile = null; targetFile = null; note = null;
            var mkvs = files.Where(IsMkv).ToList();
            var srts = files.Where(IsSrt).ToList();
            if (mkvs.Count == 0 && srts.Count == 0) { note = "Bara .mkv- och .srt-filer stöds."; return; }
            string want = Norm(wantLang) ?? "sv";
            if (mkvs.Count > 0) refFile = mkvs[0];
            if (srts.Count >= 2 && mkvs.Count == 0)
            {
                var two = srts.Take(2).ToList();
                int ti = two.FindIndex(f => Norm(GuessLang(f)) == want);
                if (ti < 0)
                {
                    // ingen har rätt språkkod: har en fil en annan känd språkkod blir den referens
                    int other = two.FindIndex(f => GuessLang(f) != null);
                    if (other >= 0) ti = 1 - other;
                    else
                    {
                        ti = 1;
                        note = "Kontrollera fälten: språket gick inte att se på filnamnen, så \"" + Path.GetFileName(two[0]) + "\" blev referens.";
                    }
                }
                refFile = two[1 - ti];
                targetFile = two[ti];
            }
            else if (srts.Count >= 1)
                targetFile = srts.FirstOrDefault(f => Norm(GuessLang(f)) == want) ?? srts[0];
        }

        public async void RouteFiles(string[] files, bool autoSync)
        {
            if (busy || files == null || files.Length == 0) return;
            string rf, tf, note;
            PlanRoute(files, langBox.Text.Trim(), out rf, out tf, out note);
            if (rf != null) LoadReference(rf);
            if (tf != null) LoadTarget(tf);
            if (note != null) { statusLabel.Text = note; statusLabel.ForeColor = ErrColor; }
            if (autoSync && syncBtn.Enabled && files.Length >= 2) await DoSync();
        }

        static string Fmt(double v, string f) { return v.ToString(f, CultureInfo.GetCultureInfo("sv-SE")); }

        static string FmtOffset(double ms)
        {
            string sign = ms >= 0 ? "+" : "−";
            return sign + Fmt(Math.Abs(ms) / 1000.0, "0.000") + " s";
        }

        static string Friendly(Exception ex)
        {
            if (ex is FileNotFoundException) return "Filen hittades inte.";
            if (ex is UnauthorizedAccessException) return "Text Sync har inte behörighet till filen eller mappen.";
            if (ex is IOException) return "Filen kunde inte läsas eller skrivas. Den kan vara öppen i ett annat program, eller så är disken full.";
            if (ex is InvalidDataException) return ex.Message;
            if (ex is OutOfMemoryException) return "Filen är för stor eller trasig.";
            return ex.Message;
        }

        static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        static void ShowInFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        void SetStatus(string text, bool ok)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = ok ? OkColor : Muted;
        }

        // ------------------------------------------------------------ inläsning
        void BrowseRef()
        {
            using (var d = new OpenFileDialog { Title = "Välj referens (MKV eller SRT)", Filter = "Video/undertext (*.mkv;*.mks;*.srt)|*.mkv;*.mks;*.mka;*.srt|Alla filer (*.*)|*.*" })
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
            refBox.Text = path; refLoadedPath = path;
            mkvPath = null; refSrtCues = null;
            trackCombo.Items.Clear(); trackCombo.Visible = trackLabel.Visible = false;
            ClearResult();
            if (path.Length == 0) { refInfo.Text = ""; return; }
            if (!File.Exists(path)) { refInfo.Text = "Filen finns inte."; refInfo.ForeColor = ErrColor; UpdateButtons(); return; }
            refInfo.ForeColor = Muted;
            try
            {
                if (!IsMkv(path) && LooksLikeText(path)) // MKV börjar alltid med byte 0x1A
                {
                    refSrtCues = Srt.Load(path);
                    if (refSrtCues.Count == 0) { refSrtCues = null; throw new InvalidDataException("Hittade inga repliker – är det en SRT-fil?"); }
                    refInfo.Text = refSrtCues.Count + " repliker, " + Duration(refSrtCues);
                }
                else
                {
                    List<MkvTrack> tracks;
                    using (var f = new MkvFile(path)) tracks = f.Tracks; // stäng direkt så filmen inte låses
                    mkvPath = path;
                    var subs = tracks.Where(t => t.IsSubtitle).ToList();
                    var text = subs.Where(t => t.IsText).ToList();
                    foreach (var t in text) trackCombo.Items.Add(t);
                    if (text.Count == 0)
                    {
                        refInfo.ForeColor = ErrColor;
                        refInfo.Text = subs.Count > 0
                            ? "MKV-filen har bara bildbaserade undertexter (PGS/VobSub) som inte kan läsas. Använd en SRT-fil som referens."
                            : "MKV-filen har inga undertextspår. Använd en SRT-fil som referens.";
                    }
                    else
                    {
                        trackCombo.Visible = trackLabel.Visible = true;
                        // förval: engelska (eller okänt språk), inte "forced" och inte SDH/hörselskadade
                        Func<MkvTrack, bool> plain = t => !t.Forced && !Regex.IsMatch(t.Name, @"\b(SDH|CC|HI|forced)\b", RegexOptions.IgnoreCase);
                        int pick = text.FindIndex(t => plain(t) && t.Language.StartsWith("en"));
                        if (pick < 0) pick = text.FindIndex(t => plain(t) && t.Language == "und");
                        if (pick < 0) pick = text.FindIndex(t => t.Language.StartsWith("en") && !t.Forced);
                        if (pick < 0) pick = text.FindIndex(t => !t.Forced);
                        trackCombo.SelectedIndex = Math.Max(0, pick);
                        refInfo.Text = text.Count + " textspår hittades";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Kunde inte läsa referens " + path, ex);
                refInfo.ForeColor = ErrColor;
                refInfo.Text = "Kunde inte läsa filen: " + Friendly(ex);
                mkvPath = null; refSrtCues = null;
            }
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
            tgtBox.Text = path; tgtLoadedPath = path;
            tgtCues = null; ClearResult();
            if (path.Length == 0) { tgtInfo.Text = ""; return; }
            try
            {
                if (IsMkv(path)) throw new InvalidDataException("Här ska undertexten (SRT) som ska synkas ligga. Lägg MKV-filen i fält 1.");
                tgtCues = Srt.Load(path, (int)Srt.Encodings[encCombo.SelectedIndex][0]);
                tgtInfo.ForeColor = Muted;
                tgtInfo.Text = tgtCues.Count + " repliker, " + Duration(tgtCues);
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
            if (resultLabel != null) resultLabel.Visible = false;
            if (openLink != null) openLink.Visible = false;
            undo.Clear(); rowMatched = null;
            if (undoLink != null) undoLink.Visible = false;
            if (timeline != null) { timeline.Ref = null; timeline.Orig = null; timeline.Synced = null; timeline.Highlight = null; timeline.Invalidate(); }
            UpdateButtons();
        }

        // ------------------------------------------------------------ redigera rader (med ångra)
        void SelectRow(int i)
        {
            list.SelectedIndices.Clear();
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
            var idx = list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
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
            int i = list.SelectedIndices[0];
            var shown = synced[i];
            double extra = (double)extraNum.Value;
            using (var d = new CueDialog("Ändra rad " + (i + 1), shown.Start, shown.End, shown.Text))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var nc = new Cue(d.StartMs - extra, d.EndMs - extra, d.CueText);
                var list2 = new List<Cue>(result.Output);
                list2[i] = nc;
                Changed(list2, nc, "Rad " + (i + 1) + " ändrad.", "Ändrade rad " + (i + 1));
            }
        }

        void AddRow()
        {
            if (busy || result == null) return;
            double extra = (double)extraNum.Value;
            double start = 0;
            if (list.SelectedIndices.Count > 0) start = synced[list.SelectedIndices[list.SelectedIndices.Count - 1]].End + 200;
            else if (synced.Count > 0) start = synced[synced.Count - 1].End + 200;
            using (var d = new CueDialog("Lägg till rad", start, start + 2000, ""))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var nc = new Cue(d.StartMs - extra, d.EndMs - extra, d.CueText);
                var list2 = new List<Cue>(result.Output); list2.Add(nc);
                Changed(list2, nc, "Rad tillagd.", "Lade till rad vid " + Srt.FormatTime(d.StartMs));
            }
        }

        void UndoChange()
        {
            if (busy || result == null || undo.Count == 0) return;
            result.Output = undo.Pop();
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
            int from = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] + 1 : 0;
            for (int k = 0; k < rowMatched.Length; k++)
            {
                int i = (from + k) % rowMatched.Length;
                if (!rowMatched[i]) { SelectRow(i); return; }
            }
            SetStatus("Alla rader har referenstext samtidigt.", true);
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

        void UpdateButtons()
        {
            if (syncBtn == null) return;
            bool haveRef = refSrtCues != null || (mkvPath != null && trackCombo.SelectedItem != null);
            syncBtn.Enabled = !busy && haveRef && tgtCues != null;
            saveBtn.Enabled = !busy && synced != null;
            muxBtn.Enabled = !busy && synced != null;
            foreach (Control c in new Control[] { refBox, tgtBox, refBtn, tgtBtn, trackCombo, modeCombo, fpsCheck, extraNum, langBox, encCombo })
                if (c != null) c.Enabled = !busy;
        }

        void SetBusy(bool b, string status)
        {
            busy = b;
            progress.Visible = b; progress.Value = 0;
            cancelBtn.Visible = b; cancelBtn.Enabled = b;
            if (b) { openLink.Visible = false; undoLink.Visible = false; }
            statusLabel.ForeColor = Muted;
            statusLabel.Text = status ?? "";
            UseWaitCursor = b;
            UpdateButtons();
        }

        void Progress(int p, string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<int, string>(Progress), p, s); return; }
            progress.Value = Math.Max(0, Math.Min(100, p));
            if (s != null) statusLabel.Text = s;
        }

        void CancelWork()
        {
            if (cts != null) cts.Cancel();
            cancelBtn.Enabled = false;
            statusLabel.Text = "Avbryter…";
            var p = muxProc;
            if (p != null) { try { if (!p.HasExited) p.Kill(); } catch { } }
        }

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
                if (refc == null)
                {
                    var track = (MkvTrack)trackCombo.SelectedItem;
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
                }
                Action<int, string> prog = (p, s) => { token.ThrowIfCancellationRequested(); Progress(p, s); };
                var r = await Task.Run(() => SyncEngine.Sync(refc, tgt, opt, prog));
                if (truncated) r.Notes.Insert(0, "MKV-filen verkar vara ofullständig – bara en del av referensen kunde läsas.");
                refCuesUsed = refc;
                result = r;
                undo.Clear();
                unsaved = true;
                extraNum.Value = 0;
                timeline.Highlight = null;
                ShowResult();
                RefreshPreview();
                Log.Info("Synk: ref=" + (mkvPath ?? refLoadedPath) + (mkvPath != null ? " spår " + ((MkvTrack)trackCombo.SelectedItem).Number : "") +
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
                sb.Append("     Rader med referenstext samtidigt: " + ok + " av " + n + " (" + Fmt(Math.Floor(1000.0 * ok / n) / 10, "0.#") + " %)");
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
            sb.Append("Redigera i listan: dubbelklicka för att ändra text och tid, Insert lägger till, Delete tar bort, Ctrl+Z ångrar.");
            resultLabel.Text = sb.ToString().TrimEnd();
            resultLabel.ForeColor = good ? OkColor : ErrColor;
            resultLabel.MaximumSize = new Size(Math.Max(200, root.ClientSize.Width - 40), 0);
            resultLabel.Visible = true;
        }

        void RefreshPreview()
        {
            if (result == null) return;
            int sel = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : -1;
            synced = SyncEngine.ApplyExtra(result.Output, (double)extraNum.Value);
            var refc = refCuesUsed;
            list.BeginUpdate();
            list.Items.Clear();
            int ri = 0, lost = 0;
            var items = new List<ListViewItem>(synced.Count);
            rowMatched = new bool[synced.Count];
            for (int i = 0; i < synced.Count; i++)
            {
                var c = synced[i];
                if (c.End <= 0) lost++;
                while (ri < refc.Count && refc[ri].End < c.Start) ri++;
                string best = ""; double bestOv = 0;
                for (int j = Math.Max(0, ri - 2); j < refc.Count && refc[j].Start < c.End; j++)
                {
                    double ov = Math.Min(c.End, refc[j].End) - Math.Max(c.Start, refc[j].Start);
                    if (ov > bestOv) { bestOv = ov; best = refc[j].Text; }
                }
                var it = new ListViewItem(new[] { (i + 1).ToString(), Srt.FormatTime(c.Start), Srt.FormatTime(c.End),
                    c.Text.Replace("\n", " / "), bestOv > 0 ? best.Replace("\n", " / ") : "— (ingen referenstext samtidigt)" });
                if (bestOv <= 0) it.ForeColor = RedRow;
                rowMatched[i] = bestOv > 0;
                items.Add(it);
            }
            list.Items.AddRange(items.ToArray());
            if (sel >= 0 && sel < list.Items.Count) { list.Items[sel].Selected = true; list.EnsureVisible(sel); }
            list.EndUpdate();

            timeline.Ref = refc; timeline.Orig = tgtCues; timeline.Synced = synced;
            if (sel >= 0 && sel < synced.Count) timeline.Highlight = synced[sel];
            else if (synced.Count > 0) { timeline.Highlight = null; timeline.Center = synced[0].Start + 20000; }
            timeline.Invalidate();
            if (lost > 0) SetStatus(lost + " repliker hamnar före filmens början och tas bort när du sparar.", false);
            ShowResult();
            UpdateButtons();
        }

        // ------------------------------------------------------------ spara
        bool TryGetLang(out string lang)
        {
            lang = langBox.Text.Trim().ToLowerInvariant();
            if (Regex.IsMatch(lang, @"^[a-z]{2,3}(-[a-z0-9]{2,8})*$")) return true;
            MessageBox.Show(this, "Skriv språkkoden med 2–3 bokstäver, t.ex. sv, en eller no.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            langBox.Focus(); langBox.SelectAll();
            return false;
        }

        string DefaultSrtPath(string lang)
        {
            if (mkvPath != null)
                return Path.Combine(Path.GetDirectoryName(mkvPath), Path.GetFileNameWithoutExtension(mkvPath) + "." + lang + ".srt");
            return Path.Combine(Path.GetDirectoryName(tgtLoadedPath), Path.GetFileNameWithoutExtension(tgtLoadedPath) + ".synkad.srt");
        }

        // Skriv via temporär fil så att en krasch aldrig lämnar en halvskriven undertext
        static void WriteSafely(string path, string text)
        {
            string tmp = path + ".textsync-tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(true));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        // Skriver man över originalet (den osynkade filen) sparas först en kopia i en undermapp,
        // så att spelare som VLC/Plex inte visar den som ett extra spår. En befintlig kopia skrivs aldrig över.
        // Returnerar sökvägen till kopian, eller null om ingen behövdes.
        public static string SaveWithBackup(string path, string originalPath, List<Cue> cues)
        {
            string backup = null;
            if (File.Exists(path) && SamePath(path, originalPath))
            {
                string bdir = Path.Combine(Path.GetDirectoryName(path), BackupFolder);
                backup = Path.Combine(bdir, Path.GetFileName(path));
                Directory.CreateDirectory(bdir);
                if (!File.Exists(backup)) File.Copy(path, backup);
            }
            WriteSafely(path, Srt.ToSrt(cues));
            return backup;
        }

        void SaveSrt()
        {
            if (synced == null) return;
            string lang;
            if (!TryGetLang(out lang)) return;
            if (!ConfirmLowMatch()) return;
            string def = DefaultSrtPath(lang);
            using (var d = new SaveFileDialog { Title = "Spara synkad undertext", Filter = "SRT-undertext (*.srt)|*.srt", FileName = Path.GetFileName(def), InitialDirectory = Path.GetDirectoryName(def), OverwritePrompt = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string path = d.FileName;
                try
                {
                    string backup = SaveWithBackup(path, tgtLoadedPath, synced);
                    string backupNote = backup != null ? "  Originalet finns kvar i mappen \"" + BackupFolder + "\"." : "";
                    if (backup != null) Log.Info("Säkerhetskopia av original: " + backup);
                    unsaved = false;
                    lastOutput = path;
                    string auto = "";
                    if (mkvPath != null && SamePath(Path.GetDirectoryName(path), Path.GetDirectoryName(mkvPath))
                        && Path.GetFileName(path).StartsWith(Path.GetFileNameWithoutExtension(mkvPath) + ".", StringComparison.OrdinalIgnoreCase))
                        auto = "  Spelare som VLC, Plex och Kodi hittar den automatiskt.";
                    SetStatus("✔ Sparad: " + Path.GetFileName(path) + "." + auto + backupNote, true);
                    openLink.Visible = true; undoLink.Visible = false;
                    Log.Info("Sparad: " + path);
                }
                catch (Exception ex)
                {
                    Log.Error("Kunde inte spara " + path, ex);
                    MessageBox.Show(this, "Kunde inte spara: " + Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static string FindMkvMerge()
        {
            var cands = new List<string>();
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (dir.Trim().Length > 0) cands.Add(Path.Combine(dir.Trim().Trim('"'), "mkvmerge.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MKVToolNix", "mkvmerge.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MKVToolNix", "mkvmerge.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MKVToolNix", "mkvmerge.exe"));
            foreach (var c in cands) { try { if (File.Exists(c)) return c; } catch { } }
            return null;
        }

        static readonly Dictionary<string, string> LangNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"sv","Svenska"},{"swe","Svenska"},{"en","English"},{"eng","English"},{"no","Norsk"},{"nor","Norsk"},
            {"da","Dansk"},{"dan","Dansk"},{"fi","Suomi"},{"fin","Suomi"},{"de","Deutsch"},{"ger","Deutsch"},{"deu","Deutsch"},
            {"fr","Français"},{"fre","Français"},{"fra","Français"},{"es","Español"},{"spa","Español"} };

        // Argument till mkvmerge: den nya texten blir standardspår, befintliga undertextspår behålls men är inte standard.
        // mkvmerge numrerar spåren 0,1,2… i samma ordning som de står i filen.
        public static string BuildMuxArgs(List<MkvTrack> srcTracks, string src, string outFile, string lang, string srtFile)
        {
            string name; if (!LangNames.TryGetValue(lang, out name)) name = lang;
            var args = new StringBuilder("--gui-mode -o " + Q(outFile) + " ");
            for (int i = 0; i < srcTracks.Count; i++)
                if (srcTracks[i].IsSubtitle) args.Append("--default-track-flag " + i + ":no ");
            args.Append(Q(src) + " --language 0:" + lang + " --track-name " + Q("0:" + name) + " --default-track-flag 0:yes --sub-charset 0:UTF-8 ");
            args.Append(Q(srtFile));
            return args.ToString();
        }

        public static string FindMkvMergePublic() { return FindMkvMerge(); }

        static string Gb(long b) { return Fmt(b / 1073741824.0, "0.0") + " GB"; }

        async Task MuxIntoMkv()
        {
            if (synced == null || busy) return;
            string lang;
            if (!TryGetLang(out lang)) return;
            if (!ConfirmLowMatch()) return;
            string exe = FindMkvMerge();
            if (exe == null)
            {
                var a = MessageBox.Show(this,
                    "För att lägga in undertexten i själva MKV-filen behövs det kostnadsfria programmet MKVToolNix.\n\n" +
                    "Vill du öppna nedladdningssidan?\n\n(Enklare alternativ: \"Spara SRT…\" bredvid filmen med samma namn – de flesta spelare hittar den då automatiskt.)",
                    Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (a == DialogResult.Yes) { try { Process.Start("https://mkvtoolnix.download/downloads.html#windows"); } catch { } }
                return;
            }
            string src = mkvPath;
            if (src == null)
            {
                using (var d = new OpenFileDialog { Title = "Välj MKV-filen som undertexten ska läggas in i", Filter = "MKV (*.mkv)|*.mkv" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    src = d.FileName;
                }
            }
            string outPath;
            using (var d = new SaveFileDialog { Title = "Spara ny MKV-fil (originalet ändras inte)", Filter = "MKV (*.mkv)|*.mkv", InitialDirectory = Path.GetDirectoryName(src), FileName = Path.GetFileNameWithoutExtension(src) + " (Text Sync).mkv", OverwritePrompt = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                outPath = d.FileName;
            }
            if (SamePath(outPath, src))
            {
                MessageBox.Show(this, "Välj ett annat filnamn än originalet – originalfilmen ska inte skrivas över.", Caption); return;
            }

            List<MkvTrack> srcTracks;
            long srcSize;
            try
            {
                using (var f = new MkvFile(src)) srcTracks = f.Tracks;
                srcSize = new FileInfo(src).Length;
            }
            catch (Exception ex)
            {
                Log.Error("Kunde inte läsa " + src, ex);
                MessageBox.Show(this, "Kunde inte läsa MKV-filen: " + Friendly(ex), Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // diskutrymme: den nya filen blir ungefär lika stor som originalet
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outPath)));
                long need = srcSize + 200L * 1024 * 1024;
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                {
                    MessageBox.Show(this, "Det finns inte tillräckligt med plats på " + drive.Name + "\n\nBehövs: " + Gb(need) + "\nLedigt: " + Gb(drive.AvailableFreeSpace) +
                        "\n\nVälj en annan disk, eller använd \"Spara SRT…\" som inte tar någon plats.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch { } // t.ex. nätverkssökväg – hoppa över kontrollen

            muxTmp = Path.Combine(Path.GetTempPath(), "TextSync_" + Guid.NewGuid().ToString("N") + ".srt");
            muxPart = outPath + ".part";
            var args = new StringBuilder(BuildMuxArgs(srcTracks, src, muxPart, lang, muxTmp));

            cts = new CancellationTokenSource();
            var token = cts.Token;
            SetBusy(true, "Skapar MKV…");
            var log = new StringBuilder();
            int code = -1;
            bool cancelled = false;
            try
            {
                Srt.Save(muxTmp, synced);
                string argStr = args.ToString();
                Log.Info("mkvmerge " + argStr);
                code = await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo(exe, argStr) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
                    using (var p = Process.Start(psi))
                    {
                        muxProc = p;
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            var m = Regex.Match(line, @"#GUI#progress\s+(\d+)%");
                            if (m.Success) Progress(int.Parse(m.Groups[1].Value), "Skapar MKV… " + m.Groups[1].Value + " %");
                            else if (line.StartsWith("#GUI#error") || line.StartsWith("#GUI#warning")) log.AppendLine(line.Substring(5));
                        }
                        p.WaitForExit();
                        muxProc = null;
                        return p.ExitCode;
                    }
                });
                cancelled = token.IsCancellationRequested;
                if (!cancelled && (code == 0 || code == 1))
                {
                    if (File.Exists(outPath)) File.Delete(outPath);
                    File.Move(muxPart, outPath);
                }
            }
            catch (Exception ex) { log.AppendLine(ex.ToString()); }
            finally
            {
                muxProc = null; cts = null;
                CleanupMux(!File.Exists(outPath) || cancelled || !(code == 0 || code == 1));
                SetBusy(false, "");
            }
            if (cancelled) { SetStatus("Avbrutet – ingen ny fil skapades.", false); return; }
            if (code == 0 || code == 1)
            {
                lastOutput = outPath;
                SetStatus("✔ Klar: " + Path.GetFileName(outPath) + (code == 1 ? " (mkvmerge gav varningar – se loggen)" : ""), true);
                openLink.Visible = true;
                if (code == 1) Log.Info("mkvmerge-varningar:\r\n" + log);
                Log.Info("MKV skapad: " + outPath);
            }
            else
            {
                Log.Error("mkvmerge misslyckades (kod " + code + "):\r\n" + log, null);
                MessageBox.Show(this, "Det gick inte att skapa MKV-filen. Ingen ny fil har sparats och originalet är orört.\n\n" +
                    "Detaljer (från mkvmerge):\n" + Truncate(log.ToString(), 600) + "\n\nMer information finns i loggen (Hjälp → loggmapp).",
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void CleanupMux(bool deletePart)
        {
            try { if (muxTmp != null && File.Exists(muxTmp)) File.Delete(muxTmp); } catch { }
            try { if (deletePart && muxPart != null && File.Exists(muxPart)) File.Delete(muxPart); } catch { }
            muxTmp = null; muxPart = null;
        }

        static string Truncate(string s, int n) { s = (s ?? "").Trim(); return s.Length <= n ? s : s.Substring(0, n) + "…"; }

        static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        void ShowHelp()
        {
            var a = MessageBox.Show(this,
                "Så här gör du:\n" +
                "1. Referens: välj filmen (MKV) – eller en SRT som redan går i takt.\n" +
                "2. Välj undertexten som inte går i takt.\n" +
                "3. Klicka Synka och kontrollera några rader i listan.\n" +
                "4. Spara SRT: sparas som Filmnamn.sv.srt bredvid filmen, så hittar spelaren den.\n\n" +
                "Tips: dra in båda filerna samtidigt. Rödmarkerade rader saknar engelsk text samtidigt – ofta helt i sin ordning (t.ex. utrop).\n\n" +
                "Text Sync " + Program.Version + " · MIT-licens\n" +
                "Loggfil (för felsökning): " + Log.FilePath + "\n\n" +
                "Vill du öppna mappen med loggfilen?",
                "Hjälp – " + Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (a == DialogResult.Yes)
            {
                try { Directory.CreateDirectory(Log.Dir); Process.Start("explorer.exe", "\"" + Log.Dir + "\""); } catch { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                var a = MessageBox.Show(this, "Text Sync arbetar fortfarande. Vill du avbryta och stänga?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (a != DialogResult.Yes) { e.Cancel = true; return; }
                CancelWork();
                var p = muxProc;
                if (p != null) { try { p.WaitForExit(5000); } catch { } }
                CleanupMux(true);
            }
            else if (unsaved && synced != null)
            {
                var a = MessageBox.Show(this, "Den synkade undertexten är inte sparad. Vill du stänga ändå?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (a != DialogResult.Yes) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }
    }

    // ---------------------------------------------------------------- tidslinje
    public class TimelinePanel : Control
    {
        public List<Cue> Ref, Orig, Synced;
        public Cue Highlight;
        public double Center = 30000;
        public double Span = 60000;
        int dragX = -1; double dragCenter;
        static readonly bool HC = SystemInformation.HighContrast;

        public TimelinePanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            BackColor = HC ? SystemColors.Window : Color.White;
            Cursor = Cursors.SizeWE;
            TabStop = true;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Tidslinje: referens, original och synkad undertext";
            AccessibleDescription = "Vänster och höger pil flyttar, plus och minus zoomar, Home går till början.";
        }

        protected override void OnMouseDown(MouseEventArgs e) { dragX = e.X; dragCenter = Center; Focus(); }
        protected override void OnMouseUp(MouseEventArgs e) { dragX = -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragX < 0) return;
            Center = dragCenter - (e.X - dragX) * Span / Math.Max(1, Width - LabelW);
            Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e) { Zoom(e.Delta > 0 ? 0.8 : 1.25); }

        void Zoom(double f)
        {
            Span = Math.Max(5000, Math.Min(30 * 60000, Span * f));
            Invalidate();
        }

        // tangentbord: piltangenter flyttar, PageUp/PageDown flyttar en hel skärm, +/- zoomar, Home = början
        protected override bool IsInputKey(Keys k)
        {
            Keys key = k & Keys.KeyCode;
            if (key == Keys.Left || key == Keys.Right) return true;
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: Center -= Span * 0.2; break;
                case Keys.Right: Center += Span * 0.2; break;
                case Keys.PageUp: Center -= Span; break;
                case Keys.PageDown: Center += Span; break;
                case Keys.Home: Center = Span / 2; break;
                case Keys.Add: case Keys.Oemplus: Zoom(0.8); e.Handled = true; return;
                case Keys.Subtract: case Keys.OemMinus: Zoom(1.25); e.Handled = true; return;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        int LabelW { get { return (int)(100 * DeviceDpi / 96.0); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(1, 1, Width - 2, Height - 2));
            int lw = LabelW, w = Width - lw - 8;
            if (w <= 10) return;
            double t0 = Center - Span / 2, t1 = Center + Span / 2;
            Func<double, float> X = t => (float)(lw + (t - t0) / Span * w);
            var muted = new SolidBrush(HC ? SystemColors.WindowText : Color.FromArgb(110, 110, 110));
            int fh = Font.Height;
            float rowH = Math.Max(fh, (Height - fh - 12) / 3f);

            double tick = Span <= 20000 ? 1000 : Span <= 90000 ? 5000 : Span <= 300000 ? 30000 : 60000;
            using (var pen = new Pen(HC ? SystemColors.GrayText : Color.FromArgb(235, 235, 235)))
                for (double t = Math.Ceiling(t0 / tick) * tick; t <= t1; t += tick)
                {
                    float x = X(t);
                    g.DrawLine(pen, x, 4, x, Height - fh - 4);
                    if (t >= 0) g.DrawString(Srt.FormatTime(t).Substring(0, 8), Font, muted, x - fh * 1.5f, Height - fh - 2);
                }

            string[] names = { "Referens", "Original", "Synkad" };
            Color[] cols = HC
                ? new[] { SystemColors.Highlight, SystemColors.GrayText, SystemColors.HotTrack }
                : new[] { Color.FromArgb(0, 103, 192), Color.FromArgb(160, 160, 160), Color.FromArgb(16, 140, 60) };
            List<Cue>[] data = { Ref, Orig, Synced };
            for (int r = 0; r < 3; r++)
            {
                float y = 6 + r * rowH;
                g.DrawString(names[r], Font, muted, 4, y + rowH / 2 - Font.Height / 2f);
                if (data[r] == null) continue;
                using (var br = new SolidBrush(HC ? cols[r] : Color.FromArgb(r == 1 ? 140 : 200, cols[r])))
                    foreach (var c in data[r])
                    {
                        if (c.End < t0 || c.Start > t1) continue;
                        float x0 = Math.Max(lw, X(c.Start)), x1 = Math.Min(lw + w, X(c.End));
                        if (x1 - x0 < 1) x1 = x0 + 1;
                        var rect = new RectangleF(x0, y + 3, x1 - x0, rowH - 8);
                        g.FillRectangle(br, rect);
                        if (r == 2 && c == Highlight)
                            using (var hp = new Pen(HC ? SystemColors.WindowText : Color.Black, 2)) g.DrawRectangle(hp, rect.X, rect.Y, rect.Width, rect.Height);
                    }
            }
            if (Ref == null)
                g.DrawString("Tidslinjen visar referensen, originalet och resultatet här efter synk. Scrolla eller +/− = zooma, dra eller piltangenter = flytta.", Font, muted, lw, Height / 2f - 8);
            muted.Dispose();
        }
    }
}