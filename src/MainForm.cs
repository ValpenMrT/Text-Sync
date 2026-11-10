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
using Microsoft.Win32;

namespace TextSync
{
    public partial class MainForm : Form
    {
        // Högkontrastläge i Windows: använd systemets färger i stället för egna. Färgerna kommer från Theme (ljust/mörkt läge).
        public static readonly bool HC = Theme.HC;
        static Color Accent { get { return Theme.Accent; } }
        static Color Bg { get { return Theme.Bg; } }
        static Color Card { get { return Theme.Card; } }
        static Color Muted { get { return Theme.Muted; } }
        static Color ErrColor { get { return Theme.Err; } }
        static Color OkColor { get { return Theme.Ok; } }
        static Color RedRow { get { return Theme.RedRow; } }
        static Color TitleColor { get { return Theme.Title; } }
        static Color RefOnlyRow { get { return Theme.RefOnly; } }
        static Color PropRow { get { return Theme.Prop; } }
        // Inställningar som görs en gång (utseende, bildfrekvensfel, text för hörselskadade, formatering) ligger i dialogen Inställningar.
        // Kryssrutorna fpsCheck, hiCheck och plainCheck finns kvar som bärare av valen och deras händelser, men visas inte i fönstret.
        int themeIndex;                // 0 = som Windows, 1 = ljust, 2 = mörkt
        Button settingsBtn, seasonBtn;
        SeasonDialog seasonDlg;        // fönstret Hel säsong. Samma fönster används igen, så att listan står kvar medan programmet är öppet.
        Label optInfo;                // vilka inställningar som avviker från det vanliga, så att de syns i huvudfönstret
        public const string BackupFolder = "Text Sync original";
        internal static readonly string Caption = "Text Sync " + Program.Version;
        readonly Stack<List<Cue>> undo = new Stack<List<Cue>>(); // för att ångra ändringar av rader
        const double MatchThreshold = 0.95;   // andel rader som ska ha referenstext samtidigt
        bool[] rowMatched;             // per synkad rad: har referenstext samtidigt
        int[] rowCue;                  // listrad -> index i synced, -1 = grå referensrad utan egen synkad rad
        int[] cueRow;                  // index i synced -> listrad
        Cue[] rowRef;                  // listrad -> referensraden som visas (eller null)
        // vilka rader listan visar: 0 = alla, 1 = dölj grå rader, 2 = bara röda rader (sparas mellan körningar)
        const int ViewAll = 0, ViewNoGray = 1, ViewOnlyRed = 2, ViewProposals = 3, ViewFixed = 4;
        ComboBox viewCombo; Label viewInfo; LinkLabel acceptAllLink; CheckBox proposeCheck;
        int refOnlyTotal;              // antal grå referensrader, även de som är dolda
        // förslag på att slå ihop eller dela rader så att de följer referensens radindelning (blå rader)
        List<AlignProposal> proposals = new List<AlignProposal>();
        int[] cueProp;                 // index i synced -> index i proposals, -1 = inget förslag
        int[] rowProp;                 // listrad -> index i proposals för raden "→ blir" som visar hur förslaget blir, annars -1
        Font propFont;                 // kursiv stil för de raderna
        TextBox searchBox; Label searchInfo; LinkLabel searchPrev, searchNext;
        readonly HashSet<string> rejected = new HashSet<string>();   // avvisade förslag (AlignProposal.Key)
        bool refIsText;                // referensen är riktig text (inte ljud), så att raderna kan matchas
        LinkLabel undoLink;
        // besked om hur säker synken är (Bra / Kontrollera / Osäkert) och hopp mellan svaga ställen
        QualityResult quality; List<int> weakStops = new List<int>(); bool[] cueWeak;
        Label verdictLabel; LinkLabel prevWeakLink, nextWeakLink; TableLayoutPanel resBox;
        // resultatrutan: beskedet på en rad, siffrorna på nästa (chip*), varningar (notesLabel) och resten under Detaljer
        Label summaryLabel, notesLabel, chipPct, chipRed, chipProp, chipGrey, chipFixed, chipSeg, chipHi;
        FlowLayoutPanel chips; TableLayoutPanel detailsPanel; Panel detailsSep; Button detailsBtn;
        Label[] detailHead, detailBody;
        static readonly int[] DetailPct = { 36, 40, 24 };   // kolumnernas bredd under Detaljer
        bool detailsOpen, weakLinks, pctGood; double pctFrac;
        bool cancelling;               // Avbryt är klickad, arbetet håller på att stanna
        int busySince;                 // Environment.TickCount när arbetet började (skydd mot dubbelklick på Synka)
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000 };
        Label gpuLabel; ComboBox gpuCombo; List<int> gpuDevices; bool gpuFilling, gpuPending;
        // ljudspår för ljudsynk och Whisper (visas bara när filmen har mer än ett ljudspår)
        Label audioLabel; ComboBox audioCombo; bool audioFilling;
        List<Cue> transcript; string transcriptLang = ""; // Whisper-text från senaste ljudsynk
        List<Cue> origSynced; OrigSrtOption origUsed; string origLang = "";    // originaltexten (t.ex. engelska) efter synk mot ljudet
        LinkLabel saveTextBtn;
        LinkLabel origLink; int refAudioIndex; string refInfoDefault = "";

