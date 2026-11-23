using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Echoa
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, "Echoa.SingleInstance.v1", out bool created))
            {
                bool exit = args.Contains("--exit");
                if (!created)
                {
                    Native.PostMessage((IntPtr)0xffff, exit ? Native.WM_EXITECHOA : Native.WM_SHOWSIP, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                if (exit) return;   // asked to close, but Echoa wasn't running
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (o, e) => Log.Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log.Crash(e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (o, e) => { Log.Crash(e.Exception); e.SetObserved(); };
                // audio events arrive on background threads and are posted through this context,
                // so it has to exist before anything is created
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                App app;
                try { app = new App(); }
                catch (Exception ex)
                {
                    Log.Crash(ex);
                    MessageBox.Show("Echoa couldn't start:\n\n" + ex.Message + "\n\nDetails are in " + Log.Path, "Echoa", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(app);
                GC.KeepAlive(mutex);
            }
        }
    }

    static class Log
    {
        public static string Path => System.IO.Path.Combine(Settings.Dir, "crash.log");
        static readonly object gate = new object();
        public static void Crash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.Dir);
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(Path);   // never let it grow forever
                    File.AppendAllText(Path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + "\r\n\r\n");
                }
            }
            catch { }
        }
    }

    class App : ApplicationContext, IAudioSink, IPanelHost, IMainHost
    {
        public Settings S { get; }
        public Snapshot Snap { get; private set; } = new Snapshot();
        public NowPlaying Media { get; private set; }
        public Icon AppIcon { get; }

        readonly SynchronizationContext ui;
        readonly Engine engine;
        readonly TrayIcon tray;
        readonly EchoaPanel panel;
        readonly Media media;
        readonly System.Windows.Forms.Timer saveTimer, mediaTimer, micTimer;
        MicMeter micMeter;
        DateTime micTestUntil;
        readonly ContextMenuStrip menu;
        MainWindow main;
        int trayPct = -1;
        bool trayMuted, trayEmpty;
        string trayTip;
        bool firstSnapshot = true;

        public App()
        {
            ui = SynchronizationContext.Current;
            S = Settings.Load();
            if (S.FirstRun) Settings.StartWithWindows = true;
            if (Theme.TryHex(S.Accent, out var accent)) Theme.SetAccent(accent);
            try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { AppIcon = SystemIcons.Application; }

            panel = new EchoaPanel(this);
            panel.VisibleChanged += (o, e) => Safe(PanelVisibility);
            menu = Menus.Make(Theme.Ui(9.5f));
            menu.Closed += (o, e) => ReleasePanel();

            engine = new Engine(this, ui) { IconPx = panel.IconPx };
            PushSettings();

            saveTimer = new System.Windows.Forms.Timer { Interval = 800 };
            saveTimer.Tick += (o, e) => { saveTimer.Stop(); S.Save(); };

            // "Now playing" is only asked for while the flyout is open
            media = new Media(ui, np => { Media = np; panel.ModelChanged(); }, panel.ArtPx);
            mediaTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            mediaTimer.Tick += (o, e) => media.Refresh();
            micTimer = new System.Windows.Forms.Timer { Interval = 33 };
            micTimer.Tick += (o, e) => Safe(MicTimerTick);

            tray = new TrayIcon { WheelEnabled = S.ScrollTray };
            tray.Activated += kb => Safe(() => ToggleFlyout(kb));
            tray.MenuRequested += pt => Safe(() => ShowTrayMenu(pt));
            tray.Wheel += n => Safe(() => TrayWheel(n));
            tray.OtherMessage = m =>
            {
                if (m.Msg == Native.WM_SHOWSIP) Safe(() => OpenMain(null, null));
                else if (m.Msg == Native.WM_EXITECHOA)
                {
                    // close cleanly after this message is handled, not inside it
                    var t = new System.Windows.Forms.Timer { Interval = 10 };
                    t.Tick += (o, e) => { t.Stop(); t.Dispose(); Safe(Exit); };
                    t.Start();
                }
            };
            UpdateTray(true);
            tray.Show();
            TrayPromote.Start();

            engine.Start();
            if (S.FirstRun)
            {
                S.Save();
                tray.Balloon("Echoa is running", "Click the ring for your devices and apps. Scroll over it to change the volume.");
            }
            if (S.SaveBlocked) tray.Balloon("Echoa couldn't read its settings", "Your settings file wasn't changed. Restart Echoa to load it again.");
        }

        static void Safe(Action a) { try { a(); } catch (Exception ex) { Log.Crash(ex); } }

        void SaveSoon() { saveTimer.Stop(); saveTimer.Start(); }

        DevSnap Default => Snap.Devices.FirstOrDefault(d => d.Id == Snap.DefaultId);
        DevSnap FindDev(string id) => Snap.Devices.FirstOrDefault(d => d.Id == id) ?? Snap.Inputs.FirstOrDefault(d => d.Id == id);

        static string Label(DevSnap d) => string.IsNullOrEmpty(d.Sub) ? d.WinName : d.WinName + " (" + d.Sub + ")";

        public float CapOf(string id) => S.CapsOn && S.Devices.TryGetValue(id, out var p) && p.Cap < 0.995f ? p.Cap : 1f;

        // copy every setting the engine acts on (call after any change)
        void PushSettings()
        {
            engine.AppMemoryOn = S.AppMemory;
            engine.DeviceMemoryOn = S.VolumeMemory;
            engine.BlockSteal = S.BlockSteal;
            engine.RulesOn = S.RulesOn;
            engine.CapsOn = S.CapsOn;
            engine.RampOn = S.RampOn;
            engine.AppMemory.Clear();
            foreach (var kv in S.AppVolumes) engine.AppMemory[kv.Key] = kv.Value;
            foreach (var kv in S.Devices) PushDevice(kv.Key, kv.Value);
        }

        void PushDevice(string id, DevPrefs p)
        {
            if (p.Vol >= 0) engine.DeviceMemory[id] = p.Vol; else engine.DeviceMemory.TryRemove(id, out _);
            if (p.NoSteal) engine.NoSteal[id] = 1; else engine.NoSteal.TryRemove(id, out _);
            if (p.Cap < 0.995f) engine.Caps[id] = p.Cap; else engine.Caps.TryRemove(id, out _);
            if (p.AutoDefault) engine.AutoDefault[id] = 1; else engine.AutoDefault.TryRemove(id, out _);
            if (p.Fallback.Length > 0) engine.Fallback[id] = p.Fallback; else engine.Fallback.TryRemove(id, out _);
        }

        // your names replace Windows' ones everywhere in Echoa
        void ApplyNames(Snapshot s)
        {
            foreach (var d in s.Devices.Concat(s.Inputs))
            {
                if (d.WinName == null) d.WinName = d.Name;
                d.Name = S.Devices.TryGetValue(d.Id, out var p) && p.Rename.Length > 0 ? p.Rename : d.WinName;
            }
            s.Devices.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            s.Inputs.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        // ------------------------------------------------------------ engine -> UI
        public void OnSnapshot(Snapshot s)
        {
            ApplyNames(s);
            // Windows couldn't report a volume this time: keep showing the last real one
            foreach (var d in s.Devices.Concat(s.Inputs))
                if (!d.VolKnown && FindDev(d.Id) is DevSnap old) { d.Vol = old.Vol; d.Muted = old.Muted; }
            Snap = s;
            bool dirty = false;
            foreach (var d in s.Devices)
            {
                var p = S.Dev(d.Id);
                if (S.VolumeMemory && p.Vol < 0 && d.VolKnown) { p.Vol = d.Vol; engine.DeviceMemory[d.Id] = d.Vol; dirty = true; }
                string label = Label(d);
                if (p.Label != label) { p.Label = label; dirty = true; }
            }
            if (dirty && !firstSnapshot) SaveSoon();
            else if (dirty) S.Save();
            firstSnapshot = false;
            UpdateTray();
            panel.ModelChanged();
            main?.ModelChanged();
        }

        public void OnDeviceVolume(string id, float vol, bool muted, bool remember)
        {
            var d = FindDev(id);
            if (d != null) { d.Vol = vol; d.Muted = muted; }
            if (remember && S.VolumeMemory && d != null && !d.Input) { S.Dev(id).Vol = vol; engine.DeviceMemory[id] = vol; SaveSoon(); }
            if (id == Snap.DefaultId) UpdateTray();
            panel.ValuesChanged();
            main?.ValuesChanged();
        }

        public void OnAppVolume(string devId, string key, float vol, bool muted)
        {
            var a = FindDev(devId)?.Apps.FirstOrDefault(x => x.Key == key);
            if (a == null) return;
            a.Vol = vol;
            a.Muted = muted;
            panel.ValuesChanged();
        }

        // ------------------------------------------------------------ UI -> audio
        public void SetDefault(string id)
        {
            engine.SetDefault(id);
            Snap.DefaultId = id;   // show it right away; the engine confirms a moment later
            UpdateTray();
            main?.ModelChanged();
        }

        public void SetDefaultInput(string id)
        {
            engine.SetDefault(id);
            Snap.DefaultInputId = id;
            main?.ValuesChanged();
            panel.ValuesChanged();
        }

        // ------------------------------------------------------------ mic area in the flyout footer
        public bool MicOn => S.MicFooter;
        public DevSnap Mic => Snap.Inputs.FirstOrDefault(d => d.Id == Snap.DefaultInputId);
        public bool MicTesting => micMeter != null;
        public bool MicTestFailed => micMeter != null && micMeter.Failed;
        public float MicLevel => micMeter?.Level ?? 0f;

        public void MicMute()
        {
            var m = Mic;
            if (m != null) DeviceMute(m.Id, !m.Muted);
        }

        // the test opens the mic only while it runs: until Stop, the flyout closes, or 30 s pass
        public void MicTest(bool on)
        {
            micMeter?.Dispose();
            micMeter = null;
            var m = Mic;
            if (on && m != null)
            {
                micMeter = new MicMeter(m.Id);
                micTestUntil = DateTime.UtcNow.AddSeconds(30);
                micTimer.Start();
            }
            else micTimer.Stop();
            panel.ValuesChanged();
        }

        void MicTimerTick()
        {
            if (micMeter == null) { micTimer.Stop(); return; }
            var m = Mic;
            if (m == null || DateTime.UtcNow > micTestUntil || !panel.Visible) { MicTest(false); return; }
            if (m.Id != micMeter.DeviceId) MicTest(true);   // the default mic changed: test the new one
            panel.MicTick();
        }

        public void DeviceVolume(string id, float v, bool final)
        {
            var d = FindDev(id);
            if (d == null) return;
            if (!d.Input) v = Math.Min(v, CapOf(id));
            if (d.Muted && v > 0) { d.Muted = false; engine.SetDeviceMute(id, false); }
            d.Vol = v;
            engine.SetDeviceVolume(id, v);
            if (!d.Input && S.VolumeMemory) { S.Dev(id).Vol = v; engine.DeviceMemory[id] = v; if (final) SaveSoon(); }
            if (id == Snap.DefaultId) UpdateTray();
            if (d.Input) main?.ValuesChanged(); else panel.ValuesChanged();
        }

        public void DeviceMute(string id, bool m)
        {
            var d = FindDev(id);
            if (d == null) return;
            d.Muted = m;
            engine.SetDeviceMute(id, m);
            if (id == Snap.DefaultId) UpdateTray();
            panel.ValuesChanged();
            main?.ValuesChanged();
        }

        public void AppVolume(string devId, string key, float v, bool final)
        {
            var a = FindDev(devId)?.Apps.FirstOrDefault(x => x.Key == key);
            if (a == null) return;
            if (a.Muted && v > 0) { a.Muted = false; engine.SetAppMute(devId, key, false); }
            a.Vol = v;
            engine.SetAppVolume(devId, key, v);
            if (final && S.AppMemory && !a.System)
            {
                S.AppVolumes[a.Exe] = v;
                engine.AppMemory[a.Exe] = v;
                SaveSoon();
                if (main != null && final) main.ModelChanged();
            }
        }

        public void AppMute(string devId, string key, bool m)
        {
            var a = FindDev(devId)?.Apps.FirstOrDefault(x => x.Key == key);
            if (a == null) return;
            a.Muted = m;
            engine.SetAppMute(devId, key, m);
            panel.ValuesChanged();
        }

        public void PlayTest(string id) => engine.PlayTest(id);

        public void MediaCommand(int c) => media.Command(c);

        public void ForgetAppVolume(string exe)
        {
            S.AppVolumes.Remove(exe);
            engine.AppMemory.TryRemove(exe, out _);
            SaveSoon();
        }

        public void UnhideApp(string exe)
        {
            S.HiddenApps.Remove(exe);
            SaveSoon();
            panel.ModelChanged();
        }

        // a device's own settings changed in the main window
        public void DevicePrefsChanged(string id)
        {
            var p = S.Dev(id);
            var d = Snap.Devices.FirstOrDefault(x => x.Id == id);
            if (d != null) p.Label = Label(d);
            PushDevice(id, p);
            if (S.CapsOn && p.Cap < 0.995f) engine.EnforceCap(id);
            SaveSoon();
            ApplyNames(Snap);
            UpdateTray(true);
            panel.ModelChanged();
            main?.ModelChanged();
        }

        // a toggle on the Settings page changed
        // the fold arrow in the flyout; the panel animates itself, so no relayout from here
        public void SetSimpleFlyout(bool on)
        {
            S.SimpleFlyout = on;
            SaveSoon();
            main?.SyncSettings();
        }

        public void SettingsChanged()
        {
            PushSettings();
            if (!S.MicFooter) MicTest(false);
            tray.WheelEnabled = S.ScrollTray;
            if (S.CapsOn) foreach (var d in Snap.Devices) engine.EnforceCap(d.Id);
            SaveSoon();
            PanelVisibility();
            panel.ModelChanged();
        }

        public Rectangle MainBounds
        {
            get => S.WinW == 0 ? Rectangle.Empty : new Rectangle(S.WinX, S.WinY, S.WinW, S.WinH);
            set { S.WinX = value.X; S.WinY = value.Y; S.WinW = value.Width; S.WinH = value.Height; SaveSoon(); }
        }

        public void SetAccent(Color c)
        {
            Theme.SetAccent(c);
            S.Accent = c.ToArgb() == Theme.DefaultAccent.ToArgb() ? "" : Theme.Hex(c);
            SaveSoon();
            UpdateTray(true);
            panel.Invalidate();
        }

        // ------------------------------------------------------------ tray
        void UpdateTray(bool force = false)
        {
            var d = Default;
            int pct = d == null ? 0 : (int)Math.Round(d.Vol * 100);
            bool muted = d != null && d.Muted, empty = d == null;
            string tip = d == null ? "Echoa · no output device" : d.Name + " · " + (muted ? "muted" : pct + "%");
            if (!force && pct == trayPct && muted == trayMuted && empty == trayEmpty && tip == trayTip) return;
            trayPct = pct; trayMuted = muted; trayEmpty = empty; trayTip = tip;
            tray.Update(TrayArt.Render(pct / 100f, muted || empty, SystemInformation.SmallIconSize.Width), tip);
        }

        void TrayWheel(int notches)
        {
            if (!S.ScrollTray) return;
            var d = Default;
            if (d == null) return;
            float v = Math.Max(0, Math.Min(1, (float)Math.Round(d.Vol * 100 + notches * S.ScrollStep) / 100f));
            DeviceVolume(d.Id, v, true);
            panel.ValuesChanged();
        }

        void ToggleFlyout(bool keyboard)
        {
            if (panel.Visible) { panel.HidePanel(); return; }
            // a click on the tray icon first takes focus from the open flyout (closing it): don't reopen it
            if (!keyboard && (DateTime.UtcNow - panel.LastHidden).TotalMilliseconds < 300) return;
            var r = tray.IconRect;
            var anchor = r.IsEmpty ? Cursor.Position : new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            panel.ShowAt(anchor, keyboard);
        }

        void PanelVisibility()
        {
            if (!panel.Visible && micMeter != null) MicTest(false);
            bool on = panel.Visible && S.NowPlaying;
            if (on && !mediaTimer.Enabled) { media.Refresh(); mediaTimer.Start(); }
            else if (!on && mediaTimer.Enabled) mediaTimer.Stop();
            if (!S.NowPlaying) Media = null;
        }

        public bool ShowNowPlaying => S.NowPlaying && Media != null;

        // ------------------------------------------------------------ main window
        public void OpenMain(string page, string devId)
        {
            if (panel.Visible) panel.HidePanel();
            if (main == null || main.IsDisposed)
            {
                main = new MainWindow(this, page, devId);
                main.FormClosed += (o, e) => main = null;
                main.Show();
            }
            else
            {
                main.ShowPageFor(page, devId);
                if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
            }
            main.Activate();
            Native.SetForegroundWindow(main.Handle);
        }

        // ------------------------------------------------------------ menus
        void ReleasePanel()
        {
            if (!panel.IsHandleCreated) { panel.SuppressHide = false; return; }
            panel.BeginInvoke((Action)(() =>
            {
                panel.SuppressHide = false;
                if (panel.Visible && Form.ActiveForm != panel) panel.Activate();
            }));
        }

        ToolStripMenuItem Mi(string text, Action click, bool check = false, bool isChecked = false, bool enabled = true)
        {
            var it = new ToolStripMenuItem(text) { ForeColor = Theme.Text, Checked = check && isChecked, Enabled = enabled };
            if (click != null) it.Click += (o, e) => Safe(click);
            return it;
        }

        void ShowMenu(ContextMenuStrip m, Point screen)
        {
            if (panel.Visible) panel.SuppressHide = true;
            else Native.SetForegroundWindow(tray.Handle);
            m.Show(screen, panel.Visible ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.AboveLeft);
        }

        void ShowTrayMenu(Point screen)
        {
            menu.Items.Clear();
            var open = Mi("Open Echoa", () => OpenMain(null, null));
            open.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Mi("Exit Echoa", Exit));
            ShowMenu(menu, screen);
        }

        ContextMenuStrip TempMenu()
        {
            var m = Menus.Make(menu.Font);
            m.Closed += (o, e) => { ReleasePanel(); ui.Post(_ => { try { m.Dispose(); } catch { } }, null); };
            return m;
        }

        public void ShowDeviceMenu(DevSnap d, Point screen)
        {
            var p = S.Dev(d.Id);
            bool isDefault = d.Id == Snap.DefaultId;
            var m = TempMenu();
            m.Items.Add(Mi(isDefault ? "This is your default output" : "Make default", () => { SetDefault(d.Id); panel.ModelChanged(); }, enabled: !isDefault));
            int apps = d.Apps.Count(a => !S.HiddenApps.ContainsKey(a.Exe));
            if (!isDefault && apps > 0) m.Items.Add(Mi(panel.IsPeeked(d.Id) ? "Hide its apps" : "Show its apps", () => panel.TogglePeek(d.Id)));
            m.Items.Add(Mi("Play test sound", () => engine.PlayTest(d.Id)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Mi("Don't let it take over when it connects", () => { p.NoSteal = !p.NoSteal; DevicePrefsChanged(d.Id); }, true, p.NoSteal, S.BlockSteal));
            m.Items.Add(Mi(p.Hidden ? "Show device" : "Hide device", () => { p.Hidden = !p.Hidden; DevicePrefsChanged(d.Id); }));
            m.Items.Add(Mi("Device settings…", () => OpenMain("Devices", d.Id)));
            ShowMenu(m, screen);
        }

        public void ShowAppMenu(DevSnap d, AppSnap a, Point screen)
        {
            var m = TempMenu();
            m.Items.Add(Mi(a.Muted ? "Unmute" : "Mute", () => AppMute(d.Id, a.Key, !a.Muted)));
            if (!a.System && S.AppVolumes.TryGetValue(a.Exe, out float rem))
                m.Items.Add(Mi("Forget remembered volume (" + (int)Math.Round(rem * 100) + ")", () => { ForgetAppVolume(a.Exe); main?.ModelChanged(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Mi("Hide " + a.Name, () => { S.HiddenApps[a.Exe] = a.Name; SaveSoon(); panel.ModelChanged(); main?.ModelChanged(); }));
            ShowMenu(m, screen);
        }

        // every step runs even if an earlier one fails, so Exit, --exit and Minibar's Stop always finish
        bool exiting;
        void Exit()
        {
            if (exiting) return;
            exiting = true;
            Safe(panel.HidePanel);
            Safe(() => main?.Close());
            Safe(saveTimer.Stop);
            Safe(mediaTimer.Stop);
            Safe(() => MicTest(false));
            Safe(S.Save);
            Safe(media.Dispose);
            Safe(engine.Dispose);
            Safe(tray.Dispose);
            ExitThread();
        }
    }
}
