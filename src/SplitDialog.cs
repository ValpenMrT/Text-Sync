// Text Sync - dialog för att dela en rad i två: markören i texten visar var raden delas
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TextSync
{
    public class SplitDialog : Form
    {
        readonly TextBox textBox, timeBox;
        readonly Label partA, partB, err;
        readonly Button ok;
        readonly double start, end;
        bool ownTime;     // tiden är skriven för hand: den följer inte längre markören
        bool quiet;
        public string PartA, PartB;
        public double MidMs;   // första delen slutar och andra börjar här

        public SplitDialog(string title, double start, double end, string text)
        {
            this.start = start; this.end = end;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = title;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 400);
            BackColor = Theme.Card; ForeColor = Theme.Fg;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14, 12, 14, 12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            t.Controls.Add(new Label
            {
                Text = "&Klicka i texten där raden ska delas, eller flytta markören med piltangenterna.",
                AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(0, 0, 0, 6)
            }, 0, 0);
            // skrivskyddad: texten ändras med Ändra rad…, här väljs bara stället
            textBox = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = true, HideSelection = false,
                Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), Dock = DockStyle.Fill,
                Margin = new Padding(0), AccessibleName = "Text att dela"
            };
            t.Controls.Add(textBox, 0, 1);
            partA = new Label { Dock = DockStyle.Fill, UseMnemonic = false, AutoEllipsis = true, Margin = new Padding(0, 10, 0, 0) };
            partB = new Label { Dock = DockStyle.Fill, UseMnemonic = false, AutoEllipsis = true, Margin = new Padding(0, 6, 0, 0) };
            t.Controls.Add(partA, 0, 2);
            t.Controls.Add(partB, 0, 3);

            var trow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            trow.Controls.Add(new Label { Text = "&Rad 2 börjar:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            timeBox = new TextBox { Width = 120, AccessibleName = "Tiden då rad 2 börjar" };
            trow.Controls.Add(timeBox);
            trow.Controls.Add(new Label
            {
                Text = "uppskattad – ändra om den inte stämmer",
                AutoSize = true, ForeColor = Theme.Dark ? Theme.Muted : SystemColors.GrayText, Margin = new Padding(8, 6, 0, 0)
            });
            t.Controls.Add(trow, 0, 4);

            err = new Label { AutoSize = true, ForeColor = Theme.Err, Margin = new Padding(0, 6, 0, 0) };
            t.Controls.Add(err, 0, 5);

            ok = new Button { Text = "Dela", Width = 90, Height = 30 };
            var cancel = new Button { Text = "Avbryt", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Validate2();
            var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok); ok.TabIndex = 0; cancel.TabIndex = 1;
            t.Controls.Add(btns, 0, 6);

            Controls.Add(t);
            AcceptButton = ok;    // Enter = Dela
            CancelButton = cancel;
            Theme.Apply(this, null);   // mörkt läge: textfält, knappar och titelrad
            ResumeLayout(true);

            // textrutan säger inte till när markören flyttas: följ musen och tangenterna
            textBox.MouseDown += (s, e) => ShowParts();
            textBox.MouseUp += (s, e) => ShowParts();
            textBox.MouseMove += (s, e) => { if (e.Button != MouseButtons.None) ShowParts(); };
            textBox.KeyDown += (s, e) => BeginInvoke((Action)ShowParts);   // efter att markören har flyttats
            textBox.KeyUp += (s, e) => ShowParts();
            timeBox.TextChanged += (s, e) => { if (quiet) return; ownTime = true; ShowParts(); };
            Shown += (s, e) =>
            {
                textBox.Focus();
                textBox.Select(Math.Max(0, LineAligner.DefaultSplit(textBox.Text)), 0);
                ShowParts();
            };
        }

        // Delarna som markören ger just nu. null = markören står inte mellan två ord.
        string[] Parts()
        {
            int pos = LineAligner.SnapSplit(textBox.Text, textBox.SelectionStart);
            return pos < 0 ? null : LineAligner.SplitAt(textBox.Text, pos);
        }

        // Visar hur de två raderna blir, med tider
        void ShowParts()
        {
            if (IsDisposed) return;
            var p = Parts();
            ok.Enabled = p != null;
            if (p == null)
            {
                partA.Text = "Ställ markören mellan två ord, så visas de två raderna här.";
                partB.Text = "";
                return;
            }
            if (!ownTime)
            {
                quiet = true;
                timeBox.Text = Srt.FormatTime(LineAligner.SplitTime(start, end, p[0], p[1]));
                quiet = false;
            }
            double mid;
            string midText = Srt.TryParseTime(timeBox.Text, out mid) && mid > start && mid < end ? Srt.FormatTime(mid) : "?";
            partA.Text = "Rad 1:  " + Srt.FormatTime(start) + " – " + midText + "\n" + p[0];
            partB.Text = "Rad 2:  " + midText + " – " + Srt.FormatTime(end) + "\n" + p[1];
            err.Text = "";
        }

        void Validate2()
        {
            var p = Parts();
            if (p == null) { Fail(textBox, "Ställ markören mellan två ord i texten."); return; }
            double mid;
            if (!Srt.TryParseTime(timeBox.Text, out mid)) { Fail(timeBox, "Skriv tiden som 00:01:23,456."); return; }
            if (mid <= start || mid >= end) { Fail(timeBox, "Tiden ska ligga mellan " + Srt.FormatTime(start) + " och " + Srt.FormatTime(end) + "."); return; }
            PartA = p[0]; PartB = p[1]; MidMs = mid;
            DialogResult = DialogResult.OK;
        }

        void Fail(Control c, string msg)
        {
            err.Text = msg;
            c.Focus();
            if (c == timeBox) timeBox.SelectAll();
        }
    }
}
