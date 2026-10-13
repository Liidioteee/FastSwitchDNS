using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FastSwitchDNS
{
    /// <summary>Draws the tray icon: a coloured badge with the first letter of the active DNS.</summary>
    static class IconFactory
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public static Color ColorFor(DnsPreset preset)
        {
            if (preset == null) return Color.FromArgb(110, 110, 120);
            switch (preset.Name)
            {
                case "Cloudflare": return Color.FromArgb(243, 128, 32);
                case "Google": return Color.FromArgb(66, 133, 244);
                case "Quad9": return Color.FromArgb(140, 70, 200);
                case "AdGuard": return Color.FromArgb(58, 170, 90);
                case "OpenDNS": return Color.FromArgb(230, 160, 0);
            }
            // Stable colour per custom name
            int h = 0;
            foreach (char c in preset.Name) h = h * 31 + c;
            var palette = new[]
            {
                Color.FromArgb(0, 150, 160), Color.FromArgb(200, 60, 120),
                Color.FromArgb(90, 100, 220), Color.FromArgb(170, 110, 50),
            };
            return palette[(h & 0x7fffffff) % palette.Length];
        }

        public static Icon Create(DnsPreset preset)
        {
            int size = SystemInformation.SmallIconSize.Width;
            string letter = preset == null ? "A" : preset.Name.Substring(0, 1).ToUpperInvariant();

            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                float r = size * 0.28f;
                var rect = new RectangleF(0.5f, 0.5f, size - 1, size - 1);
                using (var path = RoundedRect(rect, r))
                using (var brush = new SolidBrush(ColorFor(preset)))
                    g.FillPath(brush, path);

                using var font = new Font("Segoe UI", size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(letter, font, Brushes.White, new RectangleF(0, size * 0.03f, size, size), format);
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
