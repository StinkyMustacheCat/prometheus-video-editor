using System;
using System.Linq;

namespace Prometheus.Core.Model;

public readonly record struct GpuDetailSettings(double SharpenAmount, double GlowAmount, double GlowRadius)
{
    public static GpuDetailSettings Neutral => new(0, 0, 5);
}

public static class GpuDetailEffect
{
    public const string SharpenId = "prometheus.image.sharpen";
    public const string GlowId = "prometheus.image.glow";
    public static bool Supports(string id) => id is SharpenId or GlowId;
    public static double Default(string id, string key) => key switch
    { "amount" => id == GlowId ? 0.35 : 0.7, "radius" => 5, _ => 0 };

    public static void Add(Clip clip, string id)
    {
        if (!Supports(id)) throw new ArgumentException("Efecto de detalle no disponible.", nameof(id));
        var existing = clip.Effects.FirstOrDefault(effect => effect.EffectId == id);
        if (existing != null) { existing.Enabled = true; return; }
        var effect = new EffectInstance { EffectId = id };
        ImageGeometryEffect.Number(effect, "amount", id == GlowId ? 0.35 : 0.7);
        if (id == GlowId) ImageGeometryEffect.Number(effect, "radius", 5);
        clip.Effects.Add(effect);
    }

    public static GpuDetailSettings Read(Clip clip)
    {
        var sharpen = clip.Effects.FirstOrDefault(e => e.EffectId == SharpenId && e.Enabled);
        var glow = clip.Effects.FirstOrDefault(e => e.EffectId == GlowId && e.Enabled);
        return new GpuDetailSettings(
            sharpen == null ? 0 : Math.Clamp(ImageGeometryEffect.ReadNumber(sharpen, "amount", 0.7), 0, 2),
            glow == null ? 0 : Math.Clamp(ImageGeometryEffect.ReadNumber(glow, "amount", 0.35), 0, 1),
            glow == null ? 5 : Math.Clamp(ImageGeometryEffect.ReadNumber(glow, "radius", 5), 1, 30));
    }
}
