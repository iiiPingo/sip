using System;
using System.Runtime.InteropServices;
using System.Threading;
using Echoa.CoreAudio;

namespace Echoa
{
    // Live level of one microphone for the flyout's mic test. A mic only reports a level while something
    // is capturing from it, so this opens a shared-mode capture stream, throws the audio away and reads the
    // endpoint's peak meter. Runs on its own MTA thread, only while a test is on (Windows shows its
    // "microphone in use" icon for that time).
    sealed class MicMeter : IDisposable
    {
        readonly Thread thread;
        volatile bool stop;
        volatile float level;
        public readonly string DeviceId;
        public float Level => level;
        public bool Failed { get; private set; }

        public MicMeter(string deviceId)
        {
            DeviceId = deviceId;
            thread = new Thread(Run) { IsBackground = true, Name = "EchoaMicTest", Priority = ThreadPriority.BelowNormal };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        void Run()
        {
            IntPtr fmt = IntPtr.Zero;
            IAudioClient client = null;
            bool started = false;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                if (en.GetDevice(DeviceId, out var dev) != 0 || dev == null) { Failed = true; return; }
                var iid = Iids.AudioClient;
                Marshal.ThrowExceptionForHR(dev.Activate(ref iid, Iids.CLSCTX_ALL, IntPtr.Zero, out object o));
                client = (IAudioClient)o;
                var mid = Iids.Meter;
                Marshal.ThrowExceptionForHR(dev.Activate(ref mid, Iids.CLSCTX_ALL, IntPtr.Zero, out object mo));
                var meter = (IAudioMeterInformation)mo;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out fmt));
                Marshal.ThrowExceptionForHR(client.Initialize(0 /* shared */, 0, 2_000_000 /* 200 ms */, 0, fmt, IntPtr.Zero));
                var cid = Iids.CaptureClient;
                Marshal.ThrowExceptionForHR(client.GetService(ref cid, out object co));
                var capture = (IAudioCaptureClient)co;
                Marshal.ThrowExceptionForHR(client.Start());
                started = true;
                while (!stop)
                {
                    // drain what was captured (nothing is kept or saved)
                    for (int guard = 0; guard < 64 && capture.GetNextPacketSize(out int n) == 0 && n > 0; guard++)
                    {
                        if (capture.GetBuffer(out _, out int frames, out _, out _, out _) != 0) break;
                        capture.ReleaseBuffer(frames);
                    }
                    level = meter.GetPeakValue(out float p) == 0 ? Math.Max(0, Math.Min(1, p)) : 0;
                    Thread.Sleep(25);
                }
            }
            catch (Exception ex) { Failed = true; Log.Crash(ex); }
            finally
            {
                level = 0;
                try { if (started) client.Stop(); } catch { }
                if (fmt != IntPtr.Zero) Ole.CoTaskMemFree(fmt);
                try { if (client != null) Marshal.ReleaseComObject(client); } catch { }
            }
        }

        public void Dispose() { stop = true; }
    }
}
