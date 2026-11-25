using Prometheus.Client.Services;
using Prometheus.Core.Model;
using SkiaSharp;
using Xunit;

namespace Prometheus.UnitTests;

public class GpuCaptureComposerTests
{
    [Fact]
    public void CaptureUsesMontageDimensionsAndLayerOpacity()
    {
        using var baseLayer = Solid(2, 2, SKColors.Red);
        using var overlay = Solid(2, 2, SKColors.Blue);
        using var result = GpuCaptureComposer.Compose(4, 4,
        [
            new GpuCaptureComposer.Layer(baseLayer, new ClipVisualState { Framing = "stretch" }),
            new GpuCaptureComposer.Layer(overlay, new ClipVisualState { Framing = "stretch", Opacity = 0.5 })
        ]);
        var pixel = result.GetPixel(2, 2);
        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        Assert.InRange(pixel.Red, 125, 130);
        Assert.InRange(pixel.Blue, 125, 130);
    }

    [Fact]
    public void CaptureClipsMovedLayerAtMontageBoundary()
    {
        using var source = Solid(4, 4, SKColors.Red);
        using var result = GpuCaptureComposer.Compose(4, 4,
            [new GpuCaptureComposer.Layer(source, new ClipVisualState { PositionX = 0.5 })]);
        Assert.Equal(SKColors.Black, result.GetPixel(0, 2));
        Assert.Equal(SKColors.Red, result.GetPixel(3, 2));
    }

    [Fact]
    public void CaptureAppliesBaseHorizontalAndVerticalFlip()
    {
        using var source = new SKBitmap(2, 2);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Green);
        source.SetPixel(0, 1, SKColors.Blue);
        source.SetPixel(1, 1, SKColors.White);
        using var result = GpuCaptureComposer.Compose(2, 2,
            [new GpuCaptureComposer.Layer(source, new ClipVisualState
                { Framing = "stretch", FlipX = true, FlipY = true })]);
        Assert.Equal(SKColors.White, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(1, 1));
    }

    [Fact]
    public void CaptureRotatesAroundAnchor()
    {
        using var source = new SKBitmap(2, 1);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Green);
        using var result = GpuCaptureComposer.Compose(4, 4,
            [new GpuCaptureComposer.Layer(source, new ClipVisualState { RotationDegrees = 90 })]);
        Assert.True(result.GetPixel(2, 0).Red > 200);
        Assert.True(result.GetPixel(2, 3).Green > 100);
    }

    [Fact]
    public void CapturePreservesTransparentEmptyAreaAndRespectsOpaqueBackground()
    {
        using var source = Solid(2, 2, SKColors.Red);
        var layers = new[] { new GpuCaptureComposer.Layer(source,
            new ClipVisualState { PositionX = 0.5 }) };
        using var transparent = GpuCaptureComposer.Compose(4, 4, layers, SKColors.Transparent);
        using var opaque = GpuCaptureComposer.Compose(4, 4, layers, SKColors.White);
        Assert.Equal(0, transparent.GetPixel(0, 2).Alpha);
        Assert.Equal(SKColors.Red, transparent.GetPixel(3, 2));
        Assert.Equal(SKColors.White, opaque.GetPixel(0, 2));
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return bitmap;
    }
}
