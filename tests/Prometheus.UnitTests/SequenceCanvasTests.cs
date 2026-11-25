using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class SequenceCanvasTests
{
    [Fact]
    public void CanvasChange_OffersFitWithoutChangingTimingOrAudio()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var video = new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "video").Id, Visual = new ClipVisualState { Framing = "fill" } };
        var audio = new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "audio").Id, Visual = new ClipVisualState { Framing = "width" } };
        sequence.Clips.Add(video);
        sequence.Clips.Add(audio);
        var originalFps = sequence.Fps;

        SequenceOperations.SetCanvas(project, sequence.Id, 1080, 1920, false);
        Assert.Equal("fill", video.Visual.Framing);
        Assert.Equal(originalFps, sequence.Fps);

        SequenceOperations.SetCanvas(project, sequence.Id, 1080, 1080, true);
        Assert.Equal("fit", video.Visual.Framing);
        Assert.Equal("width", audio.Visual.Framing);
        Assert.Equal(1080, sequence.Width);
        Assert.Equal(1080, sequence.Height);
    }
}
