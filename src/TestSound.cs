using System;
using System.Runtime.InteropServices;
using System.Threading;
using Echoa.CoreAudio;

namespace Echoa
{
    // A short, soft two-note chime played straight to one output device (WASAPI shared mode),
    // whether or not it's the default. Runs on a thread-pool (MTA) thread.
    static class TestSound
    {
        static int busy;

        public static void Play(string deviceId)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;   // one at a time
            IntPtr fmt = IntPtr.Zero;
            IAudioClient client = null;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                if (en.GetDevice(deviceId, out var dev) != 0 || dev == null) return;
                var iid = Iids.AudioClient;
                Marshal.ThrowExceptionForHR(dev.Activate(ref iid, Iids.CLSCTX_ALL, IntPtr.Zero, out object o));
                client = (IAudioClient)o;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out fmt));

                int tag = Marshal.ReadInt16(fmt, 0) & 0xFFFF, channels = Marshal.ReadInt16(fmt, 2), rate = Marshal.ReadInt32(fmt, 4);
                int bits = Marshal.ReadInt16(fmt, 14), align = Marshal.ReadInt16(fmt, 12);
                bool isFloat = tag == 3;
                if (tag == 0xFFFE)
                {
                    var sub = (Guid)Marshal.PtrToStructure(fmt + 24, typeof(Guid));
                    isFloat = sub == new Guid("00000003-0000-0010-8000-00aa00389b71");
                    if (!isFloat && sub != new Guid("00000001-0000-0010-8000-00aa00389b71")) return;
                }
                else if (tag != 1 && tag != 3) return;
                if (!isFloat && bits != 16 && bits != 24 && bits != 32) return;

                Marshal.ThrowExceptionForHR(client.Initialize(0 /* shared */, 0, 10_000_000 /* 1 s */, 0, fmt, IntPtr.Zero));
                client.GetBufferSize(out int bufFrames);
                int frames = Math.Min(bufFrames, (int)(rate * 0.7));
                var rid = Iids.RenderClient;
                Marshal.ThrowExceptionForHR(client.GetService(ref rid, out object ro));
                var render = (IAudioRenderClient)ro;
                Marshal.ThrowExceptionForHR(render.GetBuffer(frames, out IntPtr buf));

                int bytesPer = align / Math.Max(1, channels);
                for (int i = 0; i < frames; i++)
                {
                    double t = (double)i / rate, x = 0;
                    // two soft bell notes (E6 then A6) with quick attack and gentle decay
                    if (t < 0.32) x += Note(t, 1318.5);
                    if (t >= 0.16) x += Note(t - 0.16, 1760.0);
                    float v = (float)(x * 0.22);
                    for (int c = 0; c < channels; c++)
                    {
                        IntPtr p = buf + i * align + c * bytesPer;
                        if (isFloat) Marshal.WriteInt32(p, BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
                        else if (bits == 16) Marshal.WriteInt16(p, (short)(v * 32767));
                        else if (bits == 24) { int s = (int)(v * 8388607); Marshal.WriteByte(p, (byte)s); Marshal.WriteByte(p + 1, (byte)(s >> 8)); Marshal.WriteByte(p + 2, (byte)(s >> 16)); }
                        else Marshal.WriteInt32(p, (int)(v * 2147483647.0));
                    }
                }
                render.ReleaseBuffer(frames, 0);
                client.Start();
                Thread.Sleep((int)(1000.0 * frames / rate) + 120);
                client.Stop();
            }
            finally
            {
                if (fmt != IntPtr.Zero) Ole.CoTaskMemFree(fmt);
                Interlocked.Exchange(ref busy, 0);
            }
        }

        static double Note(double t, double f)
        {
            if (t < 0) return 0;
            double env = Math.Min(1, t / 0.006) * Math.Exp(-t * 7.5);
            return env * (Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(4 * Math.PI * f * t));
        }
    }
}
