using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class SamMaskTrackStoreTests
{
    [Fact]
    public void IndependentSamLayersKeepTheirOwnTrackingAfterSaveAs()
    {
        var root = Path.Combine(Path.GetTempPath(), "prometheus-two-sam-" +
            Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source.pthproj");
        var targetPath = Path.Combine(root, "copy.pthproj");
        try
        {
            var project = ProjectStore.CreateNew();
            var clip = new Clip { MaskLayersInitialized = true };
            project.ActiveSequence!.Clips.Add(clip);
            for (var index = 0; index < 2; index++)
            {
                var track = new SamMaskTrack { StartSourceSeconds = 0,
                    FramesPerSecond = 30, FrameCount = 2, Width = 8, Height = 4 };
                var alpha = new byte[32];
                alpha[2 + index * 20] = 255;
                SamMaskTrackStore.Write(sourcePath, track, [
                    SamMaskKeyframe.Create(0, 8, 4, alpha),
                    SamMaskKeyframe.Create(1.0 / 30, 8, 4, alpha) ]);
                clip.MaskLayers.Add(new ClipMaskLayer { Kind = "sam",
                    Name = "SAM " + index, SamMaskTracks = [track] });
            }
            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
            SamMaskTrackStore.CopyReferenced(restored, sourcePath, targetPath);
            var layers = restored.ActiveSequence!.Clips.Single().MaskLayers;
            Assert.Equal(2, layers.Count);
            for (var index = 0; index < 2; index++)
            {
                var mask = SamMaskTrackStore.At(new Clip
                    { SamMaskTracks = layers[index].SamMaskTracks }, 0, 30, targetPath)!;
                Assert.Equal(255, mask.DecodeAlpha()[2 + index * 20]);
                Assert.Equal(0, mask.DecodeAlpha()[2 + (1 - index) * 20]);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TrackedMasksRemainReadableAfterProjectRoundTripAndSaveAs()
    {
        var root = Path.Combine(Path.GetTempPath(), "prometheus-sam-track-" +
            Guid.NewGuid().ToString("N"));
        var firstPath = Path.Combine(root, "first.pthproj");
        var secondPath = Path.Combine(root, "second.pthproj");
        try
        {
            var project = ProjectStore.CreateNew();
            var clip = new Clip { SourceIn = Prometheus.Core.Primitives.Rational.Time(1),
                Duration = Prometheus.Core.Primitives.Rational.Time(0.1) };
            project.ActiveSequence!.Clips.Add(clip);
            var track = new SamMaskTrack { StartSourceSeconds = 1.0,
                FramesPerSecond = 30, FrameCount = 3, Width = 8, Height = 4 };
            var frames = Enumerable.Range(0, 3).Select(frame =>
            {
                var alpha = new byte[32];
                alpha[frame] = 255;
                return SamMaskKeyframe.Create(1 + frame / 30.0, 8, 4, alpha);
            }).ToArray();
            SamMaskTrackStore.Write(firstPath, track, frames);
            clip.SamMaskTracks.Add(track);

            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
            SamMaskTrackStore.CopyReferenced(restored, firstPath, secondPath);
            var savedClip = restored.ActiveSequence!.Clips.Single();
            for (var frame = 0; frame < 3; frame++)
            {
                var mask = SamMaskTrackStore.At(savedClip, 1 + frame / 30.0, 60, secondPath);
                Assert.NotNull(mask);
                Assert.Equal(255, mask.DecodeAlpha()[frame]);
            }
            Assert.Equal(255, SamMaskTrackStore.At(savedClip, 1 + 0.75 / 30.0,
                60, secondPath)!.DecodeAlpha()[0]);
            Assert.Equal(255, SamMaskTrackStore.At(savedClip, 1 + 2.75 / 30.0,
                60, secondPath)!.DecodeAlpha()[2]);
            Assert.Null(SamMaskTrackStore.At(savedClip, 1.2, 60, secondPath));

            var corrected = new byte[32];
            corrected[7] = 255;
            savedClip.SamMasks.Add(SamMaskKeyframe.Create(1 + 1 / 30.0, 8, 4, corrected));
            Assert.Equal(corrected, SamMaskTrackStore.At(savedClip, 1 + 1 / 30.0,
                60, secondPath)!.DecodeAlpha());

            var replacement = new SamMaskTrack { StartSourceSeconds = track.StartSourceSeconds,
                FramesPerSecond = track.FramesPerSecond, FrameCount = track.FrameCount,
                Width = track.Width, Height = track.Height };
            SamMaskTrackStore.WritePatched(secondPath, track, replacement,
                new Dictionary<int, SamMaskKeyframe>
                {
                    [1] = SamMaskKeyframe.Create(1 + 1 / 30.0, 8, 4, corrected)
                });
            Assert.Equal(frames[0].DecodeAlpha(),
                SamMaskTrackStore.Read(secondPath, replacement, 0).DecodeAlpha());
            Assert.Equal(corrected,
                SamMaskTrackStore.Read(secondPath, replacement, 1).DecodeAlpha());
            Assert.Equal(frames[2].DecodeAlpha(),
                SamMaskTrackStore.Read(secondPath, replacement, 2).DecodeAlpha());
            Assert.True(SamTrackingService.CanRepair(savedClip, track, 1 + 1 / 30.0));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
