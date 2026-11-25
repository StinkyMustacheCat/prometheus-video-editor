using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>Stable shader slots for independent effect and opacity masks.</summary>
internal static class GpuMaskStages
{
    public const int Basic = 0;
    public const int Exposure = 1;
    public const int Levels = 2;
    public const int WhiteBalance = 3;
    public const int Shadows = 4;
    public const int Hue = 5;
    public const int Vibrance = 6;
    public const int Vignette = 7;
    public const int Monochrome = 8;
    public const int Pixelize = 9;
    public const int Blur = 10;
    public const int Chroma = 11;
    public const int Sharpen = 12;
    public const int Glow = 13;
    public const int Lut = 14;
    public const int Opacity = 15;
    public const int Count = 16;

    public static int ForEffect(string effectId) => effectId switch
    {
        BasicColorEffect.Id => Basic,
        ExposureEffect.Id => Exposure,
        LevelsEffect.Id => Levels,
        ColorFinishingEffect.WhiteBalanceId => WhiteBalance,
        ColorFinishingEffect.ShadowsHighlightsId => Shadows,
        ColorFinishingEffect.HueId => Hue,
        ColorFinishingEffect.VibranceId => Vibrance,
        ColorFinishingEffect.VignetteId => Vignette,
        MonochromeEffect.Id => Monochrome,
        ImageGeometryEffect.PixelizeId => Pixelize,
        GpuFilterEffect.BlurId => Blur,
        GpuFilterEffect.ChromaId or GpuFilterEffect.ChromaRgbId or
            GpuFilterEffect.ChromaMultiId => Chroma,
        GpuDetailEffect.SharpenId => Sharpen,
        GpuDetailEffect.GlowId => Glow,
        GpuLutEffect.Id => Lut,
        _ => -1
    };
}
