using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Prometheus;

class TestExportLarge {
    static void Main() {
        string runtime = Environment.GetEnvironmentVariable("PROMETHEUS_TEST_RUNTIME");
        if (String.IsNullOrWhiteSpace(runtime) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffmpeg.exe")) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffprobe.exe")))
            throw new Exception("PROMETHEUS_TEST_RUNTIME debe apuntar a un paquete con runtime\\ffmpeg.exe y runtime\\ffprobe.exe.");
        Tools.Root = runtime;
        Rendering.GeneratedCacheRoot = Path.Combine(Path.GetTempPath(), "prom-cache-" + Guid.NewGuid().ToString("N"));

        string outFile = Path.Combine(Path.GetTempPath(), "export_large_" + Guid.NewGuid().ToString("N") + ".mp4");

        try {
            var p = Store.New();
            var vTrack = p.Active.tracks.First(t => t.kind == "video");
            var aTrack = p.Active.tracks.First(t => t.kind == "audio");

            // 75 seconds total (> 60s, triggering multi-chunk export!)
            var red = GeneratedMedia.CreateSolid("RedSolid", "FF0000", 40);
            var blue = GeneratedMedia.CreateSolid("BlueSolid", "0000FF", 35);
            p.assets.Add(red);
            p.assets.Add(blue);

            Media.Insert(p, red.id, vTrack.id, 0);
            Media.Insert(p, blue.id, vTrack.id, 40);

            // Add audio
            string tone = Path.Combine(Path.GetTempPath(), "sine_tone_" + Guid.NewGuid().ToString("N") + ".wav");
            Tools.Run(Tools.FFmpeg, "-y -hide_banner -loglevel error -f lavfi -i sine=f=440:d=75 -c:a pcm_s16le " + Tools.Q(tone), CancellationToken.None);
            try {
                var toneAsset = Media.Inspect(tone, CancellationToken.None);
                p.assets.Add(toneAsset);
                Media.Insert(p, toneAsset.id, aTrack.id, 0);

                double maxProg = 0;
                Console.WriteLine("Starting 75-second multi-chunk export...");
                Rendering.Export(p, outFile, CancellationToken.None, pct => {
                    if (pct > maxProg) {
                        maxProg = pct;
                        if ((int)maxProg % 25 == 0 || maxProg >= 99) {
                            Console.WriteLine("Export Progress: " + (int)maxProg + "%");
                        }
                    }
                });

                if (!File.Exists(outFile) || new FileInfo(outFile).Length == 0) throw new Exception("Exportación multiparte no generó un archivo no vacío");
                var fi = new FileInfo(outFile);
                if (maxProg < 100) throw new Exception("La exportación multiparte no completó el progreso: " + maxProg + "%");
                Console.WriteLine("PASS: Exportación multiparte completada: " + (fi.Length / 1024) + " KB, progreso=" + maxProg + "%");

                // Verify duration with ffprobe
                string probeStr = Tools.Run(Tools.FFprobe, "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 " + Tools.Q(outFile), CancellationToken.None);
                double dur;
                if (Double.TryParse(probeStr.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dur)) {
                    Console.WriteLine("PASS: Final file verified with FFprobe, duration: " + dur + "s");
                    if (dur < 74.0) throw new Exception("FAIL: Duration too short: " + dur);
                } else {
                    throw new Exception("FAIL: Could not probe duration: " + probeStr);
                }
            } finally {
                try { File.Delete(tone); } catch {}
            }
        } finally {
            try { File.Delete(outFile); } catch {}
            try { Directory.Delete(Rendering.GeneratedCacheRoot, true); } catch {}
        }
    }
}