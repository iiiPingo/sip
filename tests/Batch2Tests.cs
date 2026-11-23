using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using Echoa.CoreAudio;

namespace Echoa
{
    class NullSink : IAudioSink
    {
        public int Count; public Snapshot Last;
        public void OnSnapshot(Snapshot s) { Count++; Last = s; }
        public void OnDeviceVolume(string id, float vol, bool muted, bool remember) { }
        public void OnAppVolume(string devId, string key, float vol, bool muted) { }
    }

    class FakeEp : IAudioEndpointVolume
    {
        public float V = 0.8f; public bool FailRead;
        public int RegisterControlChangeNotify(IAudioEndpointVolumeCallback cb) => 0;
        public int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback cb) => 0;
        public int GetChannelCount(out int count) { count = 2; return 0; }
        public int SetMasterVolumeLevel(float db, ref Guid ctx) => 0;
        public int SetMasterVolumeLevelScalar(float level, ref Guid ctx) { V = level; return 0; }
        public int GetMasterVolumeLevel(out float db) { db = 0; return 0; }
        public int GetMasterVolumeLevelScalar(out float level) { level = FailRead ? 0 : V; return FailRead ? unchecked((int)0x88890004) : 0; }
        public int SetChannelVolumeLevel(int ch, float db, ref Guid ctx) => 0;
        public int SetChannelVolumeLevelScalar(int ch, float level, ref Guid ctx) => 0;
        public int GetChannelVolumeLevel(int ch, out float db) { db = 0; return 0; }
        public int GetChannelVolumeLevelScalar(int ch, out float level) { level = V; return 0; }
        public int SetMute(bool mute, ref Guid ctx) => 0;
        public int GetMute(out bool mute) { mute = false; return 0; }
        public int GetVolumeStepInfo(out int step, out int count) { step = 0; count = 100; return 0; }
        public int VolumeStepUp(ref Guid ctx) => 0;
        public int VolumeStepDown(ref Guid ctx) => 0;
        public int QueryHardwareSupport(out int mask) { mask = 0; return 0; }
        public int GetVolumeRange(out float min, out float max, out float inc) { min = -60; max = 0; inc = 1; return 0; }
    }

    class FakePolicy : IPolicyConfig
    {
        public int Result, Calls;
        public int GetMixFormat(IntPtr a, IntPtr b) => 0;
        public int GetDeviceFormat(IntPtr a, int b, IntPtr c) => 0;
        public int ResetDeviceFormat(IntPtr a) => 0;
        public int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c) => 0;
        public int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d) => 0;
        public int SetProcessingPeriod(IntPtr a, IntPtr b) => 0;
        public int GetShareMode(IntPtr a, IntPtr b) => 0;
        public int SetShareMode(IntPtr a, IntPtr b) => 0;
        public int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d) => 0;
        public int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d) => 0;
        public int SetDefaultEndpoint(string id, ERole role) { Calls++; return Result; }
        public int SetEndpointVisibility(string id, int visible) => 0;
    }

    static partial class Tests
    {
        static void RunBatch2()
        {
            T("#5 a leftover tick can't kill the ramp-up", RampLeftoverTick);
            T("#5 a normal ramp-up reaches its target", RampFinishes);
            T("#5 restarting mid ramp keeps the real target", RampKeepsTarget);
            T("#11 startup entry is read the same way as Minibar", RunEntry);
            T("#13 a failed device switch is reported", FailedSwitch);
            T("#14 a failed volume read is marked unknown", ReadFails);
            UI("#12 tooltip follows the device at the same volume", Tooltip);
            UI("#14 a failed read is never shown or remembered as 0", FailedReadNotRemembered);
            UI("#15 a Windows Audio restart reconnects all devices", AudioRestart);
        }

        static void P(object o, string n, object v) => FI(o.GetType(), n).SetValue(o, v);

        static object RampDev(Engine e, FakeEp ep)
        {
            var type = typeof(Engine).GetNestedType("Dev", BindingFlags.NonPublic);
            var d = Activator.CreateInstance(type, Any, null, new object[] { e, "dev", null }, null);
            P(d, "Ep", ep);
            ((IDictionary)G(e, "devs"))["dev"] = d;
            return d;
        }

        static void RampLeftoverTick()
        {
            var e = new Engine(new NullSink(), SynchronizationContext.Current);
            e.DeviceMemory["dev"] = 0.8f;
            var d = RampDev(e, new FakeEp());
            C(e, "StartRamp", d);
            C(e, "StartRamp", d);
            C(e, "RampStep", d, 1);
            bool alive = G(d, "RampTimer") != null;
            ((IDisposable)G(d, "RampTimer"))?.Dispose();
            e.Dispose();
            Check(alive, "the running ramp was stopped");
        }

        static void RampFinishes()
        {
            var e = new Engine(new NullSink(), SynchronizationContext.Current);
            e.DeviceMemory["dev"] = 0.8f;
            var ep = new FakeEp();
            var d = RampDev(e, ep);
            C(e, "StartRamp", d);
            P(d, "RampStart", DateTime.UtcNow.AddSeconds(-2));
            C(e, "RampStep", d, (int)G(d, "RampGen"));
            bool done = G(d, "RampTimer") == null && (DateTime)G(d, "RampUntil") == DateTime.MinValue;
            e.Dispose();
            Check(done && Math.Abs(ep.V - 0.8f) < 0.001f, "ended at " + ep.V);
        }

        static void RampKeepsTarget()
        {
            var e = new Engine(new NullSink(), SynchronizationContext.Current) { DeviceMemoryOn = false };
            var d = RampDev(e, new FakeEp());
            C(e, "StartRamp", d);
            C(e, "StartRamp", d);
            float target = (float)G(d, "RampTarget");
            ((IDisposable)G(d, "RampTimer"))?.Dispose();
            e.Dispose();
            Check(Math.Abs(target - 0.8f) < 0.001f, "target became " + target);
        }

        static void RunEntry()
        {
            Func<string, bool> yes = _ => true, no = _ => false;
            Check(Settings.RunValuePath("\"C:\\A B\\Echoa (2).exe\" --tray") == "C:\\A B\\Echoa (2).exe", "quoted");
            Check(Settings.RunValuePath("C:\\Apps\\Echoa.exe --tray") == "C:\\Apps\\Echoa.exe", "unquoted");
            Check(!Settings.RunValueWorks(null, yes), "no entry = off");
            Check(Settings.RunValueWorks("\"C:\\E\\Echoa.exe\"", yes), "live entry = on");
            Check(!Settings.RunValueWorks("\"C:\\Gone\\Echoa.exe\"", no), "dead entry = off, like Minibar");
        }

        static void FailedSwitch()
        {
            var e = new Engine(new NullSink(), SynchronizationContext.Current);
            var pol = new FakePolicy { Result = unchecked((int)0x80004005) };
            P(e, "policy", pol);
            bool threw = false;
            try { C(e, "MakeDefault", "B"); } catch (TargetInvocationException ex) when (ex.InnerException is COMException) { threw = true; }
            Check(threw && G(e, "expectDefault") == null, "failure not reported");
            pol.Result = 0; pol.Calls = 0;
            C(e, "MakeDefault", "B");
            Check(pol.Calls == 3 && (string)G(e, "expectDefault") == "B", "normal switch must set all 3 roles");
            e.Dispose();
        }

        static void ReadFails()
        {
            var e = new Engine(new NullSink(), SynchronizationContext.Current);
            var ep = new FakeEp { FailRead = true };
            var d = RampDev(e, ep);
            var ds = new DevSnap();
            typeof(Engine).GetMethod("ReadVolume", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { d, ds });
            Check(!ds.VolKnown, "should be unknown");
            ep.FailRead = false;
            typeof(Engine).GetMethod("ReadVolume", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { d, ds });
            e.Dispose();
            Check(ds.VolKnown && Math.Abs(ds.Vol - 0.8f) < 0.001f, "good read");
        }

        // an App with no real audio, tray icon or saving
        static App FakeApp(Snapshot snap)
        {
            var a = (App)FormatterServices.GetUninitializedObject(typeof(App));
            GC.SuppressFinalize(a);
            P(a, "<S>k__BackingField", new Settings { SaveBlocked = true });
            P(a, "<Snap>k__BackingField", snap);
            P(a, "tray", new TrayIcon());
            P(a, "saveTimer", new System.Windows.Forms.Timer { Interval = 100000 });
            P(a, "engine", new Engine(new NullSink(), SynchronizationContext.Current));
            P(a, "panel", new EchoaPanel(a));
            P(a, "trayPct", -1);
            return a;
        }

        static void Tooltip()
        {
            var h = Host();
            var a = FakeApp(h.Snap);
            C(a, "UpdateTray", false);
            h.Snap.DefaultId = "B";
            C(a, "UpdateTray", false);
            string tip = (string)G(G(a, "tray"), "tip");
            ((TrayIcon)G(a, "tray")).Dispose();
            Check(tip.Contains("Device B"), tip);
        }

        static void FailedReadNotRemembered()
        {
            var old = new Snapshot { DefaultId = "A" };
            var a0 = Dev("A"); a0.Vol = 0.6f;
            old.Devices.Add(a0);
            var a = FakeApp(old);
            var next = new Snapshot { DefaultId = "A" };
            var ad = Dev("A"); ad.Vol = 0; ad.VolKnown = false;
            var nd = Dev("N"); nd.Vol = 0; nd.VolKnown = false;
            var gd = Dev("G"); gd.Vol = 0.3f;
            next.Devices.AddRange(new[] { ad, nd, gd });
            C(a, "OnSnapshot", next);
            var st = (Settings)G(a, "<S>k__BackingField");
            ((TrayIcon)G(a, "tray")).Dispose();
            Check(Math.Abs(ad.Vol - 0.6f) < 0.001f, "shown " + ad.Vol);
            Check(st.Dev("A").Vol < 0 && st.Dev("N").Vol < 0, "a failed read was remembered");
            Check(Math.Abs(st.Dev("G").Vol - 0.3f) < 0.001f, "a good read must still be remembered");
        }

        // real audio objects, read-only: pretends the service's process id changed
        static void AudioRestart()
        {
            int pid = Native.ServicePid("Audiosrv");
            Check(pid > 0, "service query");
            var sink = new NullSink();
            var e = new Engine(sink, SynchronizationContext.Current);
            e.Start();
            var t = Stopwatch.StartNew();
            while (sink.Count == 0 && t.ElapsedMilliseconds < 8000) Pump(20);
            var before = sink.Last;
            int count = sink.Count;
            C(e, "Post", (Action)(() => { P(e, "svcPid", pid + 4); C(e, "CheckAudio"); }));
            t.Restart();
            while (sink.Count == count && t.ElapsedMilliseconds < 8000) Pump(20);
            var after = sink.Last;
            e.Dispose();
            Check(sink.Count > count, "didn't reconnect");
            Check(before.DefaultId == after.DefaultId, "default changed");
            Check(before.Devices.Select(d => d.Id).OrderBy(x => x).SequenceEqual(after.Devices.Select(d => d.Id).OrderBy(x => x)), "outputs changed");
            Check(before.Inputs.Count == after.Inputs.Count, "mics changed");
        }
    }
}
