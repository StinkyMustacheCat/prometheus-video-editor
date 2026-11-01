using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Collections.Generic;
using Prometheus;

class TestCompositionVisual {
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static int CountDifferentPixels(Bitmap first, Bitmap second) {
        int different = 0;
        for (int y = 0; y < first.Height; y++) {
            for (int x = 0; x < first.Width; x++) {
                if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb())
                    different++;
            }
        }
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

        string img1 = Path.Combine(Path.GetTempPath(), "bg_red_" + Guid.NewGuid().ToString("N") + ".png");
        using (var b = new Bitmap(320, 180)) using (var g = Graphics.FromImage(b)) { g.Clear(Color.FromArgb(255, 200, 50, 50)); b.Save(img1, ImageFormat.Png); }

        var frames = new Dictionary<string, Bitmap>();
        try {
            var a1 = Media.Inspect(img1, CancellationToken.None);
            var textAsset = GeneratedMedia.CreateText("DIFF", 5);

            var p = Store.New();
            p.assets.Add(a1);
            p.assets.Add(textAsset);

            var videoTracks = p.Active.tracks.Where(t => t.kind == "video").OrderBy(t => t.order).ToList();
            var track1 = videoTracks[0];
            var track2 = videoTracks[1];

            Media.Insert(p, a1.id, track1.id, 0);
            Media.Insert(p, textAsset.id, track2.id, 0);

            var textClip = p.Active.clips.First(c => c.asset_id == textAsset.id);

            foreach (var mode in new[] { "normal", "difference", "multiply" }) {
                BlendModes01034.Set(textClip, mode);
                frames.Add(mode, Rendering.FrameExact(p, 1.0, CancellationToken.None, 320, 180, true));
            }

            var corner = frames["normal"].GetPixel(5, 5);
            Check(corner.R > corner.G + 80 && corner.R > corner.B + 80,
                  "La composición conserva el fondo rojo fuera del texto");
            Check(CountDifferentPixels(frames["normal"], frames["difference"]) > 10,
                  "El modo Diferencia altera los píxeles cubiertos por el texto");
            Check(CountDifferentPixels(frames["normal"], frames["multiply"]) > 10,
                  "El modo Multiplicar altera los píxeles cubiertos por el texto");
            Check(CountDifferentPixels(frames["difference"], frames["multiply"]) > 10,
                  "Diferencia y Multiplicar producen composiciones distintas");
        } finally {
            foreach (var frame in frames.Values) frame.Dispose();
            try { File.Delete(img1); } catch {}
            if (Directory.Exists(Rendering.GeneratedCacheRoot))
                Directory.Delete(Rendering.GeneratedCacheRoot, true);
        }
    }
}