using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Prometheus;

class TestPrepProfiles {
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
        Media.PreparedMediaCacheRoot = Path.Combine(Path.GetTempPath(), "prom-prep-test-" + Guid.NewGuid().ToString("N"));

        string interlacedFile = Path.Combine(Path.GetTempPath(), "interlaced_" + Guid.NewGuid().ToString("N") + ".mp4");
        string hdrFile = Path.Combine(Path.GetTempPath(), "hdr_" + Guid.NewGuid().ToString("N") + ".mp4");
        string mkvFile = Path.Combine(Path.GetTempPath(), "video_" + Guid.NewGuid().ToString("N") + ".mkv");

        try {
            // Generate synthetic interlaced video (tt: top field first)
            Tools.Run(Tools.FFmpeg, "-y -hide_banner -loglevel error -f lavfi -i color=c=blue:s=320x180:d=1 -vf \"tinterlace=mode=interleave_top,setfield=mode=tff\" -c:v libx264 -flags +ilme+ildct " + Tools.Q(interlacedFile), CancellationToken.None);
            
            // Generate synthetic HDR video (color_trc=smpte2084, color_primaries=bt2020)
            Tools.Run(Tools.FFmpeg, "-y -hide_banner -loglevel error -f lavfi -i color=c=red:s=320x180:d=1 -c:v libx264 -color_trc smpte2084 -color_primaries bt2020 -colorspace bt2020nc " + Tools.Q(hdrFile), CancellationToken.None);

            // Generate synthetic MKV container video
            Tools.Run(Tools.FFmpeg, "-y -hide_banner -loglevel error -f lavfi -i color=c=green:s=320x180:d=1 -c:v libx264 " + Tools.Q(mkvFile), CancellationToken.None);

            // 1. Test Interlaced
            Console.WriteLine("Inspecting Interlaced video...");
            var assetInterlaced = Media.Inspect(interlacedFile, CancellationToken.None);
            var prepExt1 = assetInterlaced.extensions.FirstOrDefault(e => e.@namespace == "prometheus.media.prep-profile");
            Check(prepExt1 != null, "El vídeo entrelazado recibe una extensión de preparación");
            Check(Convert.ToString(prepExt1.payload["profile"]) == "interlaced_deinterlaced", "El vídeo entrelazado usa el perfil de desentrelazado");
            Check(assetInterlaced.representations.Any(r => r.role == "original") &&
                  assetInterlaced.representations.Any(r => r.role == "final") &&
                  assetInterlaced.representations.Any(r => r.role == "preview"),
                  "El medio preparado conserva representaciones original, final y preview");

            // 2. Test HDR
            Console.WriteLine("Inspecting HDR video...");
            var assetHdr = Media.Inspect(hdrFile, CancellationToken.None);
            var prepExt2 = assetHdr.extensions.FirstOrDefault(e => e.@namespace == "prometheus.media.prep-profile");
            Check(prepExt2 != null, "El vídeo HDR recibe una extensión de preparación");
            Check(Convert.ToString(prepExt2.payload["profile"]) == "hdr_tonemapped_sdr", "El vídeo HDR usa el perfil de tone-mapping SDR");

            // 3. Test MKV
            Console.WriteLine("Inspecting MKV video...");
            var assetMkv = Media.Inspect(mkvFile, CancellationToken.None);
            var indexExt = assetMkv.extensions.FirstOrDefault(e => e.@namespace == "prometheus.media.index");
            Check(indexExt != null, "El contenedor MKV recibe una extensión de índice");
            Check(Convert.ToBoolean(indexExt.payload["compatible"]), "El contenedor MKV queda marcado como compatible");
            Check(((IEnumerable<string>)indexExt.payload["stages"]).SequenceEqual(new[] { "playback", "frame_seek", "proxy", "export" }),
                  "El índice MKV declara las cuatro etapas de compatibilidad");

            // 4. Test Timeline Insertion and Frame Render with prepared media
            var p = Store.New();
            p.assets.Add(assetInterlaced);
            var vTrack = p.Active.tracks.First(t => t.kind == "video");
            Media.Insert(p, assetInterlaced.id, vTrack.id, 0);

            using (var frame = Rendering.FrameExact(p, 0.5, CancellationToken.None, 320, 180, true)) {
                var px = frame.GetPixel(160, 90);
                Check(px.B > px.R + 20 && px.B > px.G + 20,
                      "El fotograma preparado mantiene el contenido azul de origen");
            }

        } finally {
            try { File.Delete(interlacedFile); } catch {}
            try { File.Delete(hdrFile); } catch {}
            try { File.Delete(mkvFile); } catch {}
            try { Directory.Delete(Media.PreparedMediaCacheRoot, true); } catch {}
        }
    }
}