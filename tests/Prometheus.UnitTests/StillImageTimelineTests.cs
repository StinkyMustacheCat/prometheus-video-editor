using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class StillImageTimelineTests
{
    [Fact]
    public void RollEditCanExtendStillPastItsRecommendedSourceDuration()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        project.Assets.Add(new Asset { Id = "still", Streams = new() { new StreamInfo { Id = "frame", Kind = "image", Duration = Rational.Time(5) } } });
        var track = sequence.Tracks[0].Id;
        var left = new Clip { Id = "left", AssetId = "still", StreamId = "frame", TrackId = track,
            Duration = Rational.Time(5), AvailableSourceDurationSeconds = 5 };
        var right = new Clip { Id = "right", AssetId = "still", StreamId = "frame", TrackId = track,
            TimelineStart = Rational.Time(5), Duration = Rational.Time(5), AvailableSourceDurationSeconds = 5 };
        sequence.Clips.Add(left);
        sequence.Clips.Add(right);

        Assert.True(StillImageRules.IsStill(project, left));
        Assert.True(CollisionEngine.RollEdit(sequence, left.Id, right.Id, 7, clip => StillImageRules.IsStill(project, clip)));
        Assert.Equal(7, left.Duration.Seconds, 5);
        Assert.Equal(7, right.TimelineStart.Seconds, 5);
        Assert.Equal(0, right.SourceIn.Seconds, 5);
    }

    [Fact]
    public void IdentifiesStillByFileExtensionWithoutMediaStreamMetadata()
    {
        var project = ProjectStore.CreateNew();
        var clip = new Clip { AssetId = "photo" };
        project.Assets.Add(new Asset { Id = "photo", Source = new MediaSource
            { Kind = "local", Uri = "D:\\captures\\portrait.PNG" } });
        Assert.True(StillImageRules.IsStill(project, clip));
    }
}
