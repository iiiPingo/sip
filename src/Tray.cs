using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Echoa
{
    // Our own notification-area icon (Shell_NotifyIcon, version 4), so we can get the icon's rectangle
    // for scroll-to-change-volume, keyboard selection (Win+B, Enter) and re-adding after Explorer restarts.
    class TrayIcon : NativeWindow, IDisposable
    {
        const int WM_TRAY = 0x8000 + 0x51;   // WM_APP + n
        const int Id = 1;
        const int WM_MOUSEMOVE = 0x200, WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401;

        public event Action<bool> Activated;      // true = from keyboard
        public event Action<Point> MenuRequested;
        public event Action<int> Wheel;           // wheel notches (+ up), fractional deltas accumulated
        public Action<Message> OtherMessage;      // single-instance "show" etc.

        Icon icon;
        string tip = "Echoa";
        bool added, rawOn;
        Rectangle iconRect;
        int wheelAcc;
        DateTime lastSelect;

        public TrayIcon()
        {
            // a hidden top-level window: receives the TaskbarCreated broadcast and raw input
            CreateHandle(new CreateParams { Caption = "EchoaTrayWindow", ExStyle = 0x80 /* WS_EX_TOOLWINDOW */ });
        }

        Native.NOTIFYICONDATA Data(int flags) => new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(Native.NOTIFYICONDATA)),
            hWnd = Handle,
            uID = Id,
            uFlags = flags,
            uCallbackMessage = WM_TRAY,
            hIcon = icon?.Handle ?? IntPtr.Zero,
            szTip = tip,
            szInfo = "",
            szInfoTitle = "",
        };

        public void Show()
        {
            var d = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref d);
            added = Native.Shell_NotifyIcon(Native.NIM_ADD, ref d);
            d.uVersion = 4;
            Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref d);
        }

        public void Update(Icon newIcon, string newTip)
        {
            var old = icon;
            icon = newIcon;
            tip = newTip.Length > 127 ? newTip.Substring(0, 127) : newTip;
            if (added)
            {
                var d = Data(Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
                if (!Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref d)) Show();
            }
            if (old != null && old != icon) old.Dispose();
        }

        public void Balloon(string title, string text)
        {
            if (!added) return;
            var d = Data(0x10 /* NIF_INFO */);
            d.szInfoTitle = title;
            d.szInfo = text;
            d.dwInfoFlags = 0;   // NIIF_NONE
            Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref d);
        }

        Rectangle GetIconRect()
        {
            var id = new Native.NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf(typeof(Native.NOTIFYICONIDENTIFIER)), hWnd = Handle, uID = Id };
            return Native.Shell_NotifyIconGetRect(ref id, out var r) == 0 ? r.ToRect() : Rectangle.Empty;
        }

        public Rectangle IconRect => GetIconRect();

        // raw mouse input only while the pointer is over the icon: that's how the wheel reaches us
        void RawOn()
        {
            iconRect = GetIconRect();
            if (rawOn || iconRect.IsEmpty) return;
            var dev = new[] { new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = Native.RIDEV_INPUTSINK, Target = Handle } };
            rawOn = Native.RegisterRawInputDevices(dev, 1, Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
            wheelAcc = 0;
        }

        void RawOff()
        {
            if (!rawOn) return;
            var dev = new[] { new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = Native.RIDEV_REMOVE, Target = IntPtr.Zero } };
            Native.RegisterRawInputDevices(dev, 1, Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
            rawOn = false;
        }

        public bool WheelEnabled = true;

        void OnRawInput(IntPtr hRaw)
        {
            int hdr = 8 + 2 * IntPtr.Size, size = 0;
            Native.GetRawInputData(hRaw, Native.RID_INPUT, IntPtr.Zero, ref size, hdr);
            if (size <= 0 || size > 512) return;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (Native.GetRawInputData(hRaw, Native.RID_INPUT, buf, ref size, hdr) != size) return;
                if (Marshal.ReadInt32(buf, 0) != Native.RIM_TYPEMOUSE) return;
                Native.GetCursorPos(out var p);
                if (!iconRect.Contains(p.X, p.Y)) { RawOff(); return; }
                ushort flags = (ushort)Marshal.ReadInt16(buf, hdr + 4);
                if ((flags & Native.RI_MOUSE_WHEEL) == 0) return;
                short delta = Marshal.ReadInt16(buf, hdr + 6);
                wheelAcc += delta;
                int notches = wheelAcc / 120;
                if (notches != 0)
                {
                    wheelAcc -= notches * 120;
                    Wheel?.Invoke(notches);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == WM_TRAY)
                {
                    int ev = (int)((long)m.LParam & 0xFFFF);
                    switch (ev)
                    {
                        case WM_MOUSEMOVE:
                            if (WheelEnabled) RawOn();
                            break;
                        case NIN_SELECT:
                        case NIN_KEYSELECT:
                            // Enter can report twice; one activation per press
                            if ((DateTime.UtcNow - lastSelect).TotalMilliseconds > 200) Activated?.Invoke(ev == NIN_KEYSELECT);
                            lastSelect = DateTime.UtcNow;
                            break;
                        case WM_CONTEXTMENU:
                            long w = (long)m.WParam;
                            MenuRequested?.Invoke(new Point((short)(w & 0xFFFF), (short)((w >> 16) & 0xFFFF)));
                            break;
                    }
                }
                else if (m.Msg == Native.WM_INPUT) OnRawInput(m.LParam);
                else if (m.Msg == Native.WM_TASKBARCREATED) Show();   // Explorer restarted
                else OtherMessage?.Invoke(m);
            }
            catch (Exception ex) { Log.Crash(ex); }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            RawOff();
            if (added)
            {
                var d = Data(0);
                Native.Shell_NotifyIcon(Native.NIM_DELETE, ref d);
                added = false;
            }
            icon?.Dispose();
            DestroyHandle();
        }
    }
}

