using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Prometheus;

class TestBlendDiffMultiply {
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static int CountDifferentPixels(Bitmap first, Bitmap second) {
        int different = 0;
        for (int y = 0; y < first.Height; y++)
            for (int x = 0; x < first.Width; x++)
                if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb())
                    different++;
        return different;
    }

    static void Main() {
        string runtime = Environment.GetEnvironmentVariable("PROMETHEUS_TEST_RUNTIME");
        if (String.IsNullOrWhiteSpace(runtime) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffmpeg.exe")) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffprobe.exe")))
            throw new Exception("PROMETHEUS_TEST_RUNTIME debe apuntar a un paquete con runtime\\ffmpeg.exe y runtime\\ffprobe.exe.");
        Tools.Root = runtime;
        Rendering.GeneratedCacheRoot = Path.Combine(Path.GetTempPath(), "prom-cache-" + Guid.NewGuid().ToString("N"));

        Bitmap difference = null;
        Bitmap multiply = null;
        try {
            var p = Store.New();
            var videoTracks = p.Active.tracks.Where(t => t.kind == "video").OrderBy(t => t.order).ToList();
            var track1 = videoTracks[0];
            var track2 = videoTracks[1];

            var background = GeneratedMedia.CreateSolid("Background", "FF3232", 5);
            p.assets.Add(background);
            Media.Insert(p, background.id, track1.id, 0);

            var textAsset = GeneratedMedia.CreateText("TEST", 5);
            p.assets.Add(textAsset);
            Media.Insert(p, textAsset.id, track2.id, 0);

            var textClip = p.Active.clips.First(c => c.asset_id == textAsset.id);
            BlendModes01034.Set(textClip, "difference");
            difference = Rendering.FrameExact(p, 1.0, CancellationToken.None, 320, 180, true);
            BlendModes01034.Set(textClip, "multiply");
            multiply = Rendering.FrameExact(p, 1.0, CancellationToken.None, 320, 180, true);

            Check(CountDifferentPixels(difference, multiply) > 10,
                  "Diferencia y Multiplicar renderizan resultados distintos sobre un fondo con color");
        } finally {
            if (difference != null) difference.Dispose();
            if (multiply != null) multiply.Dispose();
            if (Directory.Exists(Rendering.GeneratedCacheRoot))
                Directory.Delete(Rendering.GeneratedCacheRoot, true);
        }
    }
}