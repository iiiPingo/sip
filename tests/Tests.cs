using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Echoa
{
    // Fake host: no real audio devices, and its settings can never be saved.
    class TestHost : IPanelHost, IMainHost
    {
        public Snapshot Snap { get; } = new Snapshot();
        public Settings S { get; } = new Settings { SaveBlocked = true };
        public Icon AppIcon => SystemIcons.Application;
        public Rectangle MainBounds { get; set; }
        public NowPlaying Media { get; set; }
        public bool ShowNowPlaying => Media != null;
        public bool MicOn => false;
        public DevSnap Mic => Snap.Inputs.FirstOrDefault();
        public bool MicTesting => false;
        public bool MicTestFailed => false;
        public float MicLevel => 0;
        DevSnap Find(string id) => Snap.Devices.Concat(Snap.Inputs).FirstOrDefault(d => d.Id == id);
        public void SetDefault(string id) { Snap.DefaultId = id; }
        public void SetDefaultInput(string id) { Snap.DefaultInputId = id; }
        public void DeviceVolume(string id, float v, bool final) { var d = Find(id); if (d != null) d.Vol = v; }
        public void DeviceMute(string id, bool m) { var d = Find(id); if (d != null) d.Muted = m; }
        public void AppVolume(string devId, string key, float v, bool final) { }
        public void AppMute(string devId, string key, bool m) { }
        public void OpenMain(string page, string devId) { }
        public void MediaCommand(int c) { }
        public float CapOf(string id) => 1f;
        public void ShowDeviceMenu(DevSnap d, Point screen) { }
        public void ShowAppMenu(DevSnap d, AppSnap a, Point screen) { }
        public void MicMute() { }
        public void MicTest(bool on) { }
        public void SetSimpleFlyout(bool on) { S.SimpleFlyout = on; }
        public void PlayTest(string id) { }
        public void DevicePrefsChanged(string id) { }
        public void ForgetAppVolume(string exe) { }
        public void UnhideApp(string exe) { }
        public void SettingsChanged() { }
        public void SetAccent(Color c) { }
    }

    static partial class Tests
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static int passed, failed;
        static readonly bool OnWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        static readonly string[] SavedFormat =
        {
            "; Echoa settings. Echoa rewrites this file; edit it while Echoa is closed.",
            "volumememory=1", "appmemory=0", "scrolltray=1", "scrollstep=5", "blocksteal=1", "rules=1", "limits=1", "rampup=0",
            "nowplaying=1", "accent=#123456", "simpleflyout=1", "micfooter=0", "window=-8,10,-1200,-700",
            "dev.{id}=hide=1|nosteal=0|vol=0.37|cap=0.8|auto=1|fallback={other}|name=Desk|label=Speakers",
            "app.spotify.exe=0.42", "hideapp.game.exe=Game",
        };

        [STAThread]
        static int Main(string[] args)
        {
            // read-only: does this file come back exactly the same through Echoa's parser?
            if (args.Length == 2 && args[0] == "--format-check")
            {
                var lines = File.ReadAllLines(args[1]);
                bool same = Settings.Parse(lines).Serialize().SequenceEqual(lines);
                Console.WriteLine(same ? "FORMAT SAME" : "FORMAT DIFFERENT");
                return same ? 0 : 1;
            }
            Application.EnableVisualStyles();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            T("#2 wheel: small touchpad steps add up to one notch", WheelLogic);
            T("#10 locked file is never replaced", SettingsLocked);
            T("#10 lock that clears during retries loads real settings", SettingsLockClears);
            T("#10 unreadable content is never replaced", SettingsGarbage);
            T("#10 empty, comment-only and missing files behave", SettingsEmpty);
            T("#3 #10 saved format is unchanged", SettingsFormat);
            T("#10 save and reload keeps everything", SettingsRoundTrip);

            UI("#1 Alt+F4 hides the flyout and it reopens", AltF4);
            UI("#1 a plain close request also only hides it", PlainClose);
            UI("#2 flyout: touchpad equals one notch", WheelFlyout);
            UI("#2 Inputs page: touchpad equals one notch", WheelMics);
            UI("#2 volume limit slider: touchpad equals one notch", WheelCap);
            UI("#3 typed name stays on its own device", RenameRightDevice);
            UI("#3 normal renaming still works", RenameNormal);
            UI("#4 mic slider lets go when interrupted", DragMics);
            UI("#4 volume limit slider lets go when interrupted", DragCap);
            UI("#4 flyout slider lets go when interrupted", DragFlyout);
            UI("#4 normal dragging still works", DragNormal);

            RunBatch2();
            RunBatch3();
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed;
        }

        static void UI(string name, Action a)
        {
            if (!OnWindows) { Console.WriteLine("SKIP " + name + " (needs real Windows)"); return; }
            T(name, a);
        }

        static void T(string name, Action a)
        {
            try { a(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex)
            {
                failed++;
                var e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Console.WriteLine("FAIL " + name + ": " + e.Message);
            }
        }

        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static FieldInfo FI(Type t, string n) { for (; t != null; t = t.BaseType) { var f = t.GetField(n, Any); if (f != null) return f; } throw new MissingFieldException(n); }
        static object G(object o, string n) => FI(o.GetType(), n).GetValue(o);
        static object C(object o, string n, params object[] a)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethods(Any).FirstOrDefault(x => x.Name == n && x.GetParameters().Length == a.Length);
                if (m != null) return m.Invoke(o, a);
            }
            throw new MissingMethodException(n);
        }
        static IntPtr LP(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));
        static void Pump(int ms) { var t = Stopwatch.StartNew(); while (t.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); } }
        static void Post(Control c, int msg, int w, int x, int y) { Native.PostMessage(c.Handle, msg, (IntPtr)w, LP(x, y)); Pump(40); }
        const int MOVE = 0x200, DOWN = 0x201, UP = 0x202;
        static MouseEventArgs Wheel(Point p, int delta) => new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, delta);

        static DevSnap Dev(string id, bool input = false) => new DevSnap { Id = id, Name = "Device " + id, WinName = "Device " + id, Vol = 0.5f, FormFactor = 1, Input = input };
        static TestHost Host()
        {
            var h = new TestHost();
            h.Snap.Devices.AddRange(new[] { Dev("A"), Dev("B"), Dev("C") });
            h.Snap.DefaultId = "A";
            h.Snap.Inputs.Add(Dev("M0", true));
            h.Snap.DefaultInputId = "M0";
            return h;
        }
        static MainWindow Window(TestHost h, string page, string dev)
        {
            var w = new MainWindow(h, page, dev) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
            w.Show();
            Pump(60);
            return w;
        }
        static EchoaPanel Flyout(TestHost h)
        {
            var p = new EchoaPanel(h) { SuppressHide = true, Location = new Point(-20000, -20000) };
            p.Relayout();
            p.Show();
            Pump(50);
            return p;
        }
        static string TempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "EchoaTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.ini");
        }
        static void Clean(string file) { try { Directory.Delete(Path.GetDirectoryName(file), true); } catch { } }

        // ---------------------------------------------------------------- logic (runs anywhere)
        static void WheelLogic()
        {
            var w = new WheelSteps();
            Check(w.Take("a", 120) == 1, "one notch");
            w = new WheelSteps();
            int sum = 0; for (int i = 0; i < 10; i++) sum += w.Take("a", 12);
            Check(sum == 1, "10 small steps gave " + sum);
            w = new WheelSteps();
            Check(w.Take("a", 240) == 2, "fast wheel");
            Check(w.Take("a", -120) == -1, "down");
            w = new WheelSteps();
            w.Take("a", 60);
            Check(w.Take("a", -60) == 0 && w.Take("a", -60) == -1, "direction change must not cancel out");
            w = new WheelSteps();
            w.Take("a", 100);
            Check(w.Take("b", 100) == 0, "leftover must not carry to another row");
            w = new WheelSteps();
            w.Take("a", 100); Thread.Sleep(550);
            Check(w.Take("a", 100) == 0, "old leftover must expire");
        }

        static void SettingsLocked()
        {
            string f = TempFile();
            try
            {
                string original = string.Join("\r\n", SavedFormat) + "\r\n";
                File.WriteAllText(f, original);
                Settings s; Exception err;
                using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) s = Settings.LoadFile(f, out err, 2, 10);
                Check(s.SaveBlocked && err != null && !s.FirstRun, "must block saving");
                s.SaveTo(f);
                Check(File.ReadAllText(f) == original, "file was changed");
            }
            finally { Clean(f); }
        }

        static void SettingsLockClears()
        {
            string f = TempFile();
            try
            {
                File.WriteAllLines(f, SavedFormat);
                var hold = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Task.Delay(120).ContinueWith(_ => hold.Dispose());
                var s = Settings.LoadFile(f, out var err, 10, 50);
                Check(err == null && !s.SaveBlocked && s.Accent == "#123456", "real settings not loaded");
            }
            finally { Clean(f); }
        }

        static void SettingsGarbage()
        {
            string f = TempFile();
            try
            {
                string junk = "this is not a settings file\r\nsomething=else\r\n";
                File.WriteAllText(f, junk);
                var s = Settings.LoadFile(f, out _);
                Check(s.SaveBlocked, "must block saving");
                s.SaveTo(f);
                Check(File.ReadAllText(f) == junk, "file was changed");
            }
            finally { Clean(f); }
        }

        static void SettingsEmpty()
        {
            string f = TempFile();
            try
            {
                Check(Settings.LoadFile(f, out _).FirstRun, "missing file = first run");
                File.WriteAllText(f, "");
                var s = Settings.LoadFile(f, out _);
                Check(!s.SaveBlocked && !s.FirstRun, "empty file");
                File.WriteAllText(f, "; only a comment\r\n");
                Check(!Settings.LoadFile(f, out _).SaveBlocked, "comment-only file");
            }
            finally { Clean(f); }
        }

        static void SettingsFormat()
        {
            var again = Settings.Parse(SavedFormat).Serialize();
            Check(again.SequenceEqual(SavedFormat), "saved format changed");
        }

        static void SettingsRoundTrip()
        {
            string f = TempFile();
            try
            {
                File.WriteAllLines(f, SavedFormat);
                var s = Settings.LoadFile(f, out _);
                s.SaveTo(f);
                Check(File.ReadAllLines(f).SequenceEqual(SavedFormat), "changed after save");
                Check(!File.Exists(f + ".tmp"), "temp file left behind");
            }
            finally { Clean(f); }
        }

        // ---------------------------------------------------------------- UI (real WinForms, off screen)
        static void Reopens(EchoaPanel p)
        {
            Check(!p.IsDisposed, "flyout was destroyed");
            Check(!p.Visible, "flyout should be hidden");
            p.Location = new Point(-20000, -20000);
            p.Show(); Pump(30);
            Check(p.Visible, "flyout didn't reopen");
            p.HidePanel();
            p.Dispose();
        }

        static void AltF4()
        {
            var p = Flyout(Host());
            Native.PostMessage(p.Handle, 0x104, (IntPtr)0x73, (IntPtr)(1 << 29));
            Pump(200);
            Reopens(p);
        }

        static void PlainClose()
        {
            var p = Flyout(Host());
            Native.PostMessage(p.Handle, 0x10, IntPtr.Zero, IntPtr.Zero);
            Pump(150);
            Reopens(p);
        }

        static void WheelFlyout()
        {
            var h = Host();
            var p = new EchoaPanel(h);
            p.Relayout();
            var r = (Rectangle)G(((IList)G(p, "rows"))[0], "R");
            var at = new Point(r.X + r.Width / 2, r.Y + 10);
            var d = h.Snap.Devices[0];
            C(p, "OnMouseWheel", Wheel(at, 120));
            Check(Math.Abs(d.Vol - 0.52f) < 0.001f, "notch gave " + d.Vol);
            d.Vol = 0.5f; Thread.Sleep(550);
            for (int i = 0; i < 10; i++) C(p, "OnMouseWheel", Wheel(at, 12));
            Check(Math.Abs(d.Vol - 0.52f) < 0.001f, "touchpad gave " + d.Vol);
            p.Dispose();
        }

        static void WheelMics()
        {
            var h = Host();
            var w = Window(h, "Inputs", null);
            var mix = (MixList)G(w, "mix");
            var z = (Rectangle)C(mix, "Zone", (Rectangle)C(mix, "Row", 0));
            for (int i = 0; i < 10; i++) C(mix, "OnMouseWheel", Wheel(new Point(z.X + 20, z.Y + 5), 12));
            float v = h.Snap.Inputs[0].Vol;
            w.Close();
            Check(Math.Abs(v - 0.52f) < 0.001f, "touchpad gave " + v);
        }

        static void WheelCap()
        {
            var h = Host();
            h.S.CapsOn = true;
            var w = Window(h, "Devices", "A");
            var cap = (CapSlider)G(w, "dCap");
            cap.Value = 0.5f;
            for (int i = 0; i < 10; i++) C(cap, "OnMouseWheel", Wheel(new Point(20, 10), 12));
            float v = cap.Value;
            w.Close();
            Check(Math.Abs(v - 0.52f) < 0.001f, "touchpad gave " + v);
        }

        static void RenameRightDevice()
        {
            var h = Host();
            h.S.Dev("B").Rename = "Keep me";
            var w = Window(h, "Devices", "A");
            var pick = (PickList)G(w, "pick");
            var box = ((DarkBox)G(w, "dName")).Box;
            box.Focus(); Pump(40);
            box.Text = "Typed for A";
            var items = (List<PickList.Item>)G(pick, "items");
            var r = (Rectangle)C(pick, "RowRect", items.FindIndex(x => x.Id == "B"));
            Post(pick, DOWN, 1, r.X + r.Width / 2, r.Y + r.Height / 2);
            Post(pick, UP, 0, r.X + r.Width / 2, r.Y + r.Height / 2);
            string shown = box.Text;
            ((Toggle)G(w, "dShow")).Focus(); Pump(50);
            string a = h.S.Dev("A").Rename, b = h.S.Dev("B").Rename;
            w.Close();
            Check(a == "Typed for A", "A=" + a);
            Check(b == "Keep me", "B=" + b);
            Check(shown == "Keep me", "box showed " + shown);
        }

        static void RenameNormal()
        {
            var h = Host();
            var w = Window(h, "Devices", "A");
            var box = ((DarkBox)G(w, "dName")).Box;
            box.Focus(); Pump(40);
            box.Text = "  Desk speakers ";
            Native.PostMessage(box.Handle, 0x100, (IntPtr)0x0D, IntPtr.Zero); Pump(60);
            string a = h.S.Dev("A").Rename;
            w.Close();
            Check(a == "Desk speakers", "A=" + a);
        }

        static void DragMics()
        {
            var h = Host();
            var w = Window(h, "Inputs", null);
            var mix = (MixList)G(w, "mix");
            var z = (Rectangle)C(mix, "Zone", (Rectangle)C(mix, "Row", 0));
            int y = z.Y + z.Height / 2;
            Post(mix, DOWN, 1, z.X + z.Width / 2, y);
            mix.Capture = false; Pump(30);
            float before = h.Snap.Inputs[0].Vol;
            Post(mix, MOVE, 0, z.X + z.Width * 9 / 10, y);
            float after = h.Snap.Inputs[0].Vol;
            w.Close();
            Check(Math.Abs(after - before) < 0.001f, before + " -> " + after);
        }

        static void DragCap()
        {
            var h = Host();
            h.S.CapsOn = true;
            var w = Window(h, "Devices", "A");
            var cap = (CapSlider)G(w, "dCap");
            Post(cap, DOWN, 1, cap.Width / 3, cap.Height / 2);
            float held = cap.Value;
            cap.Capture = false; Pump(30);
            float saved = h.S.Dev("A").Cap;
            Post(cap, MOVE, 0, cap.Width * 2 / 3, cap.Height / 2);
            float after = cap.Value;
            w.Close();
            Check(Math.Abs(after - held) < 0.001f, held + " -> " + after);
            Check(Math.Abs(saved - held) < 0.001f, "value not kept: " + saved);
        }

        static void DragFlyout()
        {
            var h = Host();
            var p = Flyout(h);
            var z = (Rectangle)G(((IList)G(p, "rows"))[0], "Zone");
            int y = z.Y + z.Height / 2;
            Post(p, DOWN, 1, z.X + z.Width / 2, y);
            p.Capture = false; Pump(30);
            float before = h.Snap.Devices[0].Vol;
            Post(p, MOVE, 0, z.X + z.Width * 9 / 10, y);
            float after = h.Snap.Devices[0].Vol;
            p.Dispose();
            Check(Math.Abs(after - before) < 0.001f, before + " -> " + after);
        }

        static void DragNormal()
        {
            var h = Host();
            var w = Window(h, "Inputs", null);
            var mix = (MixList)G(w, "mix");
            var z = (Rectangle)C(mix, "Zone", (Rectangle)C(mix, "Row", 0));
            int y = z.Y + z.Height / 2;
            var m = h.Snap.Inputs[0];
            Post(mix, DOWN, 1, z.X + z.Width / 2, y);
            Post(mix, MOVE, 1, z.X + z.Width * 3 / 4, y);
            float during = m.Vol;
            Post(mix, UP, 0, z.X + z.Width * 3 / 4, y);
            float released = m.Vol;
            Post(mix, MOVE, 0, z.X + 5, y);
            float after = m.Vol;
            w.Close();
            Check(during > 0.6f, "drag didn't move it: " + during);
            Check(Math.Abs(released - during) < 0.01f && Math.Abs(after - released) < 0.001f, during + ", " + released + ", " + after);
        }
    }
}
