using System;
using System.Linq;

namespace Prometheus.Core.Model;

public readonly record struct GpuFilterSettings(double BlurRadius, bool ChromaEnabled,
    double KeyRed, double KeyGreen, double KeyBlue, double Tolerance, double Softness, double Spill)
{
    public static GpuFilterSettings Neutral => new(0, false, 0, 1, 0, 0.18, 0.10, 0.5);
    public int KeyCount { get; init; }
    public double Key2Red { get; init; }
    public double Key2Green { get; init; }
    public double Key2Blue { get; init; }
    public double Key3Red { get; init; }
    public double Key3Green { get; init; }
    public double Key3Blue { get; init; }
    public double Tolerance2 { get; init; }
    public double Tolerance3 { get; init; }
    public double Softness2 { get; init; }
    public double Softness3 { get; init; }
    public double Spill2 { get; init; }
    public double Spill3 { get; init; }
}

public static class GpuFilterEffect
{
    public const string BlurId = "prometheus.image.gaussian-blur";
    public const string ChromaId = "prometheus.image.chroma-key";
    public const string ChromaRgbId = "prometheus.image.chroma-rgb";
    public const string ChromaMultiId = "prometheus.image.chroma-multi";

    public static bool Supports(string id) => id is BlurId or ChromaId or ChromaRgbId or ChromaMultiId;

    public static double Default(string key) => key switch
    {
        "green" => 1, "tolerance" => 0.18, "softness" => 0.10, "spill" => 0.5,
        "radius" => 6, "green2" => 0, "blue2" => 1,
        "red3" => 1, "green3" => 0, "blue3" => 0,
        "enabled2" => 1, "enabled3" => 0, "shared" => 1,
        "tolerance2" or "tolerance3" => 0.18,
        "softness2" or "softness3" => 0.10,
        "spill2" or "spill3" => 0.5,
        "use_green" => 1, "use_blue" or "use_red" => 0, _ => 0
    };

    public static void Add(Clip clip, string id)
    {
        if (!Supports(id)) throw new ArgumentException("Filtro GPU no disponible.", nameof(id));
        if (id != BlurId)
            foreach (var other in clip.Effects.Where(e => e.EffectId is ChromaId or ChromaRgbId or ChromaMultiId && e.EffectId != id))
                other.Enabled = false;
        var effect = clip.Effects.FirstOrDefault(e => e.EffectId == id);
        if (effect != null) { effect.Enabled = true; return; }
        effect = new EffectInstance { EffectId = id };
        var keys = id switch
        {
            BlurId => new[] { "radius" },
            ChromaRgbId => new[] { "use_red", "use_green", "use_blue", "tolerance", "softness", "spill" },
            ChromaMultiId => new[] { "red", "green", "blue", "red2", "green2", "blue2",
                "red3", "green3", "blue3", "enabled2", "enabled3", "shared",
                "tolerance", "softness", "spill", "tolerance2", "softness2", "spill2",
                "tolerance3", "softness3", "spill3" },
            _ => new[] { "red", "green", "blue", "tolerance", "softness", "spill" }
        };
        foreach (var key in keys)
            ImageGeometryEffect.Number(effect, key, Default(key));
        clip.Effects.Add(effect);
    }

    public static GpuFilterSettings Read(Clip clip)
    {
        var blur = clip.Effects.FirstOrDefault(e => e.EffectId == BlurId && e.Enabled);
        var chroma = clip.Effects.LastOrDefault(e => e.EffectId is ChromaId or ChromaRgbId or ChromaMultiId && e.Enabled);
        double Value(EffectInstance? effect, string key, double max) => effect == null ? Default(key) :
            Math.Clamp(ImageGeometryEffect.ReadNumber(effect, key, Default(key)), 0, max);
        var rgb = chroma?.EffectId == ChromaRgbId;
        var multi = chroma?.EffectId == ChromaMultiId;
        var useRed = rgb && Value(chroma, "use_red", 1) > 0.5;
        var useGreen = rgb && Value(chroma, "use_green", 1) > 0.5;
        var useBlue = rgb && Value(chroma, "use_blue", 1) > 0.5;
        var keyCount = rgb ? (useRed ? 1 : 0) + (useGreen ? 1 : 0) + (useBlue ? 1 : 0) :
            chroma == null ? 0 : multi ? 1 + (Value(chroma, "enabled2", 1) > 0.5 ? 1 : 0) +
                (Value(chroma, "enabled3", 1) > 0.5 ? 1 : 0) : 1;
        var colors = rgb ? new[] { (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0) }
            .Where((_, index) => index switch { 0 => useRed, 1 => useGreen, _ => useBlue }).ToArray() :
            new[] { (Value(chroma, "red", 1), Value(chroma, "green", 1), Value(chroma, "blue", 1)),
                (Value(chroma, "red2", 1), Value(chroma, "green2", 1), Value(chroma, "blue2", 1)),
                (Value(chroma, "red3", 1), Value(chroma, "green3", 1), Value(chroma, "blue3", 1)) }
                .Where((_, index) => !multi || index == 0 ||
                    Value(chroma, index == 1 ? "enabled2" : "enabled3", 1) > 0.5).ToArray();
        var shared = !multi || Value(chroma, "shared", 1) > 0.5;
        var secondKey = multi && Value(chroma, "enabled2", 1) <= 0.5 ? "3" : "2";
        var tolerance = Value(chroma, "tolerance", 1);
        var softness = Value(chroma, "softness", 1);
        var spill = Value(chroma, "spill", 1);
        var result = new GpuFilterSettings(blur == null ? 0 : Value(blur, "radius", 24), keyCount > 0,
            keyCount > 0 ? colors[0].Item1 : 0, keyCount > 0 ? colors[0].Item2 : 1,
            keyCount > 0 ? colors[0].Item3 : 0, tolerance, softness, spill)
        {
            KeyCount = keyCount,
            Key2Red = keyCount > 1 ? colors[1].Item1 : 0,
            Key2Green = keyCount > 1 ? colors[1].Item2 : 0,
            Key2Blue = keyCount > 1 ? colors[1].Item3 : 0,
            Key3Red = keyCount > 2 ? colors[2].Item1 : 0,
            Key3Green = keyCount > 2 ? colors[2].Item2 : 0,
            Key3Blue = keyCount > 2 ? colors[2].Item3 : 0,
            Tolerance2 = shared ? tolerance : Value(chroma, "tolerance" + secondKey, 1),
            Tolerance3 = shared ? tolerance : Value(chroma, "tolerance3", 1),
            Softness2 = shared ? softness : Value(chroma, "softness" + secondKey, 1),
            Softness3 = shared ? softness : Value(chroma, "softness3", 1),
            Spill2 = shared ? spill : Value(chroma, "spill" + secondKey, 1),
            Spill3 = shared ? spill : Value(chroma, "spill3", 1)
        };
        return result;
    }
}
