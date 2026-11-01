using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Prometheus;

class TestBlackFrame {
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static void Main() {
        string runtime = Environment.GetEnvironmentVariable("PROMETHEUS_TEST_RUNTIME");
        if (String.IsNullOrWhiteSpace(runtime) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffmpeg.exe")) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffprobe.exe")))
            throw new Exception("PROMETHEUS_TEST_RUNTIME debe apuntar a un paquete con runtime\\ffmpeg.exe y runtime\\ffprobe.exe.");
        Tools.Root = runtime;
        Rendering.GeneratedCacheRoot = Path.Combine(Path.GetTempPath(), "prom-cache-" + Guid.NewGuid().ToString("N"));

        string img1 = Path.Combine(Path.GetTempPath(), "test_img1_" + Guid.NewGuid().ToString("N") + ".png");
        string img2 = Path.Combine(Path.GetTempPath(), "test_img2_" + Guid.NewGuid().ToString("N") + ".png");
        using (var b = new Bitmap(320, 180)) using (var g = Graphics.FromImage(b)) { g.Clear(Color.Red); b.Save(img1, ImageFormat.Png); }
        using (var b = new Bitmap(320, 180)) using (var g = Graphics.FromImage(b)) { g.Clear(Color.Green); b.Save(img2, ImageFormat.Png); }

        try {
            var a1 = Media.Inspect(img1, CancellationToken.None);
            var a2 = Media.Inspect(img2, CancellationToken.None);

            var p = Store.New();
            p.assets.Add(a1);
            p.assets.Add(a2);

            var videoTracks = p.Active.tracks.Where(t => t.kind == "video").OrderBy(t => t.order).ToList();
            var track1 = videoTracks[0]; // V1 (below)
            var track2 = videoTracks[1]; // V2 (above)

            Media.Insert(p, a1.id, track1.id, 0);
            Media.Insert(p, a2.id, track2.id, 0);

            using (var f1 = Rendering.FrameExact(p, 1.0, CancellationToken.None, 320, 180, true)) {
                var col1 = f1.GetPixel(160, 90);
                Check(col1.G > col1.R + 20 && col1.G > col1.B + 20,
                      "La pista superior visible compone el fotograma verde");
            }

            track2.enabled = false;

            using (var f2 = Rendering.FrameExact(p, 1.0, CancellationToken.None, 320, 180, true)) {
                var col2 = f2.GetPixel(160, 90);
                Check(col2.R > col2.G + 20 && col2.R > col2.B + 20,
                      "Al ocultar la pista superior se ve el fotograma rojo inferior, no negro");
            }
        } finally {
            try { File.Delete(img1); } catch {}
            try { File.Delete(img2); } catch {}
            if (Directory.Exists(Rendering.GeneratedCacheRoot))
                Directory.Delete(Rendering.GeneratedCacheRoot, true);
        }
    }
}