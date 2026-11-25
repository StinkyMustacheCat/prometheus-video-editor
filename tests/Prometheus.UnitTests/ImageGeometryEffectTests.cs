using Prometheus.Core.Model;

namespace Prometheus.UnitTests;

public class ImageGeometryEffectTests
{
    [Fact]
    public void DisabledEffectsDoNotChangeSamplingState()
    {
        var clip = new Clip();
        ImageGeometryEffect.Add(clip, ImageGeometryEffect.FlipHorizontalId);
        ImageGeometryEffect.Add(clip, ImageGeometryEffect.PixelizeId);
        Assert.True(ImageGeometryEffect.Read(clip).FlipHorizontal);
        Assert.Equal(16, ImageGeometryEffect.Read(clip).PixelBlock);
        foreach (var effect in clip.Effects) effect.Enabled = false;
        Assert.Equal(ImageGeometrySettings.Neutral, ImageGeometryEffect.Read(clip));
    }

    [Fact]
    public void CropCannotRemoveEntireImage()
    {
        var clip = new Clip();
        ImageGeometryEffect.Add(clip, ImageGeometryEffect.CropId);
        var crop = Assert.Single(clip.Effects);
        ImageGeometryEffect.Number(crop, "left", 80);
        ImageGeometryEffect.Number(crop, "right", 80);
        ImageGeometryEffect.Number(crop, "top", 90);
        ImageGeometryEffect.Number(crop, "bottom", 90);
        var state = ImageGeometryEffect.Read(clip);
        Assert.Equal(0.8, state.CropLeft, 5);
        Assert.Equal(0.15, state.CropRight, 5);
        Assert.Equal(0.9, state.CropTop, 5);
        Assert.Equal(0.05, state.CropBottom, 5);
    }
}
