using System;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class PlaceholderTests
{
    [Fact]
    public void ReplacingPlaceholderPreservesTimelinePositionAndDuration()
    {
        var project = ProjectStore.CreateNew();
        var video = project.ActiveSequence!.Tracks.Find(t => t.Kind == "video")!;
        var reserved = PlaceholderService.Create(project, video.Id, 4, 7);
        var media = new Asset { Id = Guid.NewGuid().ToString("N"), Name = "shot.mp4", Source = new MediaSource { Uri = "shot.mp4" } };
        var stream = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "video", Duration = Rational.Time(20) };
        media.Streams.Add(stream);
        project.Assets.Add(media);

        PlaceholderService.Replace(project, reserved.Id, media.Id, 2);

        Assert.Equal(4, reserved.TimelineStart.Seconds);
        Assert.Equal(7, reserved.Duration.Seconds);
        Assert.Equal(2, reserved.SourceIn.Seconds);
        Assert.Equal(media.Id, reserved.AssetId);
        Assert.Equal(stream.Id, reserved.StreamId);
    }

    [Fact]
    public void PlaceholderRejectsWrongMediaKind()
    {
        var project = ProjectStore.CreateNew();
        var audio = project.ActiveSequence!.Tracks.Find(t => t.Kind == "audio")!;
        var reserved = PlaceholderService.Create(project, audio.Id, 0);
        var media = new Asset { Id = Guid.NewGuid().ToString("N"), Name = "image.png" };
        media.Streams.Add(new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "image" });
        project.Assets.Add(media);

        Assert.Throws<InvalidOperationException>(() => PlaceholderService.Replace(project, reserved.Id, media.Id));
    }

    [Fact]
    public void LockedPlaceholderRejectsDropWithoutChangingItsContent()
    {
        var project = ProjectStore.CreateNew();
        var track = project.ActiveSequence!.Tracks.First(t => t.Kind == "video");
        var reserved = PlaceholderService.Create(project, track.Id, 3, 6);
        var media = new Asset { Name = "shot.mp4" };
        media.Streams.Add(new StreamInfo { Kind = "video", Duration = Rational.Time(12) });
        project.Assets.Add(media);
        track.Locked = true;

        Assert.False(PlaceholderService.Accepts(project, reserved, media));
        Assert.Throws<InvalidOperationException>(() => PlaceholderService.Replace(project, reserved.Id, media.Id));
        Assert.Equal(3, reserved.TimelineStart.Seconds);
        Assert.Equal(6, reserved.Duration.Seconds);
        Assert.NotEqual(media.Id, reserved.AssetId);
    }

    [Fact]
    public void SourceStartMustBeInsideTemporalMedia()
    {
        var project = ProjectStore.CreateNew();
        var track = project.ActiveSequence!.Tracks.First(t => t.Kind == "audio");
        var reserved = PlaceholderService.Create(project, track.Id, 0, 5);
        var media = new Asset { Name = "voice.wav" };
        media.Streams.Add(new StreamInfo { Kind = "audio", Duration = Rational.Time(3) });
        project.Assets.Add(media);

        Assert.Throws<InvalidOperationException>(() => PlaceholderService.Replace(project, reserved.Id, media.Id, 3));
        Assert.NotEqual(media.Id, reserved.AssetId);
    }
}
