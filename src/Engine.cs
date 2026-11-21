using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Sip.CoreAudio;

namespace Sip
{
    // Owns every Core Audio object on one MTA thread. Windows calls our callbacks on its own threads;
    // they only copy values and queue work here. Results go to the UI through the sink, posted.
    class Engine : IDisposable
    {
        public static Guid Ctx = new Guid("5a1c0de5-5195-4e1f-9a7e-51b0c0ffee01");   // tags changes Sip makes itself

        // settings the UI shares with us (thread-safe)
        public volatile bool AppMemoryOn = true, DeviceMemoryOn = true, BlockSteal = true;
        public readonly ConcurrentDictionary<string, float> AppMemory = new ConcurrentDictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentDictionary<string, float> DeviceMemory = new ConcurrentDictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentDictionary<string, byte> NoSteal = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        public volatile int IconPx = 20;
        // stage 2: volume limits, ramp-up, auto-switch rules
        public volatile bool CapsOn, RampOn, RulesOn = true;
        public readonly ConcurrentDictionary<string, float> Caps = new ConcurrentDictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentDictionary<string, byte> AutoDefault = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentDictionary<string, string> Fallback = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly int MyPid = Process.GetCurrentProcess().Id;

        readonly IAudioSink sink;
        readonly SynchronizationContext ui;
        readonly BlockingCollection<Action> q = new BlockingCollection<Action>();
        readonly Thread thread;
        readonly ConcurrentDictionary<string, DateTime> connectTimes = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        IMMDeviceEnumerator en;
        NotifyClient nc;
        IPolicyConfig policy;
        readonly Dictionary<string, Dev> devs = new Dictionary<string, Dev>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Dev> ins = new Dictionary<string, Dev>(StringComparer.OrdinalIgnoreCase);   // microphones
        readonly Dictionary<string, AppInfo> apps = new Dictionary<string, AppInfo>();
        string defaultId, expectDefault, defaultInId, prevDefaultId;
        DateTime expectUntil, prevChangedAt;
        bool disposed;

        // debounced refreshes
        readonly object dirtyGate = new object();
        readonly HashSet<string> dirtySessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool dirtyDevices;
        Timer dirtyTimer;

        public Engine(IAudioSink sink, SynchronizationContext ui)
        {
            this.sink = sink;
            this.ui = ui;
            thread = new Thread(Run) { IsBackground = true, Name = "SipAudio" };
            thread.SetApartmentState(ApartmentState.MTA);
        }

        public void Start() => thread.Start();

        void Run()
        {
            try { Init(); } catch (Exception ex) { Log.Crash(ex); }
            foreach (var a in q.GetConsumingEnumerable())
            {
                try { a(); }
                catch (Exception ex) { Log.Crash(ex); }
            }
            try { Shutdown(); } catch { }
        }

        void Post(Action a) { if (!disposed) try { q.Add(a); } catch { } }

        void ToUi(Action a) => ui.Post(_ => { try { a(); } catch (Exception ex) { Log.Crash(ex); } }, null);

        void Init()
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            SyncDevices(true);
            SyncInputs();
            defaultId = GetDefaultId();
            defaultInId = GetDefaultId(EDataFlow.Capture);
            nc = new NotifyClient(this);
            en.RegisterEndpointNotificationCallback(nc);
            Publish();
        }

        void Shutdown()
        {
            try { if (en != null && nc != null) en.UnregisterEndpointNotificationCallback(nc); } catch { }
            foreach (var d in devs.Values) d.Close();
            foreach (var d in ins.Values) d.Close();
            devs.Clear();
            ins.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { q.CompleteAdding(); } catch { }
            try { thread.Join(1500); } catch { }
        }

