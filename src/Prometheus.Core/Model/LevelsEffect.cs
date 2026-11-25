using System;
using System.Linq;

namespace Prometheus.Core.Model;

public readonly record struct LevelsSettings(double Black, double White, double Gamma)
{
    public static LevelsSettings Neutral => new(0, 1, 1);
}

public static class LevelsEffect
{
    public const string Id = "prometheus.color.levels";

    public static EffectInstance? Find(Clip clip) => clip.Effects.FirstOrDefault(e => e.EffectId == Id);

    public static LevelsSettings Read(Clip clip) => Find(clip) is { Enabled: true }
        ? ReadStored(clip) : LevelsSettings.Neutral;

    public static LevelsSettings ReadStored(Clip clip)
    {
        var effect = Find(clip);
        if (effect == null) return LevelsSettings.Neutral;
        double Get(string key, double fallback) => effect.Parameters.TryGetValue(key, out var value)
            ? value.Number ?? fallback : fallback;
        return new LevelsSettings(Get("black", 0), Get("white", 1), Get("gamma", 1));
    }

    public static void Set(Clip clip, bool enabled, LevelsSettings settings)
    {
        var effect = Find(clip);
        if (effect == null)
        {
            if (!enabled) return;
            effect = new EffectInstance { EffectId = Id };
            clip.Effects.Add(effect);
        }
        effect.Enabled = enabled;
        var black = Math.Clamp(settings.Black, -0.5, 0.9);
        var white = Math.Clamp(Math.Max(black + 0.01, settings.White), 0.1, 1.5);
        effect.Parameters["black"] = new EffectValue { Number = black };
        effect.Parameters["white"] = new EffectValue { Number = white };
        effect.Parameters["gamma"] = new EffectValue { Number = Math.Clamp(settings.Gamma, 0.1, 5) };
    }
}

public static class MonochromeEffect
{
    public const string Id = "prometheus.color.monochrome";

    public static bool IsEnabled(Clip clip) => clip.Effects.Any(e => e.EffectId == Id && e.Enabled);

    public static void Set(Clip clip, bool enabled)
    {
        var effect = clip.Effects.FirstOrDefault(e => e.EffectId == Id);
        if (effect == null)
        {
            if (!enabled) return;
            effect = new EffectInstance { EffectId = Id };
            clip.Effects.Add(effect);
        }
        effect.Enabled = enabled;
    }
}
