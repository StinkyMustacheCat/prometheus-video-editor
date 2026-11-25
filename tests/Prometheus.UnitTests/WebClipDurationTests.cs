using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.UnitTests;

public class WebClipDurationTests
{
    [Fact]
    public void PastedMediaChoosesLowestFreeVideoTrack()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var v1 = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 0);
        SubtitleTrack.Ensure(sequence);
        sequence.Clips.Add(new Clip { TrackId = v1.Id, TimelineStart = Rational.Time(0), Duration = Rational.Time(10) });
        var chosen = ImportPlacement.LowestFreeTrack(sequence, "video", 2, 5);
        Assert.Equal(1, chosen.Order);
        Assert.Equal(v1.Id, ImportPlacement.LowestFreeTrack(sequence, "video", 12, 5).Id);
    }

    [Fact]
    public void ReusedWebClipWithRealDurationCannotOverlapItsPreferredTrack()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var preferred = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 0);
        sequence.Clips.Add(new Clip { TrackId = preferred.Id, TimelineStart = Rational.Time(8), Duration = Rational.Time(5) });

        var destination = ImportPlacement.PreferredFreeTrack(sequence, preferred.Id, 0, 12);

        Assert.NotEqual(preferred.Id, destination.Id);
        Assert.Equal(1, destination.Order);
    }

    [Fact]
    public void LocalVideoAndAudioArePlacedTogetherWithoutOverwritingNeighbor()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var preferred = sequence.Tracks.First(t => t.Kind == "video");
        var neighbor = new Clip { TrackId = preferred.Id, TimelineStart = Rational.Time(4), Duration = Rational.Time(8) };
        sequence.Clips.Add(neighbor);
        var asset = new Asset { Id = "paired", Name = "interview.mp4", RecommendedDuration = Rational.Time(10) };
        asset.Streams.Add(new StreamInfo { Id = "picture", Kind = "video", Duration = Rational.Time(10) });
        asset.Streams.Add(new StreamInfo { Id = "sound", Kind = "audio", Duration = Rational.Time(10) });
        project.Assets.Add(asset);

        var result = ImportPlacement.PlaceAsset(project, asset.Id, preferred.Id, 2);

        Assert.Equal(4, neighbor.TimelineStart.Seconds);
        Assert.Equal(8, neighbor.Duration.Seconds);
        var inserted = sequence.Clips.Where(c => c.AssetId == asset.Id).ToArray();
        Assert.Equal(2, inserted.Length);
        Assert.NotEqual(preferred.Id, result.PrimaryTrackId);
        Assert.Equal(inserted[0].TimelineStart.Seconds, inserted[1].TimelineStart.Seconds);
        Assert.Contains(sequence.LinkGroups, g => inserted.All(c => g.ClipIds.Contains(c.Id)));
    }

    [Fact]
    public void ExplicitDropNeverJumpsBelowRequestedVideoTrack()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var v1 = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 0);
        var v2 = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 1);
        sequence.Clips.Add(new Clip { TrackId = v2.Id, TimelineStart = Rational.Time(0), Duration = Rational.Time(5) });

        var destination = ImportPlacement.PreferredFreeTrack(sequence, v2.Id, 0, 5);

        Assert.NotEqual(v1.Id, destination.Id);
        Assert.Equal(2, destination.Order);
    }

    [Fact]
    public void MetadataThenFinalDurationUpdatesSameClip()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var clip = new Clip { AssetId = "web-1", TrackId = sequence.Tracks[0].Id, Duration = Rational.Time(5), WebAutoDurationSeconds = 5 };
        sequence.Clips.Add(clip);
        WebClipDuration.Apply(project, "web-1", 12, false);
        Assert.Equal(12, clip.Duration.Seconds);
        WebClipDuration.Apply(project, "web-1", 11.5, true);
        Assert.Equal(11.5, clip.Duration.Seconds);
        Assert.Null(clip.WebAutoDurationSeconds);
    }

    [Fact]
    public void ManualTrimIsPreservedAndNeighborIsNotOverwritten()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var edited = new Clip { AssetId = "web-1", TrackId = sequence.Tracks[0].Id, Duration = Rational.Time(3), WebAutoDurationSeconds = 5 };
        var provisional = new Clip { AssetId = "web-1", TrackId = sequence.Tracks[1].Id, Duration = Rational.Time(5), WebAutoDurationSeconds = 5 };
        var neighbor = new Clip { AssetId = "other", TrackId = provisional.TrackId, TimelineStart = Rational.Time(8), Duration = Rational.Time(2) };
        sequence.Clips.AddRange(new[] { edited, provisional, neighbor });
        WebClipDuration.Apply(project, "web-1", 20, true);
        Assert.Equal(3, edited.Duration.Seconds);
        Assert.Equal(8, provisional.Duration.Seconds);
        Assert.Equal(8, neighbor.TimelineStart.Seconds);
    }

    [Fact]
    public void RequestedWebSourceRangeKeepsItsEndWhenMetadataChanges()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var clip = new Clip { AssetId = "web-range", TrackId = sequence.Tracks[0].Id,
            SourceIn = Rational.Time(4), Duration = Rational.Time(6), WebAutoDurationSeconds = 6,
            WebRequestedOutSeconds = 10 };
        var neighbor = new Clip { TrackId = clip.TrackId, TimelineStart = Rational.Time(7), Duration = Rational.Time(2) };
        sequence.Clips.AddRange(new[] { clip, neighbor });

        WebClipDuration.Apply(project, "web-range", 20, false);
        Assert.Equal(6, clip.Duration.Seconds);
        WebClipDuration.Apply(project, "web-range", 8, true);
        Assert.Equal(4, clip.Duration.Seconds);
        Assert.Equal(7, neighbor.TimelineStart.Seconds);
        Assert.Null(clip.WebRequestedOutSeconds);
    }
}
