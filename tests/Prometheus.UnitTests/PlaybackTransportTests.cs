using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Xunit;

namespace Prometheus.UnitTests;

public class PlaybackTransportTests
{
    [Fact]
    public void FrameStepsAndBoundsUseSequenceFrameRate()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Fps = new Rational(30, 1);
        sequence.Clips.Add(new Clip { TrackId = sequence.Tracks[0].Id,
            Duration = Rational.Time(2) });
        var transport = new PlaybackTransport(() => project);

        transport.StepFrames(1);
        Assert.Equal(1.0 / 30, transport.PositionSeconds, 6);
        transport.GoToEnd();
        Assert.Equal(2, transport.PositionSeconds);
        transport.StepFrames(-1);
        Assert.Equal(2 - 1.0 / 30, transport.PositionSeconds, 6);
        transport.GoToStart();
        transport.StepFrames(-1);
        Assert.Equal(0, transport.PositionSeconds);
    }

    [Fact]
    public void SeekingOnlyChangesTransportPosition()
    {
        var project = ProjectStore.CreateNew();
        var transport = new PlaybackTransport(() => project);
        var revision = project.Revision;

        transport.Seek(1.25);

        Assert.Equal(1.25, transport.PositionSeconds);
        Assert.Equal(revision, project.Revision);
        Assert.Empty(project.ActiveSequence!.Clips);
    }
}
