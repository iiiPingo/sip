using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sip
{
    class NowPlaying
    {
        public string Title = "", Artist = "", App = "";
        public bool Playing;
        public System.Drawing.Bitmap Art;   // album / video cover, square, null if none
    }

    // "Now playing" through Windows' media sessions (GSMTC), called as raw WinRT vtables because
    // .NET Framework has no projection for it. Interface IDs and slots checked against windows-rs 0.58.
    // Only does anything while the flyout is open (the UI asks for a refresh about once a second).
    class Media : IDisposable
    {
        static readonly Guid IID_ManagerStatics = new Guid("2050c4ee-11a0-57de-aed7-c97c70338245");
        static readonly Guid IID_Manager = new Guid("cace8eac-e86e-504a-ab31-5ff8ff1bce49");
        static readonly Guid IID_Session = new Guid("7148c835-9b14-5ae2-ab85-dc9b1c14e1a8");
        static readonly Guid IID_Props = new Guid("68856cf6-adb4-54b2-ac16-05837907acb6");
        static readonly Guid IID_Playback = new Guid("94b4b6cf-e8ba-51ad-87a7-c10ade106127");
        static readonly Guid IID_AsyncInfo = new Guid("00000036-0000-0000-c000-000000000046");

        // vtable slots (IInspectable takes 0-5)
        const int Statics_RequestAsync = 6, Manager_GetCurrentSession = 6, Manager_GetSessions = 7;
        const int Vector_GetAt = 6, Vector_Size = 7;   // IVectorView<T>
        const int Session_AppId = 6, Session_TryGetMediaPropertiesAsync = 7, Session_GetPlaybackInfo = 9,
            Session_SkipNext = 16, Session_SkipPrevious = 17, Session_TogglePlayPause = 20;
        const int Props_Title = 6, Props_Artist = 9, Props_Thumbnail = 15;
        const int StreamRef_OpenReadAsync = 6;
        static readonly Guid IID_StreamRef = new Guid("33ee3134-1dd6-4e3a-8067-d1c162e8642b");
        static Guid IID_IStream = new Guid("0000000c-0000-0000-c000-000000000046");
        const int Playback_Status = 7;
        const int AsyncInfo_Status = 7, AsyncOp_GetResults = 8;

        [DllImport("combase.dll")] static extern int RoInitialize(int type);
        [DllImport("shcore.dll")] static extern int CreateStreamOverRandomAccessStream(IntPtr stream, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.IStream result);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int len, out IntPtr h);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);
        [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr h, out int len);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnPtr(IntPtr self, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnInt(IntPtr self, out int result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnGetAt(IntPtr self, int index, out IntPtr result);

        readonly SynchronizationContext ui;
        readonly Action<NowPlaying> done;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread thread;
        IntPtr manager;
        volatile int pendingCmd = -1;
        volatile bool disposed, refresh;
        string chosenApp;   // the session the strip shows (and the buttons control)

        readonly int artPx;
        string artKey;
        System.Drawing.Bitmap art;
        int artTries;
        DateTime artAt;

        public Media(SynchronizationContext ui, Action<NowPlaying> done, int artPx)
        {
            this.artPx = artPx;
            this.ui = ui;
            this.done = done;
            thread = new Thread(Run) { IsBackground = true, Name = "SipMedia", Priority = ThreadPriority.BelowNormal };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Refresh() { refresh = true; wake.Set(); }

        // 0 = play/pause, 1 = next, 2 = previous
        public void Command(int c) { pendingCmd = c; wake.Set(); }

        void Run()
        {
            try { RoInitialize(1); } catch { }   // multithreaded; harmless if COM is already set up on this thread
            while (!disposed)
            {
                wake.WaitOne();
                if (disposed) continue;
                try
                {
                    if (manager == IntPtr.Zero) manager = GetManager();
                    if (manager == IntPtr.Zero) { Post(null); continue; }
                    int cmd = pendingCmd; pendingCmd = -1;
                    if (cmd >= 0)
                    {
                        RunCommand(cmd);
                        Thread.Sleep(250);   // let the player update before reading it back
                        refresh = true;
                    }
                    if (refresh) { refresh = false; Post(Read()); }
                }
                catch (Exception ex) { Log.Crash(ex); }
            }
        }

        void Post(NowPlaying np) => ui.Post(_ => { try { done(np); } catch (Exception ex) { Log.Crash(ex); } }, null);

        static IntPtr Slot(IntPtr obj, int i) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), i * IntPtr.Size);
        static int CallPtr(IntPtr obj, int slot, out IntPtr r) => ((FnPtr)Marshal.GetDelegateForFunctionPointer(Slot(obj, slot), typeof(FnPtr)))(obj, out r);
        static int CallInt(IntPtr obj, int slot, out int r) => ((FnInt)Marshal.GetDelegateForFunctionPointer(Slot(obj, slot), typeof(FnInt)))(obj, out r);

        static IntPtr QI(IntPtr p, Guid iid)
        {
            if (p == IntPtr.Zero) return IntPtr.Zero;
            return Marshal.QueryInterface(p, ref iid, out IntPtr r) == 0 ? r : IntPtr.Zero;
        }

        static void Rel(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }

        static string HString(IntPtr h)
        {
            if (h == IntPtr.Zero) return "";
            try { IntPtr raw = WindowsGetStringRawBuffer(h, out int len); return raw == IntPtr.Zero ? "" : Marshal.PtrToStringUni(raw, len); }
            finally { WindowsDeleteString(h); }
        }

        // wait (briefly) for an IAsyncOperation<T> and return its result
        static IntPtr Await(IntPtr op, int timeoutMs = 2000, bool results = true)
        {
            if (op == IntPtr.Zero) return IntPtr.Zero;
            IntPtr info = QI(op, IID_AsyncInfo);
            try
            {
                if (info == IntPtr.Zero) return IntPtr.Zero;
                var start = Environment.TickCount;
                while (true)
                {
                    if (CallInt(info, AsyncInfo_Status, out int st) != 0) return IntPtr.Zero;
                    if (st == 1) break;                 // Completed
                    if (st != 0) return IntPtr.Zero;    // Canceled / Error
                    if (Environment.TickCount - start > timeoutMs) return IntPtr.Zero;
                    Thread.Sleep(10);
                }
                if (!results) return IntPtr.Zero;
                return CallPtr(op, AsyncOp_GetResults, out IntPtr r) == 0 ? r : IntPtr.Zero;
            }
            finally { Rel(info); Rel(op); }
        }

        static IntPtr GetManager()
        {
            const string cls = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
            if (WindowsCreateString(cls, cls.Length, out IntPtr hs) != 0) return IntPtr.Zero;
            IntPtr statics = IntPtr.Zero;
            try
            {
                var iid = IID_ManagerStatics;
                if (RoGetActivationFactory(hs, ref iid, out statics) != 0 || statics == IntPtr.Zero) return IntPtr.Zero;
                if (CallPtr(statics, Statics_RequestAsync, out IntPtr op) != 0) return IntPtr.Zero;
                IntPtr m = Await(op, 5000);
                if (m == IntPtr.Zero) return IntPtr.Zero;
                IntPtr mgr = QI(m, IID_Manager);
                Rel(m);
                return mgr;
            }
            catch (DllNotFoundException) { return IntPtr.Zero; }
            catch (EntryPointNotFoundException) { return IntPtr.Zero; }
            finally { Rel(statics); WindowsDeleteString(hs); }
        }

        static string AppIdOf(IntPtr session) => CallPtr(session, Session_AppId, out IntPtr h) == 0 ? HString(h) : "";

        static int StatusOf(IntPtr session)
        {
            if (CallPtr(session, Session_GetPlaybackInfo, out IntPtr raw) != 0 || raw == IntPtr.Zero) return -1;
            IntPtr pb = QI(raw, IID_Playback);
            Rel(raw);
            if (pb == IntPtr.Zero) return -1;
            try { return CallInt(pb, Playback_Status, out int st) == 0 ? st : -1; }
            finally { Rel(pb); }
        }

        IntPtr CurrentSession()
        {
            if (CallPtr(manager, Manager_GetCurrentSession, out IntPtr s) != 0 || s == IntPtr.Zero) return IntPtr.Zero;
            IntPtr session = QI(s, IID_Session);
            Rel(s);
            return session;
        }

        // every media session Windows knows about (Spotify, browser tabs, ...)
        List<IntPtr> AllSessions()
        {
            var list = new List<IntPtr>();
            if (CallPtr(manager, Manager_GetSessions, out IntPtr vec) != 0 || vec == IntPtr.Zero) return list;
            try
            {
                if (CallInt(vec, Vector_Size, out int n) != 0) return list;
                var getAt = (FnGetAt)Marshal.GetDelegateForFunctionPointer(Slot(vec, Vector_GetAt), typeof(FnGetAt));
                for (int i = 0; i < n && i < 32; i++)
                {
                    if (getAt(vec, i, out IntPtr raw) != 0 || raw == IntPtr.Zero) continue;
                    IntPtr ses = QI(raw, IID_Session);
                    Rel(raw);
                    if (ses != IntPtr.Zero) list.Add(ses);
                }
            }
            finally { Rel(vec); }
            return list;
        }

        // Windows' "current" session is just the last app that grabbed the media keys (often a paused
        // browser tab), so pick what's actually playing: the current one if it plays, else any playing one,
        // else the one we showed before, else the current one.
        IntPtr Choose()
        {
            var all = AllSessions();
            IntPtr cur = CurrentSession();
            string curId = cur != IntPtr.Zero ? AppIdOf(cur) : null;
            Rel(cur);
            IntPtr pick = IntPtr.Zero;
            try
            {
                var ids = new List<string>(); var st = new List<int>();
                foreach (var x in all) { ids.Add(AppIdOf(x)); st.Add(StatusOf(x)); }
                int best = -1;
                for (int i = 0; i < all.Count && best < 0; i++) if (st[i] == 4 && ids[i] == curId) best = i;
                for (int i = 0; i < all.Count && best < 0; i++) if (st[i] == 4 && ids[i] == chosenApp) best = i;
                for (int i = 0; i < all.Count && best < 0; i++) if (st[i] == 4) best = i;
                for (int i = 0; i < all.Count && best < 0; i++) if (ids[i] == chosenApp && st[i] > 0) best = i;
                for (int i = 0; i < all.Count && best < 0; i++) if (ids[i] == curId && st[i] > 0) best = i;
                for (int i = 0; i < all.Count && best < 0; i++) if (st[i] > 0) best = i;
                if (best >= 0) { pick = all[best]; chosenApp = ids[best]; all[best] = IntPtr.Zero; }
            }
            finally { foreach (var x in all) Rel(x); }
            return pick;
        }

        NowPlaying Read()
        {
            IntPtr session = Choose();
            if (session == IntPtr.Zero) return null;
            try
            {
                var np = new NowPlaying { App = FriendlyApp(chosenApp) };
                int st = StatusOf(session);
                if (st == 0) return null;
                np.Playing = st == 4;
                if (CallPtr(session, Session_TryGetMediaPropertiesAsync, out IntPtr op) == 0)
                {
                    IntPtr raw = Await(op);
                    IntPtr props = QI(raw, IID_Props);
                    Rel(raw);
                    if (props != IntPtr.Zero)
                    {
                        if (CallPtr(props, Props_Title, out IntPtr t) == 0) np.Title = HString(t).Trim();
                        if (CallPtr(props, Props_Artist, out IntPtr a) == 0) np.Artist = HString(a).Trim();
                        // cover art: fetched once per track (plus one retry, since players often send it a beat late)
                        string key = chosenApp + "|" + np.Title + "|" + np.Artist;
                        if (key != artKey) { artKey = key; artTries = 0; art = null; }
                        if (artTries < 2 && (artTries == 0 || (DateTime.UtcNow - artAt).TotalMilliseconds > 1500))
                        {
                            artTries++;
                            artAt = DateTime.UtcNow;
                            try { var b = ReadArt(props); if (b != null) art = b; } catch (Exception ex) { Log.Crash(ex); }
                        }
                        np.Art = art;
                        Rel(props);
                    }
                }
                return np.Title.Length == 0 && np.Artist.Length == 0 ? null : np;
            }
            finally { Rel(session); }
        }

        System.Drawing.Bitmap ReadArt(IntPtr props)
        {
            if (CallPtr(props, Props_Thumbnail, out IntPtr raw) != 0 || raw == IntPtr.Zero) return null;
            IntPtr sref = QI(raw, IID_StreamRef);
            Rel(raw);
            if (sref == IntPtr.Zero) return null;
            IntPtr stream;
            try
            {
                if (CallPtr(sref, StreamRef_OpenReadAsync, out IntPtr op) != 0) return null;
                stream = Await(op, 2000);
            }
            finally { Rel(sref); }
            if (stream == IntPtr.Zero) return null;
            byte[] data;
            try
            {
                if (CreateStreamOverRandomAccessStream(stream, ref IID_IStream, out var ist) != 0 || ist == null) return null;
                try
                {
                    var ms = new System.IO.MemoryStream();
                    var buf = new byte[65536];
                    IntPtr pRead = Marshal.AllocHGlobal(4);
                    try
                    {
                        while (ms.Length < 8 * 1024 * 1024)
                        {
                            Marshal.WriteInt32(pRead, 0);
                            ist.Read(buf, buf.Length, pRead);
                            int n = Marshal.ReadInt32(pRead);
                            if (n <= 0) break;
                            ms.Write(buf, 0, n);
                        }
                    }
                    finally { Marshal.FreeHGlobal(pRead); }
                    data = ms.ToArray();
                }
                finally { Marshal.ReleaseComObject(ist); }
            }
            finally { Rel(stream); }
            if (data.Length < 16) return null;
            using (var src = System.Drawing.Image.FromStream(new System.IO.MemoryStream(data)))
                return Square(src, artPx);
        }

        // centre-crop to a square (video thumbnails are 16:9) and scale to the size the flyout draws
        static System.Drawing.Bitmap Square(System.Drawing.Image src, int px)
        {
            int side = Math.Min(src.Width, src.Height);
            var from = new System.Drawing.Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
            var bmp = new System.Drawing.Bitmap(px, px);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new System.Drawing.Rectangle(0, 0, px, px), from, System.Drawing.GraphicsUnit.Pixel);
            }
            return bmp;
        }

        // "Spotify.exe" -> Spotify, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" -> Spotify, Firefox's hash -> Firefox
        static string FriendlyApp(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int bang = id.LastIndexOf('!');
            if (bang >= 0) id = id.Substring(bang + 1);
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id.Substring(0, id.Length - 4);
            switch (id.ToLowerInvariant())
            {
                case "308046b0af4a39cb": return "Firefox";
                case "msedge": return "Edge";
                case "chrome": return "Chrome";
                case "app": return "";
            }
            if (id.Length > 0 && char.IsLower(id[0])) id = char.ToUpperInvariant(id[0]) + id.Substring(1);
            return id.Length > 24 ? "" : id;
        }

        void RunCommand(int c)
        {
            // control the session the strip is showing, not whatever Windows calls current
            IntPtr session = IntPtr.Zero;
            foreach (var x in AllSessions())
            {
                if (session == IntPtr.Zero && AppIdOf(x) == chosenApp) session = x; else Rel(x);
            }
            if (session == IntPtr.Zero) session = CurrentSession();
            if (session == IntPtr.Zero) return;
            try
            {
                int slot = c == 1 ? Session_SkipNext : c == 2 ? Session_SkipPrevious : Session_TogglePlayPause;
                if (CallPtr(session, slot, out IntPtr op) == 0) Await(op, 1500, false);   // IAsyncOperation<bool>: only wait, the result is a bool
            }
            finally { Rel(session); }
        }

        public void Dispose()
        {
            disposed = true;
            wake.Set();
        }
    }
}
