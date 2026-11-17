// Text Sync - fönstret Nyheter: vad som är ändrat i varje version, den nyaste först (texten kommer från releaserna på GitHub)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TextSync
{
    public class NewsDialog : Form
    {
        // DialogResult.Yes = användaren klickade Ladda ner. Knappen finns bara när en nyare version finns.
        public NewsDialog(List<UpdateCheck.Release> all, string current)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Nyheter i Text Sync";
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Card; ForeColor = Theme.Fg;
            ClientSize = new Size(640, 520);
            MinimumSize = new Size(380, 260);

            int newer = UpdateCheck.NewerCount(all, current);
            string latest = all != null && all.Count > 0 ? all[0].Version : current;
            var head = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(16, 14, 16, 0), UseMnemonic = false,
                Font = new Font("Segoe UI Semibold", 10F),
                Text = newer > 0 ? "Den senaste versionen är " + latest + ". Du har " + current + "."
                                 : "Du har den senaste versionen, " + current + "."
            };
            // skrivskyddad textruta: går att rulla och att kopiera ur
            var box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                Text = UpdateCheck.NewsText(all, current), TabStop = false, AccessibleName = "Vad som är ändrat i varje version"
            };
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 4, 8) };
            wrap.Controls.Add(box);

            var close = new Button { Text = "Stäng", Width = 100, Height = 30, DialogResult = DialogResult.Cancel, TabIndex = 1 };
            var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Right, WrapContents = false, Margin = new Padding(0) };
            btns.Controls.Add(close);
            Button get = null;
            if (newer > 0)
            {
                get = new Button { Text = "Ladda ner " + latest, AutoSize = true, MinimumSize = new Size(150, 30), Height = 30, DialogResult = DialogResult.Yes, TabIndex = 0, Tag = "primary", FlatStyle = FlatStyle.Flat };
                btns.Controls.Add(get);
            }
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(16, 10, 13, 10) };
            bar.Controls.Add(btns);
            bar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.BtnBorder });

            Controls.Add(wrap); Controls.Add(head); Controls.Add(bar);
            AcceptButton = get ?? close; CancelButton = close;
            Theme.Apply(this, null);
            if (get != null && !Theme.HC) get.ForeColor = Color.White;
            ResumeLayout(true);
            Shown += (s, e) => { box.Select(0, 0); (get ?? close).Focus(); };
        }

        // Dialogen blir aldrig större än skärmen
        protected override void OnLoad(EventArgs e)
        {
            var wa = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
            Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
            base.OnLoad(e);
            Location = new Point(Math.Max(wa.Left, Math.Min(Left, wa.Right - Width)), Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height)));
        }
    }
}
