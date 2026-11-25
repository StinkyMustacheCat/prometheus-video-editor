using Prometheus.Core.Model;

namespace Prometheus.UnitTests;

public class GpuFilterEffectTests
{
    [Fact]
    public void BlurAndChromaKeepIndependentSettingsWhenDisabled()
    {
        var clip = new Clip();
        Assert.Equal(0, GpuFilterEffect.Read(clip).BlurRadius);
        Assert.False(GpuFilterEffect.Read(clip).ChromaEnabled);

        GpuFilterEffect.Add(clip, GpuFilterEffect.BlurId);
        GpuFilterEffect.Add(clip, GpuFilterEffect.ChromaId);
        var settings = GpuFilterEffect.Read(clip);
        Assert.Equal(6, settings.BlurRadius);
        Assert.True(settings.ChromaEnabled);
        Assert.Equal(1, settings.KeyGreen);

        clip.Effects.Single(e => e.EffectId == GpuFilterEffect.BlurId).Enabled = false;
        Assert.Equal(0, GpuFilterEffect.Read(clip).BlurRadius);
        Assert.True(GpuFilterEffect.Read(clip).ChromaEnabled);
        GpuFilterEffect.Add(clip, GpuFilterEffect.BlurId);
        Assert.Equal(2, clip.Effects.Count);
        Assert.Equal(6, GpuFilterEffect.Read(clip).BlurRadius);
    }

    [Fact]
    public void FilterParametersAreClampedBeforeGpuUse()
    {
        var clip = new Clip();
        GpuFilterEffect.Add(clip, GpuFilterEffect.BlurId);
        GpuFilterEffect.Add(clip, GpuFilterEffect.ChromaId);
        ImageGeometryEffect.Number(clip.Effects.Single(e => e.EffectId == GpuFilterEffect.BlurId), "radius", 999);
        ImageGeometryEffect.Number(clip.Effects.Single(e => e.EffectId == GpuFilterEffect.ChromaId), "tolerance", -1);
        var settings = GpuFilterEffect.Read(clip);
        Assert.Equal(24, settings.BlurRadius);
        Assert.Equal(0, settings.Tolerance);
    }

    [Fact]
    public void RgbAndMultiChromaResolveDistinctColorsAndOnlyOneMode()
    {
        var clip = new Clip();
        GpuFilterEffect.Add(clip, GpuFilterEffect.ChromaRgbId);
        var rgb = clip.Effects.Single(e => e.EffectId == GpuFilterEffect.ChromaRgbId);
        ImageGeometryEffect.Number(rgb, "use_red", 1);
        ImageGeometryEffect.Number(rgb, "use_blue", 1);
        var settings = GpuFilterEffect.Read(clip);
        Assert.Equal(3, settings.KeyCount);
        Assert.Equal(1, settings.KeyRed);
        Assert.Equal(1, settings.Key3Blue);

        GpuFilterEffect.Add(clip, GpuFilterEffect.ChromaMultiId);
        Assert.False(rgb.Enabled);
        var multi = clip.Effects.Single(e => e.EffectId == GpuFilterEffect.ChromaMultiId);
        ImageGeometryEffect.Number(multi, "enabled2", 0);
        ImageGeometryEffect.Number(multi, "enabled3", 1);
        ImageGeometryEffect.Number(multi, "shared", 0);
        ImageGeometryEffect.Number(multi, "tolerance3", 0.3);
        settings = GpuFilterEffect.Read(clip);
        Assert.Equal(2, settings.KeyCount);
        Assert.Equal(1, settings.Key2Red);
        Assert.Equal(0.3, settings.Tolerance2, 5);
    }
}
