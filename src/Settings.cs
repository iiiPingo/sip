using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Sip
{
    class DevPrefs
    {
        public bool Hidden;
        public bool NoSteal;      // don't let it become the default as it connects
        public float Vol = -1;    // remembered volume (-1 = none yet)
        public string Label = ""; // name shown in the hidden list, even while it's unplugged
        public string Rename = "";  // your own name for it ("" = Windows' name)
        public float Cap = 1f;      // volume limit (1 = none); only used while volume limits are on
        public bool AutoDefault;    // make it the default when it connects
        public string Fallback = "";// when it disconnects while it's the default, switch to this device

        public bool IsEmpty => !Hidden && !NoSteal && Vol < 0 && Rename.Length == 0 && Cap >= 0.995f && !AutoDefault && Fallback.Length == 0;
    }

    class Settings
    {
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sip");
        static string FilePath => Path.Combine(Dir, "settings.ini");

        public bool VolumeMemory = true;
        public bool AppMemory = true;
        public bool ScrollTray = true;
        public int ScrollStep = 2;
        public bool BlockSteal = true;
        public bool RulesOn = true;      // auto-switch rules (set per device)
        public bool CapsOn = false;      // volume limits (set per device)
        public bool RampOn = false;      // gentle ramp-up when switching to headphones
        public bool NowPlaying = true;   // "Now playing" strip in the flyout
        public string Accent = "";       // accent colour (#RRGGBB); empty = Sip blue
        public bool SimpleFlyout;        // flyout shows only the main output and its apps
        public int WinX, WinY, WinW, WinH;   // main window bounds (W/H negative = maximized; 0 = default)
        public readonly Dictionary<string, DevPrefs> Devices = new Dictionary<string, DevPrefs>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, float> AppVolumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> HiddenApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // exe -> name
        public bool FirstRun;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public DevPrefs Dev(string id)
        {
            if (!Devices.TryGetValue(id, out var p)) Devices[id] = p = new DevPrefs();
            return p;
        }

        public static Settings Load() => Parse(File.Exists(FilePath) ? SafeRead() : null);

        static string[] SafeRead() { try { return File.ReadAllLines(FilePath, Encoding.UTF8); } catch { return new string[0]; } }

        public static Settings Parse(string[] lines)
        {
            var s = new Settings();
            if (lines == null) { s.FirstRun = true; return s; }
            foreach (var line in lines)
            {
                try
                {
                    int i = line.IndexOf('=');
                    if (i <= 0 || line.TrimStart().StartsWith(";")) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    string kl = k.ToLowerInvariant();
                    if (kl.StartsWith("dev.")) s.Devices[k.Substring(4)] = ParseDev(v);
                    else if (kl.StartsWith("app.")) { if (float.TryParse(v, NumberStyles.Float, Inv, out float f) && f >= 0 && f <= 1) s.AppVolumes[k.Substring(4)] = f; }
                    else if (kl.StartsWith("hideapp.")) s.HiddenApps[k.Substring(8)] = v;
                    else switch (kl)
                        {
                            case "volumememory": s.VolumeMemory = v != "0"; break;
                            case "appmemory": s.AppMemory = v != "0"; break;
                            case "scrolltray": s.ScrollTray = v != "0"; break;
                            case "scrollstep": if (int.TryParse(v, out int st) && st >= 1 && st <= 20) s.ScrollStep = st; break;
                            case "blocksteal": s.BlockSteal = v != "0"; break;
                            case "rules": s.RulesOn = v != "0"; break;
                            case "limits": s.CapsOn = v == "1"; break;
                            case "rampup": s.RampOn = v == "1"; break;
                            case "nowplaying": s.NowPlaying = v != "0"; break;
                            case "accent": s.Accent = v; break;
                            case "simpleflyout": s.SimpleFlyout = v == "1"; break;
                            case "window":
                                var wv = v.Split(',');
                                if (wv.Length == 4 && int.TryParse(wv[0], out int wx) && int.TryParse(wv[1], out int wy) && int.TryParse(wv[2], out int ww) && int.TryParse(wv[3], out int wh))
                                { s.WinX = wx; s.WinY = wy; s.WinW = ww; s.WinH = wh; }
                                break;
                        }
                }
                catch { }
            }
            return s;
        }

        static DevPrefs ParseDev(string v)
        {
            var p = new DevPrefs();
            foreach (var part in v.Split('|'))
            {
                int i = part.IndexOf('=');
                if (i <= 0) continue;
                string k = part.Substring(0, i).Trim().ToLowerInvariant(), x = part.Substring(i + 1);
                switch (k)
                {
                    case "hide": p.Hidden = x == "1"; break;
                    case "nosteal": p.NoSteal = x == "1"; break;
                    case "vol": if (float.TryParse(x, NumberStyles.Float, Inv, out float f) && f >= 0 && f <= 1) p.Vol = f; break;
                    case "label": p.Label = x; break;
                    case "name": p.Rename = x.Trim(); break;
                    case "cap": if (float.TryParse(x, NumberStyles.Float, Inv, out float c) && c >= 0.05f && c <= 1) p.Cap = c; break;
                    case "auto": p.AutoDefault = x == "1"; break;
                    case "fallback": p.Fallback = x.Trim(); break;
                }
            }
            return p;
        }

        public string[] Serialize()
        {
            var l = new List<string>
            {
                "; Sip settings. Sip rewrites this file; edit it while Sip is closed.",
                "volumememory=" + B(VolumeMemory),
                "appmemory=" + B(AppMemory),
                "scrolltray=" + B(ScrollTray),
                "scrollstep=" + ScrollStep,
                "blocksteal=" + B(BlockSteal),
                "rules=" + B(RulesOn),
                "limits=" + B(CapsOn),
                "rampup=" + B(RampOn),
                "nowplaying=" + B(NowPlaying),
                "accent=" + Accent,
                "simpleflyout=" + B(SimpleFlyout),
                "window=" + WinX + "," + WinY + "," + WinW + "," + WinH,
            };
            foreach (var kv in Devices.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var p = kv.Value;
                if (p.IsEmpty) continue;
                l.Add("dev." + kv.Key + "=hide=" + B(p.Hidden) + "|nosteal=" + B(p.NoSteal) + "|vol=" + (p.Vol < 0 ? "" : p.Vol.ToString("0.###", Inv))
                    + "|cap=" + p.Cap.ToString("0.###", Inv) + "|auto=" + B(p.AutoDefault) + "|fallback=" + Clean(p.Fallback)
                    + "|name=" + Clean(p.Rename) + "|label=" + Clean(p.Label));
            }
            foreach (var kv in AppVolumes.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                l.Add("app." + kv.Key + "=" + kv.Value.ToString("0.###", Inv));
            foreach (var kv in HiddenApps.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                l.Add("hideapp." + kv.Key + "=" + Clean(kv.Value));
            return l.ToArray();
        }

        static string Clean(string s) => (s ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, string.Join("\r\n", Serialize()) + "\r\n", Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Crash(ex); }
        }

        static string B(bool b) => b ? "1" : "0";

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool StartWithWindows
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue("Sip") != null; } catch { return false; } }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue("Sip", "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue("Sip", false);
                    }
                }
                catch { }
            }
        }
    }
}
