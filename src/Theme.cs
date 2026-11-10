using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TextSync
{
    // Färger för ljust och mörkt läge. Valet "theme" i settings.txt: auto (som Windows), light eller dark.
    // Högkontrastläge i Windows går alltid före: då används systemets färger och mörkt läge stängs av.
    public static class Theme
    {
        public static readonly bool HC = SystemInformation.HighContrast;
        public static bool Dark { get; private set; }

        // färger som byts ut på kontrollerna när läget ändras (gammal färg → ny färg)
        public static Color Bg, Card, Fg, Muted, Err, Ok, Title, Warn;
        // färger som sätts efter typ av kontroll eller används vid ritning
        public static Color Accent, Link, Input, BtnBg, BtnBorder, BtnDisabled, RedRow, RefOnly, Prop;
        public static Color Grid, TlRef, TlOrig, TlSynced, TlHighlight;
        // när muspekaren är över: listrad, knappar (vanlig/blå) och nedtryckt knapp
        public static Color HotBg, BtnHover, BtnDown, AccentHover, AccentDown;
        public static readonly Color SelBg = Color.FromArgb(0, 95, 170);   // markerad rad i listan i mörkt läge (vit text)

        static Theme() { Set(false); }

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        // Byter palett och returnerar vilken gammal färg som motsvarar vilken ny
        public static Dictionary<int, Color> Set(bool dark)
        {
            var old = new[] { Bg, Card, Fg, Muted, Err, Ok, Title, Warn };
            Dark = dark && !HC;
            if (HC)
            {
                Bg = SystemColors.Control; Card = SystemColors.Window; Fg = SystemColors.ControlText; Muted = SystemColors.WindowText;
                Err = SystemColors.WindowText; Ok = SystemColors.WindowText; Title = SystemColors.WindowText; Warn = SystemColors.WindowText;
                Accent = SystemColors.Highlight; Link = SystemColors.HotTrack; Input = SystemColors.Window;
                BtnBg = SystemColors.Control; BtnBorder = SystemColors.ControlDark; BtnDisabled = SystemColors.ControlDark;
                RedRow = SystemColors.GrayText; RefOnly = SystemColors.GrayText; Prop = SystemColors.HotTrack;
                Grid = SystemColors.GrayText; TlRef = SystemColors.Highlight; TlOrig = SystemColors.GrayText; TlSynced = SystemColors.HotTrack; TlHighlight = SystemColors.WindowText;
                HotBg = SystemColors.Window; BtnHover = SystemColors.Control; BtnDown = SystemColors.Control; AccentHover = SystemColors.Highlight; AccentDown = SystemColors.Highlight;
            }
            else if (Dark)
            {
                Bg = C(32, 32, 32); Card = C(43, 43, 43); Fg = C(228, 228, 228); Muted = C(160, 160, 160);
                Err = C(255, 125, 115); Ok = C(108, 203, 95); Title = C(245, 245, 245); Warn = C(240, 190, 80);
                Accent = C(0, 120, 212); Link = C(110, 175, 255); Input = C(25, 25, 25);
                BtnBg = C(55, 55, 55); BtnBorder = C(85, 85, 85); BtnDisabled = C(70, 70, 70);
                RedRow = C(255, 130, 130); RefOnly = C(135, 135, 135); Prop = C(105, 170, 255);
                Grid = C(58, 58, 58); TlRef = C(76, 160, 255); TlOrig = C(120, 120, 120); TlSynced = C(70, 190, 110); TlHighlight = C(240, 240, 240);
                HotBg = C(52, 52, 52); BtnHover = C(72, 72, 72); BtnDown = C(90, 90, 90); AccentHover = C(25, 140, 230); AccentDown = C(0, 95, 170);
            }
            else
            {
                Bg = C(243, 243, 243); Card = C(255, 255, 255); Fg = C(0, 0, 0); Muted = C(96, 96, 96);
                Err = Color.Firebrick; Ok = C(16, 110, 40); Title = C(28, 28, 28); Warn = C(138, 90, 0);
                Accent = C(0, 103, 192); Link = Accent; Input = C(255, 255, 255);
                BtnBg = C(255, 255, 255); BtnBorder = C(200, 200, 200); BtnDisabled = C(200, 200, 200);
                RedRow = C(170, 60, 60); RefOnly = C(110, 110, 110); Prop = C(0, 90, 180);
                Grid = C(235, 235, 235); TlRef = C(0, 103, 192); TlOrig = C(160, 160, 160); TlSynced = C(16, 140, 60); TlHighlight = C(0, 0, 0);
                HotBg = C(229, 241, 251); BtnHover = C(229, 241, 251); BtnDown = C(204, 228, 247); AccentHover = C(0, 120, 215); AccentDown = C(0, 84, 153);
            }
            var now = new[] { Bg, Card, Fg, Muted, Err, Ok, Title, Warn };
            var map = new Dictionary<int, Color>();
            for (int i = 0; i < old.Length; i++)
                if (!old[i].IsEmpty && !map.ContainsKey(old[i].ToArgb())) map[old[i].ToArgb()] = now[i];
            return map;
        }

        // ------------------------------------------------------------ valet
        public const string Auto = "auto", Light = "light", Dark_ = "dark";

        public static string Mode
        {
            get { string m = Tools.GetSetting("theme", Auto); return m == Light || m == Dark_ ? m : Auto; }
            set { Tools.SetSetting("theme", value); }
        }

        // Mörkt läge för appar i Windows (Inställningar → Anpassning → Färger)
        public static bool WindowsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static bool WantDark(string mode) { return mode == Dark_ || (mode == Auto && WindowsDark()); }

        // ------------------------------------------------------------ tillämpa på kontroller
        // map = gammal färg → ny färg (tom vid start). Kontroller som ärver färgen från sin förälder lämnas orörda.
        public static void Apply(Control c, Dictionary<int, Color> map)
        {
            if (HC) return;
            if (c is TextBoxBase || c is ComboBox || c is NumericUpDown || c is ListView)
            {
                c.BackColor = Input; c.ForeColor = Fg;
                if (c is ListView) c.Invalidate();   // rubriker, markerad rad och raden under muspekaren ritas om i nya färger
                Native(c);
            }
            else if (c is Button && ((Button)c).FlatStyle == FlatStyle.Flat || c is Button && Dark)
            {
                var b = (Button)c;
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 1;
                if ("primary".Equals(b.Tag)) { b.BackColor = b.Enabled ? Accent : BtnDisabled; b.FlatAppearance.BorderColor = b.BackColor; }
                else { b.BackColor = BtnBg; b.ForeColor = Fg; b.FlatAppearance.BorderColor = BtnBorder; }
                // syns tydligt när muspekaren är över knappen och när den trycks ned
                bool prim = "primary".Equals(b.Tag);
                b.FlatAppearance.MouseOverBackColor = prim ? AccentHover : BtnHover;
                b.FlatAppearance.MouseDownBackColor = prim ? AccentDown : BtnDown;
            }
            else
            {
                Color n;
                if (map != null && map.TryGetValue(c.BackColor.ToArgb(), out n) && c.BackColor != Color.Transparent) c.BackColor = n;
                if (map != null && map.TryGetValue(c.ForeColor.ToArgb(), out n)) c.ForeColor = n;
                var l = c as LinkLabel;
                if (l != null) { l.LinkColor = Link; l.ActiveLinkColor = Link; l.VisitedLinkColor = Link; }
            }
            if (c is Form) { c.BackColor = c is MainForm ? Bg : Card; c.ForeColor = Fg; TitleBar((Form)c); }
            foreach (Control ch in c.Controls) Apply(ch, map);
        }

        // Högerklicksmenyer ritas av WinForms själv
        public static void ApplyMenu(ToolStrip m)
        {
            if (m == null || HC) return;
            if (Dark) { m.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()); m.BackColor = Card; m.ForeColor = Fg; }
            else { m.RenderMode = ToolStripRenderMode.ManagerRenderMode; m.ResetBackColor(); m.ResetForeColor(); }
        }

        class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Card; } }
            public override Color ImageMarginGradientBegin { get { return Card; } }
            public override Color ImageMarginGradientMiddle { get { return Card; } }
            public override Color ImageMarginGradientEnd { get { return Card; } }
            public override Color MenuBorder { get { return BtnBorder; } }
            public override Color MenuItemBorder { get { return C(70, 70, 70); } }
            public override Color MenuItemSelected { get { return C(65, 65, 65); } }
            public override Color SeparatorDark { get { return BtnBorder; } }
            public override Color SeparatorLight { get { return Card; } }
        }

        // ------------------------------------------------------------ Windows-delar (rullister, rubriker, titelrad)
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        const int LVM_GETHEADER = 0x101F;

        static readonly HashSet<Control> hooked = new HashSet<Control>();
        static bool everDark;   // i ljust läge rörs Windows-temat bara om programmet har varit mörkt

        static void Native(Control c)
        {
            if (Dark) everDark = true;
            if (!everDark) return;
            if (hooked.Add(c))
            {
                c.HandleCreated += (s, e) => NativeNow(c);
                c.Disposed += (s, e) => hooked.Remove(c);
            }
            if (c.IsHandleCreated) NativeNow(c);
        }

        static void NativeNow(Control c)
        {
            try
            {
                if (c is ComboBox) SetWindowTheme(c.Handle, Dark ? "DarkMode_CFD" : null, null);
                else SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : null, null);
                if (c is ListView)
                {
                    var hdr = SendMessage(c.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    if (hdr != IntPtr.Zero) SetWindowTheme(hdr, Dark ? "DarkMode_ItemsView" : null, null);
                }
            }
            catch { }   // äldre Windows utan mörka teman: kontrollen behåller sitt ljusa utseende
        }

        public static void TitleBar(Form f)
        {
            if (HC) return;
            if (Dark) everDark = true;
            if (!everDark) return;
            Action set = () =>
            {
                try
                {
                    int v = Dark ? 1 : 0;
                    if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);   // 19 = äldre Windows 10
                }
                catch { }
            };
            if (f.IsHandleCreated) set(); else f.HandleCreated += (s, e) => set();
        }
    }
}
