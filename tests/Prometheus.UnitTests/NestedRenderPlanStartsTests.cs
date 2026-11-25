using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class NestedRenderPlanStartsTests
{
    [Fact]
    public void UpcomingStarts_IncludesBriefOverlayAndNestedChildAtProjectedTime()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var videoTrack = sequence.Tracks.First(track => track.Kind == "video");
        sequence.Clips.Add(new Clip
        {
            TrackId = videoTrack.Id, TimelineStart = Rational.Time(1.1),
            Duration = Rational.Time(0.08)
        });
        var childTrack = new Track { Kind = "video" };
        var nested = new Clip
        {
            TrackId = videoTrack.Id, TimelineStart = Rational.Time(1),
            Duration = Rational.Time(2), SourceIn = Rational.Time(0.2),
            Speed = Rational.Time(2), NestedRole = "video",
            NestedContent = new NestedContent()
        };
        nested.NestedContent.Tracks.Add(childTrack);
        nested.NestedContent.Clips.Add(new Clip
        {
            TrackId = childTrack.Id, TimelineStart = Rational.Time(0.8),
            Duration = Rational.Time(0.1)
        });
        sequence.Clips.Add(nested);

        var starts = NestedRenderPlan.UpcomingStarts(project, 1.05, 1.5, "video");
        Assert.Contains(starts, value => System.Math.Abs(value - 1.1) < 0.0001);
        Assert.Contains(starts, value => System.Math.Abs(value - 1.3) < 0.0001);
        Assert.DoesNotContain(starts, value => System.Math.Abs(value - 1) < 0.0001);
    }
}
