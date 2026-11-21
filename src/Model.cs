using System.Collections.Generic;
using System.Drawing;

namespace Sip
{
    // UI-side view of the audio world. The engine builds these on its own thread and hands them over;
    // after that only the UI thread touches them.
    class AppSnap
    {
        public string Key;      // grouping key: exe path (lowercase), "exe:<name>" or "system"
        public string Exe;      // file name, lowercase ("osu!.exe"), "system" for system sounds
        public string Name;
        public Bitmap Icon;     // null = draw a glyph instead
        public float Vol;
        public bool Muted;
        public bool Active;
        public bool System;
    }

    class DevSnap
    {
        public string Id;
        public string Name;     // e.g. "WH-1000XM5", "Speakers"
        public string Sub;      // the other half of the Windows name, e.g. "Headphones", "Realtek(R) Audio"
        public int FormFactor;  // EndpointFormFactor
        public bool Bluetooth;
        public bool Input;      // a microphone
        public string WinName;  // the name Windows uses, before any rename
        public float Vol;
        public bool Muted;
        public List<AppSnap> Apps = new List<AppSnap>();
    }

    class Snapshot
    {
        public List<DevSnap> Devices = new List<DevSnap>();
        public string DefaultId;
        public List<DevSnap> Inputs = new List<DevSnap>();
        public string DefaultInputId;
    }

    interface IAudioSink
    {
        void OnSnapshot(Snapshot s);
        // a device's volume changed outside Sip; remember = worth saving for volume memory
        void OnDeviceVolume(string id, float vol, bool muted, bool remember);
        void OnAppVolume(string devId, string key, float vol, bool muted);
    }

    static class Names
    {
        static readonly string[] Chipset = { "realtek", "high definition audio", "nvidia", "amd ", "amd(", "intel", "conexant", "cirrus", "via hd", "idt ", "sound blaster", "usb audio device", "usb pnp", "microsoft" };

        // Windows calls a device "<description> (<interface>)": "Speakers (Realtek(R) Audio)", "Headphones (WH-1000XM5)".
        // Lead with whichever half actually identifies it.
        public static void Split(string friendly, string desc, string iface, out string name, out string sub)
        {
            desc = (desc ?? "").Trim();
            iface = (iface ?? "").Trim();
            if (desc.Length == 0 && iface.Length == 0) { name = (friendly ?? "Unknown device").Trim(); sub = ""; return; }
            if (iface.Length == 0) { name = desc; sub = ""; return; }
            if (desc.Length == 0) { name = iface; sub = ""; return; }
            string low = iface.ToLowerInvariant() + " ";
            bool chip = false;
            foreach (var c in Chipset) if (low.Contains(c)) { chip = true; break; }
            if (chip) { name = desc; sub = iface; }
            else { name = iface; sub = desc; }
            if (string.Equals(name, sub, System.StringComparison.OrdinalIgnoreCase)) sub = "";
        }
    }
}
