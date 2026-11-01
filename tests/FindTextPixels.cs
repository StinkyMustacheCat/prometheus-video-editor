using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Prometheus;

class FindTextPixels {
    static void Main() {
        Rendering.GeneratedCacheRoot = Path.Combine(Path.GetTempPath(), "prom-cache-" + Guid.NewGuid().ToString("N"));

        var textAsset = GeneratedMedia.CreateText("DIFF", 5);
        string file = TextRaster0817.RenderFile(textAsset, 320, 180);
        using (var bmp = new Bitmap(file)) {
            int nonTransparent = 0;
            int sampleX = -1, sampleY = -1;
            for (int y = 0; y < bmp.Height; y++) {
                for (int x = 0; x < bmp.Width; x++) {
                    var px = bmp.GetPixel(x, y);
                    if (px.A > 200) {
                        nonTransparent++;
                        if (sampleX == -1) { sampleX = x; sampleY = y; }
                    }
                }
            }
            Console.WriteLine("Non-transparent pixels in text image: " + nonTransparent + ", sample at (" + sampleX + ", " + sampleY + ")");
        }
    }
}