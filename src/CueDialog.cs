// Text Sync - dialog för att ändra eller lägga till en rad (text + tider)
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TextSync
{
    public class CueDialog : Form
    {
        readonly TextBox startBox, endBox, textBox;
        readonly Label err;
        public double StartMs, EndMs;
        public string CueText;

        public CueDialog(string title, double start, double end, string text)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = title;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 300);
            BackColor = Color.White;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, Padding = new Padding(14, 12, 14, 12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            startBox = new TextBox { Text = Srt.FormatTime(start), Width = 120, Anchor = AnchorStyles.Left, AccessibleName = "Starttid" };
            endBox = new TextBox { Text = Srt.FormatTime(end), Width = 120, Anchor = AnchorStyles.Left, AccessibleName = "Sluttid" };
            var hint = new Label { Text = "tim:min:sek,ms", AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Left };
            t.Controls.Add(new Label { Text = "&Start:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
            t.Controls.Add(startBox, 1, 0);
            t.Controls.Add(hint, 2, 0);
            t.Controls.Add(new Label { Text = "S&lut:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 1);
            t.Controls.Add(endBox, 1, 1);

            textBox = new TextBox
            {
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0), AccessibleName = "Text"
            };
            var tl = new Label { Text = "&Text:", AutoSize = true, Margin = new Padding(0, 10, 8, 0) };
            t.Controls.Add(tl, 0, 2);
            t.Controls.Add(textBox, 1, 2); t.SetColumnSpan(textBox, 2);

            err = new Label { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(0, 6, 0, 0) };
            t.Controls.Add(err, 1, 3); t.SetColumnSpan(err, 2);

            var ok = new Button { Text = "OK", Width = 90, Height = 30 };
            var cancel = new Button { Text = "Avbryt", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Validate2();
            var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok); ok.TabIndex = 0; cancel.TabIndex = 1;
            t.Controls.Add(btns, 0, 4); t.SetColumnSpan(btns, 3);

            Controls.Add(t);
            AcceptButton = ok;    // Enter i tidsfälten = OK (i textfältet blir Enter ny rad)
            CancelButton = cancel;
            ResumeLayout(true);
            Shown += (s, e) => { textBox.Focus(); textBox.SelectionStart = textBox.TextLength; };
        }

        void Validate2()
        {
            double s, e;
            if (!Srt.TryParseTime(startBox.Text, out s)) { Fail(startBox, "Skriv starttiden som 00:01:23,456."); return; }
            if (!Srt.TryParseTime(endBox.Text, out e)) { Fail(endBox, "Skriv sluttiden som 00:01:25,000."); return; }
            if (e <= s) { Fail(endBox, "Sluttiden måste vara efter starttiden."); return; }
            string txt = textBox.Text.Replace("\r\n", "\n").Trim('\n', ' ');
            if (txt.Length == 0) { Fail(textBox, "Skriv en text (vill du ta bort raden: använd Delete i listan)."); return; }
            StartMs = s; EndMs = e; CueText = txt;
            DialogResult = DialogResult.OK;
        }

        void Fail(Control c, string msg)
        {
            err.Text = msg;
            c.Focus();
            var tb = c as TextBox; if (tb != null) tb.SelectAll();
        }
    }
}
