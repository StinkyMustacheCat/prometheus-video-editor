using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class FitClipSpeedTests
{
    [Fact]
    public void FitsExactlyAndKeepsLinkedClipStartAndSourceSpan()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var video = Clip(sequence, "V1", 1, 2);
        var audio = Clip(sequence, "A1", 1, 2);
        var next = Clip(sequence, "V1", 4.2, 1);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = [video.Id, audio.Id] });
        KeyframeAnimation.Set(video, "visual.opacity", 2, 0.5);

        var rate = CollisionEngine.FitSpeedToNextClip(sequence, video.Id);

        Assert.Equal(new Rational(5, 8), video.Speed);
        Assert.Equal(rate, audio.Speed.Seconds);
        Assert.Equal(Rational.Time(1), video.TimelineStart);
        Assert.Equal(next.TimelineStart, video.TimelineStart + video.Duration);
        Assert.Equal(next.TimelineStart, audio.TimelineStart + audio.Duration);
        Assert.Equal(Rational.Time(2), video.Duration * video.Speed);
        Assert.Equal(1.60, video.KeyframeTracks.Single().Points.Single().TimeSeconds, 6);
    }

    [Fact]
    public void LinkedObstructionLeavesBothClipsUnchanged()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var video = Clip(sequence, "V1", 0, 1);
        var audio = Clip(sequence, "A1", 0, 1);
        Clip(sequence, "V1", 3, 1);
        Clip(sequence, "A1", 2, 1);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = [video.Id, audio.Id] });

        Assert.Throws<InvalidOperationException>(() => CollisionEngine.FitSpeedToNextClip(sequence, video.Id));
        Assert.Equal(Rational.One, video.Duration);
        Assert.Equal(Rational.One, audio.Duration);
        Assert.Equal(Rational.One, audio.Speed);
    }

    [Fact]
    public void RepeatedTrimsAndFitsKeepExactEndpointAndVisibleSource()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var random = new Random(37);
        var video = Clip(sequence, "V1", 0, 1 + random.NextDouble());
        var next = Clip(sequence, "V1", 2 + random.NextDouble(), 1);
        for (var edit = 0; edit < 100; edit++)
        {
            var sourceSpan = video.Duration.Seconds * video.Speed.Seconds;
            CollisionEngine.FitSpeedToNextClip(sequence, video.Id);
            Assert.Equal(next.TimelineStart, video.TimelineStart + video.Duration);
            Assert.InRange(Math.Abs(video.Duration.Seconds * video.Speed.Seconds - sourceSpan), 0, 1e-9);
            video.Duration -= Rational.Time(.001);
            next.TimelineStart += Rational.Time(.002);
        }
    }

    private static Clip Clip(Sequence sequence, string track, double start, double duration)
    {
        var clip = new Clip { TrackId = sequence.Tracks.Single(item => item.Name == track).Id,
            TimelineStart = Rational.Time(start), Duration = Rational.Time(duration), Speed = Rational.One };
        sequence.Clips.Add(clip);
        return clip;
    }
}
