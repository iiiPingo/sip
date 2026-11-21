using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Echoa
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 32, 32);
        public static readonly Color Card = Color.FromArgb(45, 45, 45);
        public static readonly Color CardSoft = Color.FromArgb(39, 39, 39);
        public static readonly Color CardHover = Color.FromArgb(58, 58, 58);
        public static readonly Color Field = Color.FromArgb(52, 52, 52);
        public static readonly Color Border = Color.FromArgb(62, 62, 62);
        public static readonly Color Text = Color.FromArgb(242, 242, 242);
        public static readonly Color Sub = Color.FromArgb(170, 170, 170);
        public static readonly Color Dim = Color.FromArgb(120, 120, 120);
        public static readonly Color Track = Color.FromArgb(88, 88, 88);
        public static readonly Color ThumbOuter = Color.FromArgb(69, 69, 69);
        public static readonly Color ThumbEdge = Color.FromArgb(86, 86, 86);

        // accent colour: cocoa by default, changeable in Settings (the exe icon stays cocoa)
        public static readonly Color DefaultAccent = Color.FromArgb(217, 154, 98);   // #D99A62
        static readonly Color DefaultLogoA = Color.FromArgb(58, 30, 20), DefaultLogoB = Color.FromArgb(201, 138, 85);   // #3A1E14 -> #C98A55
        public static Color Accent { get; private set; } = DefaultAccent;
        public static Color AccentHover { get; private set; } = Mix(DefaultAccent, Color.White, 0.22f);
        public static Color AccentSoft { get; private set; } = Color.FromArgb(40, DefaultAccent);
        public static Color LogoA { get; private set; } = DefaultLogoA;
        public static Color LogoB { get; private set; } = DefaultLogoB;

        // Pour, Pulp, Fizz and Juice colours are in here too
        public static readonly Color[] Presets =
        {
            DefaultAccent,                      // Echoa cocoa
            Color.FromArgb(76, 168, 255),       // blue
            Color.FromArgb(61, 219, 176),       // mint
            Color.FromArgb(95, 208, 104),       // Juice green
            Color.FromArgb(242, 193, 78),       // honey
            Color.FromArgb(255, 138, 61),       // Pulp tangerine
            Color.FromArgb(208, 100, 74),       // Pour terracotta
            Color.FromArgb(242, 95, 92),        // cherry red
            Color.FromArgb(255, 92, 158),       // Fizz pink
            Color.FromArgb(176, 124, 255),      // ube
            Color.FromArgb(200, 200, 200),      // silver
        };

        public static void SetAccent(Color c)
        {
            c = Color.FromArgb(255, c.R, c.G, c.B);
            if (c.ToArgb() == DefaultAccent.ToArgb())
            {
                Accent = DefaultAccent;
                AccentHover = Mix(DefaultAccent, Color.White, 0.22f);
                AccentSoft = Color.FromArgb(40, DefaultAccent);
                LogoA = DefaultLogoA;
                LogoB = DefaultLogoB;
                return;
            }
            Accent = c;
            AccentHover = Mix(c, Color.White, 0.22f);
            AccentSoft = Color.FromArgb(40, c);
            LogoA = Mix(c, Color.Black, 0.55f);
            LogoB = Mix(c, Color.White, 0.08f);
        }

        public static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        public static bool TryHex(string s, out Color c)
        {
            c = DefaultAccent;
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v)) return false;
            c = Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
            return true;
        }

        public static readonly string IconFont = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        static readonly string UiFamily = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        static readonly string UiSemiFamily = FontExists("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold"
            : FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : null;
        public static Font Ui(float size) => new Font(UiFamily, size);
        public static Font UiSemi(float size) => UiSemiFamily != null ? new Font(UiSemiFamily, size) : new Font(UiFamily, size, FontStyle.Bold);
        static readonly string UiDisplaySemi = FontExists("Segoe UI Variable Display Semibold") ? "Segoe UI Variable Display Semibold" : null;
        public static Font UiTitle(float size) => UiDisplaySemi != null ? new Font(UiDisplaySemi, size) : UiSemi(size);
        public static readonly Color Nav = Color.FromArgb(27, 27, 27);
        public static readonly Color Red = Color.FromArgb(240, 96, 96);

        // Segoe Fluent Icons / MDL2 glyphs
        public const string GSpeakers = "", GHeadphones = "", GHeadset = "", GBluetooth = "", GMonitor = "",
            GVol0 = "", GVol1 = "", GVol2 = "", GVol3 = "", GMute = "", GSettings = "",
            GApp = "", GSystem = "", GCheck = "",
            GMic = "", GPrev = "", GNext = "", GPlay = "", GPause = "", GMusic = "";

        public static string VolumeGlyph(float v, bool muted) => muted ? GMute : v <= 0.001f ? GVol0 : v < 0.34f ? GVol1 : v < 0.67f ? GVol2 : GVol3;

        public static string DeviceGlyph(int formFactor)
        {
            switch (formFactor)
            {
                case 3: return GHeadphones;       // Headphones
                case 5: case 6: return GHeadset;  // Headset, Handset
                case 9: return GMonitor;          // HDMI / DisplayPort display
                default: return GSpeakers;
            }
        }

        static bool FontExists(string name)
        {
            try { using (var f = new Font(name, 10)) return f.Name == name; } catch { return false; }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, Color c, float rad)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, Color c, float rad, float w = 1)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static void Disc(Graphics g, PointF c, float r, Color col)
        {
            using (var b = new SolidBrush(col)) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
        }

        // ---------------------------------------------------------------- logo: drop + two sound waves
        static PointF[] Drop(float cx, float top, float h)
        {
            float w = h * 0.72f;
            var pts = new PointF[96];
            for (int i = 0; i < 96; i++)
            {
                double t = 2 * Math.PI * i / 96;
                double x = Math.Sin(t) * Math.Sin(t / 2), y = -Math.Cos(t);
                pts[i] = new PointF(cx + (float)(x * w * 0.62), top + (float)((y + 1) / 2 * h));
            }
            return pts;
        }

        // ---------------------------------------------------------------- logo: cocoa bean + two sound waves (Ripple)
        public static void DrawGlyph(Graphics g, RectangleF r, Color c, bool small)
        {
            float n = r.Width, cx = r.X + 0.37f * n, cy = r.Y + 0.52f * n;
            float h = (small ? 0.50f : 0.46f) * n, w = h * 0.66f;
            using (var bean = new GraphicsPath())
            using (var groove = new GraphicsPath())
            using (var cut = new Pen(Color.Black, Math.Max(1f, h * 0.075f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                bean.AddEllipse(cx - w / 2, cy - h / 2, w, h);
                var pts = new PointF[41];
                for (int i = 0; i <= 40; i++)
                {
                    double t = i / 40.0;
                    pts[i] = new PointF(cx + (float)(Math.Sin((t - 0.5) * Math.PI * 1.6) * w * 0.10), cy - h * 0.40f + (float)t * h * 0.80f);
                }
                groove.AddLines(pts);
                groove.Widen(cut);
                using (var region = new Region(bean))
                using (var b = new SolidBrush(c))
                {
                    region.Exclude(groove);
                    g.FillRegion(b, region);
                }
            }
            float lw = (small ? 0.07f : 0.055f) * n;
            using (var pen = new Pen(c, lw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                foreach (float rr in small ? new[] { 0.29f } : new[] { 0.25f, 0.37f })
                {
                    float R = rr * n;
                    g.DrawArc(pen, cx - R, cy - R, 2 * R, 2 * R, -42, 84);
                }
        }

        public static void DrawLogo(Graphics g, RectangleF r)
        {
            using (var path = Round(r, r.Width * 0.24f))
            using (var b = new LinearGradientBrush(new PointF(r.X, r.Y), new PointF(r.Right, r.Bottom), LogoA, LogoB))
                g.FillPath(b, path);
            DrawGlyph(g, r, Color.White, r.Width < 40);
        }
    }

    // Juice-style ring: dim track, accent arc for the level from 12 o'clock; muted = grey ring with a slash
    static class TrayArt
    {
        public static Icon Render(float level, bool muted, int size)
        {
            int sz = Math.Max(16, size), big = sz * 4;
            using (var hi = new Bitmap(big, big))
            using (var bmp = new Bitmap(sz, sz))
            {
                using (var g = Graphics.FromImage(hi))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    float c = big / 2f, w = big * 0.19f, rm = big * 0.47f - w / 2;
                    var rr = new RectangleF(c - rm, c - rm, 2 * rm, 2 * rm);
                    using (var track = new Pen(Color.FromArgb(muted ? 60 : 72, 255, 255, 255), w)) g.DrawEllipse(track, rr);
                    if (muted)
                    {
                        using (var p = new Pen(Color.FromArgb(205, 205, 205), big * 0.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, c - big * 0.26f, c + big * 0.26f, c + big * 0.26f, c - big * 0.26f);
                    }
                    else
                    {
                        float sweep = 360f * Math.Max(0, Math.Min(1, level));
                        if (sweep > 0.5f)
                            using (var p = new Pen(Theme.Accent, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            {
                                if (sweep >= 359.5f) g.DrawEllipse(p, rr);
                                else g.DrawArc(p, rr, -90, sweep);
                            }
                        Theme.Disc(g, new PointF(c, c), big * 0.1f, Color.White);
                    }
                }
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(hi, new Rectangle(0, 0, sz, sz));
                }
                IntPtr h = bmp.GetHicon();
                using (var tmp = Icon.FromHandle(h))
                {
                    var ico = (Icon)tmp.Clone();
                    Native.DestroyIcon(h);
                    return ico;
                }
            }
        }
    }

    // ---------------------------------------------------------------- dark menu
    class DarkColors : ProfessionalColorTable
    {
        static readonly Color Back = Color.FromArgb(40, 40, 40), Hi = Color.FromArgb(60, 60, 60);
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Hi;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color MenuItemPressedGradientBegin => Hi;
        public override Color MenuItemPressedGradientEnd => Hi;
        public override Color SeparatorDark => Color.FromArgb(64, 64, 64);
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hi;
        public override Color CheckPressedBackground => Hi;
    }

    static class Menus
    {
        // Windows 11 can round any popup itself; Windows 10 keeps the square menu
        public static readonly bool Win11 = Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;

        public static ContextMenuStrip Make(Font font)
        {
            var m = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = font, ShowImageMargin = true, ShowCheckMargin = false };
            m.Padding = new Padding(4, 5, 4, 5);
            if (Win11)
            {
                m.DropShadowEnabled = false;          // the old square shadow; DWM draws a rounded one
                m.HandleCreated += (o, e) => Round(m.Handle);
                m.Opened += (o, e) => Round(m.Handle);
            }
            return m;
        }

        static void Round(IntPtr h)
        {
            try
            {
                int round = 2; Native.DwmSetWindowAttribute(h, 33, ref round, 4);        // rounded corners
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                Native.DwmSetWindowAttribute(h, 34, ref border, 4);                      // thin border in our grey
            }
            catch { }
        }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        // on Windows 11 the window itself is rounded with a DWM border, so skip the square one
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (Menus.Win11 && e.ToolStrip is ToolStripDropDown) return;
            base.OnRenderToolStripBorder(e);
        }

        // hover: a rounded pill inset from the edges, like Windows 11 menus
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!(e.ToolStrip is ToolStripDropDown) || !e.Item.Selected || !e.Item.Enabled) { if (!(e.ToolStrip is ToolStripDropDown)) base.OnRenderMenuItemBackground(e); return; }
            var g = e.Graphics;
            var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            using (var b = new SolidBrush(Color.FromArgb(60, 60, 60)))
            {
                int d = 8;
                path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(b, path);
            }
            g.SmoothingMode = old;
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? (e.Item is ToolStripMenuItem mi && mi.ShortcutKeyDisplayString == e.Text ? Theme.Dim : Theme.Text) : Theme.Dim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Sub;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using (var f = new Font(Theme.IconFont, 9f))
                TextRenderer.DrawText(e.Graphics, Theme.GCheck, f, e.ImageRectangle, Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
