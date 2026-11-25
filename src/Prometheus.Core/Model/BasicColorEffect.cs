using System;
using System.Linq;

namespace Prometheus.Core.Model;

public readonly record struct BasicColorSettings(double Brightness, double Contrast, double Saturation)
{
    public static BasicColorSettings Neutral => new(0, 1, 1);
}

public static class BasicColorEffect
{
    public const string Id = "prometheus.color.basic";

    public static EffectInstance? Find(Clip clip) => clip.Effects.FirstOrDefault(e => e.EffectId == Id);

    public static BasicColorSettings Read(Clip clip)
    {
        var effect = Find(clip);
        if (effect is not { Enabled: true }) return BasicColorSettings.Neutral;
        return ReadStored(clip);
    }

    public static BasicColorSettings ReadStored(Clip clip)
    {
        var effect = Find(clip);
        if (effect == null) return BasicColorSettings.Neutral;
        double Get(string name, double fallback) => effect.Parameters.TryGetValue(name, out var value) ? value.Number ?? fallback : fallback;
        return new BasicColorSettings(Get("brightness", 0), Get("contrast", 1), Get("saturation", 1));
    }

    public static void Set(Clip clip, bool enabled, BasicColorSettings settings)
    {
        var effect = Find(clip);
        if (effect == null)
        {
            if (!enabled) return;
            effect = new EffectInstance { EffectId = Id };
            clip.Effects.Add(effect);
        }
        effect.Enabled = enabled;
        effect.Parameters["brightness"] = new EffectValue { Number = Math.Clamp(settings.Brightness, -1, 1) };
        effect.Parameters["contrast"] = new EffectValue { Number = Math.Clamp(settings.Contrast, 0, 2) };
        effect.Parameters["saturation"] = new EffectValue { Number = Math.Clamp(settings.Saturation, 0, 3) };
    }
}
