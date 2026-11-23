using System;
using System.Collections;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Echoa
{
    static partial class Tests
    {
        [DllImport("user32.dll")] static extern bool GetUpdateRect(IntPtr h, IntPtr rect, bool erase);

        static void RunBatch3()
        {
            UI("#6 flyout keeps its taskbar edge when Now Playing arrives", FlyoutEdge);
            UI("#7 dropdown redraws for the newly picked device", DropdownRedraw);
            UI("#8 Devices page fits at the smallest window", DevicesFit);
            UI("#9 every microphone can be scrolled to", MicsScroll);
            UI("#9 the slider still changes volume in a long list", MicsSliderInLongList);
            UI("#16 a maximized window comes back maximized", MaximizedBack);
        }

        static void FlyoutEdge()
        {
            var h = Host();
            var p = Flyout(h);
            int bottom = p.Bottom;
            P(p, "animTarget", p.Top); P(p, "animFrom", p.Top); P(p, "animStart", DateTime.UtcNow);
            ((System.Windows.Forms.Timer)G(p, "anim")).Start();
            h.Media = new NowPlaying { Title = "Song", Artist = "Artist", Playing = true };
            p.ModelChanged();
            Pump(350);
            int moved = p.Bottom - bottom;
            p.Dispose();
            Check(moved == 0, "bottom edge moved " + moved + "px");
        }

        static void DropdownRedraw()
        {
            var h = Host();
            h.S.Dev("A").Fallback = "B";
            h.S.Dev("B").Fallback = "A";
            var w = Window(h, "Devices", "A");
            var pick = (PickList)G(w, "pick");
            var combo = (DarkCombo)G(w, "dFallback");
            w.Refresh(); Pump(30);
            pick.Select("B");
            bool queued = GetUpdateRect(combo.Handle, IntPtr.Zero, false);
            string value = combo.SelectedItem?.ToString();
            w.Close();
            Check(queued, "no redraw queued");
            Check(value == "Device A", "value " + value);
        }

        static void DevicesFit()
        {
            var w = Window(Host(), "Devices", "A");
            w.ClientSize = new Size(100, 100);   // Windows stops it at the minimum size
            Pump(50);
            var test = (Control)G(w, "dTest");
            var def = (Control)G(w, "dDefault");
            var page = test.Parent;
            int right = def.Right - page.ClientSize.Width, bottom = test.Bottom - page.ClientSize.Height;
            w.Close();
            Check(right <= 0, "Make default sticks out " + right + "px");
            Check(bottom <= 0, "buttons cut off by " + bottom + "px");
        }

        static TestHost ManyMics()
        {
            var h = Host();
            for (int i = 1; i < 12; i++) h.Snap.Inputs.Add(Dev("M" + i, true));
            return h;
        }

        static void MicsScroll()
        {
            var h = ManyMics();
            var w = Window(h, "Inputs", null);
            var mix = (MixList)G(w, "mix");
            var r = (Rectangle)C(mix, "Row", 0);
            for (int i = 0; i < 12; i++) C(mix, "OnMouseWheel", Wheel(new Point(r.X + 12, r.Y + 12), -120));
            Pump(700);
            int bottom = ((Rectangle)C(mix, "Row", 11)).Bottom, height = mix.Height;
            bool same = h.Snap.Inputs.All(m => Math.Abs(m.Vol - 0.5f) < 0.001f);
            w.Close();
            Check(bottom <= height, "last mic ends at " + bottom + " of " + height);
            Check(same, "scrolling changed a volume");
        }

        static void MicsSliderInLongList()
        {
            var h = ManyMics();
            var w = Window(h, "Inputs", null);
            var mix = (MixList)G(w, "mix");
            var z = (Rectangle)C(mix, "Zone", (Rectangle)C(mix, "Row", 0));
            C(mix, "OnMouseWheel", Wheel(new Point(z.X + z.Width / 2, z.Y + z.Height / 2), 120));
            float v = h.Snap.Inputs[0].Vol;
            w.Close();
            Check(Math.Abs(v - 0.52f) < 0.001f, "volume " + v);
        }

        static void MaximizedBack()
        {
            var h = Host();
            var w = new MainWindow(h, "Inputs", null) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, Opacity = 0 };
            w.Show(); Pump(50);
            w.WindowState = FormWindowState.Maximized; Pump(60);
            w.WindowState = FormWindowState.Minimized; Pump(60);
            w.RestoreIfMinimized(); Pump(60);
            bool back = w.WindowState == FormWindowState.Maximized;
            w.WindowState = FormWindowState.Minimized; Pump(60);
            w.Close(); Pump(30);
            Check(back, "came back as " + (back ? "" : "not maximized"));
            Check(h.MainBounds.Width < 0, "closing while minimized forgot it was maximized");
        }
    }
}
