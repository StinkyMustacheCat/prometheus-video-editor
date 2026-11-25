using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using SkiaSharp;
using Xunit;

namespace Prometheus.UnitTests;

public class SequenceExportTests
{
    [Fact]
    public async Task ExportsExactFrameCountWithAudioToPlayableMp4()
    {
        Assert.True(File.Exists(LocalMediaService.Tool("ffmpeg")), "FFmpeg no encontrado");
        Assert.True(File.Exists(LocalMediaService.Tool("ffprobe")), "FFprobe no encontrado");
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Width = 64;
        sequence.Height = 64;
        sequence.Fps = new Rational(10, 1);
        sequence.Clips.Add(new Clip { TrackId = sequence.Tracks[0].Id,
            Duration = Rational.Time(0.4) });
        var output = Path.Combine(Path.GetTempPath(), "prometheus-export-test-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await SequenceExportService.ExportAsync(project, output, (time, _) =>
            {
                var frame = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul));
                frame.Erase(time < 0.2 ? SKColors.Red : SKColors.Blue);
                return Task.FromResult(frame);
            });
            using var probe = new Process { StartInfo = new ProcessStartInfo(LocalMediaService.Tool("ffprobe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true
            }};
            foreach (var argument in new[] { "-v", "error", "-show_entries",
                         "stream=codec_type,nb_frames", "-of", "csv=p=0", output })
                probe.StartInfo.ArgumentList.Add(argument);
            probe.Start();
            var details = await probe.StandardOutput.ReadToEndAsync();
            await probe.WaitForExitAsync();
            Assert.Equal(0, probe.ExitCode);
            Assert.Contains("video,4", details);
            Assert.Contains("audio", details);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
