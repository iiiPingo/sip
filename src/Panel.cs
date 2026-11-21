using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Sip
{
    interface IPanelHost
    {
        Snapshot Snap { get; }
        Settings S { get; }
        void SetDefault(string id);
        void DeviceVolume(string id, float v, bool final);
        void DeviceMute(string id, bool m);
        void AppVolume(string devId, string key, float v, bool final);
        void AppMute(string devId, string key, bool m);
        void OpenMain(string page, string devId);
        NowPlaying Media { get; }
        bool ShowNowPlaying { get; }
        void MediaCommand(int c);   // 0 play/pause, 1 next, 2 previous
        float CapOf(string id);
        void ShowDeviceMenu(DevSnap d, Point screen);
        void ShowAppMenu(DevSnap d, AppSnap a, Point screen);
    }

    class SipPanel : Form
    {
        enum RK { Device, App, Empty }

        class Row
        {
            public RK K;
            public DevSnap D;
            public AppSnap A;
            public bool Default, Open;
            public int AppCount;
            public Rectangle R, Icon, Mute, Zone, Num, Chip;
            public string Key;
        }

        class Group { public Rectangle R; public bool Default; }

        class Drag { public string Dev, App; public RectangleF Track; public string Key; }

        readonly IPanelHost host;
        readonly float s;
        readonly Font fName, fBody, fSmall, fNum, fIcon, fIconSmall, fIconTiny, fFooter;
        readonly List<Row> rows = new List<Row>();
        readonly List<Group> groups = new List<Group>();
        readonly HashSet<string> peeked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Timer anim;
        string hover, pressKey, focusKey;
        bool focusVisible, anchorBottom = true;
        Drag drag;
        int scroll, contentH, viewH;
        DateTime animStart;
        int animTarget, animFrom;
        public bool SuppressHide;
        public DateTime LastHidden = DateTime.MinValue;

        int S(float v) => (int)Math.Round(v * s);
        int W => S(380);
        int Pad => S(8);
        int TopPad => S(8);
        int DevH => S(64);
        int AppH => S(38);
        int EmptyH => S(32);
        int GroupPadB => S(6);
        int Gap => S(4);
        int FooterH => S(44);
        public int IconPx => S(20);
        public int ArtPx => S(40);
        bool npShown;                                   // "Now playing" strip laid out this time
        int NpH => npShown ? S(60) : 0;
        int ListTop => TopPad + NpH;
        Rectangle NpRect => new Rectangle(Pad, TopPad, W - Pad * 2, S(54));

        public SipPanel(IPanelHost h)
        {
            host = h;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            Text = "Sip";
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            using (var g = CreateGraphics()) s = g.DpiX / 96f;

            fName = Theme.UiSemi(10f);
            fBody = Theme.Ui(9.5f);
            fSmall = Theme.Ui(8.5f);
            fNum = Theme.Ui(9f);
            fFooter = Theme.Ui(9f);
            fIcon = new Font(Theme.IconFont, 13f);
            fIconSmall = new Font(Theme.IconFont, 11f);
            fIconTiny = new Font(Theme.IconFont, 6.5f);

            anim = new Timer { Interval = 10 };
            anim.Tick += (o, e) => AnimTick();

            Size = new Size(W, S(200));
            var _ = Handle;   // create the window now so the first open is instant
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                cp.ExStyle |= 0x80;         // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);   // rounded corners (Win11)
                int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);     // dark mode
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);                 // border colour
            }
            catch { }
        }

        // ------------------------------------------------------------ layout
        bool DeviceShown(DevSnap d, string def) =>
            d.Id == def || !(host.S.Devices.TryGetValue(d.Id, out var p) && p.Hidden);

        List<AppSnap> VisibleApps(DevSnap d) => d.Apps.Where(a => !host.S.HiddenApps.ContainsKey(a.Exe)).ToList();

        void BuildRows()
        {
            rows.Clear();
            groups.Clear();
            var snap = host.Snap;
            int x = Pad, w = W - Pad * 2, y = 0;
            foreach (var d in snap.Devices)
            {
                if (!DeviceShown(d, snap.DefaultId)) continue;
                if (host.S.SimpleFlyout && d.Id != snap.DefaultId) continue;   // simple flyout: main output only
                bool def = d.Id == snap.DefaultId;
                var apps = VisibleApps(d);
                bool open = def || (peeked.Contains(d.Id) && apps.Count > 0);
                int gy = y;
                var dr = new Row { K = RK.Device, D = d, Default = def, Open = open, AppCount = apps.Count, Key = "d|" + d.Id, R = new Rectangle(x, y, w, DevH) };
                rows.Add(dr);
                y += DevH;
                if (open)
                {
                    if (apps.Count == 0) { rows.Add(new Row { K = RK.Empty, D = d, Key = "e|" + d.Id, R = new Rectangle(x, y, w, EmptyH) }); y += EmptyH; }
                    foreach (var a in apps)
                    {
                        rows.Add(new Row { K = RK.App, D = d, A = a, Key = "a|" + d.Id + "|" + a.Key, R = new Rectangle(x, y, w, AppH) });
                        y += AppH;
                    }
                    y += GroupPadB;
                    groups.Add(new Group { R = new Rectangle(x, gy, w, y - gy), Default = def });
                }
                y += Gap;
            }
            contentH = Math.Max(rows.Count == 0 ? S(72) : y - Gap, 0);
        }

        void PlaceParts(int dy)
        {
            foreach (var r in rows)
            {
                r.R.Offset(0, dy);
                var b = r.R;
                if (r.K == RK.Device)
                {
                    r.Icon = new Rectangle(b.X + S(26), b.Y + S(7), S(24), S(24));
                    r.Mute = new Rectangle(b.X + S(26), b.Y + S(33), S(24), S(24));
                    r.Zone = Rectangle.FromLTRB(b.X + S(56), b.Y + S(33), b.Right - S(42), b.Y + S(57));
                    r.Num = Rectangle.FromLTRB(b.Right - S(42), b.Y + S(33), b.Right - S(10), b.Y + S(57));
                    r.Chip = Rectangle.Empty;
                    if (!r.Default && r.AppCount > 0)
                    {
                        string t = ChipText(r.AppCount);
                        int tw = TextRenderer.MeasureText(t, fSmall, Size.Empty, TextFormatFlags.NoPadding).Width + S(18);
                        r.Chip = new Rectangle(b.Right - S(10) - tw, b.Y + S(8), tw, S(22));
                    }
                }
                else if (r.K == RK.App)
                {
                    r.Icon = new Rectangle(b.X + S(56), b.Y + (b.Height - S(20)) / 2, S(20), S(20));
                    r.Zone = Rectangle.FromLTRB(b.X + S(178), b.Y + (b.Height - S(24)) / 2, b.Right - S(42), b.Y + (b.Height + S(24)) / 2);
                    r.Num = Rectangle.FromLTRB(b.Right - S(42), r.Zone.Y, b.Right - S(10), r.Zone.Bottom);
                }
            }
            foreach (var g in groups) g.R.Offset(0, dy);
        }

        static string ChipText(int n) => n == 1 ? "1 app" : n + " apps";

        Rectangle WorkArea(Point anchor) => Screen.FromPoint(anchor).WorkingArea;
        Point lastAnchor;

        // recompute rows and window size; keeps the edge next to the taskbar where it is
        public void Relayout()
        {
            npShown = host.ShowNowPlaying;
            BuildRows();
            var wa = WorkArea(lastAnchor);
            int maxView = Math.Max(S(120), wa.Height - S(24) - ListTop - FooterH);
            viewH = Math.Min(contentH, maxView);
            scroll = Math.Max(0, Math.Min(scroll, contentH - viewH));
            PlaceParts(ListTop - scroll);
            int h = ListTop + viewH + S(6) + FooterH;
            if (Height != h || Width != W)
            {
                if (Visible && anchorBottom && !anim.Enabled) { int bottom = Bottom; Size = new Size(W, h); Top = bottom - h; }
                else Size = new Size(W, h);
            }
            Invalidate();
        }

        // ------------------------------------------------------------ show / hide
        public void ShowAt(Point anchor, bool keyboard)
        {
            lastAnchor = anchor;
            scroll = 0;
            hover = null;
            drag = null;
            Relayout();
            var scr = Screen.FromPoint(anchor);
            Rectangle wa = scr.WorkingArea, b = scr.Bounds;
            int m = S(12), x = wa.Right - Width - m, y;
            anchorBottom = !(wa.Top > b.Top && wa.Bottom == b.Bottom);
            if (wa.Left > b.Left && wa.Right == b.Right && wa.Top == b.Top && wa.Bottom == b.Bottom) x = wa.Left + m;
            y = anchorBottom ? wa.Bottom - Height - m : wa.Top + m;
            Location = new Point(x, y);

            focusVisible = keyboard;
            focusKey = keyboard ? rows.FirstOrDefault(r => r.K == RK.Device && r.Default)?.Key ?? rows.FirstOrDefault(r => r.K != RK.Empty)?.Key : null;

            animTarget = y;
            animFrom = y + (anchorBottom ? S(14) : -S(14));
            Top = animFrom;
            Opacity = 0;
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            animStart = DateTime.UtcNow;
            anim.Start();
        }

        void AnimTick()
        {
            double t = Math.Min(1, (DateTime.UtcNow - animStart).TotalMilliseconds / 140.0);
            double e = 1 - Math.Pow(1 - t, 3);
            Top = (int)Math.Round(animFrom + (animTarget - animFrom) * e);
            if (t >= 1) { anim.Stop(); Opacity = 1; Relayout(); }
            else Opacity = e;
        }

        public void HidePanel()
        {
            anim.Stop();
            Opacity = 1;
            if (Capture) Capture = false;
            drag = null;
            Hide();
            LastHidden = DateTime.UtcNow;
            peeked.Clear();
            hover = null;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!SuppressHide && Visible) HidePanel();
        }

        public void ModelChanged()
        {
            if (!Visible) return;
            if (focusKey != null && !rows.Any(r => r.Key == focusKey)) focusKey = null;
            Relayout();
        }

        public void ValuesChanged() { if (Visible) Invalidate(); }

        // ------------------------------------------------------------ painting
        const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        bool Hov(string key) => hover == key;
        bool HovRow(Row r) => hover != null && hover.StartsWith(r.Key + "#");

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            PaintAll(g);
        }

        public void PaintAll(Graphics g)
        {
            if (npShown) DrawNowPlaying(g);
            var listClip = new Rectangle(0, ListTop, W, viewH);
            var state = g.Save();
            g.SetClip(listClip);

            foreach (var gr in groups) Theme.Fill(g, gr.R, gr.Default ? Theme.Card : Theme.CardSoft, S(8));

            if (rows.Count == 0)
                TextRenderer.DrawText(g, "No output devices", fBody, new Rectangle(Pad, ListTop, W - Pad * 2, contentH), Theme.Sub, C);

            foreach (var r in rows)
            {
                if (r.R.Bottom < listClip.Top || r.R.Top > listClip.Bottom) continue;
                if (r.K == RK.Device) DrawDevice(g, r);
                else if (r.K == RK.App) DrawApp(g, r);
                else TextRenderer.DrawText(g, "No apps playing", fSmall, new Rectangle(r.R.X + S(56), r.R.Y, r.R.Width - S(66), r.R.Height), Theme.Dim, L);
                if (focusVisible && r.Key == focusKey)
                    Theme.Stroke(g, Rectangle.Inflate(r.R, -S(1), -S(1)), Theme.Accent, S(7), Math.Max(1.5f, s * 1.5f));
            }

            if (contentH > viewH)
            {
                int trackH = viewH - S(8);
                int thumbH = Math.Max(S(24), trackH * viewH / contentH);
                int thumbY = ListTop + S(4) + (trackH - thumbH) * scroll / Math.Max(1, contentH - viewH);
                Theme.Fill(g, new Rectangle(W - S(5), thumbY, S(3), thumbH), Theme.Border, S(2));
            }
            g.Restore(state);

            // footer
            int fy = Height - FooterH;
            using (var p = new Pen(Theme.Border, Math.Max(1, s))) g.DrawLine(p, 0, fy, W, fy);
            Theme.DrawLogo(g, new RectangleF(Pad + S(10), fy + (FooterH - S(18)) / 2f, S(18), S(18)));
            TextRenderer.DrawText(g, "Sip", fFooter, new Rectangle(Pad + S(36), fy, S(80), FooterH), Theme.Sub, L);
            var gear = new Rectangle(W - Pad - S(36), fy + (FooterH - S(32)) / 2, S(32), S(32));
            if (Hov("gear#")) Theme.Fill(g, gear, Theme.CardHover, S(6));
            TextRenderer.DrawText(g, Theme.GSettings, fIconSmall, gear, Hov("gear#") ? Theme.Text : Theme.Sub, C);
        }

        Rectangle GearRect { get { int fy = Height - FooterH; return new Rectangle(W - Pad - S(36), fy + (FooterH - S(32)) / 2, S(32), S(32)); } }

        void DrawDevice(Graphics g, Row r)
        {
            var d = r.D;
            bool rowHov = HovRow(r);
            if (!r.Default && rowHov && drag == null)
                Theme.Fill(g, r.R, r.Open ? Theme.CardHover : Theme.CardSoft, S(8));

            // default dot
            var dc = new PointF(r.R.X + S(14), r.R.Y + S(19));
            if (r.Default) Theme.Disc(g, dc, S(4), Theme.Accent);
            else using (var p = new Pen(rowHov ? Theme.Sub : Theme.Dim, Math.Max(1, s))) g.DrawEllipse(p, dc.X - S(4), dc.Y - S(4), S(8), S(8));

            // device glyph (+ Bluetooth badge)
            TextRenderer.DrawText(g, Theme.DeviceGlyph(d.FormFactor), fIcon, r.Icon, r.Default ? Theme.Text : Theme.Sub, C);
            if (d.Bluetooth)
            {
                var bc = new PointF(r.Icon.Right - S(2), r.Icon.Bottom - S(3));
                Theme.Disc(g, bc, S(6), r.Default ? Theme.Card : rowHov ? Theme.CardSoft : Theme.Bg);
                TextRenderer.DrawText(g, Theme.GBluetooth, fIconTiny, new Rectangle((int)bc.X - S(6), (int)bc.Y - S(6), S(12), S(12)), Theme.Accent, C);
            }

            // name + sub
            int nx = r.R.X + S(56), right = (r.Chip.IsEmpty ? r.R.Right - S(10) : r.Chip.X - S(8));
            var nameR = new Rectangle(nx, r.R.Y + S(7), Math.Max(0, right - nx), S(24));
            int nw = Math.Min(nameR.Width, TextRenderer.MeasureText(g, d.Name, fName, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + S(2));
            TextRenderer.DrawText(g, d.Name, fName, new Rectangle(nameR.X, nameR.Y, nw, nameR.Height), Theme.Text, L);
            string sub = d.Sub ?? "";
            if (host.S.Devices.TryGetValue(d.Id, out var pr) && pr.Hidden) sub = sub.Length > 0 ? sub + " · hidden" : "hidden";
            if (sub.Length > 0 && nameR.Right - (nameR.X + nw + S(6)) > S(30))
                TextRenderer.DrawText(g, sub, fSmall, Rectangle.FromLTRB(nameR.X + nw + S(6), nameR.Y + S(1), nameR.Right, nameR.Bottom), Theme.Dim, L);

            // apps chip (peek without switching)
            if (!r.Chip.IsEmpty)
            {
                bool ch = Hov(r.Key + "#chip");
                Theme.Fill(g, r.Chip, r.Open ? Theme.AccentSoft : ch ? Theme.CardHover : Theme.Field, r.Chip.Height / 2f);
                TextRenderer.DrawText(g, ChipText(r.AppCount), fSmall, r.Chip, r.Open ? Theme.Accent : ch ? Theme.Text : Theme.Sub, C);
            }

            // mute + slider + number
            bool mh = Hov(r.Key + "#mute");
            if (mh) Theme.Fill(g, r.Mute, Theme.CardHover, S(5));
            TextRenderer.DrawText(g, Theme.VolumeGlyph(d.Vol, d.Muted), fIconSmall, r.Mute, d.Muted ? Theme.Dim : mh ? Theme.Text : Theme.Sub, C);
            float cap = host.CapOf(d.Id);
            if (cap < 1f)
            {
                var t = TrackOf(r.Zone);
                float cx = t.X + t.Width * cap;
                using (var p = new Pen(Theme.Sub, Math.Max(1.5f, s * 1.5f))) g.DrawLine(p, cx, t.Y - S(5), cx, t.Bottom + S(5));
            }
            DrawSlider(g, r.Zone, d.Vol, d.Muted, Hov(r.Key + "#slider"), drag != null && drag.Key == r.Key);
            TextRenderer.DrawText(g, Pct(d.Vol), fNum, r.Num, d.Muted ? Theme.Dim : Theme.Sub, R);
        }

        Rectangle NpButton(Rectangle nr, int i)
        {
            int bw = S(32), right = nr.Right - S(8);
            return new Rectangle(right - bw * (3 - i), nr.Y + (nr.Height - bw) / 2, bw, bw);
        }

        void DrawNowPlaying(Graphics g)
        {
            var np = host.Media;
            var nr = NpRect;
            Theme.Fill(g, nr, Theme.CardSoft, S(8));
            if (np == null) return;
            var gr = new Rectangle(nr.X + S(8), nr.Y + (nr.Height - ArtPx) / 2, ArtPx, ArtPx);
            if (np.Art != null)
            {
                var st = g.Save();
                using (var path = Theme.Round(gr, S(6))) g.SetClip(path, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(np.Art, gr);
                g.Restore(st);
            }
            else
            {
                Theme.Fill(g, gr, Theme.AccentSoft, S(6));
                TextRenderer.DrawText(g, Theme.GMusic, fIconSmall, gr, Theme.Accent, C);
            }
            int tx = gr.Right + S(10), tr = NpButton(nr, 0).X - S(6);
            TextRenderer.DrawText(g, np.Title.Length > 0 ? np.Title : np.Artist, fName, new Rectangle(tx, nr.Y + S(7), tr - tx, S(22)), Theme.Text, L);
            string line2 = np.Title.Length > 0 ? np.Artist : "";
            if (line2.Length > 0)
                TextRenderer.DrawText(g, line2, fSmall, new Rectangle(tx, nr.Y + S(28), tr - tx, S(18)), Theme.Sub, L);
            string[] glyphs = { Theme.GPrev, np.Playing ? Theme.GPause : Theme.GPlay, Theme.GNext };
            string[] keys = { "np#prev", "np#play", "np#next" };
            for (int i = 0; i < 3; i++)
            {
                var b = NpButton(nr, i);
                bool h = Hov(keys[i]);
                if (h) Theme.Fill(g, b, Theme.CardHover, S(6));
                TextRenderer.DrawText(g, glyphs[i], i == 1 ? fIcon : fIconSmall, b, h ? Theme.Text : Theme.Sub, C);
            }
        }

        static string Pct(float v) => ((int)Math.Round(v * 100)).ToString();

        static readonly ImageAttributes Faded = MakeFaded();
        static ImageAttributes MakeFaded()
        {
            var ia = new ImageAttributes();
            ia.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f });
            return ia;
        }

        void DrawApp(Graphics g, Row r)
        {
            var a = r.A;
            bool ih = Hov(r.Key + "#icon");
            if (ih) Theme.Fill(g, Rectangle.Inflate(r.Icon, S(5), S(5)), Theme.CardHover, S(5));
            if (a.Icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (a.Muted) g.DrawImage(a.Icon, r.Icon, 0, 0, a.Icon.Width, a.Icon.Height, GraphicsUnit.Pixel, Faded);
                else g.DrawImage(a.Icon, r.Icon);
            }
            else TextRenderer.DrawText(g, a.System ? Theme.GSystem : Theme.GApp, fIconSmall, Rectangle.Inflate(r.Icon, S(2), S(2)), a.Muted ? Theme.Dim : Theme.Sub, C);
            if (a.Muted)
            {
                var mc = new PointF(r.Icon.Right - S(1), r.Icon.Bottom - S(1));
                Theme.Disc(g, mc, S(6), Theme.Card);
                TextRenderer.DrawText(g, Theme.GMute, fIconTiny, new Rectangle((int)mc.X - S(6), (int)mc.Y - S(6), S(12), S(12)), Theme.Sub, C);
            }
            var nameR = Rectangle.FromLTRB(r.R.X + S(84), r.R.Y, r.Zone.X - S(4), r.R.Bottom);
            TextRenderer.DrawText(g, a.Name, fBody, nameR, a.Muted ? Theme.Dim : a.Active ? Theme.Text : Theme.Sub, L);
            DrawSlider(g, r.Zone, a.Vol, a.Muted, Hov(r.Key + "#slider"), drag != null && drag.Key == r.Key);
            TextRenderer.DrawText(g, Pct(a.Vol), fNum, r.Num, a.Muted ? Theme.Dim : Theme.Sub, R);
        }

        RectangleF TrackOf(Rectangle zone)
        {
            float th = Math.Max(2, S(4));
            return new RectangleF(zone.X + S(10), zone.Y + zone.Height / 2f - th / 2, Math.Max(1, zone.Width - S(20)), th);
        }

        void DrawSlider(Graphics g, Rectangle zone, float v, bool muted, bool hot, bool pressed)
        {
            var t = TrackOf(zone);
            v = Math.Max(0, Math.Min(1, v));
            Theme.Fill(g, t, Theme.Track, t.Height / 2);
            float x = t.X + t.Width * v;
            if (x - t.X >= 1) Theme.Fill(g, new RectangleF(t.X, t.Y, x - t.X, t.Height), muted ? Theme.Dim : Theme.Accent, t.Height / 2);
            var c = new PointF(x, t.Y + t.Height / 2);
            Theme.Disc(g, c, S(9.5f), Theme.ThumbEdge);
            Theme.Disc(g, c, S(8.5f), Theme.ThumbOuter);
            Theme.Disc(g, c, pressed ? S(4.5f) : hot ? S(6.5f) : S(5.5f), muted ? Theme.Sub : hot && !pressed ? Theme.AccentHover : Theme.Accent);
        }

        // ------------------------------------------------------------ hit testing
        string HitKey(Point p, out Row row)
        {
            row = null;
            if (GearRect.Contains(p)) return "gear#";
            if (npShown)
            {
                var nr = NpRect;
                if (NpButton(nr, 0).Contains(p)) return "np#prev";
                if (NpButton(nr, 1).Contains(p)) return "np#play";
                if (NpButton(nr, 2).Contains(p)) return "np#next";
                if (nr.Contains(p)) return null;
            }
            if (p.Y < ListTop || p.Y >= ListTop + viewH) return null;
            foreach (var r in rows)
            {
                if (!r.R.Contains(p)) continue;
                row = r;
                if (r.K == RK.Empty) return null;
                if (Rectangle.Inflate(r.Zone, S(4), S(4)).Contains(p)) return r.Key + "#slider";
                if (r.K == RK.Device)
                {
                    if (r.Mute.Contains(p)) return r.Key + "#mute";
                    if (!r.Chip.IsEmpty && Rectangle.Inflate(r.Chip, S(2), S(4)).Contains(p)) return r.Key + "#chip";
                    return r.Key + "#row";
                }
                if (Rectangle.Inflate(r.Icon, S(6), S(8)).Contains(p)) return r.Key + "#icon";
                return r.Key + "#row";
            }
            return null;
        }

        bool Clickable(string key, Row r)
        {
            if (key == null) return false;
            if (key.EndsWith("#row")) return r != null && r.K == RK.Device && !r.Default;
            return true;
        }

        // ------------------------------------------------------------ mouse
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != null) { ApplyDrag(e.X, false); return; }
            string k = HitKey(e.Location, out var row);
            if (k != hover) { hover = k; Invalidate(); }
            Cursor = Clickable(k, row) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null && drag == null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            focusVisible = false;
            string k = HitKey(e.Location, out var row);
            pressKey = k;
            if (e.Button == MouseButtons.Left && k != null && k.EndsWith("#slider") && row != null)
            {
                drag = new Drag { Dev = row.D.Id, App = row.A?.Key, Track = TrackOf(row.Zone), Key = row.Key };
                focusKey = row.Key;
                Capture = true;
                ApplyDrag(e.X, false);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag != null)
            {
                ApplyDrag(e.X, true);
                drag = null;
                Capture = false;
                hover = HitKey(e.Location, out _);
                Invalidate();
                return;
            }
            string k = HitKey(e.Location, out var row);
            if (k == null || k != pressKey) return;
            if (e.Button == MouseButtons.Right)
            {
                if (row?.K == RK.Device) host.ShowDeviceMenu(row.D, PointToScreen(e.Location));
                else if (row?.K == RK.App) host.ShowAppMenu(row.D, row.A, PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (k == "gear#") { host.OpenMain(null, null); return; }
            if (k == "np#prev") { host.MediaCommand(2); return; }
            if (k == "np#play") { host.MediaCommand(0); if (host.Media != null) host.Media.Playing = !host.Media.Playing; Invalidate(); return; }
            if (k == "np#next") { host.MediaCommand(1); return; }
            if (row == null) return;
            if (k.EndsWith("#mute")) host.DeviceMute(row.D.Id, !row.D.Muted);
            else if (k.EndsWith("#chip")) TogglePeek(row.D.Id);
            else if (k.EndsWith("#icon") && row.A != null) host.AppMute(row.D.Id, row.A.Key, !row.A.Muted);
            else if (k.EndsWith("#row") && row.K == RK.Device && !row.Default) MakeDefault(row.D.Id);
            Invalidate();
        }

        public void TogglePeek(string devId)
        {
            if (!peeked.Remove(devId)) peeked.Add(devId);
            Relayout();
        }

        public bool IsPeeked(string devId) => peeked.Contains(devId);

        void MakeDefault(string id)
        {
            peeked.Remove(id);
            host.SetDefault(id);
            focusKey = "d|" + id;
            Relayout();
        }

        void ApplyDrag(int mx, bool final)
        {
            var t = drag.Track;
            float v = (float)Math.Round(Math.Max(0, Math.Min(1, (mx - t.X) / t.Width)) * 100) / 100f;
            if (drag.App == null) host.DeviceVolume(drag.Dev, v, final);
            else host.AppVolume(drag.Dev, drag.App, v, final);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int notches = e.Delta / 120;
            if (notches == 0) notches = Math.Sign(e.Delta);
            HitKey(e.Location, out var row);
            if (row != null && row.K != RK.Empty) { Nudge(row, notches * host.S.ScrollStep); return; }
            if (contentH > viewH) { scroll -= notches * S(40); Relayout(); }
        }

        void Nudge(Row r, int pct)
        {
            float cur = r.A != null ? r.A.Vol : r.D.Vol;
            float v = Math.Max(0, Math.Min(1, (float)Math.Round(cur * 100 + pct) / 100f));
            if (r.A != null) host.AppVolume(r.D.Id, r.A.Key, v, true);
            else host.DeviceVolume(r.D.Id, v, true);
            Invalidate();
        }

        // ------------------------------------------------------------ keyboard
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            var focusables = rows.Where(r => r.K != RK.Empty).ToList();
            int idx = focusables.FindIndex(r => r.Key == focusKey);
            Row cur = idx >= 0 ? focusables[idx] : null;

            switch (k)
            {
                case Keys.Escape: HidePanel(); return true;
                case Keys.Up: case Keys.Down: case Keys.Tab:
                    if (focusables.Count == 0) return true;
                    int dir = k == Keys.Up || (k == Keys.Tab && shift) ? -1 : 1;
                    idx = idx < 0 ? (dir > 0 ? 0 : focusables.Count - 1) : (idx + dir + focusables.Count) % focusables.Count;
                    focusKey = focusables[idx].Key;
                    focusVisible = true;
                    EnsureVisible(focusables[idx]);
                    Invalidate();
                    return true;
                case Keys.Left: case Keys.Right: case Keys.PageUp: case Keys.PageDown:
                    if (cur == null) { cur = focusables.FirstOrDefault(r => r.Default) ?? focusables.FirstOrDefault(); if (cur == null) return true; focusKey = cur.Key; }
                    focusVisible = true;
                    int step = k == Keys.PageUp || k == Keys.PageDown || shift ? 10 : host.S.ScrollStep;
                    Nudge(cur, k == Keys.Left || k == Keys.PageDown ? -step : step);
                    return true;
                case Keys.Enter:
                    if (cur == null) return true;
                    if (cur.K == RK.Device && shift) { if (!cur.Default && cur.AppCount > 0) TogglePeek(cur.D.Id); }
                    else if (cur.K == RK.Device) { if (!cur.Default) MakeDefault(cur.D.Id); }
                    else host.AppMute(cur.D.Id, cur.A.Key, !cur.A.Muted);
                    Invalidate();
                    return true;
                case Keys.Space: case Keys.M:
                    if (cur == null) return true;
                    if (cur.K == RK.Device) host.DeviceMute(cur.D.Id, !cur.D.Muted);
                    else host.AppMute(cur.D.Id, cur.A.Key, !cur.A.Muted);
                    Invalidate();
                    return true;
                case Keys.Apps:
                    if (cur == null) return true;
                    var pt = PointToScreen(new Point(cur.R.X + S(40), cur.R.Bottom));
                    if (cur.K == RK.Device) host.ShowDeviceMenu(cur.D, pt); else host.ShowAppMenu(cur.D, cur.A, pt);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void EnsureVisible(Row r)
        {
            int top = r.R.Top - ListTop, bottom = r.R.Bottom - ListTop;
            if (top < 0) { scroll += top; Relayout(); }
            else if (bottom > viewH) { scroll += bottom - viewH; Relayout(); }
        }
    }
}
