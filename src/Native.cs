using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Echoa
{
    static class Native
    {
        public static readonly int WM_SHOWSIP = RegisterWindowMessage("Echoa.Show.v1");
        public static readonly int WM_EXITECHOA = RegisterWindowMessage("Echoa.Exit.v1");   // "Echoa.exe --exit" (Minibar's Stop / updates)
        public static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);
        public const int SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOCOPYBITS = 0x100, SWP_NOOWNERZORDER = 0x200;
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        // ------------------------------------------------------------ Windows Audio service (read-only status)
        [StructLayout(LayoutKind.Sequential)] struct SERVICE_STATUS_PROCESS { public int type, state, accepted, exitCode, specificExit, checkPoint, waitHint, processId, flags; }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenSCManager(string machine, string database, int access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenService(IntPtr scm, string name, int access);
        [DllImport("advapi32.dll")] static extern bool QueryServiceStatusEx(IntPtr service, int level, out SERVICE_STATUS_PROCESS status, int size, out int needed);
        [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr h);

        // the service's process id; 0 = not running; -1 = couldn't ask
        public static int ServicePid(string name)
        {
            IntPtr scm = OpenSCManager(null, null, 1 /* SC_MANAGER_CONNECT */);
            if (scm == IntPtr.Zero) return -1;
            IntPtr svc = IntPtr.Zero;
            try
            {
                svc = OpenService(scm, name, 4 /* SERVICE_QUERY_STATUS */);
                if (svc == IntPtr.Zero) return -1;
                if (!QueryServiceStatusEx(svc, 0, out var st, Marshal.SizeOf(typeof(SERVICE_STATUS_PROCESS)), out _)) return -1;
                return st.state == 4 /* SERVICE_RUNNING */ ? st.processId : 0;
            }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                CloseServiceHandle(scm);
            }
        }

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public Rectangle ToRect() => Rectangle.FromLTRB(Left, Top, Right, Bottom); }

        // ------------------------------------------------------------ tray
        public const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
        public const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public int uID; public Guid guidItem; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA d);
        [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);

        // ------------------------------------------------------------ raw mouse input (only while the cursor is on the tray icon)
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public int Flags; public IntPtr Target; }
        [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, int count, int size);
        [DllImport("user32.dll")] public static extern int GetRawInputData(IntPtr hRaw, int cmd, IntPtr data, ref int size, int headerSize);
        public const int WM_INPUT = 0x00FF, RID_INPUT = 0x10000003, RIDEV_INPUTSINK = 0x100, RIDEV_REMOVE = 0x1, RIM_TYPEMOUSE = 0, RI_MOUSE_WHEEL = 0x0400;

        // ------------------------------------------------------------ processes
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out int code);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);
        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static string ProcessPath(int pid)
        {
            if (pid <= 0) return null;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(h); }
        }

        public static bool ProcessAlive(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return Marshal.GetLastWin32Error() == 5;   // access denied = it exists
            try { return !GetExitCodeProcess(h, out int code) || code == 259; }  // STILL_ACTIVE
            finally { CloseHandle(h); }
        }

        // ------------------------------------------------------------ icons
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, int count, int flags);
    }
}