        // ------------------------------------------------------------ commands from the UI
        public void SetDeviceVolume(string id, float v) => Post(() =>
        {
            var d = Any(id);
            if (d?.Ep == null) return;
            d.RampGen++;              // you moved it yourself: stop any ramp-up
            d.RampUntil = DateTime.MinValue;
            d.Ep.SetMasterVolumeLevelScalar(Clamp(v), ref Ctx);
        });
        public void SetDeviceMute(string id, bool m) => Post(() => { Any(id)?.Ep?.SetMute(m, ref Ctx); });
        public void EnforceCap(string id) => Post(() => { if (devs.TryGetValue(id, out var d)) ApplyCap(d); });
        public void PlayTest(string id) => ThreadPool.QueueUserWorkItem(_ => { try { TestSound.Play(id); } catch (Exception ex) { Log.Crash(ex); } });

        Dev Any(string id) => id == null ? null : devs.TryGetValue(id, out var d) ? d : ins.TryGetValue(id, out d) ? d : null;

        float CapOf(string id) => CapsOn && Caps.TryGetValue(id, out float c) && c < 0.995f ? c : 1f;

        // over the limit (from anywhere): bring it down to the limit
        bool ApplyCap(Dev d)
        {
            float cap = CapOf(d.Id);
            if (cap >= 1f || d.Ep == null) return false;
            d.Ep.GetMasterVolumeLevelScalar(out float v);
            if (v <= cap + 0.005f) return false;
            d.Ep.SetMasterVolumeLevelScalar(cap, ref Ctx);
            d.Ep.GetMute(out bool m);
            ToUi(() => sink.OnDeviceVolume(d.Id, cap, m, false));
            return true;
        }
        public void SetAppVolume(string devId, string key, float v) => Post(() => ForApp(devId, key, s => s.V.SetMasterVolume(Clamp(v), ref Ctx)));
        public void SetAppMute(string devId, string key, bool m) => Post(() => ForApp(devId, key, s => s.V.SetMute(m, ref Ctx)));
        public void SetDefault(string id) => Post(() =>
        {
            try { MakeDefault(id); }
            catch (Exception ex) { Log.Crash(ex); Publish(); }   // put the UI back to what Windows really has
        });
        public void Refresh() => Post(() => { SyncDevices(false); foreach (var d in devs.Values) SyncSessions(d, false); Publish(); });

        static float Clamp(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        void ForApp(string devId, string key, Action<Sess> f)
        {
            if (!devs.TryGetValue(devId, out var d)) return;
            foreach (var s in d.Sessions.Values)
                if (s.Info.Key == key && s.V != null)
                    try { f(s); } catch (Exception ex) when (IsGone(ex)) { MarkSessions(devId); }
        }

        static bool IsGone(Exception ex) => ex is COMException || ex is InvalidComObjectException || ex is InvalidCastException;

        void MakeDefault(string id)
        {
            if (policy == null) policy = (IPolicyConfig)new PolicyConfigClientCo();
            expectDefault = id;
            expectUntil = DateTime.UtcNow.AddSeconds(4);
            for (int r = 0; r < 3; r++) policy.SetDefaultEndpoint(id, (ERole)r);
        }

        string GetDefaultId(EDataFlow flow = EDataFlow.Render)
        {
            try
            {
                if (en.GetDefaultAudioEndpoint(flow, ERole.Console, out var d) != 0 || d == null) return null;
                d.GetId(out var id);
                return id;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------ devices
        void SyncDevices(bool initial)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (en.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out var coll) == 0 && coll != null)
            {
                coll.GetCount(out int n);
                for (int i = 0; i < n; i++)
                {
                    if (coll.Item(i, out var md) != 0 || md == null) continue;
                    if (md.GetId(out var id) != 0 || id == null) continue;
                    seen.Add(id);
                    if (devs.ContainsKey(id)) continue;
                    var d = new Dev(this, id, md);
                    try { d.Open(); }
                    catch (Exception ex) { Log.Crash(ex); d.Close(); continue; }
                    devs[id] = d;
                    SyncSessions(d, true);
                    if (!initial) Connected(d);
                }
                try { Marshal.ReleaseComObject(coll); } catch { }
            }
            var now = DateTime.UtcNow;
            foreach (var id in devs.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                devs[id].Close();
                devs.Remove(id);
                // rule: when this device goes away while it's the output, switch to its chosen fallback
                bool wasDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(id, prevDefaultId, StringComparison.OrdinalIgnoreCase) && now - prevChangedAt < TimeSpan.FromSeconds(6));
                if (!initial && RulesOn && wasDefault && Fallback.TryGetValue(id, out var fb) && fb != null && seen.Contains(fb))
                    try { MakeDefault(fb); } catch (Exception ex) { Log.Crash(ex); }
            }
        }