namespace Echoa
{
    // Windows 11 drops every new tray icon into the ^ overflow. It keeps one registry entry per icon
    // (HKCU\Control Panel\NotifyIconSettings\<id>: ExecutablePath, IsPromoted). Undocumented but stable since 22H2.
    // Echoa turns IsPromoted on for itself only while that value doesn't exist yet: once you've moved
    // the icon yourself, Windows has written your choice and Echoa never touches it again.
    static class TrayPromote
    {
        static System.Windows.Forms.Timer timer;
        static int attempt;
        static readonly int[] DelaysMs = { 2000, 8000, 30000, 120000 };   // Explorer creates the entry a little after the icon appears

        public static void Start()
        {
            attempt = 0;
            timer = new System.Windows.Forms.Timer { Interval = DelaysMs[0] };
            timer.Tick += (o, e) =>
            {
                bool done;
                try { done = TryPromote(); } catch (Exception ex) { Log.Crash(ex); done = true; }
                if (done || ++attempt >= DelaysMs.Length) { timer.Stop(); timer.Dispose(); timer = null; return; }
                timer.Interval = DelaysMs[attempt];
            };
            timer.Start();
        }

        // true = nothing more to do (found it, or this isn't Windows 11)
        static bool TryPromote()
        {
            using (var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
            {
                if (root == null) return true;
                string exe = Application.ExecutablePath;
                foreach (var name in root.GetSubKeyNames())
                {
                    using (var k = root.OpenSubKey(name, true))
                    {
                        if (!(k?.GetValue("ExecutablePath") is string path) || !SameExe(path, exe)) continue;
                        if (k.GetValue("IsPromoted") == null) k.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        return true;
                    }
                }
            }
            return false;
        }

        // paths under known folders are stored as "{FOLDERID}\rest", so compare the tail when needed
        static bool SameExe(string stored, string exe)
        {
            if (string.Equals(stored, exe, StringComparison.OrdinalIgnoreCase)) return true;
            if (!stored.StartsWith("{")) return false;
            int slash = stored.IndexOf('\\');
            return slash > 0 && exe.EndsWith(stored.Substring(slash), StringComparison.OrdinalIgnoreCase);
        }
    }
}