        TextBox langBox;
        Label refName, tgtName;        // filnamnen i ruta 1 och 2 (hela sökvägen står i tipsrutan)
        ComboBox trackCombo, modeCombo, encCombo;
        Label refInfo, tgtInfo, statusLabel, trackLabel;
        LinkLabel openLink, helpLink;
        CheckBox fpsCheck, hiCheck, plainCheck;
        int plainCount;                // rader där formatering (kursiv m.m.) tas bort av rutan "Ta bort kursiv och annan formatering"
        string hiInfo;                 // vad som togs bort som text för hörselskadade (visas i resultatet)
        int hiUndoDepth;               // ångra-nivån där den synkade texten rensades i efterhand (0 = inte gjort)
        bool hiQuiet;                  // rutan ändras av programmet, inte av användaren
        Button syncBtn, saveBtn, muxBtn, refBtn, tgtBtn;
        Button busyBtn;                // knappen som startade arbetet och är Avbryt så länge (null = Synka)
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
            Text = "Text Sync " + Program.Version;
            Font = new Font("Segoe UI", 9.5F);
            Theme.Set(Theme.WantDark(Theme.Mode));
            BackColor = Bg; ForeColor = Theme.Fg;
            MinimumSize = new Size(940, 600);
            Size = new Size(1080, 1000);
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
            // Inställningar längst upp till höger: utseende (som Windows, ljust, mörkt), bildfrekvensfel, text för hörselskadade, formatering
            string tm = Theme.Mode;
            themeIndex = tm == Theme.Light ? 1 : tm == Theme.Dark_ ? 2 : 0;
            settingsBtn = MakeButton("&Inställningar…", false);
            settingsBtn.Width = 130; settingsBtn.Margin = new Padding(0);   // fast bredd: med AutoSize blev knappen dubbelt så stor vid 200 % skalning
            settingsBtn.Click += (s, e) => OpenSettings();
            head.Controls.Add(settingsBtn);
            // Hel säsong: alla avsnitt i en mapp synkas i kö (eget fönster, SeasonDialog)
            seasonBtn = MakeButton("&Hel säsong…", false);
            seasonBtn.Width = 130; seasonBtn.Margin = new Padding(0);
            seasonBtn.Click += (s, e) => OpenSeason(null);
            tips.SetToolTip(seasonBtn, "Synka alla avsnitt i en säsong på en gång. Det går också att dra in en mapp i fönstret.");
            head.Controls.Add(seasonBtn);
            head.Layout += (s, e) =>
            {
                ver.Left = title.Right + 6; ver.Top = title.Bottom - ver.Height - (int)(title.Height * 0.12);
                settingsBtn.Left = head.ClientSize.Width - settingsBtn.Width; settingsBtn.Top = 2;
                seasonBtn.Left = settingsBtn.Left - seasonBtn.Width - settingsBtn.Width / 16; seasonBtn.Top = 2;
            };
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Z && !(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown) && undo.Count > 0)
                { UndoChange(); e.Handled = true; }
                else if (e.KeyCode == Keys.F3 && synced != null) { NextUnmatched(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.F) { searchBox.Focus(); searchBox.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.F4 && !e.Alt && synced != null) { JumpWeak(e.Shift ? -1 : 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape && StopPlay()) { if (statusLabel.Text.StartsWith("▶")) SetStatus("", true); e.Handled = true; }   // Esc stoppar uppspelningen även härifrån
            };
            root.Controls.Add(head, 0, 0);

            // --- 1. referens
            // Ruta 1 och 2 står bredvid varandra. Listorna fyller rutans bredd, och den öppnade listan är så bred som den längsta raden.
            refName = MakeFileLabel(); tgtName = MakeFileLabel();
            refBtn = MakeButton("Bläddra…", false);
            refBtn.Click += (s, e) => BrowseRef();
            trackLabel = new Label { Text = "Referens:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Visible = false };
            trackCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Visible = false, Margin = new Padding(0, 3, 0, 0) };
            trackCombo.DropDown += WidenDropDown;
            trackCombo.SelectedIndexChanged += (s, e) =>
            {
                if (!busy) ClearResult();
                UpdateGpuVisibility();
                var oo = trackCombo.SelectedItem as OrigSrtOption;
                if (oo != null) { refInfo.ForeColor = Muted; refInfo.Text = "Originaltext: " + oo.Cues.Count + " repliker – synkas mot ljudet och kontrolleras med Whisper."; }
                else if (refInfoDefault.Length > 0 && refInfo.ForeColor != ErrColor) refInfo.Text = refInfoDefault;
            };
            refInfo = new Label { AutoSize = true, ForeColor = Muted, UseMnemonic = false, Text = "Klicka här eller dra in en MKV- eller SRT-fil." };
            // grafikkort för Whisper (visas bara när en ljudmetod med Whisper är vald)
            gpuLabel = new Label { Text = "&Grafikkort:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Visible = false };
            gpuCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 0), Visible = false, AccessibleName = "Grafikkort för taligenkänning" };
            gpuCombo.DropDown += WidenDropDown;
            gpuCombo.SelectedIndexChanged += (s, e) =>
            {
                if (gpuFilling || gpuDevices == null || gpuCombo.SelectedIndex < 0 || gpuCombo.SelectedIndex >= gpuDevices.Count) return;
                int dev = gpuDevices[gpuCombo.SelectedIndex];
                Audio.WhisperDevice = dev;
                Tools.SetSetting("gpu", dev < 0 ? "cpu" : dev.ToString());
            };
            // separat undertext på filmens originalspråk (t.ex. engelska), som synkas mot ljudet och kontrolleras av Whisper
            origLink = new LinkLabel { Text = "Lägg till undertext på filmens originalspråk (SRT)…", AutoSize = true, Margin = new Padding(0, 6, 0, 0), Visible = false, LinkColor = Accent };
            origLink.LinkClicked += (s, e) => BrowseOrig();
            audioLabel = new Label { Text = "Lj&udspår:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Visible = false };
            audioCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 0), Visible = false, AccessibleName = "Ljudspår för synk mot ljudet" };
            audioCombo.DropDown += WidenDropDown;
            audioCombo.SelectedIndexChanged += (s, e) => { if (!audioFilling && audioCombo.SelectedIndex >= 0) SetAudioIndex(audioCombo.SelectedIndex); };
            // under den klickbara delen: en rad per lista (namn till vänster, listan fyller resten), sist länken för originaltext
            var rinfo = MakeRows();
            rinfo.Controls.Add(trackLabel, 0, 0); rinfo.Controls.Add(trackCombo, 1, 0);
            rinfo.Controls.Add(gpuLabel, 0, 1); rinfo.Controls.Add(gpuCombo, 1, 1);
            rinfo.Controls.Add(audioLabel, 0, 2); rinfo.Controls.Add(audioCombo, 1, 2);
            rinfo.Controls.Add(origLink, 0, 3); rinfo.SetColumnSpan(origLink, 2);
            Tools.GpuScan();   // letar efter NVIDIA-kort i bakgrunden redan nu, så att listan Grafikkort är klar när den behövs
            var refCard = MakeCard("1.  Referens – undertext i takt med filmen (textspår i MKV eller SRT) eller filmens ljud", refName, refBtn, refInfo, rinfo, BrowseRef, LoadReference);

            // --- 2. mål
            tgtBtn = MakeButton("Bläddra…", false);
            tgtBtn.Click += (s, e) => BrowseTarget();
            tgtInfo = new Label { AutoSize = true, ForeColor = Muted, Text = "Klicka här eller dra in SRT-filen.", UseMnemonic = false };   // texten kan innehålla "&amp;"
            // teckenkodning: automatisk räcker nästan alltid; manuellt val om å/ä/ö eller t.ex. kyrilliska tecken blir fel
            encCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 0), AccessibleName = "Teckenkodning" };
            encCombo.DropDown += WidenDropDown;
            foreach (var en in Srt.Encodings) encCombo.Items.Add(en[1]);
            encCombo.SelectedIndex = 0;
            encCombo.SelectedIndexChanged += (s, e) => { if (!busy && !encReset && !string.IsNullOrEmpty(tgtLoadedPath)) LoadTarget(tgtLoadedPath); };
            var tinfo = MakeRows();
            tinfo.Controls.Add(new Label { Text = "&Teckenkodning:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) }, 0, 0);
            tinfo.Controls.Add(encCombo, 1, 0);
            var tgtCard = MakeCard("2.  Undertext att synka (SRT som inte går i takt)", tgtName, tgtBtn, tgtInfo, tinfo, BrowseTarget, LoadTarget);

            // rutorna delar bredden lika och blir lika höga
            var cards = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            refCard.Margin = new Padding(0, 0, 4, 8); tgtCard.Margin = new Padding(4, 0, 0, 8);
            cards.Controls.Add(refCard, 0, 0); cards.Controls.Add(tgtCard, 1, 0);
            root.Controls.Add(cards, 0, 1);   // rad 2 i root är tom sedan rutorna hamnade bredvid varandra
            ShowFile(refName, null); ShowFile(tgtName, null);

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
            // ikryssad från början, valet sparas till nästa gång (visas i dialogen Inställningar)
            fpsCheck = new CheckBox { Text = "Upptäck bildfrekvensfel (25 ↔ 23,976 fps m.fl.)", Checked = Tools.GetSetting("fps", "1") == "1", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };
            fpsCheck.CheckedChanged += (s, e) => { Tools.SetSetting("fps", fpsCheck.Checked ? "1" : "0"); UpdateOptInfo(); };
            // text för hörselskadade: inte ikryssad från början, valet sparas till nästa gång
            hiCheck = new CheckBox { Text = "Ta bort text för hörselskadade ([MUSIK], (skrattar), talarnamn)", AutoSize = true, Margin = new Padding(0, 8, 16, 0), Checked = Tools.GetSetting("removehi", "0") == "1" };
            hiCheck.CheckedChanged += (s, e) =>
            {
                Tools.SetSetting("removehi", hiCheck.Checked ? "1" : "0");
                UpdateOptInfo();
                if (hiQuiet || busy || result == null) return;
                if (hiCheck.Checked)
                {
                    // finns redan en synkad text rensas den direkt, som en ändring som går att ångra
                    int ch, rm;
                    var cleaned = Sdh.Apply(result.Output, out ch, out rm);
                    if (cleaned.Count == 0) SetStatus("Nästan allt i texten ser ut som text för hörselskadade, så ingenting togs bort.", false);
                    else if (ch + rm > 0)
                    {
                        hiInfo = Sdh.Summary(ch, rm); hiUndoDepth = undo.Count + 1;
                        Changed(cleaned, null, hiInfo + " Ctrl+Z ångrar.", "Text för hörselskadade borttagen ur den synkade texten: " + ch + " ändrade, " + rm + " borttagna");
                    }
                    else SetStatus(Sdh.Summary(0, 0), true);
                }
                else if (hiInfo != null && !hiInfo.StartsWith("Ingen")) SetStatus("Inställningen gäller från nästa synk. Synka igen för att få tillbaka texten som togs bort.", false);
            };
            // formatering: <i>, <b>, <u>, <s>, <font> och {\an8}. Inte ikryssad från början, valet sparas till nästa gång.
            // Rutan ändrar inte result.Output, utan bara det som visas, spelas upp och sparas (se RefreshPreview).
            // Därför kommer formateringen tillbaka när rutan kryssas ur.
            plainCheck = new CheckBox { Text = "Ta bort kursiv och annan formatering (för TV som visar <i> som text)", AutoSize = true, Margin = new Padding(0, 8, 16, 0), Checked = Tools.GetSetting("striptags", "0") == "1" };
            plainCheck.CheckedChanged += (s, e) =>
            {
                Tools.SetSetting("striptags", plainCheck.Checked ? "1" : "0");
                UpdateOptInfo();
                if (busy || result == null) return;
                int before = plainCount;
                RefreshPreview();
                if (before != plainCount) { unsaved = true; openLink.Visible = false; }   // bara om texten som sparas ändrades
                if (plainCheck.Checked) SetStatus(plainCount == 0 ? "Texten har ingen formatering att ta bort." : "Formatering borttagen på " + plainCount + (plainCount == 1 ? " rad" : " rader") + ". Kryssa ur rutan i Inställningar för att få tillbaka den.", plainCount == 0);
                else SetStatus("Formateringen är tillbaka.", false);
                Log.Info("Ta bort formatering: " + (plainCheck.Checked ? "på, " + plainCount + " rader" : "av"));
            };

            // hur SRT-filen sparas (dialogen Inställningar): 0 = UTF-8 med BOM, 1 = UTF-8 utan BOM, 2 = mycket gamla spelare
            int sf;
            Srt.SaveFormat = int.TryParse(Tools.GetSetting("srtformat", "0"), out sf) && sf >= Srt.FormatUtf8Bom && sf <= Srt.FormatLegacy ? sf : Srt.FormatUtf8Bom;

            syncBtn = MakeButton("&Synka", true);
            syncBtn.Width = 120; syncBtn.Height = 34; syncBtn.Margin = new Padding(0, 2, 12, 0);
            // samma knapp är Avbryt så länge programmet arbetar (se ShowSyncButton)
            // Ett dubbelklick på Synka ska inte avbryta direkt: klick den första dryga halvsekunden efter starten räknas inte.
            syncBtn.Click += async (s, e) =>
            {
                if (!busy) await DoSync();
                else if (unchecked(Environment.TickCount - busySince) > 700) CancelWork();
            };
            opts.Controls.Add(syncBtn);
            progress = new ProgressBar { Width = 160, Height = 10, Margin = new Padding(0, 14, 10, 0), Visible = false };
            opts.Controls.Add(progress);
            statusLabel = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 9, 6, 0) };
            opts.Controls.Add(statusLabel);
            openLink = new LinkLabel { Text = "Visa i mappen", AutoSize = true, Margin = new Padding(0, 9, 0, 0), Visible = false, LinkColor = Accent };
            openLink.LinkClicked += (s, e) => ShowInFolder(lastOutput);
            opts.Controls.Add(openLink);
            undoLink = new LinkLabel { Text = "Ångra", AutoSize = true, Margin = new Padding(0, 9, 0, 0), Visible = false, LinkColor = Accent };
            undoLink.LinkClicked += (s, e) => UndoChange();
            opts.Controls.Add(undoLink);
            // på en egen rad under: inställningar som avviker från det vanliga, så att de inte glöms bort i dialogen
            optInfo = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 0, 4), Visible = false };
            // efter synk mot ljudet: spara det Whisper hörde, eller originaltexten (t.ex. engelska) som synkades mot ljudet
            saveTextBtn = new LinkLabel { Text = "Spara igenkänd text…", AutoSize = true, Margin = new Padding(12, 9, 0, 0), Visible = false, LinkColor = Accent };
            saveTextBtn.LinkClicked += (s, e) => { if (!busy) SaveTranscript(); };
            opts.Controls.Add(saveTextBtn);
            var optWrap = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            optWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            optWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize)); optWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            optWrap.Controls.Add(opts, 0, 0); optWrap.Controls.Add(optInfo, 0, 1);
            FitFlowHeight(opts);   // annars blir det tomrum mellan den här raden och resultatet
            root.Controls.Add(optWrap, 0, 3);

            // --- resultat
            // Kompakt, så att listan får platsen:
            // rad 1: beskedet (Bra / Kontrollera / Osäkert), en mening, länkar till svaga ställen och knappen Detaljer
            // rad 2: siffrorna, med samma färger som raderna i listan
            // rad 3: varningar (visas alltid), och under Detaljer resten: hur synken gjordes, färgerna och hur man redigerar
            verdictLabel = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 11.5F), Margin = new Padding(0, 0, 14, 0) };
            summaryLabel = new Label { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseMnemonic = false, Margin = new Padding(0, 4, 14, 0) };
            FitLabelHeight(summaryLabel);
            prevWeakLink = new LinkLabel { Text = "◀ Föregående ställe", AutoSize = true, Margin = new Padding(0, 4, 14, 0), Visible = false, LinkColor = Accent };
            nextWeakLink = new LinkLabel { Text = "Nästa ställe ▶", AutoSize = true, Margin = new Padding(0, 4, 14, 0), Visible = false, LinkColor = Accent };
            prevWeakLink.LinkClicked += (s, e) => JumpWeak(-1);
            nextWeakLink.LinkClicked += (s, e) => JumpWeak(1);
            tips.SetToolTip(prevWeakLink, "Skift+F4"); tips.SetToolTip(nextWeakLink, "F4");
            detailsBtn = MakeButton("", false); detailsBtn.Width = 124; detailsBtn.Height = 26; detailsBtn.Margin = new Padding(0);
            detailsBtn.Click += (s, e) => { SetDetails(!detailsOpen); Tools.SetSetting("details", detailsOpen ? "1" : "0"); };
            var vrow = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 5, RowCount = 2, Margin = new Padding(10, 8, 10, 0) };
            vrow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            vrow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) vrow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            vrow.Controls.Add(verdictLabel, 0, 0); vrow.Controls.Add(summaryLabel, 1, 0); vrow.Controls.Add(prevWeakLink, 2, 0); vrow.Controls.Add(nextWeakLink, 3, 0); vrow.Controls.Add(detailsBtn, 4, 0);

            chipPct = MakeChip(null, 80); chipRed = MakeChip(() => RedRow, 15); chipProp = MakeChip(() => PropRow, 15); chipGrey = MakeChip(() => RefOnlyRow, 15);
            chipFixed = MakeChip(null, 0); chipSeg = MakeChip(null, 0); chipHi = MakeChip(null, 0);
            // stapeln framför procenttalet: röd under gränsen (95 %), annars grön
            chipPct.Paint += (s, e) =>
            {
                int bw = chipPct.Padding.Left * 72 / 80, bh = Math.Max(5, chipPct.Padding.Left * 7 / 80), y = (chipPct.Height - bh) / 2 + 1;
                using (var fill = new SolidBrush(HC ? SystemColors.Highlight : pctGood ? OkColor : RedRow)) e.Graphics.FillRectangle(fill, 0, y, (int)Math.Round(bw * Math.Max(0, Math.Min(1, pctFrac))), bh);
                using (var pen = new Pen(HC ? SystemColors.WindowText : Theme.BtnBorder)) e.Graphics.DrawRectangle(pen, 0, y, bw - 1, bh - 1);
            };
            tips.SetToolTip(chipProp, "Blå rader är förslag om att slå ihop eller dela rader. Högerklicka på raden för att godkänna eller avvisa.");
            tips.SetToolTip(chipGrey, "Grå rader finns bara i referensen och sparas inte.");
            tips.SetToolTip(chipFixed, "Rader märkta ✎ rättades vid inläsningen. Håll muspekaren över raden för att se vad som stod i filen.");
            chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(10, 6, 10, 6) };
            chips.Controls.AddRange(new Control[] { chipPct, chipRed, chipProp, chipGrey, chipFixed, chipSeg, chipHi });
            FitFlowHeight(chips);
            notesLabel = new Label { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseMnemonic = false, ForeColor = Theme.Warn, Margin = new Padding(10, 0, 10, 6), Visible = false };
            FitLabelHeight(notesLabel);

            detailsSep = new Panel { Height = 1, Dock = DockStyle.Top, Margin = new Padding(10, 0, 10, 0), BackColor = Theme.BtnBorder, Visible = false };
            detailsPanel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 2, Margin = new Padding(10, 8, 10, 10), Visible = false };
            string[] heads = { "Så gjordes synken", "Färgerna i listan", "Redigera i listan" };
            detailHead = new Label[3]; detailBody = new Label[3];
            for (int i = 0; i < 3; i++)
            {
                detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, DetailPct[i]));
                detailHead[i] = new Label { Text = heads[i], AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5F), Margin = new Padding(0, 0, 16, 4) };
                detailBody[i] = new Label { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, UseMnemonic = false, Margin = new Padding(0, 0, 16, 0) };
                FitLabelHeight(detailBody[i]);
                detailsPanel.Controls.Add(detailHead[i], i, 0); detailsPanel.Controls.Add(detailBody[i], i, 1);
            }
            detailBody[2].Text = "Dubbelklicka – ändra text och tid\nInsert – lägg till en rad\nDelete – ta bort raden\nCtrl+Z – ångra";

            resBox = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 5, Margin = new Padding(0, 2, 0, 6), BackColor = Card, Visible = false };
            resBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            resBox.Controls.Add(vrow, 0, 0); resBox.Controls.Add(chips, 0, 1); resBox.Controls.Add(notesLabel, 0, 2); resBox.Controls.Add(detailsSep, 0, 3); resBox.Controls.Add(detailsPanel, 0, 4);
            root.Controls.Add(resBox, 0, 4);
            root.Resize += (s, e) => LayoutResult();
            SetDetails(Tools.GetSetting("details", "0") == "1");   // minns om Detaljer var utfällt

            // --- förhandsvisning
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Bg };
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false, BorderStyle = BorderStyle.None };
            // Listan ritas delvis av programmet (OwnerDraw):
            // - mörkt läge: kolumnrubrikerna, eftersom Windows mörka tema ritar dem med grå text
            // - raden under muspekaren (båda lägena) och den markerade raden i mörkt läge, som annars blir grå på grå
            // Allt annat ritas av Windows som vanligt. Högkontrast ritas helt av Windows.
            list.OwnerDraw = !HC;
            int hotRow = -1;
            Action<int> redrawRow = i => { if (i >= 0 && i < list.Items.Count) list.Invalidate(list.GetItemRect(i)); };
            list.MouseMove += (s, e) =>
            {
                var it = list.GetItemAt(e.X, e.Y);
                int i = it != null ? it.Index : -1;
                list.Cursor = !busy && OnPlayCell(e.Location) != null ? Cursors.Hand : Cursors.Default;   // play-knappen
                if (i == hotRow) return;
                int old = hotRow; hotRow = i;
                redrawRow(old); redrawRow(i);
            };
            list.MouseLeave += (s, e) => { int old = hotRow; hotRow = -1; redrawRow(old); };
            list.DrawColumnHeader += (s, e) =>
            {
                if (!Theme.Dark) { e.DrawDefault = true; return; }
                using (var bg = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(bg, e.Bounds);
                using (var pen = new Pen(Theme.BtnBorder))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                }
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                    (e.Header.TextAlign == HorizontalAlignment.Right && e.ColumnIndex > 0 ? TextFormatFlags.Right : TextFormatFlags.Left);   // Windows vänsterjusterar alltid första kolumnen
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, r, Theme.Title, flags);
            };
            // true = programmet ritar raden: markerad i mörkt läge, eller under muspekaren (och inte markerad)
            Func<ListViewItem, bool> ownRow = it => (Theme.Dark && it.Selected) || (it.Index == hotRow && !it.Selected);
            // DrawDefault = false här, så att DrawSubItem anropas för varje cell
            list.DrawItem += (s, e) => e.DrawDefault = false;
            list.DrawSubItem += (s, e) =>
            {
                bool own = ownRow(e.Item);
                // Programmet ritar alla celler (play-knappen i länkfärg, texten i radens färg), utom på en markerad rad i ljust läge, som Windows ritar
                // (play-knappen ritas alltid av programmet, eftersom cellens text är till för skärmläsare: "Spela rad 12")
                bool winSel = !own && e.Item.Selected;
                if (winSel && e.ColumnIndex != 0) { e.DrawDefault = true; return; }
                bool sel = e.Item.Selected;
                // första kolumnen får hela radens yta: begränsa till kolumnens bredd
                var b = e.ColumnIndex == 0 ? new Rectangle(e.Bounds.X, e.Bounds.Y, e.Header.Width, e.Bounds.Height) : e.Bounds;
                Color cellBg = winSel ? (list.Focused ? SystemColors.Highlight : SystemColors.Control) : !own ? e.Item.BackColor : sel ? Theme.SelBg : Theme.HotBg;
                using (var bg = new SolidBrush(cellBg)) e.Graphics.FillRectangle(bg, b);
                // fokusramen (t.ex. efter Ctrl+pil) ritade Windows förut
                if (e.Item.Focused && list.Focused && !sel)
                {
                    // en ram runt hela raden: ritas cell för cell, klippt till cellen
                    var clip = e.Graphics.Clip;
                    e.Graphics.SetClip(b);
                    ControlPaint.DrawFocusRectangle(e.Graphics, e.Item.Bounds);
                    e.Graphics.Clip = clip;
                }
                if (e.ColumnIndex == 0)
                {
                    // en triangel i länkfärgen, lite större när muspekaren är över raden
                    float h = b.Height * (e.Item.Index == hotRow ? 0.62f : 0.5f), w = h * 0.9f;
                    float x0 = b.X + (b.Width - w) / 2f, y0 = b.Y + (b.Height - h) / 2f;
                    var old = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var br = new SolidBrush(winSel ? (list.Focused ? SystemColors.HighlightText : Theme.Link) : sel ? Color.White : Theme.Link))
                        e.Graphics.FillPolygon(br, new[] { new PointF(x0, y0), new PointF(x0 + w, y0 + h / 2f), new PointF(x0, y0 + h) });
                    e.Graphics.SmoothingMode = old;
                    return;
                }
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                    (e.Header.TextAlign == HorizontalAlignment.Right && e.ColumnIndex > 0 ? TextFormatFlags.Right : TextFormatFlags.Left);   // Windows vänsterjusterar alltid första kolumnen
                // radens egen färg (röd, blå, grå) behålls när muspekaren är över
                // samma marginaler som Windows: 6 px i första kolumnen, 2 px vänster och 3 px höger i övriga
                var tr = e.ColumnIndex == 0 ? new Rectangle(b.X + 6, b.Y, b.Width - 8, b.Height) : new Rectangle(b.X + 2, b.Y, b.Width - 5, b.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, tr, sel ? Color.White : e.Item.ForeColor, flags);
            };
            float k = 1f;
            using (var gr = CreateGraphics()) k = gr.DpiX / 96f;
            list.Columns.Add("▶", (int)(34 * k));   // play-knapp på varje rad: spelar filmen runt raden
            list.Columns.Add("#", (int)(110 * k), HorizontalAlignment.Right);   // "Dela? 12" / "Ihop? 12" på förslagsrader, ⚠ på svaga ställen, ✎ på rader som rättades vid inläsningen
            list.Columns.Add("Före synk", (int)(95 * k));
            list.Columns.Add("Synkad start", (int)(95 * k));
            list.Columns.Add("Synkad slut", (int)(95 * k));
            list.Columns.Add("Flyttad", (int)(75 * k), HorizontalAlignment.Right);
            list.Columns.Add("Synkad text", (int)(300 * k));
            list.Columns.Add("Referenstid", (int)(95 * k));
            list.Columns.Add("Referens (samtidigt)", (int)(300 * k));
            // textkolumnerna delar på bredden som blir över, så att listan inte får en vågrät rullist
            list.ClientSizeChanged += (s, e) =>
            {
                int fixedW = 0;
                foreach (ColumnHeader ch in list.Columns) if (ch.Index != 6 && ch.Index != 8) fixedW += ch.Width;
                int w = Math.Max((int)(150 * k), (list.ClientSize.Width - fixedW - 2) / 2);
                list.Columns[6].Width = w; list.Columns[8].Width = w;
            };
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedIndices.Count > 0 && synced != null && rowCue != null && list.SelectedIndices[0] < rowCue.Length)
                {
                    int row = list.SelectedIndices[0];
                    int ci = rowCue[row];
                    var c = ci >= 0 ? synced[ci] : rowRef[row];
                    if (c == null) return;
                    timeline.Center = (c.Start + c.End) / 2; timeline.Highlight = ci >= 0 ? c : null; timeline.Invalidate();
                }
            };
            // redigera rader: dubbelklick/Enter = ändra, Insert = lägg till, Delete = ta bort, F3 = nästa röda rad, Ctrl+Z = ångra
            list.MultiSelect = true;
            list.ShowItemToolTips = true;
            list.DoubleClick += (s, e) => { if (OnPlayCell(list.PointToClient(Cursor.Position)) == null) EditSelected(); };   // inte vid dubbelklick på play-knappen
            list.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var it = OnPlayCell(e.Location);
                if (it != null) PlayRow(it.Index);
            };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Return) { EditSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Insert) { AddRow(); e.Handled = true; }
                else if (e.KeyCode == Keys.P && !e.Control && !e.Alt) { PlaySelected(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in list.Items) it.Selected = true; e.Handled = true; }
            };
            var menu = new ContextMenuStrip();
            var miEdit = new ToolStripMenuItem("Ändra rad…", null, (s, e) => EditSelected()) { ShortcutKeyDisplayString = "Enter" };
            var miPlay = new ToolStripMenuItem("Spela upp i filmen", null, (s, e) => PlaySelected()) { ShortcutKeyDisplayString = "P" };
            var miAdd = new ToolStripMenuItem("Lägg till rad…", null, (s, e) => AddRow()) { ShortcutKeyDisplayString = "Insert" };
            var miDel = new ToolStripMenuItem("Ta bort markerade rader", null, (s, e) => DeleteSelected()) { ShortcutKeyDisplayString = "Delete" };
            var miNext = new ToolStripMenuItem("Gå till nästa röda rad", null, (s, e) => NextUnmatched()) { ShortcutKeyDisplayString = "F3" };
            var miUndo = new ToolStripMenuItem("Ångra", null, (s, e) => UndoChange()) { ShortcutKeyDisplayString = "Ctrl+Z" };
            var miAccept = new ToolStripMenuItem("Godkänn förslaget", null, (s, e) => AcceptProposals(SelectedProposals()));
            var miReject = new ToolStripMenuItem("Avvisa förslaget", null, (s, e) => RejectProposals(SelectedProposals()));
            var miAcceptAll = new ToolStripMenuItem("Godkänn alla förslag", null, (s, e) => AcceptProposals(proposals));
            var propSep = new ToolStripSeparator();
            menu.Items.AddRange(new ToolStripItem[] { miPlay, new ToolStripSeparator(), miEdit, miAdd, miDel, new ToolStripSeparator(), miNext, miUndo, propSep, miAccept, miReject, miAcceptAll });
            menu.Opening += (s, e) =>
            {
                if (synced == null || busy) { e.Cancel = true; return; }
                int n = SelectedCues().Count;
                bool refOnly = list.SelectedIndices.Count == 1 && rowCue != null && rowCue[list.SelectedIndices[0]] < 0 && !(rowProp != null && rowProp[list.SelectedIndices[0]] >= 0);
                miEdit.Text = refOnly ? "Lägg till rad här…" : "Ändra rad…";
                miEdit.Enabled = (n == 1 && list.SelectedIndices.Count == 1) || refOnly;
                miPlay.Enabled = list.SelectedIndices.Count == 1;
                miDel.Enabled = n > 0;
                miDel.Text = n > 1 ? "Ta bort " + n + " markerade rader" : "Ta bort markerad rad";
                miNext.Enabled = rowMatched != null && rowMatched.Any(m => !m);
                miUndo.Enabled = undo.Count > 0;
                int np = SelectedProposals().Count;
                propSep.Visible = miAccept.Visible = miReject.Visible = miAcceptAll.Visible = proposals.Count > 0;
                miAccept.Enabled = miReject.Enabled = np > 0;
                miAccept.Text = np > 1 ? "Godkänn " + np + " markerade förslag" : "Godkänn förslaget";
                miReject.Text = np > 1 ? "Avvisa " + np + " markerade förslag" : "Avvisa förslaget";
                miAcceptAll.Text = "Godkänn alla förslag (" + proposals.Count + ")";
            };
            list.ContextMenuStrip = menu;
            split.Panel1.Controls.Add(list);
            // val av vilka rader som visas (läggs till efter listan så att den dockas överst)
            var vbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4) };
            vbar.Controls.Add(new Label { Text = "&Visa:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
            viewCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, Margin = new Padding(0, 3, 12, 0), AccessibleName = "Vilka rader listan visar" };
            viewCombo.Items.AddRange(new object[] {
                "Alla rader",
                "Dölj grå rader (finns bara i referensen)",
                "Bara röda rader (ingen referenstext samtidigt)",
                "Bara förslag (blå rader att slå ihop eller dela)",
                "Bara rättade rader (märkta ✎, ändrade vid inläsningen)" });
            int view; if (!int.TryParse(Tools.GetSetting("view", "0"), out view) || view < 0 || view >= viewCombo.Items.Count) view = ViewAll;
            viewCombo.SelectedIndex = view;
            viewCombo.SelectedIndexChanged += (s, e) =>
            {
                Tools.SetSetting("view", viewCombo.SelectedIndex.ToString());
                if (result != null) RefreshPreview();
            };
            vbar.Controls.Add(viewCombo);
            // förslag om att dela/slå ihop rader är avstängda från början (ägarens val) och sparas mellan körningar
            proposeCheck = new CheckBox { Text = "Föreslå radindelning som referensen", AutoSize = true, Margin = new Padding(0, 6, 12, 0), Checked = Tools.GetSetting("propose", "0") == "1" };
            proposeCheck.CheckedChanged += (s, e) =>
            {
                Tools.SetSetting("propose", proposeCheck.Checked ? "1" : "0");
                if (result != null) RefreshPreview();
            };
            vbar.Controls.Add(proposeCheck);
            viewInfo = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 7, 12, 0) };
            vbar.Controls.Add(viewInfo);
            acceptAllLink = new LinkLabel { AutoSize = true, Margin = new Padding(0, 7, 0, 0), Visible = false, LinkColor = Accent };
            acceptAllLink.LinkClicked += (s, e) => AcceptProposals(proposals);
            vbar.Controls.Add(acceptAllLink);
            // sök i texten (Ctrl+F): Enter = nästa träff, Skift+Enter = föregående, Esc = töm
            // en egen rad i raden, så att etikett, ruta och träffar håller ihop när fönstret är smalt
            var srow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            srow.Controls.Add(new Label { Text = "Sö&k:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
            searchBox = new TextBox { Width = 200, Margin = new Padding(0, 3, 8, 0), AccessibleName = "Sök i texten" };
            searchBox.TextChanged += (s, e) => Find(0);
            searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return) { Find(e.Shift ? -1 : 1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape && searchBox.TextLength > 0) { searchBox.Clear(); list.Focus(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            srow.Controls.Add(searchBox);
            searchPrev = new LinkLabel { Text = "Föregående", AutoSize = true, Margin = new Padding(0, 7, 8, 0), LinkColor = Accent };
            searchNext = new LinkLabel { Text = "Nästa", AutoSize = true, Margin = new Padding(0, 7, 8, 0), LinkColor = Accent };
            searchPrev.LinkClicked += (s, e) => Find(-1);
            searchNext.LinkClicked += (s, e) => Find(1);
            srow.Controls.Add(searchPrev); srow.Controls.Add(searchNext);
            searchInfo = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 7, 0, 0) };
            srow.Controls.Add(searchInfo);
            vbar.Controls.Add(srow);
            split.Panel1.Controls.Add(vbar);
            timeline = new TimelinePanel { Dock = DockStyle.Fill };
            split.Panel2.Controls.Add(timeline);
            split.FixedPanel = FixedPanel.Panel2;
            root.Controls.Add(split, 0, 5);
            Shown += (s, e) => BeginInvoke(new Action(() =>
            {
                RelayoutAll(root);   // höjder som sattes medan fönstret skalades (t.ex. 200 %) ska gälla innan listan får sin plats
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
            saveBtn = MakeButton("S&para SRT…", true); saveBtn.Enabled = false; saveBtn.Width = 130; saveBtn.Height = 34; saveBtn.Margin = new Padding(0, 0, 6, 0);
            saveBtn.Click += (s, e) => SaveSrt();
            ctrls.Add(saveBtn);
            muxBtn = MakeButton("Lägg in i &MKV…", false); muxBtn.Enabled = false; muxBtn.Width = 140; muxBtn.Height = 34;
            // samma knapp är Avbryt medan MKV-filen skapas, med samma skydd mot dubbelklick som Synka
            muxBtn.Click += async (s, e) =>
            {
                if (!busy) await MuxIntoMkv();
                else if (busyBtn == muxBtn && unchecked(Environment.TickCount - busySince) > 700) CancelWork();
            };
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

            UpdateOptInfo();
            Theme.Apply(this, null);
            Theme.ApplyMenu(list.ContextMenuStrip);
            // "Som Windows": följ med när mörkt läge slås på eller av i Windows
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            // Skalningen (t.ex. 200 %) slås på först här, när alla delar finns. Sätts den överst i konstruktorn skalas ett delträd
            // varje gång det läggs till, och en kontroll som har fått ny höjd däremellan skalas en gång till (marginaler blev 24 i stället för 12).
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
        }

        // Raden vars play-knapp (första kolumnen) ligger under punkten, annars null. HitTest tar hänsyn till rullning.
        ListViewItem OnPlayCell(Point pt)
        {
            var hit = list.HitTest(pt);
            return hit.Item != null && hit.SubItem == hit.Item.SubItems[0] ? hit.Item : null;
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || Theme.Mode != Theme.Auto || IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(ApplyTheme));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnFormClosed(e);
        }

        // Dialogen Inställningar. Valen läggs i de dolda kryssrutorna, vars händelser sparar dem och ändrar texten direkt
        // (text för hörselskadade som en ändring som går att ångra, formatering utan att texten i minnet ändras).
        void OpenSettings()
        {
            if (busy) return;
            using (var d = new SettingsDialog(themeIndex, !HC, fpsCheck.Checked, hiCheck.Checked, plainCheck.Checked, Srt.SaveFormat))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.SrtFormat != Srt.SaveFormat)
                {
                    Srt.SaveFormat = d.SrtFormat;
                    Tools.SetSetting("srtformat", Srt.SaveFormat.ToString());
                    Log.Info("SRT-filen sparas för: " + SettingsDialog.FormatNames[Srt.SaveFormat]);
                    UpdateOptInfo();
                    // en fil som redan är sparad ändras inte av inställningen
                    if (synced != null && !busy) { SetStatus("Gäller nästa gång du sparar. Klicka Spara SRT… för att spara om filen.", false); openLink.Visible = false; }
                }
                if (d.ThemeIndex != themeIndex)
                {
                    themeIndex = d.ThemeIndex;
                    Theme.Mode = themeIndex == 1 ? Theme.Light : themeIndex == 2 ? Theme.Dark_ : Theme.Auto;
                    ApplyTheme();
                }
                fpsCheck.Checked = d.DetectFps;
                hiCheck.Checked = d.RemoveHi;
                plainCheck.Checked = d.StripFormatting;
            }
        }

        // "Inställningar: text för hörselskadade tas bort · formatering tas bort". Tomt (dolt) när allt är som vanligt.
        void UpdateOptInfo()
        {
            if (optInfo == null || fpsCheck == null || hiCheck == null || plainCheck == null) return;
            var parts = new List<string>();
            if (hiCheck.Checked) parts.Add("text för hörselskadade tas bort");
            if (plainCheck.Checked) parts.Add("kursiv och annan formatering tas bort");
            if (!fpsCheck.Checked) parts.Add("bildfrekvensfel upptäcks inte");
            if (Srt.SaveFormat == Srt.FormatUtf8) parts.Add("SRT sparas som UTF-8 utan BOM");
            if (Srt.SaveFormat == Srt.FormatLegacy) parts.Add("SRT sparas för mycket gamla spelare");
            optInfo.Text = parts.Count == 0 ? "" : "Inställningar: " + string.Join("  ·  ", parts);
            optInfo.Visible = parts.Count > 0;
        }

        // Byt mellan ljust och mörkt medan programmet körs
        void ApplyTheme()
        {
            if (HC) return;
            bool dark = Theme.WantDark(Theme.Mode);
            if (dark == Theme.Dark) return;
            var map = Theme.Set(dark);
            SuspendLayout();
            Theme.Apply(this, map);
            Theme.ApplyMenu(list.ContextMenuStrip);
            ResumeLayout(true);
            if (result != null) RefreshPreview();   // radfärgerna i listan
            Invalidate(true);
        }

        // Fönstret får aldrig bli större än skärmen (t.ex. laptop med 150 % skalning).
        // Storlek, plats och maximerat läge från förra gången används om de finns (`window` i settings.txt),
        // annars hamnar fönstret mitt på skärmen där muspekaren är.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Rectangle saved, fit = Rectangle.Empty; bool max;
            bool have = Tools.ParseWindow(Tools.GetSetting("window", ""), out saved, out max);
            if (have) fit = Tools.FitWindow(saved, Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), wa, MinimumSize, out wa);
            MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width), Math.Min(MinimumSize.Height, wa.Height));
            if (have)
            {
                Bounds = fit;
                if (max) WindowState = FormWindowState.Maximized;
                return;
            }
            Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        }

        // ------------------------------------------------------------ UI-hjälp
        // Ruta för en fil (1 Referens, 2 Undertext att synka).
        // Den övre delen (rubrik, filnamn, Bläddra och beskedet under) är klickbar överallt och öppnar Välj fil.
        // Det som ligger i extra (listor och länkar) fungerar som vanligt.
        // En fil som släpps någonstans på rutan hamnar i just den rutan, vilken sorts fil det än är.
        TableLayoutPanel MakeCard(string caption, Label name, Button btn, Label info, Control extra, Action browse, Action<string> load)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2,
                BackColor = Card, AllowDrop = true
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize)); card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.DragEnter += OnDragEnter;
            card.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                if (files.Length > 1 || LooksLikeSeason(files)) { RouteFiles(files, false); return; }   // flera filer, eller en mapp (hel säsong)
                load(files[0]);
            };

            var zone = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 3,
                BackColor = Card, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0)
            };
            zone.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            zone.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            zone.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) zone.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var cap = new Label { Text = caption, Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0) };   // luften under läggs till i FitLabelHeight: som marginal blev den dubbelt så stor i ruta 1 vid 200 % skalning
            zone.Controls.Add(cap, 0, 0); zone.SetColumnSpan(cap, 3);
            // ett litet pappersark i länkfärgen framför filnamnet
            var icon = new Panel { Size = new Size(16, 20), Margin = new Padding(1, 5, 8, 0) };
            icon.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                float w = icon.Width - 3, h = icon.Height - 3, f = w * 0.4f, o = 1.5f;
                using (var pen = new Pen(HC ? SystemColors.WindowText : Theme.Link, Math.Max(1.2f, icon.Width / 12f)))
                {
                    g.DrawLines(pen, new[] { new PointF(o, o), new PointF(o + w - f, o), new PointF(o + w, o + f), new PointF(o + w, o + h), new PointF(o, o + h), new PointF(o, o) });
                    g.DrawLines(pen, new[] { new PointF(o + w - f, o), new PointF(o + w - f, o + f), new PointF(o + w, o + f) });
                }
            };
            btn.Margin = new Padding(0);
            zone.Controls.Add(icon, 0, 1); zone.Controls.Add(name, 1, 1); zone.Controls.Add(btn, 2, 1);
            info.Margin = new Padding(0, 6, 0, 0); info.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            zone.Controls.Add(info, 0, 2); zone.SetColumnSpan(info, 3);
            // rubriken och beskedet bryts efter rutans bredd
            FitLabelHeight(cap, 6); FitLabelHeight(info);
            card.Controls.Add(zone, 0, 0);
            if (extra != null) { extra.Dock = DockStyle.Top; extra.Margin = new Padding(12, 0, 12, 10); card.Controls.Add(extra, 0, 1); }

            // klick var som helst i den övre delen väljer fil; den lyser upp när muspekaren är över
            var parts = new Control[] { zone, cap, icon, name, info };
            Action<bool> hot = on => { if (HC) return; foreach (var c in parts) c.BackColor = on ? Theme.HotBg : Card; };
            EventHandler leave = (s, e) => { if (!zone.ClientRectangle.Contains(zone.PointToClient(Cursor.Position))) hot(false); };
            foreach (var c in parts)
            {
                c.Cursor = Cursors.Hand;
                // bara vänsterklick (Click kommer för alla musknappar). Markeringen släcks efteråt: pekaren står kvar över rutan när dialogen stängs.
                c.MouseClick += (s, e) => { if (e.Button != MouseButtons.Left || busy) return; browse(); hot(false); };
                c.MouseEnter += (s, e) => hot(!busy);
                c.MouseLeave += leave;
            }
            btn.MouseLeave += leave;
            return card;
        }

        // Filnamnet i en ruta: en rad som kortas med … om den inte får plats
        Label MakeFileLabel()
        {
            return new Label { AutoSize = false, AutoEllipsis = true, UseMnemonic = false, Height = 28, Font = new Font("Segoe UI", 11F), TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 8, 0) };
        }

        // Visar filens namn i rutan. Hela sökvägen står i tipsrutan när muspekaren hålls över namnet.
        void ShowFile(Label name, string path)
        {
            bool has = !string.IsNullOrEmpty(path);
            string file = path;
            try { if (has) file = Path.GetFileName(path); } catch { }
            name.Text = has ? file : "Ingen fil vald";
            name.ForeColor = has ? Theme.Fg : Muted;
            tips.SetToolTip(name, has ? path : null);
            // skärmläsare: båda knapparna heter Bläddra…, så namnet säger vilken ruta det är och vilken fil som är vald
            var btn = name == refName ? refBtn : tgtBtn;
            string what = name == refName ? "Välj referens" : "Välj undertext att synka";
            name.AccessibleName = btn.AccessibleName = what + (has ? ". Vald fil: " + path : ". Ingen fil vald");
        }

        // Rader under den klickbara delen av en ruta: namn till vänster, lista som fyller resten
        static TableLayoutPanel MakeRows()
        {
            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 4 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return t;
        }

        // En etikett som fyller sin cell på bredden och bryter texten efter den. Höjden räknas ut här.
        // En AutoSize-etikett i en tabell mäts ibland som om den vore några bildpunkter bred: vid 200 % skalning
        // blev ruta 1 och 2 då flera tusen bildpunkter höga och fyllde hela fönstret.
        // below = luft under texten, i bildpunkter vid 100 % skalning
        internal static void FitLabelHeight(Label l, int below = 0)
        {
            l.AutoSize = false;
            EventHandler fit = (s, e) =>
            {
                var flags = TextFormatFlags.WordBreak | (l.UseMnemonic ? TextFormatFlags.Default : TextFormatFlags.NoPrefix);
                int w = Math.Max(20, l.ClientSize.Width - l.Padding.Horizontal);
                int h = TextRenderer.MeasureText(l.Text.Length == 0 ? " " : l.Text, l.Font, new Size(w, 0), flags).Height + l.Padding.Vertical
                    + below * l.Font.Height / 18;   // följer skalningen via typsnittets höjd (cirka 18 bildpunkter vid 100 %)
                if (l.Height != h) { l.Height = h; RelayoutLater(l); }
            };
            l.SizeChanged += fit; l.TextChanged += fit; l.FontChanged += fit;
            fit(l, EventArgs.Empty);
        }

        // När höjden ändras mitt i förälderns egen layout (den har just satt bredden) räknar föräldern inte om sina rader,
        // och raden behåller den gamla höjden. Därför görs layouten om strax efteråt, hela vägen upp till fönstret.
        static void RelayoutLater(Control c)
        {
            if (!c.IsHandleCreated) return;   // före visning: RelayoutAll körs när fönstret visas
            c.BeginInvoke(new Action(() => { for (var p = c.Parent; p != null && !(p is Form); p = p.Parent) p.PerformLayout(); }));
        }

        // Gör om layouten inifrån och ut, så att varje tabell räknar sina rader efter delarnas slutliga höjd
        static void RelayoutAll(Control c)
        {
            foreach (Control ch in c.Controls) RelayoutAll(ch);
            if (c.Controls.Count > 0) c.PerformLayout();
        }

        // En FlowLayoutPanel med AutoSize i en tabell mäts som om varje del stod på en egen rad, och då blir det tomrum under den.
        // Höjden sätts därför här, efter den bredd panelen faktiskt har (flera rader bara när delarna inte får plats på en).
        static void FitFlowHeight(FlowLayoutPanel p)
        {
            p.AutoSize = false;
            EventHandler fit = (s, e) =>
            {
                int h = p.GetPreferredSize(new Size(p.ClientSize.Width, 0)).Height;
                if (p.Height != h) { p.Height = h; RelayoutLater(p); }
            };
            p.Layout += (s, e) => fit(s, e);
            p.SizeChanged += fit;
        }

        // Den öppnade listan blir så bred som sin längsta rad, även när själva rutan är smal
        static void WidenDropDown(object sender, EventArgs e)
        {
            var cb = (ComboBox)sender;
            int w = cb.Width;
            foreach (var it in cb.Items) w = Math.Max(w, TextRenderer.MeasureText(cb.GetItemText(it), cb.Font).Width + SystemInformation.VerticalScrollBarWidth + 8);
            cb.DropDownWidth = w;
        }

        // En siffra i resultatrutan ("51 förslag"). dot = färgen på rutan framför (samma som raderna i listan), pad = plats till vänster om texten.
        Label MakeChip(Func<Color> dot, int pad)
        {
            var l = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 22, 2), Padding = new Padding(pad, 0, 0, 0), Visible = false };
            if (dot != null) l.Paint += (s, e) =>
            {
                int d = l.Padding.Left * 9 / 15;
                using (var b = new SolidBrush(dot())) e.Graphics.FillRectangle(b, 0, (l.Height - d) / 2 + 1, d, d);
            };
            return l;
        }

        // Fäller ut eller ihop Detaljer i resultatrutan
        void SetDetails(bool open)
        {
            detailsOpen = open;
            detailsPanel.Visible = detailsSep.Visible = open;
            detailsBtn.Text = open ? "Dölj &detaljer  ▴" : "&Detaljer  ▾";
            LayoutResult();
        }

        // Meningen efter beskedet flyttas till en egen rad när den inte får plats bredvid. Texterna bryts av FitLabelHeight.
        void LayoutResult()
        {
            if (resBox == null || !resBox.Visible) return;
            int w = Math.Max(300, root.ClientSize.Width - root.Padding.Horizontal - 20);
            int used = verdictLabel.Width + verdictLabel.Margin.Horizontal + summaryLabel.Margin.Horizontal + detailsBtn.Width
                + (weakLinks ? prevWeakLink.Width + prevWeakLink.Margin.Horizontal + nextWeakLink.Width + nextWeakLink.Margin.Horizontal : 0);
            // meningen står bredvid beskedet när den får plats på ungefär en rad där, annars på en egen rad under
            var vrow = (TableLayoutPanel)summaryLabel.Parent;
            bool below = w - used < Math.Min(TextRenderer.MeasureText(summaryLabel.Text, summaryLabel.Font).Width + 8, w * 45 / 100);
            if (below != (vrow.GetRow(summaryLabel) == 1))
            {
                vrow.SuspendLayout();
                vrow.SetColumnSpan(summaryLabel, 1);
                vrow.SetCellPosition(summaryLabel, new TableLayoutPanelCellPosition(below ? 0 : 1, below ? 1 : 0));
                vrow.SetColumnSpan(summaryLabel, below ? 5 : 1);
                summaryLabel.Margin = below ? new Padding(0, 4, 0, 0) : new Padding(0, 4, 14, 0);
                vrow.ResumeLayout();
            }
        }

        internal Button MakeButton(string text, bool primary)
        {
            var b = new Button { Text = text, AutoSize = false, Width = 100, Height = 30, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            if (HC) { b.FlatStyle = FlatStyle.System; b.UseVisualStyleBackColor = true; return b; }
            b.FlatAppearance.BorderSize = 1;
            b.Tag = primary ? "primary" : "secondary";   // Theme.Apply färgar knappen efter detta
            if (primary)
            {
                b.BackColor = Accent; b.ForeColor = Color.White; b.FlatAppearance.BorderColor = Accent;
                b.Font = new Font("Segoe UI Semibold", 9.5F);
                // inte medan Synka-knappen är Avbryt (då är den grå, se ShowSyncButton)
                b.EnabledChanged += (s, e) => { if (!"primary".Equals(b.Tag)) return; b.BackColor = b.Enabled ? Accent : Theme.BtnDisabled; b.FlatAppearance.BorderColor = b.BackColor; };
            }
            else
            {
                b.BackColor = Theme.BtnBg; b.ForeColor = Theme.Fg; b.FlatAppearance.BorderColor = Theme.BtnBorder;
            }
            return b;
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        internal static bool IsMkv(string f) { string x = Path.GetExtension(f).ToLowerInvariant(); return x == ".mkv" || x == ".mks" || x == ".mka"; }
        internal static bool IsSrt(string f) { return Path.GetExtension(f).ToLowerInvariant() == ".srt"; }

        static readonly Dictionary<string, string> Iso3 = new Dictionary<string, string> {
            {"swe","sv"},{"eng","en"},{"nor","no"},{"nob","no"},{"dan","da"},{"fin","fi"},{"ger","de"},{"deu","de"},
            {"fre","fr"},{"fra","fr"},{"spa","es"},{"ita","it"},{"dut","nl"},{"nld","nl"},{"pol","pl"} };
        internal static string Norm(string l)
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
            string orig;
            PlanRoute(files, wantLang, out refFile, out targetFile, out orig, out note);
        }

        // Film + två SRT: den med rätt språkkod synkas, den andra blir originaltext (t.ex. engelska) som kontrolleras mot ljudet.
        public static void PlanRoute(string[] files, string wantLang, out string refFile, out string targetFile, out string origFile, out string note)
        {
            refFile = null; targetFile = null; origFile = null; note = null;
            var mkvs = files.Where(f => IsMkv(f) || Audio.IsVideo(f)).ToList();
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
                        note = "Kontrollera rutorna: språket gick inte att se på filnamnen, så \"" + Path.GetFileName(two[0]) + "\" blev referens.";
                    }
                }
                refFile = two[1 - ti];
                targetFile = two[ti];
            }
            else if (srts.Count >= 2)
            {
                var two = srts.Take(2).ToList();
                int ti = two.FindIndex(f => Norm(GuessLang(f)) == want);
                if (ti < 0)
                {
                    int other = two.FindIndex(f => GuessLang(f) != null);
                    if (other >= 0) ti = 1 - other;
                    else
                    {
                        ti = 1;
                        note = "Kontrollera rutorna: språket gick inte att se på filnamnen, så \"" + Path.GetFileName(two[0]) + "\" blev originaltext.";
                    }
                }
                targetFile = two[ti];
                origFile = two[1 - ti];
                if (srts.Count > 2) note = "Bara två SRT-filer används: \"" + Path.GetFileName(targetFile) + "\" synkas och \"" + Path.GetFileName(origFile) + "\" blir originaltext.";
            }
            else if (srts.Count >= 1)
                targetFile = srts.FirstOrDefault(f => Norm(GuessLang(f)) == want) ?? srts[0];
        }

        // En mapp, eller minst två filmer, är en hel säsong: de hamnar i fönstret Hel säsong i stället för i ruta 1 och 2
        public static bool LooksLikeSeason(string[] files)
        {
            if (files == null) return false;
            int films = 0;
            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f)) continue;
                try { if (Directory.Exists(f)) return true; } catch { }
                if (IsMkv(f) || Audio.IsVideo(f)) films++;
            }
            return films >= 2;
        }

        // Öppnar fönstret Hel säsong, med paths (mappar eller filer) tillagda i listan. Inställningarna följer huvudfönstret.
        void OpenSeason(string[] paths)
        {
            if (busy) return;
            StopPlay();
            string lang = langBox.Text.Trim().ToLowerInvariant();
            if (!Regex.IsMatch(lang, @"^[a-z]{2,3}(-[a-z0-9]{2,8})*$")) lang = "sv";
            lang = Norm(lang);   // "swe" → "sv", så att filerna heter lika hur koden än står i rutan
            // ett nytt fönster om utseendet har bytts sedan det byggdes (listan töms då, men det som är sparat går fortfarande att återställa)
            if (seasonDlg != null && (seasonDlg.IsDisposed || seasonDlg.BuiltDark != Theme.Dark)) { seasonDlg.Dispose(); seasonDlg = null; }
            if (seasonDlg == null) seasonDlg = new SeasonDialog(this);
            seasonDlg.Prepare(new SeasonOptions { Lang = lang, Mode = (SyncMode)modeCombo.SelectedIndex, DetectFramerate = fpsCheck.Checked, RemoveHi = hiCheck.Checked, StripFormatting = plainCheck.Checked }, paths);
            Log.Info("Öppnar Hel säsong" + (paths != null ? ": " + string.Join("; ", paths) : ""));
            var res = seasonDlg.ShowDialog(this);
            // "Öppna i huvudfönstret": läs in avsnittet och synka, så att raderna går att granska
            if (res == DialogResult.OK && seasonDlg.OpenRef != null)
            {
                string rf = seasonDlg.OpenRef, tf = seasonDlg.OpenTarget;
                LoadReference(rf);
                LoadTarget(tf);
                langBox.Text = lang;   // LoadTarget gissar koden ur filnamnet ("swe"): Spara SRT… ska föreslå samma fil som säsongssynken sparade
                if (syncBtn.Enabled) BeginInvoke(new Action(async () => await DoSync()));
            }
        }

        public async void RouteFiles(string[] files, bool autoSync)
        {
            if (busy || files == null || files.Length == 0) return;
            // startas efter att dra-och-släpp är klart, så att Utforskaren inte står låst medan fönstret är öppet
            if (LooksLikeSeason(files)) { BeginInvoke(new Action(() => OpenSeason(files))); return; }
            string rf, tf, of, note;
            // filmen ligger redan i ruta 1 och bara SRT-filer dras in: behåll filmen, den andra SRT:n blir originaltext
            bool keepFilm = mkvPath != null && !files.Any(f => IsMkv(f) || Audio.IsVideo(f)) && files.Count(IsSrt) >= 2;
            PlanRoute(keepFilm ? new[] { mkvPath }.Concat(files).ToArray() : files, langBox.Text.Trim(), out rf, out tf, out of, out note);
            if (rf != null && !keepFilm) LoadReference(rf);
            if (tf != null) LoadTarget(tf);
            if (of != null)
            {
                if (mkvPath != null) AddOrig(of);
                else if (note == null) note = "\"" + Path.GetFileName(of) + "\" användes inte – filmen kunde inte läsas.";
            }
            if (note != null) { statusLabel.Text = note; statusLabel.ForeColor = ErrColor; }
            // startas efter att dra-och-släpp är klart, så att Utforskaren inte står låst om en varningsruta visas
            if (autoSync && syncBtn.Enabled && files.Length >= 2) BeginInvoke(new Action(async () => await DoSync()));
        }

        internal static string Fmt(double v, string f) { return v.ToString(f, CultureInfo.GetCultureInfo("sv-SE")); }

        internal static string FmtOffset(double ms)
        {
            string sign = ms >= 0 ? "+" : "−";
            return sign + Fmt(Math.Abs(ms) / 1000.0, "0.000") + " s";
        }

        internal static string Friendly(Exception ex)
        {
            if (ex is UserMessageException) return ex.Message;
            if (ex is AggregateException && ex.InnerException != null) return Friendly(ex.InnerException);
            if (ex is FileNotFoundException) return "Filen hittades inte.";
            if (ex is UnauthorizedAccessException) return "Text Sync har inte behörighet till filen eller mappen.";
            if (ex is IOException) return "Filen kunde inte läsas eller skrivas. Den kan vara öppen i ett annat program, eller så är disken full.";
            if (ex is InvalidDataException) return ex.Message;
            if (ex is OutOfMemoryException) return "Filen är för stor eller trasig.";
            return ex.Message;
        }

        internal static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        internal static void ShowInFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        void SetStatus(string text, bool ok)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = ok ? OkColor : Muted;
        }


        void UpdateButtons()
        {
            if (syncBtn == null) return;
            bool haveRef = refSrtCues != null || (mkvPath != null && trackCombo.SelectedItem != null);
            // medan programmet arbetar är knappen som startade arbetet Avbryt (Synka om inget annat är sagt)
            var owner = busyBtn ?? syncBtn;
            syncBtn.Enabled = busy ? owner == syncBtn && !cancelling : haveRef && tgtCues != null;
            saveBtn.Enabled = !busy && synced != null;
            muxBtn.Enabled = busy ? owner == muxBtn && !cancelling : synced != null;
            foreach (Control c in new Control[] { refBtn, tgtBtn, trackCombo, modeCombo, settingsBtn, seasonBtn, extraNum, langBox, encCombo, gpuCombo, audioCombo, origLink })
                if (c != null) c.Enabled = !busy;
            gpuCombo.Enabled = !busy && !gpuPending;
        }

        void SetBusy(bool b, string status)
        {
            if (b && !busy) busySince = Environment.TickCount;
            busy = b; cancelling = false;
            if (!b) busyBtn = null;
            progress.Visible = b; progress.Value = 0;
            ShowSyncButton();
            if (b) { openLink.Visible = false; undoLink.Visible = false; }
            statusLabel.ForeColor = Muted;
            statusLabel.Text = status ?? "";
            UseWaitCursor = b;
            UpdateButtons();
        }

        // Synka-knappen är Avbryt så länge programmet arbetar (synk, nedladdning, lägga in i MKV): samma knapp på samma plats,
        // grå i stället för blå. Efter ett klick står det Avbryter… och knappen går inte att klicka förrän arbetet har stannat.
        // Startades arbetet med Lägg in i MKV… är det den knappen som blir Avbryt (busyBtn), så att Avbryt finns där man just klickade.
        void ShowSyncButton()
        {
            var owner = busy ? (busyBtn ?? syncBtn) : null;
            string cancelText = cancelling ? "Avbryter…" : "■  &Avbryt";
            syncBtn.Text = owner == syncBtn ? cancelText : "&Synka";
            if (muxBtn != null) muxBtn.Text = owner == muxBtn ? cancelText : "Lägg in i &MKV…";
            if (HC) return;
            syncBtn.Tag = owner == syncBtn ? "secondary" : "primary";
            Theme.Apply(syncBtn, null);   // färgerna följer Tag
            syncBtn.ForeColor = owner == syncBtn ? Theme.Fg : Color.White;
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
            cancelling = true;
            ShowSyncButton(); (busyBtn ?? syncBtn).Enabled = false;
            statusLabel.Text = "Avbryter…";
            var p = muxProc;
            if (p != null) { try { if (!p.HasExited) p.Kill(); } catch { } }
        }

        void ShowHelp()
        {
            var a = MessageBox.Show(this,
                "Så här gör du:\n" +
                "1. Referens: välj filmen (MKV) – eller en SRT som redan går i takt. Klicka i rutan eller dra in filen.\n" +
                "2. Välj undertexten som inte går i takt.\n" +
                "3. Klicka Synka. Medan programmet arbetar är samma knapp Avbryt. Beskedet ovanför listan säger om synken blev bra, och Detaljer bredvid visar förskjutningen, vad färgerna i listan betyder och hur du ändrar rader. Står det \"Kontrollera\" hoppar du mellan ställena med länkarna bredvid (märkta ⚠ i listan).\n" +
                "4. Spara SRT: sparas som Filmnamn.sv.srt bredvid filmen, så hittar spelaren den.\n\n" +
                "Klicka på ▶ på en rad (eller tryck P) för att spela filmen 5 sekunder före och efter raden, med texten ovanpå. Esc stänger filmfönstret.\n\n" +
                "Har filmen ingen engelsk text men du har en engelsk SRT som inte går i takt: klicka \"Lägg till undertext på filmens originalspråk\" " +
                "(eller dra in filmen och båda SRT-filerna). Den engelska synkas då mot filmens ljud och kontrolleras med Whisper, och sedan synkas den svenska mot den.\n\n" +
                "Inställningar (knappen längst upp till höger): \"Ta bort text för hörselskadade\" tar bort [MUSIK], (skrattar) och talarnamn som JOHN: ur texten. Resultatet säger hur många rader som togs bort. " +
                "Där väljer du också ljust eller mörkt utseende.\n\n" +
                "Kodade tecken som &amp; i texten görs alltid om till riktiga tecken (&), och trasig formatering som <i> utan avslut lagas. Sådana rader är märkta ✎ i listan: håll muspekaren över raden för att se vad som stod i filen. " +
                "Visar din TV <i> som text? Kryssa i \"Ta bort kursiv och annan formatering\" i Inställningar. Kryssar du ur rutan kommer formateringen tillbaka.\n\n" +
                "Hel säsong (knappen Hel säsong… uppe till höger, eller dra in en mapp): alla avsnitt synkas i kö och varje undertext sparas bredvid sitt avsnitt. " +
                "Lägg in i MKV… i det fönstret lägger in de sparade undertexterna i en kopia av varje film. Där väljer du också om osäkra avsnitt ska sparas. Återställ… i det fönstret ångrar allt: filerna som fanns före kommer tillbaka och nya undertexter tas bort. Det går även efter att programmet har stängts.\n\n" +
                "Tips: dra in båda filerna samtidigt. Rödmarkerade rader saknar engelsk text samtidigt – ofta helt i sin ordning (t.ex. utrop).\n" +
                "Kryssa i \"Föreslå radindelning som referensen\" så föreslår programmet att dela eller slå ihop rader som de engelska (blå, \"Dela?\"/\"Ihop?\") – högerklicka för att godkänna. " +
                "Under varje förslag står hur raden blir om du godkänner det (→ blir). " +
                "Visa ovanför listan döljer grå rader eller visar bara röda eller blå. Sök (Ctrl+F) letar i texten, Enter hoppar till nästa träff.\n\n" +
                "Verktygen för synk mot ljudet (0,6–1,4 GB) ligger i mappen tools i " + Log.Dir + ". Mappen kan tas bort när Text Sync är stängt, och verktygen hämtas igen vid behov.\n\n" +
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
                StopPlay();
                var p = muxProc;
                if (p != null) { try { p.WaitForExit(5000); } catch { } }
                CleanupMux(true);
            }
            else if (unsaved && synced != null)
            {
                var a = MessageBox.Show(this, "Den synkade undertexten är inte sparad. Vill du stänga ändå?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (a != DialogResult.Yes) { e.Cancel = true; return; }
            }
            StopPlay();
            // kom ihåg storlek och plats till nästa gång. Är fönstret maximerat sparas storleken det hade före.
            if (WindowState != FormWindowState.Minimized || !RestoreBounds.IsEmpty)
                Tools.SetSetting("window", Tools.FormatWindow(WindowState == FormWindowState.Normal ? Bounds : RestoreBounds, WindowState == FormWindowState.Maximized));
            base.OnFormClosing(e);
        }
    }
}
