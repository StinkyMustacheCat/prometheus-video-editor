using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class PreviewGeometryTests
{
    [Theory]
    [InlineData(1920, 1080, 0, 218.75, 1000, 562.5)]
    [InlineData(1080, 1920, 218.75, 0, 562.5, 1000)]
    [InlineData(1080, 1080, 0, 0, 1000, 1000)]
    [InlineData(1440, 1080, 0, 125, 1000, 750)]
    public void SequenceCanvas_KeepsItsAspectInSquareViewport(
        int width, int height, double x, double y, double shownWidth, double shownHeight)
    {
        var frame = PreviewGeometry.FitCanvas(width, height, 1000, 1000);
        Assert.Equal(x, frame.X, 4);
        Assert.Equal(y, frame.Y, 4);
        Assert.Equal(shownWidth, frame.Width, 4);
        Assert.Equal(shownHeight, frame.Height, 4);
    }

    [Fact]
    public void HorizontalClipFitsInsideVerticalCanvas_WithoutReplacingCanvasAspect()
    {
        var clip = PreviewGeometry.PlaceLayer(1080, 1920, 1920, 1080, new ClipVisualState());
        Assert.Equal(1080, clip.Width, 4);
        Assert.Equal(607.5, clip.Height, 4);
        Assert.Equal(656.25, clip.Y, 4);
    }

    [Fact]
    public void PositionIsMeasuredAgainstFullSequenceCanvas()
    {
        var clip = PreviewGeometry.PlaceLayer(1080, 1920, 1080, 1920,
            new ClipVisualState { PositionX = 0.5, PositionY = -0.25 });
        Assert.Equal(540, clip.X, 4);
        Assert.Equal(-480, clip.Y, 4);
    }

    [Fact]
    public void FramingModes_KeepTheirDistinctMeanings()
    {
        var fit = PreviewGeometry.PlaceLayer(1920, 1080, 1080, 1920, new ClipVisualState());
        var fill = PreviewGeometry.PlaceLayer(1920, 1080, 1080, 1920, new ClipVisualState { Framing = "fill" });
        var stretch = PreviewGeometry.PlaceLayer(1920, 1080, 1080, 1920, new ClipVisualState { Framing = "stretch" });
        Assert.Equal(1080, fit.Height, 4);
        Assert.Equal(1920, fill.Width, 4);
        Assert.True(fill.Height > 1080);
        Assert.Equal(1920, stretch.Width, 4);
        Assert.Equal(1080, stretch.Height, 4);
    }

    [Fact]
    public void ZoomKeepsTheImagePointUnderTheCursor()
    {
        var original = PreviewGeometry.FitCanvas(1920, 1080, 1000, 700);
        var pivotX = 320.0;
        var pivotY = 180.0;
        var oldImageX = (pivotX - original.X) / original.Width;
        var oldImageY = (pivotY - original.Y) / original.Height;
        var pan = PreviewGeometry.PanForZoom(original, 1920, 1080, 1000, 700, 1.5, pivotX, pivotY);
        var zoomed = PreviewGeometry.ZoomCanvas(1920, 1080, 1000, 700, 1.5, pan.X, pan.Y);
        Assert.Equal(oldImageX, (pivotX - zoomed.X) / zoomed.Width, 6);
        Assert.Equal(oldImageY, (pivotY - zoomed.Y) / zoomed.Height, 6);
    }

    [Fact]
    public void MovingAnchorWithPositionCompensationKeepsImageInPlace()
    {
        var original = PreviewGeometry.PlaceLayer(1920, 1080, 800, 600, new ClipVisualState());
        var changed = PreviewGeometry.PlaceLayer(1920, 1080, 800, 600,
            new ClipVisualState { AnchorX = 0.75, AnchorY = 0.25,
                PositionX = 0.25 * original.Width / 1920,
                PositionY = -0.25 * original.Height / 1080 });
        Assert.Equal(original.X, changed.X, 5);
        Assert.Equal(original.Y, changed.Y, 5);
        Assert.Equal(original.Width, changed.Width, 5);
    }

    [Fact]
    public void LayerRotationUsesItsAnchorAsPivot()
    {
        var state = new ClipVisualState { AnchorX = 0, AnchorY = 0,
            RotationDegrees = 90, Framing = "stretch" };
        var rect = PreviewGeometry.PlaceLayer(100, 100, 100, 100, state);
        var anchor = PreviewGeometry.RotateLayerPoint(rect, state, 0, 0);
        var right = PreviewGeometry.RotateLayerPoint(rect, state, 1, 0);
        Assert.Equal(anchor.X, right.X, 5);
        Assert.Equal(anchor.Y + 100, right.Y, 5);
    }

    [Fact]
    public void EyedropperMapsDisplayThroughRotationAndFlips()
    {
        var state = new ClipVisualState { RotationDegrees = 90, FlipX = true };
        var rect = new PreviewRectangle(10, 20, 100, 100);
        var displayed = PreviewGeometry.RotateLayerPoint(rect, state, 0.2, 0.3);
        var source = PreviewGeometry.MapDisplayPointToSource(rect, state, displayed.X, displayed.Y);
        Assert.NotNull(source);
        Assert.Equal(0.8, source.Value.X, 5);
        Assert.Equal(0.3, source.Value.Y, 5);
        Assert.Null(PreviewGeometry.MapDisplayPointToSource(rect, state, -100, -100));
    }

    [Fact]
    public void EyedropperKeepsTopAndBottomInTheSamePlace()
    {
        var rect = new PreviewRectangle(0, 0, 100, 100);
        Assert.Equal(0.1, PreviewGeometry.MapDisplayPointToSource(rect,
            new ClipVisualState(), 50, 10)!.Value.Y, 5);
        Assert.Equal(0.9, PreviewGeometry.MapDisplayPointToSource(rect,
            new ClipVisualState { FlipY = true }, 50, 10)!.Value.Y, 5);
    }
}
