using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sip
{
    // Small dark controls for the main window (same family as Pulp's settings window).
    static class Ux
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        public static void Dark(Control c) => c.HandleCreated += (o, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
        public static void Cue(TextBox b, string text) { try { if (b.IsHandleCreated) SendMessage(b.Handle, 0x1501, (IntPtr)1, text); } catch { } }
        public const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        public const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    }

    abstract class SipControl : Control
    {
        protected readonly float s;
        protected SipControl(float scale)
        {
            s = scale;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected int S(float v) => (int)Math.Round(v * s);
    }

    class DarkBox : Panel
    {
        public readonly TextBox Box;
        readonly float s;
        public DarkBox(float scale)
        {
            s = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Padding = new Padding(S(10), S(7), S(10), S(5));
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = Theme.Ui(10f), Dock = DockStyle.Fill };
            Box.GotFocus += (o, e) => Invalidate();
            Box.LostFocus += (o, e) => Invalidate();
            Controls.Add(Box);
        }
        int S(float v) => (int)Math.Round(v * s);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Theme.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Fill(e.Graphics, r, Theme.Field, S(6));
            Theme.Stroke(e.Graphics, r, Box.Focused ? Theme.Accent : Theme.Border, S(6), Math.Max(1, s));
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
    }

    class Toggle : SipControl
    {
        bool on;
        public event Action Changed;
        public string Hint;
        public Toggle(string text, float scale) : base(scale)
        {
            Text = text;
            Font = Theme.Ui(10f);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            Height = S(28);
        }
        public bool On { get => on; set { if (on != value) { on = value; Invalidate(); } } }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); if (!Enabled) return; On = !On; Changed?.Invoke(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { On = !On; Changed?.Invoke(); e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int top = Hint != null ? S(2) : (Height - S(20)) / 2;
            var pill = new RectangleF(S(1), top, S(40), S(20));
            if (on) Theme.Fill(g, pill, Enabled ? Theme.Accent : Theme.Dim, pill.Height / 2);
            else Theme.Stroke(g, pill, Focused ? Theme.Text : Enabled ? Theme.Sub : Theme.Dim, pill.Height / 2, Math.Max(1, s));
            float d = on ? S(14) : S(12);
            float kx = on ? pill.Right - S(3) - d : pill.X + S(4);
            using (var b = new SolidBrush(on ? Color.FromArgb(24, 24, 24) : Enabled ? Theme.Sub : Theme.Dim))
                g.FillEllipse(b, kx, pill.Y + (pill.Height - d) / 2, d, d);
            var tr = new Rectangle(S(52), (int)pill.Y - S(2), Width - S(52), S(24));
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Dim, Ux.L);
            if (Hint != null)
                using (var f = Theme.Ui(8.75f))
                    TextRenderer.DrawText(g, Hint, f, new Rectangle(S(52), tr.Bottom, Width - S(52), Height - tr.Bottom), Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    class FlatBtn : SipControl
    {
        public bool Primary;
        bool hover, down;
        public FlatBtn(string text, float scale, bool primary = false) : base(scale)
        {
            Text = text;
            Primary = primary;
            Font = primary ? Theme.UiSemi(10f) : Theme.Ui(10f);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(S(120), S(32));
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, fg;
            if (!Enabled) { bg = Theme.CardSoft; fg = Theme.Dim; }
            else if (Primary) { bg = hover ? Theme.AccentHover : Theme.Accent; fg = Color.FromArgb(20, 20, 20); }
            else { bg = down ? Theme.CardHover : hover ? Color.FromArgb(52, 52, 52) : Theme.Card; fg = Theme.Text; }
            Theme.Fill(g, r, bg, S(6));
            if (Focused && !Primary && Enabled) Theme.Stroke(g, r, Theme.Sub, S(6), Math.Max(1, s));
            TextRenderer.DrawText(g, Text, Font, r, fg, Ux.C);
        }
    }

    // a dark drop-down list (owner drawn so it matches)
    class DarkCombo : ComboBox
    {
        readonly float s;
        public DarkCombo(float scale)
        {
            s = scale;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f);
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = (int)Math.Round(24 * s);
            IntegralHeight = false;
            MaxDropDownItems = 12;
            Ux.Dark(this);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.CardHover : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = e.Bounds; r.X += (int)(8 * s); r.Width -= (int)(8 * s);
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, r, Theme.Text, Ux.L);
        }
    }

    class ComboItem
    {
        public string Id, Text;
        public override string ToString() => Text;
    }

    // a single horizontal slider with its number; used for a device's volume limit
    class CapSlider : SipControl
    {
        float val = 1f;
        bool hover, drag;
        public float Min = 0.05f;
        public event Action<bool> Changed;   // true = finished (mouse up / key)
        public CapSlider(float scale) : base(scale)
        {
            Font = Theme.Ui(9.5f);
            Height = S(32);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }
        public float Value { get => val; set { val = Math.Max(Min, Math.Min(1, value)); Invalidate(); } }
        RectangleF Track => new RectangleF(S(10), Height / 2f - S(2), Width - S(86), S(4));
        void SetFromX(int x, bool final)
        {
            var t = Track;
            Value = (float)Math.Round(Math.Max(0, Math.Min(1, (x - t.X) / t.Width)) * 100) / 100f;
            Changed?.Invoke(final);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (!Enabled || e.Button != MouseButtons.Left) return; Focus(); drag = true; Capture = true; SetFromX(e.X, false); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFromX(e.X, false); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (!drag) return; drag = false; Capture = false; SetFromX(e.X, true); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (!Enabled) return; Value = (float)Math.Round(val * 100 + Math.Sign(e.Delta) * 2) / 100f; Changed?.Invoke(true); }
        protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.Shift ? 10 : 2;
            if (e.KeyCode == Keys.Left) { Value = (float)Math.Round(val * 100 - step) / 100f; Changed?.Invoke(true); }
            else if (e.KeyCode == Keys.Right) { Value = (float)Math.Round(val * 100 + step) / 100f; Changed?.Invoke(true); }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var t = Track;
            Color acc = Enabled ? Theme.Accent : Theme.Dim;
            Theme.Fill(g, t, Theme.Track, t.Height / 2);
            float x = t.X + t.Width * val;
            Theme.Fill(g, new RectangleF(t.X, t.Y, x - t.X, t.Height), acc, t.Height / 2);
            var c = new PointF(x, t.Y + t.Height / 2);
            Theme.Disc(g, c, S(9.5f), Focused ? Theme.Sub : Theme.ThumbEdge);
            Theme.Disc(g, c, S(8.5f), Theme.ThumbOuter);
            Theme.Disc(g, c, hover || drag ? S(6.5f) : S(5.5f), acc);
            string txt = val >= 0.995f ? "No limit" : ((int)Math.Round(val * 100)) + "%";
            TextRenderer.DrawText(g, txt, Font, new Rectangle(Width - S(70), 0, S(70), Height), Enabled ? Theme.Text : Theme.Dim, Ux.R);
        }
    }

    // a simple list of rows with one action each (remembered volumes, hidden apps)
    class ItemList : SipControl
    {
        public class Item { public string Title, Sub, Value, Action; public Bitmap Icon; public Action Click; }
        List<Item> items = new List<Item>();
        int hover = -1, scroll;
        bool hoverAction;
        public string Empty = "Nothing here";
        readonly Font fBody, fSmall;
        int RowH => S(44);

        public ItemList(float scale) : base(scale)
        {
            fBody = Theme.Ui(10f);
            fSmall = Theme.Ui(9f);
        }

        public void SetItems(List<Item> list) { items = list; scroll = Math.Max(0, Math.Min(scroll, MaxScroll)); Invalidate(); }
        int MaxScroll => Math.Max(0, items.Count * RowH - Height);

        Rectangle RowRect(int i) => new Rectangle(0, i * RowH - scroll, Width - S(6), RowH - S(4));
        Rectangle ActionRect(int i, Graphics g = null)
        {
            var r = RowRect(i);
            int w = TextRenderer.MeasureText(items[i].Action ?? "", fSmall).Width + S(24);
            return new Rectangle(r.Right - S(8) - w, r.Y + (r.Height - S(28)) / 2, w, S(28));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (items.Count == 0)
            {
                Theme.Fill(g, new Rectangle(0, 0, Width - S(6), RowH - S(4)), Theme.CardSoft, S(8));
                TextRenderer.DrawText(g, Empty, fSmall, new Rectangle(S(14), 0, Width - S(20), RowH - S(4)), Theme.Dim, Ux.L);
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var r = RowRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                var it = items[i];
                Theme.Fill(g, r, i == hover ? Theme.Card : Theme.CardSoft, S(8));
                int x = r.X + S(14);
                if (it.Icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(it.Icon, new Rectangle(x, r.Y + (r.Height - S(20)) / 2, S(20), S(20))); x += S(30); }
                var ar = ActionRect(i);
                int right = ar.X - S(10);
                if (it.Value != null)
                {
                    int vw = TextRenderer.MeasureText(it.Value, fBody).Width;
                    TextRenderer.DrawText(g, it.Value, fBody, new Rectangle(right - vw, r.Y, vw, r.Height), Theme.Sub, Ux.R);
                    right -= vw + S(14);
                }
                if (it.Sub != null)
                {
                    TextRenderer.DrawText(g, it.Title, fBody, new Rectangle(x, r.Y + S(4), right - x, r.Height / 2), Theme.Text, Ux.L);
                    TextRenderer.DrawText(g, it.Sub, fSmall, new Rectangle(x, r.Y + r.Height / 2 - S(1), right - x, r.Height / 2 - S(4)), Theme.Dim, Ux.L);
                }
                else TextRenderer.DrawText(g, it.Title, fBody, new Rectangle(x, r.Y, right - x, r.Height), Theme.Text, Ux.L);
                if (it.Action != null)
                {
                    bool h = i == hover && hoverAction;
                    Theme.Fill(g, ar, h ? Theme.CardHover : Theme.Field, S(6));
                    TextRenderer.DrawText(g, it.Action, fSmall, ar, h ? Theme.Text : Theme.Sub, Ux.C);
                }
            }
            if (MaxScroll > 0)
            {
                int total = items.Count * RowH, th = Math.Max(S(24), Height * Height / total);
                int ty = (Height - th) * scroll / MaxScroll;
                Theme.Fill(g, new Rectangle(Width - S(3), ty, S(3), th), Theme.Border, S(1.5f));
            }
        }

        int HitRow(Point p) { for (int i = 0; i < items.Count; i++) if (RowRect(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitRow(e.Location);
            bool ha = h >= 0 && items[h].Action != null && ActionRect(h).Contains(e.Location);
            if (h != hover || ha != hoverAction) { hover = h; hoverAction = ha; Cursor = ha ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; hoverAction = false; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = HitRow(e.Location);
            if (h >= 0 && e.Button == MouseButtons.Left && items[h].Action != null && ActionRect(h).Contains(e.Location)) items[h].Click?.Invoke();
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); scroll = Math.Max(0, Math.Min(MaxScroll, scroll - Math.Sign(e.Delta) * RowH)); Invalidate(); }
    }

    // accent colour picker: preset swatches + a custom colour
    class SwatchRow : SipControl
    {
        public Color Value = Theme.DefaultAccent;
        public event Action<Color> Picked;
        int hover = -1;
        readonly Font fIcon;
        int Count => Theme.Presets.Length + 1;
        public SwatchRow(float scale) : base(scale)
        {
            Height = S(36);
            Cursor = Cursors.Hand;
            fIcon = new Font(Theme.IconFont, 10f);
        }
        Rectangle Cell(int i) => new Rectangle(i * S(36), 0, S(36), S(36));
        int CustomIndex => Theme.Presets.Length;
        bool IsPreset => Array.Exists(Theme.Presets, p => p.ToArgb() == Value.ToArgb());
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            for (int i = 0; i < Count; i++)
            {
                var c = Cell(i);
                var center = new PointF(c.X + c.Width / 2f, c.Y + c.Height / 2f);
                bool custom = i == CustomIndex;
                Color col = custom ? (IsPreset ? Theme.Card : Value) : Theme.Presets[i];
                bool sel = custom ? !IsPreset : Theme.Presets[i].ToArgb() == Value.ToArgb();
                if (sel || i == hover) Theme.Disc(g, center, S(15), sel ? Theme.Text : Theme.Border);
                Theme.Disc(g, center, S(13), BackColor);
                Theme.Disc(g, center, S(11), col);
                if (custom && IsPreset) TextRenderer.DrawText(g, "\uE710", fIcon, c, Theme.Sub, Ux.C);   // Add
            }
        }
        int Hit(Point p) { for (int i = 0; i < Count; i++) if (Cell(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = Hit(e.Location);
            if (h < 0 || e.Button != MouseButtons.Left) return;
            if (h < CustomIndex) { Value = Theme.Presets[h]; Invalidate(); Picked?.Invoke(Value); return; }
            using (var dlg = new ColorDialog { Color = Value, FullOpen = true, AnyColor = true })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                Value = Color.FromArgb(255, dlg.Color);
                Invalidate();
                Picked?.Invoke(Value);
            }
        }
    }
}
