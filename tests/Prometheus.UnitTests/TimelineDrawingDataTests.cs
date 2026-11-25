using Prometheus.Client.Timeline;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.UnitTests;

public class TimelineDrawingDataTests
{
    [Fact]
    public void ViewportPreservesLayerOrderAndFindsEarlierOverlappingClips()
    {
        Clip At(string track, double start, double length) => new()
        {
            TrackId = track, TimelineStart = Rational.Time(start), Duration = Rational.Time(length)
        };

        var ending = At("V1", 9, 1);
        var outside = At("V1", 6, 2);
        var longClip = At("V1", 0, 15);
        var crossing = At("V1", 11, 2);
        var otherTrack = At("V2", 10, 1);
        var sequence = new Sequence { Clips = [ending, outside, longClip, crossing, otherTrack] };
        var drawing = new TimelineDrawingData(sequence, null);

        Assert.Equal(new[] { ending, longClip, crossing }, drawing.VisibleClips("v1", 10, 12));
        Assert.Equal(new[] { otherTrack }, drawing.VisibleClips("V2", 10, 12));
        Assert.Empty(drawing.VisibleClips("V1", 20, 30));
    }
}
