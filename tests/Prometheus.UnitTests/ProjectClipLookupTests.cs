using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class ProjectClipLookupTests
{
    [Fact]
    public void NestedChildUsesItsOwnTrackAndConvertsSequenceTime()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var childTrack = new Track { Kind = "video", Name = "V interna" };
        var child = new Clip { TrackId = childTrack.Id, TimelineStart = Rational.Time(4),
            Duration = Rational.Time(2) };
        var parent = new Clip { TrackId = sequence.Tracks.First(track => track.Kind == "video").Id,
            TimelineStart = Rational.Time(10), Duration = Rational.Time(2),
            SourceIn = Rational.Time(2), Speed = Rational.Time(2), NestedRole = "video",
            NestedContent = new NestedContent { Tracks = [childTrack], Clips = [child] } };
        sequence.Clips.Add(parent);
        var location = ProjectClipLookup.Find(project, child.Id, 11, parent.Id);
        Assert.NotNull(location);
        Assert.Same(child, location!.Clip);
        Assert.Same(childTrack, location.Track);
        Assert.Equal(4, location.LocalTime);
        Assert.Equal(11, location.ToRootTime(4));
    }
}
