using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Sip
{
    static class Icons
    {
        // the app's own icon at the exact size the flyout draws it (crisper than scaling a 32 px icon)
        public static Bitmap FromExe(string path, int px)
        {
            try
            {
                var h = new IntPtr[1];
                var ids = new int[1];
                if (Native.PrivateExtractIcons(path, 0, px, px, h, ids, 1, 0) > 0 && h[0] != IntPtr.Zero)
                {
                    try { using (var ico = Icon.FromHandle(h[0])) return ico.ToBitmap(); }
                    finally { Native.DestroyIcon(h[0]); }
                }
            }
            catch { }
            try
            {
                using (var ico = Icon.ExtractAssociatedIcon(path))
                {
                    if (ico == null) return null;
                    var bmp = new Bitmap(px, px);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawIcon(ico, new Rectangle(0, 0, px, px));
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }
    }
}
