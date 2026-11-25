using System;
using System.Linq;

namespace Prometheus.Core.Model;

public readonly record struct ExposureSettings(double Stops, double Black)
{
    public static ExposureSettings Neutral => new(0, 0);
}

public static class ExposureEffect
{
    public const string Id = "prometheus.color.exposure";

    public static EffectInstance? Find(Clip clip) => clip.Effects.FirstOrDefault(e => e.EffectId == Id);

    public static ExposureSettings Read(Clip clip) => Find(clip) is { Enabled: true }
        ? ReadStored(clip) : ExposureSettings.Neutral;

    public static ExposureSettings ReadStored(Clip clip)
    {
        var effect = Find(clip);
        if (effect == null) return ExposureSettings.Neutral;
        double Get(string key) => effect.Parameters.TryGetValue(key, out var value) ? value.Number ?? 0 : 0;
        return new ExposureSettings(Get("stops"), Get("black"));
    }

    public static void Set(Clip clip, bool enabled, ExposureSettings settings)
    {
        var effect = Find(clip);
        if (effect == null)
        {
            if (!enabled) return;
            effect = new EffectInstance { EffectId = Id };
            clip.Effects.Add(effect);
        }
        effect.Enabled = enabled;
        effect.Parameters["stops"] = new EffectValue { Number = Math.Clamp(settings.Stops, -3, 3) };
        effect.Parameters["black"] = new EffectValue { Number = Math.Clamp(settings.Black, -0.5, 0.5) };
    }
}
