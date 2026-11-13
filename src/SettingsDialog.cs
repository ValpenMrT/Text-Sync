// Text Sync - dialogen Inställningar: val som görs en gång och sparas till nästa gång
// (utseende, bildfrekvensfel, text för hörselskadade, formatering). Metod och Synka står kvar i huvudfönstret.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TextSync
{
    public class SettingsDialog : Form
    {
        readonly ComboBox themeCombo;
        readonly CheckBox fpsCheck, hiCheck, plainCheck, meaningCheck, autoCheck;
        public bool AutoAccept { get { return autoCheck.Checked; } }
        readonly ComboBox engineCombo;
        public bool UseParakeet { get { return engineCombo.SelectedIndex == 1; } }   // text ur ljudet: 0 = Whisper, 1 = Parakeet
        public bool CompareMeaning { get { return meaningCheck.Checked; } }

        public int ThemeIndex { get { return themeCombo.SelectedIndex; } }   // 0 = som Windows, 1 = ljust, 2 = mörkt
        public bool DetectFps { get { return fpsCheck.Checked; } }
        public bool RemoveHi { get { return hiCheck.Checked; } }
        public bool StripFormatting { get { return plainCheck.Checked; } }
        public int SrtFormat { get { return Math.Max(0, formatCombo.SelectedIndex); } }   // Srt.FormatUtf8Bom, FormatUtf8 eller FormatLegacy
        readonly ComboBox formatCombo;

        // valen i listan "SRT-filen sparas för", i samma ordning som Srt.Format-konstanterna
        public static readonly string[] FormatNames = {
            "De flesta spelare – Plex, Jellyfin, VLC, Kodi, TV (UTF-8 med BOM)",
            "UTF-8 utan BOM – om första raden börjar med skräptecken",
            "Mycket gamla spelare – Windows-teckentabell efter språket" };

        // showTheme = false i högkontrastläge, där Windows färger alltid används
        public SettingsDialog(int themeIndex, bool showTheme, bool detectFps, bool removeHi, bool stripFormatting, int srtFormat, bool compareMeaning, bool autoAccept, bool useParakeet)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Inställningar";
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Card; ForeColor = Theme.Fg;
            Color muted = Theme.Dark ? Theme.Muted : SystemColors.GrayText;

            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<string, Label> head = s => new Label { Text = s, AutoSize = true, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0, 10, 0, 4) };
            Func<string, Label> note = s => new Label { Text = s, AutoSize = true, ForeColor = muted, MaximumSize = new Size(520, 0), Margin = new Padding(20, 0, 0, 6), UseMnemonic = false };

            themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 2, 0, 0), AccessibleName = "Utseende" };
            themeCombo.Items.AddRange(new object[] { "Som Windows", "Ljust", "Mörkt" });
            themeCombo.SelectedIndex = Math.Max(0, Math.Min(2, themeIndex));
            if (showTheme)
            {
                var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
                row.Controls.Add(new Label { Text = "&Utseende:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
                row.Controls.Add(themeCombo);
                t.Controls.Add(row);
            }

            t.Controls.Add(head("Synk"));
            fpsCheck = new CheckBox { Text = "Upptäck &bildfrekvensfel (25 ↔ 23,976 fps m.fl.)", AutoSize = true, Checked = detectFps, Margin = new Padding(0, 2, 0, 0) };
            t.Controls.Add(fpsCheck);
            t.Controls.Add(note("Ska nästan alltid vara ikryssad. Kryssa ur om synken blir fel på en text som redan har rätt hastighet."));
            meaningCheck = new CheckBox { Text = "&Jämför också vad raderna betyder (när referensen är en text)", AutoSize = true, Checked = compareMeaning, Margin = new Padding(0, 2, 0, 0) };
            t.Controls.Add(meaningCheck);
            long mb = (Tools.LlamaCpu.Size + Tools.ModelBgeM3.Size) / 1048576;
            t.Controls.Add(note("Efter den vanliga synken paras raderna ihop efter betydelse, och rader som ligger fel flyttas. " +
                (Tools.MeaningReady() ? "Verktygen finns redan på datorn." : "Första gången frågar programmet om det får hämta gratisverktyg (cirka " + mb + " MB).") +
                " Det tar längre tid första gången en text synkas. Det är nytt och bara utprovat på en film, så kontrollera resultatet."));

            autoCheck = new CheckBox { Text = "&Godkänn förslag om radindelning automatiskt (när referensen är en text)", AutoSize = true, Checked = autoAccept, Margin = new Padding(0, 2, 0, 0) };
            t.Controls.Add(autoCheck);
            t.Controls.Add(note("Rader delas, slås ihop och får repliker flyttade så att de följer referensens rader och tider (Dela?, Ihop? och Flytta?), utan att du godkänner varje förslag. " +
                "Gäller både en enskild fil och Hel säsong. I huvudfönstret ångrar Ctrl+Z, i Hel säsong Återställ…. " +
                "Delningar och sammanslagningar jämför bara tider och längd, så en delning kan ibland hamna fel."));

            t.Controls.Add(head("Text ur ljudet"));
            engineCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 500, Margin = new Padding(0, 2, 0, 4), AccessibleName = "Text ur ljudet skrivs med" };
            engineCombo.Items.AddRange(new object[] {
                "Whisper – över 90 språk, snabb med NVIDIA-grafikkort",
                "Parakeet – snabb utan grafikkort, 25 europeiska språk (rekommenderas)" });
            engineCombo.SelectedIndex = useParakeet ? 1 : 0;
            t.Controls.Add(new Label { Text = "Te&xt ur ljudet skrivs med:", AutoSize = true, Margin = new Padding(0, 2, 0, 2) });
            t.Controls.Add(engineCombo);
            long pkMb = (Tools.SherpaOnnx.Size + Tools.ModelParakeet.Size) / 1048576;
            t.Controls.Add(note("Gäller \"Skapa text ur ljudet…\" och valet Ljud C i listan Referens. Parakeet tar ett par minuter per avsnitt utan grafikkort, " +
                "där Whisper tar ungefär lika lång tid som filmen. I ett provat engelskt avsnitt blev Parakeets text och tider något bättre än Whispers. " +
                "Parakeet klarar bland annat svenska, norska, danska, finska, engelska, tyska, franska och spanska. " +
                (Tools.ParakeetNeeds().Count == 0 ? "Verktygen för Parakeet finns redan på datorn." : "Första gången frågar programmet om det får hämta Parakeet (cirka " + pkMb + " MB).")));

            t.Controls.Add(head("Texten"));
            hiCheck = new CheckBox { Text = "Ta bort text för &hörselskadade ([MUSIK], (skrattar), talarnamn)", AutoSize = true, Checked = removeHi, Margin = new Padding(0, 2, 0, 0) };
            t.Controls.Add(hiCheck);
            t.Controls.Add(note("Resultatet säger hur många rader som ändrades och togs bort."));
            plainCheck = new CheckBox { Text = "Ta bort &kursiv och annan formatering (för TV som visar <i> som text)", AutoSize = true, Checked = stripFormatting, Margin = new Padding(0, 2, 0, 0) };
            t.Controls.Add(plainCheck);
            t.Controls.Add(note("Gäller det som visas, spelas upp och sparas. Kryssar du ur rutan kommer formateringen tillbaka."));
            t.Controls.Add(note("Kodade tecken som &amp; och trasig formatering rättas alltid när en text läses in. Sådana rader är märkta ✎ i listan."));

            t.Controls.Add(head("Spara"));
            formatCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 500, Margin = new Padding(0, 2, 0, 4), AccessibleName = "SRT-filen sparas för" };
            formatCombo.Items.AddRange(FormatNames);
            formatCombo.SelectedIndex = Math.Max(0, Math.Min(FormatNames.Length - 1, srtFormat));
            t.Controls.Add(new Label { Text = "SRT-&filen sparas för:", AutoSize = true, Margin = new Padding(0, 2, 0, 2) });
            t.Controls.Add(formatCombo);
            t.Controls.Add(note("Det första valet fungerar nästan överallt. Välj det sista bara om å, ä och ö blir fel i en gammal dvd-spelare eller TV: " +
                "då sparas filen i den teckentabell som passar textens språk, och tecken som inte finns där (t.ex. ♪) byts mot liknande. Lägg in i MKV… påverkas inte."));

            var ok = new Button { Text = "OK", Width = 90, Height = 30, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Avbryt", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 10, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok); ok.TabIndex = 0; cancel.TabIndex = 1;
            t.Controls.Add(btns);

            Controls.Add(t);
            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this, null);   // mörkt läge: listor, knappar och titelrad
            ResumeLayout(true);
        }
    }
}
