using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Prometheus;

class TestExportErrorReporting {
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

        string outFile = Path.Combine(Path.GetTempPath(), "export_err_" + Guid.NewGuid().ToString("N") + ".mp4");
        string dummyFile = Path.Combine(Path.GetTempPath(), "dummy_video_" + Guid.NewGuid().ToString("N") + ".mp4");

        try {
            Tools.Run(Tools.FFmpeg, "-y -hide_banner -loglevel error -f lavfi -i color=c=red:s=320x180:d=45 -c:v libx264 " + Tools.Q(dummyFile), CancellationToken.None);

            var p = Store.New();
            var vTrack = p.Active.tracks.First(t => t.kind == "video");

            var red = GeneratedMedia.CreateSolid("ClipBueno", "FF0000", 30);
            p.assets.Add(red);
            Media.Insert(p, red.id, vTrack.id, 0);

            var asset = Media.Inspect(dummyFile, CancellationToken.None);
            asset.name = "VideoCorrupto";
            p.assets.Add(asset);
            Media.Insert(p, asset.id, vTrack.id, 30);
            p.Active.clips.First(c => c.asset_id == asset.id).name = "VideoCorrupto";

            // Bypass the import-change guard so the export render reports the decoder failure.
            File.WriteAllBytes(dummyFile, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            var originalRepresentation = asset.representations.First(r => r.role == "original");
            originalRepresentation.size_bytes = null;
            originalRepresentation.modified_utc = null;

            Exception exportFailure = null;
            try {
                Rendering.Export(p, outFile, CancellationToken.None, null);
            } catch (Exception ex) {
                exportFailure = ex;
            }

            Check(exportFailure != null, "La exportación de un medio corrupto falla");
            Check(exportFailure.Message.Contains("VideoCorrupto"),
                  "El error identifica el medio corrupto: " + exportFailure.Message);
            Check(exportFailure.Message.Contains("fragmento"),
                  "El error incluye el fragmento de exportación: " + exportFailure.Message);
        } finally {
            try { File.Delete(dummyFile); } catch {}
            try { File.Delete(outFile); } catch {}
            try { Directory.Delete(Rendering.GeneratedCacheRoot, true); } catch {}
        }
    }
}