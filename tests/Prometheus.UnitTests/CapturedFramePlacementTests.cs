using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class CapturedFramePlacementTests
{
    private static Asset Still() => new()
    {
        Id = "captured", Name = "Fotograma", RecommendedDuration = Rational.Time(5),
        Streams = { new StreamInfo { Id = "image", Kind = "image" } }
    };

    [Fact]
    public void Insert_SplitsLinkedMediaAndMovesLaterClipsMarkersAndRange()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var video = new Clip { Id = "video", TrackId = sequence.Tracks.First(t => t.Kind == "video").Id,
            TimelineStart = Rational.Zero, Duration = Rational.Time(10) };
        var audio = new Clip { Id = "audio", TrackId = sequence.Tracks.First(t => t.Kind == "audio").Id,
            TimelineStart = Rational.Zero, Duration = Rational.Time(10) };
        sequence.Clips.Add(video);
        sequence.Clips.Add(audio);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = { video.Id, audio.Id } });
        sequence.Markers.Add(new SequenceMarker { Time = Rational.Time(6) });
        sequence.MarkInSeconds = 2;
        sequence.MarkOutSeconds = 8;

        var placed = CapturedFramePlacement.Insert(project, Still(), 4);

        Assert.Equal(4, placed.TimelineStart.Seconds, 4);
        Assert.Equal(5, placed.Duration.Seconds, 4);
        Assert.Equal(4, video.Duration.Seconds, 4);
        Assert.Equal(4, audio.Duration.Seconds, 4);
        Assert.Equal(2, sequence.Clips.Count(c => c.TimelineStart.Seconds == 9));
        Assert.Equal(2, sequence.LinkGroups.Count);
        Assert.Equal(11, sequence.Markers[0].Time.Seconds, 4);
        Assert.Equal(2, sequence.MarkInSeconds);
        Assert.Equal(13, sequence.MarkOutSeconds);
    }

    [Fact]
    public void Overlay_UsesFreeVideoTrackWithoutMovingExistingMedia()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var original = new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "video").Id,
            TimelineStart = Rational.Zero, Duration = Rational.Time(10) };
        sequence.Clips.Add(original);

        var placed = CapturedFramePlacement.Overlay(project, Still(), 2);

        Assert.NotEqual(original.TrackId, placed.TrackId);
        Assert.Equal(10, original.Duration.Seconds, 4);
        Assert.Equal(0, original.TimelineStart.Seconds, 4);
        Assert.Equal(2, placed.TimelineStart.Seconds, 4);
    }
}
