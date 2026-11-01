using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class TestGpuAndCpuExport {
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static void CheckOutput(string path, double progress, string encoder, int width, int height) {
        Check(File.Exists(path) && new FileInfo(path).Length > 0,
              encoder + " genera un archivo de exportación no vacío");
        Check(progress >= 100,
              encoder + " completa el progreso de exportación");

        string probe = Tools.Run(
            Tools.FFprobe,
            "-v error -select_streams v:0 -show_entries stream=codec_name,width,height -of default=noprint_wrappers=1:nokey=1 " + Tools.Q(path),
            CancellationToken.None);
        string[] fields = probe.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Check(fields.Length == 3 && fields[0] == "h264" &&
              fields[1] == width.ToString() && fields[2] == height.ToString(),
              encoder + " produce un vídeo H.264 de " + width + "x" + height + " verificable con FFprobe");
    }

    static void Main() {
        string runtime = Environment.GetEnvironmentVariable("PROMETHEUS_TEST_RUNTIME");
        if (String.IsNullOrWhiteSpace(runtime) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffmpeg.exe")) ||
            !File.Exists(Path.Combine(runtime, "runtime", "ffprobe.exe")))
            throw new Exception("PROMETHEUS_TEST_RUNTIME debe apuntar a un paquete con runtime\\ffmpeg.exe y runtime\\ffprobe.exe.");
        Tools.Root = runtime;
        if (!Tools.CanUseNvenc())
            throw new Exception("La suite GPU requiere una GPU NVIDIA, controladores compatibles y FFmpeg con NVENC disponible.");

        Rendering.GeneratedCacheRoot = Path.Combine(Path.GetTempPath(), "prom-cache-" + Guid.NewGuid().ToString("N"));
        string sample = Path.Combine(Path.GetTempPath(), "test_export_source_" + Guid.NewGuid().ToString("N") + ".mp4");
        string outGpu = Path.Combine(Path.GetTempPath(), "test_gpu_" + Guid.NewGuid().ToString("N") + ".mp4");
        string outCpu = Path.Combine(Path.GetTempPath(), "test_cpu_" + Guid.NewGuid().ToString("N") + ".mp4");

        try {
            Tools.Run(Tools.FFmpeg,
                "-y -hide_banner -loglevel error -f lavfi -i color=c=blue:s=320x180:r=30:d=2 -c:v libx264 -pix_fmt yuv420p -an " + Tools.Q(sample),
                CancellationToken.None);
            Check(File.Exists(sample) && new FileInfo(sample).Length > 0,
                  "FFmpeg crea el medio sintético requerido por las pruebas de exportación");

            var p = Store.New();
            var vTrack = p.Active.tracks.First(t => t.kind == "video");
            var asset = Media.Inspect(sample, CancellationToken.None);
            p.assets.Add(asset);
            Media.Insert(p, asset.id, vTrack.id, 0);

            var optGpu = ExportOptions.For(p);
            optGpu.Acceleration = "nvenc";
            optGpu.Width = 568;
            optGpu.Height = 320;
            double gpuProgress = 0;
            Rendering.Export(p, outGpu, CancellationToken.None, pct => {
                if (pct > gpuProgress) gpuProgress = pct;
            }, optGpu);
            CheckOutput(outGpu, gpuProgress, "NVENC", optGpu.Width, optGpu.Height);

            var optCpu = ExportOptions.For(p);
            optCpu.Acceleration = "cpu";
            optCpu.Width = 568;
            optCpu.Height = 320;
            double cpuProgress = 0;
            Rendering.Export(p, outCpu, CancellationToken.None, pct => {
                if (pct > cpuProgress) cpuProgress = pct;
            }, optCpu);
            CheckOutput(outCpu, cpuProgress, "libx264", optCpu.Width, optCpu.Height);
        } finally {
            if (File.Exists(sample)) File.Delete(sample);
            if (File.Exists(outGpu)) File.Delete(outGpu);
            if (File.Exists(outCpu)) File.Delete(outCpu);
            if (Directory.Exists(Rendering.GeneratedCacheRoot))
                Directory.Delete(Rendering.GeneratedCacheRoot, true);
        }
    }
}
