// Text Sync - varningsruta med egna knapptexter (t.ex. "Synka ändå" och "Avbryt"), som följer ljust och mörkt läge
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TextSync
{
    public class ConfirmDialog : Form
    {
        // Avbryt är förvald knapp, så att Enter aldrig går vidare av misstag
        public ConfirmDialog(string title, string message, string okText, string cancelText, string altText = null)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = title;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Card; ForeColor = Theme.Fg;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Padding = new Padding(16, 14, 16, 12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var icon = new PictureBox { Image = SystemIcons.Warning.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 2, 14, 0) };
            var msg = new Label { Text = message, AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(0, 0, 0, 14) };
            t.Controls.Add(icon, 0, 0); t.Controls.Add(msg, 1, 0);

            var ok = new Button { Text = okText, AutoSize = true, MinimumSize = new Size(110, 30), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = cancelText, AutoSize = true, MinimumSize = new Size(110, 30), DialogResult = DialogResult.Cancel };
            var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            btns.Controls.Add(cancel);
            // ett andra val (t.ex. "Använd ljud A"), mellan det första valet och Avbryt
            if (altText != null) btns.Controls.Add(new Button { Text = altText, AutoSize = true, MinimumSize = new Size(110, 30), DialogResult = DialogResult.Yes, TabIndex = 2 });
            btns.Controls.Add(ok); cancel.TabIndex = 0; ok.TabIndex = 1;
            t.Controls.Add(btns, 0, 1); t.SetColumnSpan(btns, 2);

            Controls.Add(t);
            AcceptButton = cancel; CancelButton = cancel;
            Theme.Apply(this, null);
            ResumeLayout(true);
            Shown += (s, e) => cancel.Focus();
        }

        // Två val och Avbryt. Returnerar 1 för det första valet, 2 för det andra och 0 för Avbryt.
        public static int Choose(IWin32Window owner, string title, string message, string firstText, string secondText, string cancelText)
        {
            using (var d = new ConfirmDialog(title, message, firstText, cancelText, secondText))
            {
                var r = d.ShowDialog(owner);
                return r == DialogResult.OK ? 1 : r == DialogResult.Yes ? 2 : 0;
            }
        }

        public static bool Ask(IWin32Window owner, string title, string message, string okText, string cancelText)
        {
            using (var d = new ConfirmDialog(title, message, okText, cancelText))
                return d.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
