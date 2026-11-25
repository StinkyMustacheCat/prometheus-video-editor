using System;
using System.Linq;

namespace Prometheus.Core.Model;

public readonly record struct ColorFinishingSettings(double Temperature, double Tint,
    double Shadows, double Highlights, double HueDegrees, double Vibrance, double Vignette)
{
    public static ColorFinishingSettings Neutral => new(6500, 0, 0, 0, 0, 0, 0);
}

public static class ColorFinishingEffect
{
    public const string WhiteBalanceId = "prometheus.color.white-balance";
    public const string ShadowsHighlightsId = "prometheus.color.shadows-highlights";
    public const string HueId = "prometheus.color.hue";
    public const string VibranceId = "prometheus.color.vibrance";
    public const string VignetteId = "prometheus.image.vignette";

    public static bool Supports(string id) => id is WhiteBalanceId or ShadowsHighlightsId or HueId or VibranceId or VignetteId;

    public static void Add(Clip clip, string id)
    {
        if (!Supports(id)) throw new ArgumentException("Efecto de color no disponible.", nameof(id));
        var existing = clip.Effects.FirstOrDefault(e => e.EffectId == id);
        if (existing != null) { existing.Enabled = true; return; }
        var effect = new EffectInstance { EffectId = id };
        if (id == WhiteBalanceId) { Number(effect, "temperature", 6500); Number(effect, "tint", 0); }
        else if (id == ShadowsHighlightsId) { Number(effect, "shadows", 0); Number(effect, "highlights", 0); }
        else if (id == HueId) Number(effect, "degrees", 0);
        else if (id == VibranceId) Number(effect, "intensity", 0);
        else Number(effect, "amount", 0.35);
        clip.Effects.Add(effect);
    }

    public static void Number(EffectInstance effect, string key, double value) =>
        effect.Parameters[key] = new EffectValue { Kind = "number", Number = value };

    public static double ReadNumber(EffectInstance effect, string key, double fallback) =>
        effect.Parameters.TryGetValue(key, out var value) && value.Number is { } number && double.IsFinite(number)
            ? number : fallback;

    public static ColorFinishingSettings Read(Clip clip)
    {
        double Value(string id, string key, double neutral) =>
            clip.Effects.FirstOrDefault(e => e.EffectId == id && e.Enabled) is { } effect
                ? ReadNumber(effect, key, neutral) : neutral;
        return new ColorFinishingSettings(
            Math.Clamp(Value(WhiteBalanceId, "temperature", 6500), 1000, 40000),
            Math.Clamp(Value(WhiteBalanceId, "tint", 0), -1, 1),
            Math.Clamp(Value(ShadowsHighlightsId, "shadows", 0), -1, 1),
            Math.Clamp(Value(ShadowsHighlightsId, "highlights", 0), -1, 1),
            Math.Clamp(Value(HueId, "degrees", 0), -180, 180),
            Math.Clamp(Value(VibranceId, "intensity", 0), -2, 2),
            Math.Clamp(Value(VignetteId, "amount", 0), 0, 1));
    }
}
