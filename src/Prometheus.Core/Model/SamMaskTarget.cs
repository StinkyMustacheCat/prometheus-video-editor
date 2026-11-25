using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus.Core.Model;

/// <summary>Which part of a clip receives its SAM mask.</summary>
public static class SamMaskTarget
{
    public const string Opacity = "opacity";
    public const string Unassigned = "unassigned";
    public const string EffectPrefix = "effect:";

    private static readonly HashSet<string> CompatibleEffectIds = new(StringComparer.Ordinal)
    {
        BasicColorEffect.Id, ExposureEffect.Id, LevelsEffect.Id, MonochromeEffect.Id,
        ColorFinishingEffect.WhiteBalanceId, ColorFinishingEffect.ShadowsHighlightsId,
        ColorFinishingEffect.HueId, ColorFinishingEffect.VibranceId,
        ColorFinishingEffect.VignetteId, ImageGeometryEffect.PixelizeId,
        GpuFilterEffect.BlurId, GpuFilterEffect.ChromaId,
        GpuFilterEffect.ChromaRgbId, GpuFilterEffect.ChromaMultiId,
        GpuDetailEffect.SharpenId, GpuDetailEffect.GlowId, GpuLutEffect.Id
    };

    public static bool IsCompatible(EffectInstance effect) =>
        effect.Enabled && CompatibleEffectIds.Contains(effect.EffectId);

    public static EffectInstance? SelectedEffect(Clip clip)
    {
        var target = clip.SamMaskTarget;
        if (target == null || !target.StartsWith(EffectPrefix, StringComparison.Ordinal)) return null;
        var id = target[EffectPrefix.Length..];
        return clip.Effects.FirstOrDefault(effect => effect.Id == id && IsCompatible(effect));
    }

    public static string ForEffect(EffectInstance effect) => EffectPrefix + effect.Id;
}
