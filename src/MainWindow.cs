using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Echoa
{
    interface IMainHost
    {
        Snapshot Snap { get; }
        Settings S { get; }
        Icon AppIcon { get; }
        void SetDefault(string id);
        void SetDefaultInput(string id);
        void DeviceVolume(string id, float v, bool final);
        void DeviceMute(string id, bool m);
        void PlayTest(string id);
        void DevicePrefsChanged(string id);
        void ForgetAppVolume(string exe);
        void UnhideApp(string exe);
        void SettingsChanged();
        void SetAccent(Color c);
        Rectangle MainBounds { get; set; }   // remembered window position/size (empty = centred default)
    }

    // Microphones as rows: click a row to make it the default, mute button, volume slider.
    class MixList : EchoaControl
    {
        readonly IMainHost app;
        readonly Font fName, fSmall, fNum, fIcon, fIconSmall;
        readonly WheelSteps wheel = new WheelSteps();
        readonly SmoothScroller sc;
        string hover, dragId;
        RectangleF dragTrack;
        int dragX;
        int RowH => S(64);

        public MixList(IMainHost a, float scale) : base(scale)
        {
            app = a;
            fName = Theme.UiSemi(10f);
            fSmall = Theme.Ui(8.5f);
            fNum = Theme.Ui(9f);
            fIcon = new Font(Theme.IconFont, 13f);
            fIconSmall = new Font(Theme.IconFont, 11f);
            sc = new SmoothScroller(this, () => ContentH - Height);
        }

        List<DevSnap> Rows => app.Snap.Inputs;
        int ContentH => Math.Max(0, Rows.Count * (RowH + S(4)) - S(4));
        bool Overflow => ContentH > Height;
        Rectangle Row(int i) => new Rectangle(0, i * (RowH + S(4)) - sc.Value, Width - (Overflow ? S(8) : S(2)), RowH);
        public void Refit() { sc.Fit(); Invalidate(); }
        Rectangle Mute(Rectangle r) => new Rectangle(r.X + S(26), r.Y + S(33), S(24), S(24));
        Rectangle Zone(Rectangle r) => Rectangle.FromLTRB(r.X + S(56), r.Y + S(33), r.Right - S(42), r.Y + S(57));
        RectangleF Track(Rectangle z) => new RectangleF(z.X + S(10), z.Y + z.Height / 2f - S(2), Math.Max(1, z.Width - S(20)), S(4));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);
            var rows = Rows;
            if (rows.Count == 0)
            {
                Theme.Fill(g, new Rectangle(0, 0, Width - S(2), S(48)), Theme.CardSoft, S(8));
                TextRenderer.DrawText(g, "No microphones connected", Theme.Ui(9.5f), new Rectangle(S(16), 0, Width, S(48)), Theme.Dim, Ux.L);
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var d = rows[i];
                var r = Row(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                bool def = d.Id == app.Snap.DefaultInputId;
                bool hov = hover != null && hover.StartsWith(d.Id + "#");
                Theme.Fill(g, r, def ? Theme.Card : hov ? Theme.CardSoft : Color.FromArgb(36, 36, 36), S(8));
                var dc = new PointF(r.X + S(14), r.Y + S(19));
                if (def) Theme.Disc(g, dc, S(4), Theme.Accent);
                else using (var p = new Pen(hov ? Theme.Sub : Theme.Dim, Math.Max(1, s))) g.DrawEllipse(p, dc.X - S(4), dc.Y - S(4), S(8), S(8));
                TextRenderer.DrawText(g, Theme.GMic, fIcon, new Rectangle(r.X + S(26), r.Y + S(7), S(24), S(24)), def ? Theme.Text : Theme.Sub, Ux.C);
                int nx = r.X + S(56);
                int nw = Math.Min(r.Width - S(160), TextRenderer.MeasureText(g, d.Name, fName, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + S(2));
                TextRenderer.DrawText(g, d.Name, fName, new Rectangle(nx, r.Y + S(7), nw, S(24)), Theme.Text, Ux.L);
                string sub = (d.Sub ?? "") + (def ? (string.IsNullOrEmpty(d.Sub) ? "" : " · ") + "default" : "");
                if (sub.Length > 0) TextRenderer.DrawText(g, sub, fSmall, Rectangle.FromLTRB(nx + nw + S(6), r.Y + S(8), r.Right - S(12), r.Y + S(31)), Theme.Dim, Ux.L);
                var mr = Mute(r);
                bool mh = hover == d.Id + "#mute";
                if (mh) Theme.Fill(g, mr, Theme.CardHover, S(5));
                TextRenderer.DrawText(g, d.Muted ? Theme.GMute : Theme.GMic, fIconSmall, mr, d.Muted ? Theme.Accent : mh ? Theme.Text : Theme.Sub, Ux.C);
                var z = Zone(r);
                var t = Track(z);
                Theme.Fill(g, t, Theme.Track, t.Height / 2);
                float x = t.X + t.Width * Math.Max(0, Math.Min(1, d.Vol));
                Theme.Fill(g, new RectangleF(t.X, t.Y, x - t.X, t.Height), d.Muted ? Theme.Dim : Theme.Accent, t.Height / 2);
                var c = new PointF(x, t.Y + t.Height / 2);
                Theme.Disc(g, c, S(9.5f), Theme.ThumbEdge);
                Theme.Disc(g, c, S(8.5f), Theme.ThumbOuter);
                Theme.Disc(g, c, hover == d.Id + "#slider" ? S(6.5f) : S(5.5f), d.Muted ? Theme.Sub : Theme.Accent);
                TextRenderer.DrawText(g, ((int)Math.Round(d.Vol * 100)).ToString(), fNum, Rectangle.FromLTRB(r.Right - S(42), z.Y, r.Right - S(10), z.Bottom), d.Muted ? Theme.Dim : Theme.Sub, Ux.R);
            }
            if (Overflow)
            {
                int th = Math.Max(S(24), Height * Height / ContentH);
                int ty = (Height - th) * sc.Value / Math.Max(1, ContentH - Height);
                Theme.Fill(g, new Rectangle(Width - S(3), ty, S(3), th), Theme.Border, S(1.5f));
            }
        }

        string Hit(Point p, out DevSnap dev)
        {
            dev = null;
            var rows = Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = Row(i);
                if (!r.Contains(p)) continue;
                dev = rows[i];
                if (Rectangle.Inflate(Zone(r), S(4), S(4)).Contains(p)) return dev.Id + "#slider";
                if (Mute(r).Contains(p)) return dev.Id + "#mute";
                return dev.Id + "#row";
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragId != null) { Apply(e.X, false); return; }
            string h = Hit(e.Location, out var d);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h != null && !(h.EndsWith("#row") && d.Id == app.Snap.DefaultInputId) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (dragId == null && hover != null) { hover = null; Invalidate(); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string h = Hit(e.Location, out var d);
            if (e.Button != MouseButtons.Left || h == null || !h.EndsWith("#slider")) return;
            int i = Rows.IndexOf(d);
            dragId = d.Id;
            dragTrack = Track(Zone(Row(i)));
            Capture = true;
            Apply(e.X, false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragId != null) { Apply(e.X, true); dragId = null; Capture = false; return; }
            string h = Hit(e.Location, out var d);
            if (h == null || e.Button != MouseButtons.Left) return;
            if (h.EndsWith("#mute")) app.DeviceMute(d.Id, !d.Muted);
            else if (h.EndsWith("#row") && d.Id != app.Snap.DefaultInputId) app.SetDefaultInput(d.Id);
            Invalidate();
        }

        void Apply(int mx, bool final)
        {
            dragX = mx;
            float v = (float)Math.Round(Math.Max(0, Math.Min(1, (mx - dragTrack.X) / dragTrack.Width)) * 100) / 100f;
            app.DeviceVolume(dragId, v, final);
            Invalidate();
        }

        // Alt+Tab or a popup took the mouse mid-drag: finish there, so the slider can't follow the pointer afterwards
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragId == null || Capture) return;
            Apply(dragX, true);
            dragId = null;
            hover = null;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (dragId != null) return;
            string h = Hit(e.Location, out var d);
            // a list too long to fit scrolls; only the slider itself changes the volume there
            if (Overflow && (h == null || !h.EndsWith("#slider"))) { sc.Wheel(e.Delta, RowH); return; }
            if (d == null) return;
            int notches = wheel.Take(d.Id, e.Delta);
            if (notches == 0) return;
            float v = Math.Max(0, Math.Min(1, (float)Math.Round(d.Vol * 100 + notches * app.S.ScrollStep) / 100f));
            app.DeviceVolume(d.Id, v, true);
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); sc?.Fit(); }
    }

    // the output devices as a pickable list (left side of the Devices page)
    class PickList : EchoaControl
    {
        public class Item { public string Id, Title, Sub, Glyph; public bool Connected; }
        List<Item> items = new List<Item>();
        public string Selected;
        public event Action SelectedChanged;
        int hover = -1;
        readonly SmoothScroller sc;
        int scroll => sc.Value;
        readonly Font fName, fSmall, fIcon;
        int RowH => S(52);

        public PickList(float scale) : base(scale)
        {
            fName = Theme.Ui(10f);
            fSmall = Theme.Ui(8.5f);
            fIcon = new Font(Theme.IconFont, 12f);
            sc = new SmoothScroller(this, () => items.Count * RowH - Height);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        public void SetItems(List<Item> list)
        {
            items = list;
            if (Selected == null || !items.Any(i => i.Id == Selected)) { Selected = items.FirstOrDefault()?.Id; SelectedChanged?.Invoke(); }
            sc.Fit();
            Invalidate();
        }

        Rectangle RowRect(int i) => new Rectangle(0, i * RowH - scroll, Width - S(6), RowH - S(4));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            for (int i = 0; i < items.Count; i++)
            {
                var r = RowRect(i);
                var it = items[i];
                bool sel = it.Id == Selected;
                if (sel) Theme.Fill(g, r, Theme.Card, S(8));
                else if (i == hover) Theme.Fill(g, r, Theme.CardSoft, S(8));
                if (sel) Theme.Fill(g, new Rectangle(r.X, r.Y + S(14), S(3), r.Height - S(28)), Theme.Accent, S(1.5f));
                Color c = it.Connected ? (sel ? Theme.Text : Theme.Sub) : Theme.Dim;
                TextRenderer.DrawText(g, it.Glyph, fIcon, new Rectangle(r.X + S(10), r.Y, S(26), r.Height), c, Ux.C);
                TextRenderer.DrawText(g, it.Title, fName, new Rectangle(r.X + S(44), r.Y + S(6), r.Width - S(50), r.Height / 2 - S(2)), it.Connected ? Theme.Text : Theme.Sub, Ux.L);
                TextRenderer.DrawText(g, it.Sub, fSmall, new Rectangle(r.X + S(44), r.Y + r.Height / 2, r.Width - S(50), r.Height / 2 - S(6)), Theme.Dim, Ux.L);
            }
        }

        int Hit(Point p) { for (int i = 0; i < items.Count; i++) if (RowRect(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); int h = Hit(e.Location); if (h >= 0) Select(items[h].Id); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); sc.Wheel(e.Delta, RowH); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); sc?.Fit(); }
        protected override bool IsInputKey(Keys k) => k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int i = items.FindIndex(x => x.Id == Selected);
            if (e.KeyCode == Keys.Down && i < items.Count - 1) Select(items[i + 1].Id);
            else if (e.KeyCode == Keys.Up && i > 0) Select(items[i - 1].Id);
        }
        public void Select(string id) { if (Selected == id) return; Selected = id; Invalidate(); SelectedChanged?.Invoke(); }
    }

    // ---------------------------------------------------------------- the main window
    class MainWindow : Form
    {
        readonly IMainHost app;
        readonly float s;
        readonly string[] pages = { "Inputs", "Devices", "Apps", "Settings" };
        readonly List<Panel> pagePanels = new List<Panel>();
        readonly Font fBody, fSmall, fTitle, fNav, fH2;
        int page, hoverNav = -1;

        // inputs
        MixList mix;
        // devices
        PickList pick;
        Label dTitle, dWin, capHint;
        DarkBox dName;
        Toggle dShow, dAuto, dSteal;
        DarkCombo dFallback;
        CapSlider dCap;
        FlatBtn dTest, dDefault;
        bool binding;
        string boundDev;   // the device whose name is in the name box
        // apps
        ItemList remembered, hiddenApps;
        // settings
        readonly List<Tuple<Toggle, Func<bool>>> settingToggles = new List<Tuple<Toggle, Func<bool>>>();

        int S(float v) => (int)Math.Round(v * s);
        int NavW => S(176);
        int W => S(800);
        int H => S(640);
        Rectangle Content => new Rectangle(NavW + S(28), S(22), W - NavW - S(52), H - S(44));

        public MainWindow(IMainHost a, string startPage, string devId)
        {
            app = a;
            Text = "Echoa";
            Icon = app.AppIcon;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            ClientSize = new Size(W, H);
            fBody = Theme.Ui(10f);
            fSmall = Theme.Ui(9f);
            fTitle = Theme.UiTitle(15f);
            fH2 = Theme.UiSemi(10.5f);
            fNav = Theme.Ui(10.5f);

            BuildInputs();
            BuildDevices();
            BuildApps();
            BuildSettings();
            ModelChanged();
            if (devId != null) pick.Select(devId);
            int start = Array.IndexOf(pages, startPage ?? "");
            ShowPage(start < 0 ? 0 : start);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                int cap = Theme.Nav.R | (Theme.Nav.G << 8) | (Theme.Nav.B << 16);
                Native.DwmSetWindowAttribute(Handle, 35, ref cap, 4);   // caption colour (Win11)
            }
            catch { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && !(dName != null && dName.Box.Focused)) { Close(); e.Handled = true; }
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D4) { ShowPage(e.KeyCode - Keys.D1); e.Handled = true; }
        }

        public void ShowPageFor(string pageName, string devId)
        {
            int i = Array.IndexOf(pages, pageName ?? "");
            if (devId != null) pick.Select(devId);
            if (i >= 0) ShowPage(i);
        }

        // ------------------------------------------------------------ helpers
        Panel NewPage(string title, string subtitle)
        {
            var c = Content;
            var p = new Panel { Location = c.Location, Size = c.Size, BackColor = Theme.Bg, Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(new Label { Text = title, Font = fTitle, ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, 0), BackColor = Theme.Bg });
            if (subtitle != null)
                p.Controls.Add(new Label { Text = subtitle, Font = fSmall, ForeColor = Theme.Sub, AutoSize = false, Size = new Size(c.Width, S(20)), Location = new Point(0, S(36)), BackColor = Theme.Bg, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right });
            Controls.Add(p);
            pagePanels.Add(p);
            return p;
        }

        Label Lbl(Control p, string text, int x, int y, int w, int h, Color? color = null, Font f = null)
        {
            var l = new Label { Text = text, Font = f ?? fBody, ForeColor = color ?? Theme.Text, BackColor = Theme.Bg, AutoSize = false, Location = new Point(x, y), Size = new Size(w, h), UseMnemonic = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(l);
            return l;
        }

        Toggle Tg(Control p, string text, int x, int y, int w, string hint = null)
        {
            var t = new Toggle(text, s) { Location = new Point(x, y), Width = w, Hint = hint, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            if (hint != null) t.Height = S(46);
            p.Controls.Add(t);
            return t;
        }

        // ------------------------------------------------------------ pages
        void BuildInputs()
        {
            var p = NewPage("Inputs", "Click a microphone to make it the default for every app.");
            mix = new MixList(app, s) { Location = new Point(0, S(68)), Size = new Size(p.Width, p.Height - S(68)), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(mix);
        }

        void BuildDevices()
        {
            var p = NewPage("Devices", null);
            int lw = S(214), top = S(52);
            pick = new PickList(s) { Location = new Point(0, top), Size = new Size(lw, p.Height - top), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            pick.SelectedChanged += BindDevice;
            p.Controls.Add(pick);

            int x = lw + S(22), w = p.Width - x, y = top;
            dTitle = Lbl(p, "", x, y, w, S(28), Theme.Text, Theme.UiTitle(13f));
            dWin = Lbl(p, "", x, y + S(28), w, S(20), Theme.Dim, fSmall);
            y += S(58);
            Lbl(p, "Name in Echoa", x, y, w, S(20), Theme.Sub, fSmall);
            dName = new DarkBox(s) { Location = new Point(x, y + S(22)), Size = new Size(w, S(34)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            dName.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveName(); } };
            dName.Box.LostFocus += (o, e) => SaveName();
            p.Controls.Add(dName);
            y += S(68);
            dShow = Tg(p, "Show in the flyout", x, y, w);
            dShow.Changed += () => EditPrefs(pr => pr.Hidden = !dShow.On);
            y += S(34);
            dAuto = Tg(p, "Make it the output when it connects", x, y, w);
            dAuto.Changed += () => EditPrefs(pr => pr.AutoDefault = dAuto.On);
            y += S(38);
            Lbl(p, "When it disconnects, switch to", x, y, w, S(20), Theme.Sub, fSmall);
            dFallback = new DarkCombo(s) { Location = new Point(x, y + S(22)), Width = w, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            dFallback.SelectedIndexChanged += (o, e) => { if (dFallback.SelectedItem is ComboItem ci) EditPrefs(pr => pr.Fallback = ci.Id ?? ""); };
            p.Controls.Add(dFallback);
            y += S(64);
            dSteal = Tg(p, "Don't let it take over when it connects", x, y, w);
            dSteal.Changed += () => EditPrefs(pr => pr.NoSteal = dSteal.On);
            y += S(40);
            Lbl(p, "Volume limit", x, y, w, S(20), Theme.Sub, fSmall);
            dCap = new CapSlider(s) { Location = new Point(x - S(10), y + S(20)), Width = w + S(10), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            dCap.Changed += final => { if (final) EditPrefs(pr => pr.Cap = dCap.Value >= 0.995f ? 1f : dCap.Value); };
            p.Controls.Add(dCap);
            capHint = Lbl(p, "Volume limits are off. Turn them on in Settings.", x, y + S(52), w, S(20), Theme.Dim, fSmall);
            y += S(82);
            dTest = new FlatBtn("Play test sound", s) { Location = new Point(x, y), Width = S(140) };
            dTest.Click += (o, e) => { if (pick.Selected != null) app.PlayTest(pick.Selected); };
            dDefault = new FlatBtn("Make default", s) { Location = new Point(x + S(150), y), Width = S(130) };
            dDefault.Click += (o, e) => { if (pick.Selected != null) { app.SetDefault(pick.Selected); BindDevice(); } };
            p.Controls.Add(dTest);
            p.Controls.Add(dDefault);
        }

        void SaveName()
        {
            if (binding || boundDev == null) return;
            string n = dName.Box.Text.Trim();
            var pr = app.S.Dev(boundDev);
            if (pr.Rename == n) return;
            pr.Rename = n;
            app.DevicePrefsChanged(boundDev);
        }

        void EditPrefs(Action<DevPrefs> edit)
        {
            if (binding || pick.Selected == null) return;
            string id = pick.Selected;
            edit(app.S.Dev(id));
            app.DevicePrefsChanged(id);
        }

        string LabelFor(string id)
        {
            var d = app.Snap.Devices.FirstOrDefault(x => x.Id == id);
            if (d != null) return d.Name;
            return app.S.Devices.TryGetValue(id, out var p) && p.Label.Length > 0 ? (p.Rename.Length > 0 ? p.Rename : p.Label) : "Unknown device";
        }

        List<PickList.Item> DeviceItems()
        {
            var list = new List<PickList.Item>();
            foreach (var d in app.Snap.Devices)
            {
                var pr = app.S.Devices.TryGetValue(d.Id, out var x) ? x : null;
                string sub = d.Id == app.Snap.DefaultId ? "Default output" : pr != null && pr.Hidden ? "Hidden" : "Connected";
                list.Add(new PickList.Item { Id = d.Id, Title = d.Name, Sub = sub, Glyph = Theme.DeviceGlyph(d.FormFactor), Connected = true });
            }
            foreach (var kv in app.S.Devices.Where(kv => !kv.Value.IsEmpty && kv.Value.Label.Length > 0 && !app.Snap.Devices.Any(d => d.Id == kv.Key))
                                            .OrderBy(kv => kv.Value.Label, StringComparer.CurrentCultureIgnoreCase))
                list.Add(new PickList.Item { Id = kv.Key, Title = kv.Value.Rename.Length > 0 ? kv.Value.Rename : kv.Value.Label, Sub = "Not connected" + (kv.Value.Hidden ? " · hidden" : ""), Glyph = Theme.GSpeakers, Connected = false });
            return list;
        }

        void BindDevice()
        {
            if (pick.Selected != boundDev) SaveName();   // what was typed belongs to the device being left
            string id = pick.Selected;
            binding = true;
            try
            {
                bool switched = id != boundDev;
                boundDev = id;
                bool has = id != null;
                foreach (Control c in new Control[] { dName, dShow, dAuto, dFallback, dSteal, dCap, dTest, dDefault }) c.Visible = has;
                capHint.Visible = has && !app.S.CapsOn;
                if (!has) { dName.Box.Text = ""; dTitle.Text = "No output devices"; dWin.Text = ""; return; }
                var d = app.Snap.Devices.FirstOrDefault(x => x.Id == id);
                var pr = app.S.Devices.TryGetValue(id, out var x0) ? x0 : new DevPrefs();
                dTitle.Text = LabelFor(id);
                string win = d != null ? (string.IsNullOrEmpty(d.Sub) ? d.WinName : d.WinName + " (" + d.Sub + ")") : pr.Label;
                dWin.Text = (d == null ? "Not connected · " : "") + "Windows calls it " + win;
                if (switched || !dName.Box.Focused) dName.Box.Text = pr.Rename;
                Ux.Cue(dName.Box, d?.WinName ?? pr.Label);
                dShow.On = !pr.Hidden;
                dAuto.On = pr.AutoDefault;
                dAuto.Enabled = app.S.RulesOn;
                dSteal.On = pr.NoSteal;
                dSteal.Enabled = app.S.BlockSteal;
                dFallback.Enabled = app.S.RulesOn;
                dFallback.Items.Clear();
                dFallback.Items.Add(new ComboItem { Id = "", Text = "Let Windows choose" });
                int sel = 0;
                foreach (var it in DeviceItems())
                {
                    if (it.Id == id) continue;
                    dFallback.Items.Add(new ComboItem { Id = it.Id, Text = it.Title + (it.Connected ? "" : " (not connected)") });
                    if (it.Id == pr.Fallback) sel = dFallback.Items.Count - 1;
                }
                dFallback.SelectedIndex = sel;
                dCap.Value = pr.Cap;
                dCap.Enabled = app.S.CapsOn;
                dTest.Enabled = d != null;
                dDefault.Enabled = d != null && id != app.Snap.DefaultId;
                dDefault.Text = d != null && id == app.Snap.DefaultId ? "It's the default" : "Make default";
            }
            finally { binding = false; }
        }

        void BuildApps()
        {
            var p = NewPage("Apps", null);
            int y = S(52);
            Lbl(p, "Remembered volumes", 0, y, p.Width, S(24), Theme.Text, fH2);
            Lbl(p, "Set an app's volume in Echoa and it starts at that level every time it opens, on any device.", 0, y + S(24), p.Width, S(20), Theme.Dim, fSmall);
            remembered = new ItemList(s) { Location = new Point(0, y + S(50)), Size = new Size(p.Width, S(178)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Empty = "Nothing yet. Move an app's slider in the flyout and Echoa remembers it." };
            p.Controls.Add(remembered);
            y += S(246);
            Lbl(p, "Hidden apps", 0, y, p.Width, S(24), Theme.Text, fH2);
            Lbl(p, "Hidden apps don't show in the flyout. Right-click an app in the flyout to hide it.", 0, y + S(24), p.Width, S(20), Theme.Dim, fSmall);
            hiddenApps = new ItemList(s) { Location = new Point(0, y + S(50)), Size = new Size(p.Width, p.Height - y - S(50)), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Empty = "No hidden apps" };
            p.Controls.Add(hiddenApps);
        }

        void FillApps()
        {
            var seen = new Dictionary<string, AppSnap>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in app.Snap.Devices) foreach (var a in d.Apps) if (!seen.ContainsKey(a.Exe)) seen[a.Exe] = a;
            remembered.SetItems(app.S.AppVolumes.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv =>
            {
                string exe = kv.Key;
                seen.TryGetValue(exe, out var a);
                return new ItemList.Item { Title = a?.Name ?? System.IO.Path.GetFileNameWithoutExtension(exe), Sub = exe, Icon = a?.Icon, Value = ((int)Math.Round(kv.Value * 100)).ToString(), Action = "Forget", Click = () => { app.ForgetAppVolume(exe); FillApps(); } };
            }).ToList());
            hiddenApps.SetItems(app.S.HiddenApps.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase).Select(kv =>
            {
                string exe = kv.Key;
                seen.TryGetValue(exe, out var a);
                return new ItemList.Item { Title = kv.Value.Length > 0 ? kv.Value : exe, Sub = exe == "system" ? null : exe, Icon = a?.Icon, Action = "Show", Click = () => { app.UnhideApp(exe); FillApps(); } };
            }).ToList());
        }

        void BuildSettings()
        {
            var p = NewPage("Settings", "Everything here takes effect right away.");
            p.AutoScroll = true;   // scrolls when the window is made small
            Ux.Dark(p);
            int y = S(64), w = p.Width;
            var accentLabel = Lbl(p, "Accent colour", 0, y, S(120), S(36), Theme.Text, fBody);
            accentLabel.TextAlign = ContentAlignment.MiddleLeft;
            accentLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;   // fixed width: it sits on top of the swatches, so it must not stretch over them
            var sw = new SwatchRow(s) { Location = new Point(S(120), y), Width = w - S(120), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Theme.TryHex(app.S.Accent, out var cur);
            sw.Value = app.S.Accent.Length == 0 ? Theme.DefaultAccent : cur;
            sw.Picked += c => { app.SetAccent(c); RefreshAll(this); };
            p.Controls.Add(sw);
            y += S(46);
            var st = app.S;
            void Add(string text, string hint, Func<bool> get, Action<bool> set)
            {
                var t = Tg(p, text, 0, y, w, hint);
                t.On = get();
                t.Changed += () => { set(t.On); app.SettingsChanged(); BindDevice(); };
                settingToggles.Add(Tuple.Create(t, get));
                y += hint != null ? S(50) : S(34);
            }
            Add("Remember each device's volume", "When a device reconnects, it gets its last volume back.", () => st.VolumeMemory, v => st.VolumeMemory = v);
            Add("Remember app volumes", "Apps start at the volume you last set for them in Echoa.", () => st.AppMemory, v => st.AppMemory = v);
            Add("Auto-switch rules", "Set per device on the Devices page: switch on connect, fall back on disconnect.", () => st.RulesOn, v => st.RulesOn = v);
            Add("Stop marked devices taking over", "Per device on the Devices page.", () => st.BlockSteal, v => st.BlockSteal = v);
            Add("Volume limits", "Per device on the Devices page. Echoa keeps a device at or below its limit.", () => st.CapsOn, v => st.CapsOn = v);
            Add("Gentle ramp-up when switching to headphones", "Starts low and eases up to the volume over about a second.", () => st.RampOn, v => st.RampOn = v);
            Add("Simple flyout: only the main output", "Hides the other devices so there's nothing to switch: just the main output and its apps. The arrow on the main output in the flyout flips this too.", () => st.SimpleFlyout, v => st.SimpleFlyout = v);
            Add("Show \"Now playing\" in the flyout", null, () => st.NowPlaying, v => st.NowPlaying = v);
            Add("Mic controls in the flyout", "Replaces the logo at the bottom of the flyout: mute button, mic picker (click the name) and a mic test.", () => st.MicFooter, v => st.MicFooter = v);
            Add("Scroll on the tray icon changes volume", null, () => st.ScrollTray, v => st.ScrollTray = v);
            Add("Start Echoa with Windows", null, () => Settings.StartWithWindows, v => Settings.StartWithWindows = v);
            var folder = new FlatBtn("Open data folder", s) { Location = new Point(0, y + S(10)), Width = S(150) };
            folder.Click += (o, e) => { try { Process.Start("explorer.exe", "\"" + Settings.Dir + "\""); } catch { } };
            p.Controls.Add(folder);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            bool max = WindowState == FormWindowState.Maximized || (WindowState == FormWindowState.Minimized && Native.RestoresMaximized(Handle));
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            app.MainBounds = max ? new Rectangle(b.Location, new Size(-b.Width, -b.Height)) : b;
        }

        // back from the taskbar the way it was, maximized included
        public void RestoreIfMinimized()
        {
            if (WindowState == FormWindowState.Minimized) Native.ShowWindow(Handle, Native.SW_RESTORE);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            MinimumSize = SizeFromClientSize(new Size(S(750), S(520)));   // the smallest size where every page fits
            var r = app.MainBounds;
            if (r.IsEmpty) return;
            bool max = r.Width < 0;
            r = new Rectangle(r.X, r.Y, Math.Abs(r.Width), Math.Abs(r.Height));
            // only restore if it's still on a screen (monitors change)
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X, r.Y, Math.Min(r.Width, 200), 60))))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = r;
            }
            if (max) WindowState = FormWindowState.Maximized;
        }

        // ------------------------------------------------------------ live updates
        public void ModelChanged()
        {
            mix.Refit();
            pick.SetItems(DeviceItems());
            if (!dName.Box.Focused && !(dFallback.DroppedDown)) BindDevice();
            FillApps();
        }

        public void ValuesChanged() => mix.Invalidate();

        // ------------------------------------------------------------ nav
        Rectangle NavItem(int i) => new Rectangle(S(10), S(70) + i * S(40), NavW - S(20), S(36));

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < pagePanels.Count; k++) pagePanels[k].Visible = k == i;
            if (i == 2) FillApps();
            if (i == 3) foreach (var t in settingToggles) t.Item1.On = t.Item2();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            using (var b = new SolidBrush(Theme.Nav)) g.FillRectangle(b, 0, 0, NavW, ClientSize.Height);
            Theme.DrawLogo(g, new RectangleF(S(18), S(20), S(30), S(30)));
            TextRenderer.DrawText(g, "Echoa", Theme.UiTitle(13f), new Rectangle(S(56), S(18), S(110), S(34)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            for (int i = 0; i < pages.Length; i++)
            {
                var r = NavItem(i);
                if (i == page) Theme.Fill(g, r, Theme.Card, S(6));
                else if (i == hoverNav) Theme.Fill(g, r, Color.FromArgb(36, 36, 36), S(6));
                if (i == page) Theme.Fill(g, new Rectangle(r.X, r.Y + S(9), S(3), r.Height - S(18)), Theme.Accent, S(1.5f));
                TextRenderer.DrawText(g, pages[i], fNav, new Rectangle(r.X + S(14), r.Y, r.Width - S(14), r.Height), i == page ? Theme.Text : Theme.Sub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, "Version " + Application.ProductVersion, fSmall, new Rectangle(S(18), ClientSize.Height - S(40), NavW - S(20), S(20)), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < pages.Length; i++) if (NavItem(i).Contains(e.Location)) h = i;
            if (h != hoverNav) { hoverNav = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hoverNav != -1) { hoverNav = -1; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < pages.Length; i++) if (NavItem(i).Contains(e.Location)) ShowPage(i);
        }

        public void SyncSettings() { foreach (var t in settingToggles) t.Item1.On = t.Item2(); }

        static void RefreshAll(Control c) { c.Invalidate(); foreach (Control k in c.Controls) RefreshAll(k); }

        // for the offscreen layout check
        public void ShowPageForTest(int i) => ShowPage(i);
    }
}
