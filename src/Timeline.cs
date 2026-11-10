// Text Sync - tidslinjen under listan
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
            BackColor = Theme.Card;
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
            var muted = new SolidBrush(HC ? SystemColors.WindowText : Theme.Dark ? Theme.Muted : Color.FromArgb(110, 110, 110));
            int fh = Font.Height;
            float rowH = Math.Max(fh, (Height - fh - 12) / 3f);

            double tick = Span <= 20000 ? 1000 : Span <= 90000 ? 5000 : Span <= 300000 ? 30000 : 60000;
            using (var pen = new Pen(Theme.Grid))
                for (double t = Math.Ceiling(t0 / tick) * tick; t <= t1; t += tick)
                {
                    float x = X(t);
                    g.DrawLine(pen, x, 4, x, Height - fh - 4);
                    if (t >= 0) g.DrawString(Srt.FormatTime(t).Substring(0, 8), Font, muted, x - fh * 1.5f, Height - fh - 2);
                }

            string[] names = { "Referens", "Original", "Synkad" };
            Color[] cols = HC
                ? new[] { SystemColors.Highlight, SystemColors.GrayText, SystemColors.HotTrack }
                : new[] { Theme.TlRef, Theme.TlOrig, Theme.TlSynced };
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
                            using (var hp = new Pen(Theme.TlHighlight, 2)) g.DrawRectangle(hp, rect.X, rect.Y, rect.Width, rect.Height);
                    }
            }
            if (Ref == null)
                g.DrawString("Tidslinjen visar referensen, originalet och resultatet här efter synk. Scrolla eller +/− = zooma, dra eller piltangenter = flytta.", Font, muted, lw, Height / 2f - 8);
            muted.Dispose();
        }
    }
}