        void SyncInputs()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (en.EnumAudioEndpoints(EDataFlow.Capture, DeviceState.Active, out var coll) == 0 && coll != null)
            {
                coll.GetCount(out int n);
                for (int i = 0; i < n; i++)
                {
                    if (coll.Item(i, out var md) != 0 || md == null) continue;
                    if (md.GetId(out var id) != 0 || id == null) continue;
                    seen.Add(id);
                    if (ins.ContainsKey(id)) continue;
                    var d = new Dev(this, id, md);
                    try { d.Open(false); }
                    catch (Exception ex) { Log.Crash(ex); d.Close(); continue; }
                    ins[id] = d;
                }
                try { Marshal.ReleaseComObject(coll); } catch { }
            }
            foreach (var id in ins.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                ins[id].Close();
                ins.Remove(id);
            }
        }

        // a device just showed up: give it back its remembered volume once Bluetooth has settled
        void Connected(Dev d)
        {
            d.ConnectedAt = DateTime.UtcNow;
            // rule: this device becomes the output as soon as it connects
            if (RulesOn && AutoDefault.ContainsKey(d.Id) && !string.Equals(d.Id, defaultId, StringComparison.OrdinalIgnoreCase))
                try { MakeDefault(d.Id); } catch (Exception ex) { Log.Crash(ex); }
            if (!DeviceMemoryOn || !DeviceMemory.TryGetValue(d.Id, out float v0)) { ApplyCap(d); return; }
            float v = Math.Min(v0, CapOf(d.Id));
            d.RestoreTimer = new Timer(_ => Post(() =>
            {
                if (!devs.TryGetValue(d.Id, out var cur) || cur != d || d.Ep == null) return;
                d.RestoreVal = v;
                d.RestoreUntil = DateTime.UtcNow.AddSeconds(4);
                d.Restores = 0;
                if (DateTime.UtcNow < d.RampUntil) { d.RampTarget = v; return; }   // the ramp-up will land on it
                d.Ep.SetMasterVolumeLevelScalar(v, ref Ctx);
                d.Ep.GetMute(out bool m);
                ToUi(() => sink.OnDeviceVolume(d.Id, v, m, false));
            }), null, 1200, Timeout.Infinite);
        }

        void OnDeviceVolume(string id, float v, bool m, Guid ctx)
        {
            if (ctx == Ctx) return;   // our own change: the UI already shows it
            if (ins.ContainsKey(id)) { ToUi(() => sink.OnDeviceVolume(id, v, m, false)); return; }
            if (!devs.TryGetValue(id, out var d)) return;
            var now = DateTime.UtcNow;
            if (now < d.RampUntil) { d.Ep.SetMasterVolumeLevelScalar(d.RampNow, ref Ctx); return; }   // mid ramp-up: keep ramping
            if (ApplyCap(d)) return;
            if (now < d.RestoreUntil && d.Restores < 3 && Math.Abs(v - d.RestoreVal) > 0.005f && DeviceMemoryOn)
            {
                // the headset pushed its own level right after connecting: put ours back
                d.Restores++;
                d.Ep.SetMasterVolumeLevelScalar(d.RestoreVal, ref Ctx);
                return;
            }
            bool settled = now > d.RestoreUntil && (d.ConnectedAt == DateTime.MinValue || now > d.ConnectedAt.AddSeconds(5));
            ToUi(() => sink.OnDeviceVolume(id, v, m, DeviceMemoryOn && settled));
        }

        void OnDefaultChanged(string newId)
        {
            if (newId != null && !devs.ContainsKey(newId)) SyncDevices(false);
            if (string.Equals(newId, defaultId, StringComparison.OrdinalIgnoreCase)) return;
            var now = DateTime.UtcNow;
            bool ours = newId != null && string.Equals(newId, expectDefault, StringComparison.OrdinalIgnoreCase) && now < expectUntil;
            if (!ours && BlockSteal && newId != null && defaultId != null && devs.ContainsKey(defaultId)
                && NoSteal.ContainsKey(newId) && JustConnected(newId, now))
            {
                MakeDefault(defaultId);   // it tried to take over as it connected: switch straight back
                return;
            }
            prevDefaultId = defaultId;
            prevChangedAt = now;
            defaultId = newId;
            if (RampOn && newId != null && devs.TryGetValue(newId, out var nd) && (nd.FormFactor == 3 || nd.FormFactor == 5)) StartRamp(nd);
            Publish();
        }

        void OnDefaultInputChanged(string id)
        {
            if (id != null && !ins.ContainsKey(id)) SyncInputs();
            if (string.Equals(id, defaultInId, StringComparison.OrdinalIgnoreCase)) return;
            defaultInId = id;
            Publish();
        }

        // switching to headphones: start at a quarter and ease up to the real level over ~1.2 s
        void StartRamp(Dev d)
        {
            if (d.Ep == null) return;
            d.Ep.GetMasterVolumeLevelScalar(out float cur);
            float target = DeviceMemoryOn && DeviceMemory.TryGetValue(d.Id, out float mem) ? mem : cur;
            target = Math.Min(target, CapOf(d.Id));
            if (target < 0.04f) return;
            int gen = ++d.RampGen;
            d.RampTarget = target;
            d.RampStart = DateTime.UtcNow;
            d.RampUntil = d.RampStart.AddMilliseconds(1600);
            d.RampNow = target * 0.25f;
            d.Ep.SetMasterVolumeLevelScalar(d.RampNow, ref Ctx);
            d.RampTimer?.Dispose();
            d.RampTimer = new Timer(_ => Post(() => RampStep(d, gen)), null, 60, 60);
        }

        void RampStep(Dev d, int gen)
        {
            if (gen != d.RampGen || d.Ep == null || !devs.ContainsKey(d.Id)) { d.RampTimer?.Dispose(); d.RampTimer = null; return; }
            double t = Math.Min(1, (DateTime.UtcNow - d.RampStart).TotalMilliseconds / 1200.0);
            double e = 1 - Math.Pow(1 - t, 2);
            d.RampNow = (float)(d.RampTarget * (0.25 + 0.75 * e));
            d.Ep.SetMasterVolumeLevelScalar(d.RampNow, ref Ctx);
            d.Ep.GetMute(out bool m);
            float v = d.RampNow;
            ToUi(() => sink.OnDeviceVolume(d.Id, v, m, false));
            if (t >= 1)
            {
                d.RampTimer?.Dispose();
                d.RampTimer = null;
                d.RampUntil = DateTime.MinValue;
            }
        }

        bool JustConnected(string id, DateTime now)
        {
            if (connectTimes.TryGetValue(id, out var t) && now - t < TimeSpan.FromSeconds(10)) return true;
            return devs.TryGetValue(id, out var d) && d.ConnectedAt != DateTime.MinValue && now - d.ConnectedAt < TimeSpan.FromSeconds(10);
        }

        // ------------------------------------------------------------ sessions
        void SyncSessions(Dev d, bool initial)
        {
            if (d.Sm == null) return;
            if (d.Sm.GetSessionEnumerator(out var e) != 0 || e == null) return;
            var seen = new HashSet<string>();
            e.GetCount(out int n);
            for (int i = 0; i < n; i++)
            {
                try
                {
                    if (e.GetSession(i, out var ctl) != 0 || ctl == null) continue;
                    var c2 = ctl as IAudioSessionControl2;
                    if (c2 == null) continue;
                    c2.GetState(out var st);
                    if (st == SessionState.Expired) continue;
                    if (c2.GetSessionInstanceIdentifier(out var iid) != 0 || iid == null) continue;
                    c2.GetProcessId(out int pid);
                    bool system = c2.IsSystemSoundsSession() == 0;
                    if (!system && pid != 0 && !Native.ProcessAlive(pid)) continue;
                    if (pid == MyPid) continue;   // Sip's own test sound
                    seen.Add(iid);
                    if (d.Sessions.ContainsKey(iid)) continue;

                    var s = new Sess { Iid = iid, C = c2, V = ctl as ISimpleAudioVolume, Pid = pid, Created = DateTime.UtcNow };
                    if (s.V == null) continue;
                    s.Info = Resolve(pid, c2, system);
                    s.Ev = new SessEvents(this, d.Id, iid);
                    try { c2.RegisterAudioSessionNotification(s.Ev); } catch { s.Ev = null; }
                    d.Sessions[iid] = s;

                    if (!initial && AppMemoryOn && !system && AppMemory.TryGetValue(s.Info.Exe, out float v))
                    {
                        s.RestoreVal = v;
                        s.RestoreUntil = DateTime.UtcNow.AddSeconds(3);
                        s.V.SetMasterVolume(v, ref Ctx);
                    }
                }
                catch (Exception ex) when (IsGone(ex)) { }
            }
            try { Marshal.ReleaseComObject(e); } catch { }
            foreach (var iid in d.Sessions.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                d.Sessions[iid].Close();
                d.Sessions.Remove(iid);
            }
        }

        void OnAppVolume(string devId, string iid, float v, bool m, Guid ctx)
        {
            if (ctx == Ctx) return;
            if (!devs.TryGetValue(devId, out var d) || !d.Sessions.TryGetValue(iid, out var s)) return;
            if (DateTime.UtcNow < s.RestoreUntil && !s.Reapplied && Math.Abs(v - s.RestoreVal) > 0.005f)
            {
                // the app reset its own volume while starting up: apply the remembered one again, once
                s.Reapplied = true;
                s.V.SetMasterVolume(s.RestoreVal, ref Ctx);
                return;
            }
            string key = s.Info.Key;
            ToUi(() => sink.OnAppVolume(devId, key, v, m));
        }

        AppInfo Resolve(int pid, IAudioSessionControl2 c, bool system)
        {
            if (system)
            {
                if (!apps.TryGetValue("system", out var sys)) apps["system"] = sys = new AppInfo { Key = "system", Exe = "system", Name = "System sounds", System = true };
                return sys;
            }
            string path = Native.ProcessPath(pid), key, exe;
            if (path != null) { key = path.ToLowerInvariant(); exe = Path.GetFileName(path).ToLowerInvariant(); }
            else
            {
                string pn = null;
                try { pn = Process.GetProcessById(pid).ProcessName; } catch { }
                exe = ((pn ?? ("pid " + pid)) + ".exe").ToLowerInvariant();
                key = "exe:" + exe;
            }
            if (apps.TryGetValue(key, out var info)) return info;
            info = new AppInfo { Key = key, Exe = exe };
            string dn = null;
            try { c.GetDisplayName(out dn); } catch { }
            if (!string.IsNullOrWhiteSpace(dn) && !dn.StartsWith("@")) info.Name = dn.Trim();
            if (info.Name == null && path != null)
                try
                {
                    var fd = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                    if (!string.IsNullOrEmpty(fd) && fd.Length <= 40) info.Name = fd;
                }
                catch { }
            if (info.Name == null) info.Name = Path.GetFileNameWithoutExtension(exe);
            if (path != null) info.Icon = Icons.FromExe(path, IconPx);
            apps[key] = info;
            return info;
        }

        // ------------------------------------------------------------ publishing
        internal void MarkDevices() { lock (dirtyGate) { dirtyDevices = true; Arm(); } }
        internal void MarkSessions(string devId) { lock (dirtyGate) { dirtySessions.Add(devId); Arm(); } }

        void Arm()
        {
            if (dirtyTimer == null) dirtyTimer = new Timer(_ => Post(Flush), null, Timeout.Infinite, Timeout.Infinite);
            dirtyTimer.Change(120, Timeout.Infinite);
        }

        void Flush()
        {
            bool devices;
            string[] sess;
            lock (dirtyGate)
            {
                devices = dirtyDevices; dirtyDevices = false;
                sess = dirtySessions.ToArray(); dirtySessions.Clear();
            }
            if (devices)
            {
                SyncDevices(false);
                SyncInputs();
                var def = GetDefaultId();
                if (!string.Equals(def, defaultId, StringComparison.OrdinalIgnoreCase)) { OnDefaultChanged(def); }
                defaultInId = GetDefaultId(EDataFlow.Capture);
            }
            foreach (var id in sess) if (devs.TryGetValue(id, out var d)) SyncSessions(d, false);
            Publish();
        }

        void Publish()
        {
            var snap = new Snapshot { DefaultId = defaultId };
            foreach (var d in devs.Values)
            {
                var ds = new DevSnap { Id = d.Id, Name = d.Name, Sub = d.Sub, FormFactor = d.FormFactor, Bluetooth = d.Bluetooth };
                try { d.Ep.GetMasterVolumeLevelScalar(out ds.Vol); d.Ep.GetMute(out ds.Muted); } catch { }
                foreach (var g in d.Sessions.Values.GroupBy(s => s.Info.Key))
                {
                    AppSnap a = null;
                    foreach (var s in g)
                    {
                        float v = 0; bool m = false; SessionState st = SessionState.Inactive;
                        try { s.V.GetMasterVolume(out v); s.V.GetMute(out m); s.C.GetState(out st); }
                        catch { continue; }
                        if (st == SessionState.Expired) continue;
                        if (a == null) a = new AppSnap { Key = s.Info.Key, Exe = s.Info.Exe, Name = s.Info.Name, Icon = s.Info.Icon, System = s.Info.System, Vol = v, Muted = m };
                        if (st == SessionState.Active && !a.Active) { a.Active = true; a.Vol = v; a.Muted = m; }
                    }
                    if (a != null) ds.Apps.Add(a);
                }
                ds.Apps.Sort((x, y) => x.System != y.System ? (x.System ? 1 : -1) : string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
                snap.Devices.Add(ds);
            }
            snap.Devices.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            snap.DefaultInputId = defaultInId;
            foreach (var d in ins.Values)
            {
                var ds = new DevSnap { Id = d.Id, Name = d.Name, Sub = d.Sub, FormFactor = d.FormFactor, Bluetooth = d.Bluetooth, Input = true };
                try { d.Ep.GetMasterVolumeLevelScalar(out ds.Vol); d.Ep.GetMute(out ds.Muted); } catch { }
                snap.Inputs.Add(ds);
            }
            snap.Inputs.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            ToUi(() => sink.OnSnapshot(snap));
        }

        // ------------------------------------------------------------ live objects
        class AppInfo
        {
            public string Key, Exe, Name;
            public Bitmap Icon;
            public bool System;
        }

        class Sess
        {
            public string Iid;
            public IAudioSessionControl2 C;
            public ISimpleAudioVolume V;
            public SessEvents Ev;
            public int Pid;
            public AppInfo Info;
            public DateTime Created, RestoreUntil;
            public float RestoreVal;
            public bool Reapplied;

            public void Close()
            {
                try { if (Ev != null) C.UnregisterAudioSessionNotification(Ev); } catch { }
                Ev = null;
            }
        }

        class Dev
        {
            readonly Engine e;
            public readonly string Id;
            IMMDevice md;
            public IAudioEndpointVolume Ep;
            public IAudioSessionManager2 Sm;
            EpCallback cb;
            SessNotify sn;
            public string Name = "", Sub = "";
            public int FormFactor = 10;
            public bool Bluetooth;
            public readonly Dictionary<string, Sess> Sessions = new Dictionary<string, Sess>();
            public DateTime ConnectedAt = DateTime.MinValue, RestoreUntil = DateTime.MinValue;
            public float RestoreVal;
            public int Restores;
            public Timer RestoreTimer, RampTimer;
            public int RampGen;
            public DateTime RampStart, RampUntil = DateTime.MinValue;
            public float RampTarget, RampNow;

            public Dev(Engine e, string id, IMMDevice md) { this.e = e; Id = id; this.md = md; }

            public void Open(bool sessions = true)
            {
                ReadProps();
                var iid = Iids.EndpointVolume;
                Marshal.ThrowExceptionForHR(md.Activate(ref iid, Iids.CLSCTX_ALL, IntPtr.Zero, out object o));
                Ep = (IAudioEndpointVolume)o;
                cb = new EpCallback(e, Id);
                Ep.RegisterControlChangeNotify(cb);
                var sid = Iids.SessionManager2;
                if (sessions && md.Activate(ref sid, Iids.CLSCTX_ALL, IntPtr.Zero, out object so) == 0 && so != null)
                {
                    Sm = (IAudioSessionManager2)so;
                    sn = new SessNotify(e, Id);
                    Sm.RegisterSessionNotification(sn);
                }
            }

            public void ReadProps()
            {
                if (md.OpenPropertyStore(0, out var ps) != 0 || ps == null) return;
                try
                {
                    string friendly = Str(ps, Keys.FriendlyName), desc = Str(ps, Keys.DeviceDesc), iface = Str(ps, Keys.InterfaceName);
                    Names.Split(friendly, desc, iface, out Name, out Sub);
                    int ff = Int(ps, Keys.FormFactor);
                    if (ff >= 0) FormFactor = ff;
                    string parent = (Str(ps, Keys.ParentId) ?? "") + " " + (Str(ps, Keys.EnumeratorName) ?? "");
                    Bluetooth = parent.IndexOf("BTH", StringComparison.OrdinalIgnoreCase) >= 0
                        || (friendly ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                finally { try { Marshal.ReleaseComObject(ps); } catch { } }
            }

            static string Str(IPropertyStore ps, PropertyKey k)
            {
                try
                {
                    if (ps.GetValue(ref k, out var pv) != 0) return null;
                    string s = pv.AsString();
                    Ole.PropVariantClear(ref pv);
                    return s;
                }
                catch { return null; }
            }

            static int Int(IPropertyStore ps, PropertyKey k)
            {
                try
                {
                    if (ps.GetValue(ref k, out var pv) != 0) return -1;
                    int v = pv.AsInt();
                    Ole.PropVariantClear(ref pv);
                    return v;
                }
                catch { return -1; }
            }

            public void Close()
            {
                try { RestoreTimer?.Dispose(); RampTimer?.Dispose(); } catch { }
                foreach (var s in Sessions.Values) s.Close();
                Sessions.Clear();
                try { if (Sm != null && sn != null) Sm.UnregisterSessionNotification(sn); } catch { }
                try { if (Ep != null && cb != null) Ep.UnregisterControlChangeNotify(cb); } catch { }
                Sm = null; Ep = null; md = null;
            }
        }

        internal void PropsChanged(string id) => Post(() => { var d = Any(id); if (d != null) { d.ReadProps(); Publish(); } });
        internal void NoteConnected(string id) { if (id != null) connectTimes[id] = DateTime.UtcNow; }
        internal void DefaultChanged(string id) => Post(() => OnDefaultChanged(id));
        internal void DefaultInputChanged(string id) => Post(() => OnDefaultInputChanged(id));
        internal void DeviceVolumeEvent(string id, float v, bool m, Guid ctx) => Post(() => OnDeviceVolume(id, v, m, ctx));
        internal void AppVolumeEvent(string dev, string iid, float v, bool m, Guid ctx) => Post(() => OnAppVolume(dev, iid, v, m, ctx));
    }

    // ---------------------------------------------------------------- callbacks
    // Windows calls these on its own threads: copy values and queue work on the engine thread, nothing else.
    // Public so the CLR can hand them to COM.
    [ComVisible(true)]
    public sealed class NotifyClient : IMMNotificationClient
    {
        readonly Engine e;
        internal NotifyClient(Engine e) { this.e = e; }
        public void OnDeviceStateChanged(string id, int state)
        {
            try { if (state == DeviceState.Active) e.NoteConnected(id); e.MarkDevices(); } catch { }
        }
        public void OnDeviceAdded(string id) { try { e.NoteConnected(id); e.MarkDevices(); } catch { } }
        public void OnDeviceRemoved(string id) { try { e.MarkDevices(); } catch { } }
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string id)
        {
            if (role != ERole.Console) return;
            try { if (flow == EDataFlow.Render) e.DefaultChanged(id); else if (flow == EDataFlow.Capture) e.DefaultInputChanged(id); } catch { }
        }
        public void OnPropertyValueChanged(string id, PropertyKey key)
        {
            try { if (id != null && (key.Is(Sip.CoreAudio.Keys.FriendlyName) || key.Is(Sip.CoreAudio.Keys.DeviceDesc))) e.PropsChanged(id); } catch { }
        }
    }

    [ComVisible(true)]
    public sealed class EpCallback : IAudioEndpointVolumeCallback
    {
        readonly Engine e; readonly string id;
        internal EpCallback(Engine e, string id) { this.e = e; this.id = id; }
        public int OnNotify(IntPtr data)
        {
            try
            {
                // AUDIO_VOLUME_NOTIFICATION_DATA: GUID ctx; BOOL muted; float master; ...
                var ctx = (Guid)Marshal.PtrToStructure(data, typeof(Guid));
                bool m = Marshal.ReadInt32(data, 16) != 0;
                float v = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
                e.DeviceVolumeEvent(id, v, m, ctx);
            }
            catch { }
            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class SessNotify : IAudioSessionNotification
    {
        readonly Engine e; readonly string id;
        internal SessNotify(Engine e, string id) { this.e = e; this.id = id; }
        public int OnSessionCreated(IAudioSessionControl s) { try { e.MarkSessions(id); } catch { } return 0; }
    }

    [ComVisible(true)]
    public sealed class SessEvents : IAudioSessionEvents
    {
        readonly Engine e; readonly string dev, iid;
        internal SessEvents(Engine e, string dev, string iid) { this.e = e; this.dev = dev; this.iid = iid; }
        public int OnDisplayNameChanged(string name, ref Guid ctx) => 0;
        public int OnIconPathChanged(string path, ref Guid ctx) => 0;
        public int OnSimpleVolumeChanged(float level, bool mute, ref Guid ctx)
        {
            try { e.AppVolumeEvent(dev, iid, level, mute, ctx); } catch { }
            return 0;
        }
        public int OnChannelVolumeChanged(int count, IntPtr volumes, int changed, ref Guid ctx) => 0;
        public int OnGroupingParamChanged(ref Guid g, ref Guid ctx) => 0;
        public int OnStateChanged(SessionState state) { try { e.MarkSessions(dev); } catch { } return 0; }
        public int OnSessionDisconnected(int reason) { try { e.MarkSessions(dev); } catch { } return 0; }
    }
}
