using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Media;

namespace Prometheus.Client.Services;

/// <summary>Samples one visible desktop pixel only when the user clicks the eyedropper.</summary>
public static class ScreenColorSampler
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    public static Color Read(int x, int y, int radius = 0)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new Win32Exception("No se pudo leer el monitor.");
        try
        {
            radius = Math.Clamp(radius, 0, 2);
            var red = 0; var green = 0; var blue = 0; var count = 0;
            for (var py = y - radius; py <= y + radius; py++)
            for (var px = x - radius; px <= x + radius; px++)
            {
                var value = GetPixel(dc, px, py);
                if (value == 0xFFFFFFFF) continue;
                red += (byte)value;
                green += (byte)(value >> 8);
                blue += (byte)(value >> 16);
                count++;
            }
            if (count == 0) throw new Win32Exception("El píxel no está disponible.");
            return Color.FromRgb((byte)(red / count), (byte)(green / count), (byte)(blue / count));
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
}
