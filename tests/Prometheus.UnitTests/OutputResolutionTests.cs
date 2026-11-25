using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.UnitTests;

public class OutputResolutionTests
{
    [Theory]
    [InlineData(16, 9, 360, 640, 360)]
    [InlineData(16, 9, 480, 854, 480)]
    [InlineData(9, 16, 480, 480, 854)]
    [InlineData(16, 9, 720, 1280, 720)]
    [InlineData(16, 9, 1080, 1920, 1080)]
    [InlineData(16, 9, 1440, 2560, 1440)]
    [InlineData(16, 9, 2160, 3840, 2160)]
    [InlineData(9, 16, 2160, 2160, 3840)]
    [InlineData(1, 1, 2160, 2160, 2160)]
    [InlineData(4, 3, 2160, 2880, 2160)]
    [InlineData(3, 4, 2160, 2160, 2880)]
    [InlineData(64, 27, 1080, 2560, 1080)]
    [InlineData(64, 27, 2160, 5120, 2160)]
    public void PresetsPreserveOrientationAndEvenDimensions(int w, int h, int level, int expectedW, int expectedH)
    {
        var size = CanvasResolutions.AtShortEdge(w, h, level);
        Assert.Equal((expectedW, expectedH), size);
        Assert.True(CanvasResolutions.SameAspect(w, h, size.Width, size.Height));
    }

    [Fact]
    public void ExportChangesOnlyEncodingDimensionsAndLeavesDesignAndTimingIntact()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        seq.Fps = new Rational(30000, 1001);
        seq.Clips.Add(new Clip { TrackId = seq.Tracks[0].Id, Duration = Rational.Time(1),
            Visual = new ClipVisualState { PositionX = .2, ScaleY = 1.5 } });
        var before = ProjectStore.Serialize(project);
        var output = ExportPresentation.OutputSequence(seq, new(false, 12, 192, Width: 3840, Height: 2160));
        Assert.Equal((3840, 2160), (output.Width, output.Height));
        Assert.Equal(seq.Fps, output.Fps);
        Assert.Equal(before, ProjectStore.Serialize(project));
        var restored = System.Text.Json.JsonSerializer.Deserialize<GpuExportOptions>(
            System.Text.Json.JsonSerializer.Serialize(new GpuExportOptions(false, 24, 192, Recoverable: true, Width: 3840, Height: 2160)))!;
        Assert.Equal(3840, restored.Width);
        Assert.Equal(2160, restored.Height);
        Assert.True(restored.Recoverable);
    }

    [Theory]
    [InlineData(3840, null)]
    [InlineData(null, 2160)]
    [InlineData(1080, 1920)]
    [InlineData(0, 0)]
    [InlineData(9000, 5062)]
    public void RejectsPartialInvalidOrDifferentAspectOutputs(int? width, int? height)
    {
        Assert.Throws<ArgumentException>(() => ExportPresentation.OutputSequence(new Sequence(),
            new(false, 12, 192, Width: width, Height: height)));
    }

    [Theory]
    [InlineData("Montaje 1", "Rob run - Montaje 1")]
    [InlineData("Secuencia 02", "Rob run - Secuencia 02")]
    [InlineData("Intro", "Intro")]
    [InlineData("Montaje final", "Montaje final")]
    [InlineData("Intro: cierre?", "Intro_ cierre_")]
    [InlineData("CON", "_CON")]
    public void ExportNameUsesProjectOnlyForGenericMontages(string sequenceName, string expected)
    {
        var project = ProjectStore.CreateNew(); project.Name = "Rob run";
        project.ActiveSequence!.Name = sequenceName;
        Assert.Equal(expected, ExportPresentation.SuggestedName(project, project.ActiveSequence));
    }

    [Fact]
    public void OutputPreflightChecksTargetDimensionsNotDesignAndAllowsAudioOnly()
    {
        var project = ProjectStore.CreateNew(); var seq = project.ActiveSequence!;
        seq.Width = 853; seq.Height = 480;
        seq.Clips.Add(new Clip { TrackId = seq.Tracks[0].Id, Duration = Rational.Time(.2) });
        Assert.False(SequenceExportService.Inspect(project, null, null, new(false, 12, 192, AllowOffline: true)).Ready);
        Assert.True(SequenceExportService.Inspect(project, null, null,
            new(false, 12, 192, AllowOffline: true, Width: 854, Height: 480)).Ready);
        Assert.True(SequenceExportService.Inspect(project, null, null,
            new(false, 12, 192, ExportFormat.Wav, AllowOffline: true)).Ready);
    }
}
