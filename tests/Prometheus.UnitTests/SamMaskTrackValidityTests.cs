using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.UnitTests;

public class SamMaskTrackValidityTests
{
    [Fact]
    public void DetectsMissingCoverageAndChangedSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-sam-source-" +
            Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            var file = new FileInfo(path);
            var track = new SamMaskTrack { StartSourceSeconds = 1,
                FramesPerSecond = 30, FrameCount = 90,
                Width = 8, Height = 8,
                SourceFileLength = file.Length,
                SourceFileWriteUtcTicks = file.LastWriteTimeUtc.Ticks };
            var clip = new Clip { SourceIn = Rational.Time(1),
                Duration = Rational.Time(2) };
            Assert.Null(SamMaskTrackValidity.Problem(clip, track, path));

            clip.Duration = Rational.Time(4);
            Assert.Contains("recorte", SamMaskTrackValidity.Problem(clip, track, path));
            clip.Duration = Rational.Time(2);
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            Assert.Contains("cambió", SamMaskTrackValidity.Problem(clip, track, path));
        }
        finally { File.Delete(path); }
    }
}
