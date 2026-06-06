using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace OKXMonitor.Utils;

/// Renders a short uPnL string into a 32x32 tray icon, colored by sign.
/// Mirrors the macOS menu-bar live-PnL number.
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Render(string text, Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            float size = text.Length <= 4 ? 14f : text.Length <= 6 ? 11f : 9f;
            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, new RectangleF(0, 0, 32, 32), fmt);
        }
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}
