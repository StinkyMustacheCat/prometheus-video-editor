using System;
using System.Linq;

namespace Prometheus.Core.Model;

public readonly record struct ImageGeometrySettings(bool FlipHorizontal, bool FlipVertical,
    double CropLeft, double CropTop, double CropRight, double CropBottom, double PixelBlock)
{
    public static ImageGeometrySettings Neutral => new(false, false, 0, 0, 0, 0, 0);
}

public static class ImageGeometryEffect
{
    public const string FlipHorizontalId = "prometheus.image.flip-horizontal";
    public const string FlipVerticalId = "prometheus.image.flip-vertical";
    public const string CropId = "prometheus.image.crop";
    public const string PixelizeId = "prometheus.image.pixelize";

    public static bool Supports(string id) => id is FlipHorizontalId or FlipVerticalId or CropId or PixelizeId;

    public static void Add(Clip clip, string id)
    {
        if (!Supports(id)) throw new ArgumentException("Efecto de imagen no disponible.", nameof(id));
        var existing = clip.Effects.FirstOrDefault(e => e.EffectId == id);
        if (existing != null) { existing.Enabled = true; return; }
        var effect = new EffectInstance { EffectId = id };
        if (id == CropId)
        {
            foreach (var key in new[] { "left", "top", "right", "bottom" }) Number(effect, key, 0);
        }
        else if (id == PixelizeId) Number(effect, "block", 16);
        clip.Effects.Add(effect);
    }

    public static void Number(EffectInstance effect, string key, double value) =>
        effect.Parameters[key] = new EffectValue { Kind = "number", Number = value };

    public static double ReadNumber(EffectInstance effect, string key, double fallback) =>
        effect.Parameters.TryGetValue(key, out var value) && value.Number is { } number && double.IsFinite(number)
            ? number : fallback;

    public static ImageGeometrySettings Read(Clip clip)
    {
        var crop = clip.Effects.FirstOrDefault(e => e.EffectId == CropId && e.Enabled);
        double Crop(string key) => crop == null ? 0 : Math.Clamp(ReadNumber(crop, key, 0), 0, 95);
        var left = Crop("left"); var top = Crop("top");
        var right = Math.Min(Crop("right"), 95 - left);
        var bottom = Math.Min(Crop("bottom"), 95 - top);
        var pixel = clip.Effects.FirstOrDefault(e => e.EffectId == PixelizeId && e.Enabled);
        return new ImageGeometrySettings(
            clip.Effects.Any(e => e.EffectId == FlipHorizontalId && e.Enabled),
            clip.Effects.Any(e => e.EffectId == FlipVerticalId && e.Enabled),
            left / 100, top / 100, right / 100, bottom / 100,
            pixel == null ? 0 : Math.Clamp(ReadNumber(pixel, "block", 16), 2, 256));
    }
}
