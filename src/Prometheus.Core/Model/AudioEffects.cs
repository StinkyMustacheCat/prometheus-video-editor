using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus.Core.Model;

public sealed record AudioEffectParameter(string Key, string Label, double Default, double Minimum, double Maximum, string Unit = "");
public sealed record AudioEffectDefinition(string Id, string Name, string Family, string Description,
    IReadOnlyList<AudioEffectParameter> Parameters);

public static class AudioEffects
{
    public const string HighPassId = "prometheus.audio.highpass";
    public const string LowPassId = "prometheus.audio.lowpass";
    public const string CompressorId = "prometheus.audio.compressor";
    public const string EqualizerId = "prometheus.audio.eq3";
    public const string LimiterId = "prometheus.audio.limiter";
    public const string DeEsserId = "prometheus.audio.deesser";
    public const string GateId = "prometheus.audio.gate";
    public const string PitchId = "prometheus.audio.pitch";
    public const string EchoId = "prometheus.audio.echo";
    public const string ChorusId = "prometheus.audio.chorus";
    public const string TremoloId = "prometheus.audio.tremolo";
    public const string VoiceCleanId = "prometheus.audio.voice-clean";

    private static AudioEffectParameter P(string key, string name, double value, double min, double max, string unit = "") =>
        new(key, name, value, min, max, unit);

    public static IReadOnlyList<AudioEffectDefinition> All { get; } =
    [
        new(HighPassId, "Corte de graves", "Filtros", "Paso alto", [P("frequency", "Frecuencia", 80, 20, 2000, " Hz")]),
        new(LowPassId, "Corte de agudos", "Filtros", "Paso bajo", [P("frequency", "Frecuencia", 10000, 100, 20000, " Hz")]),
        new(EqualizerId, "Ecualizador de tres bandas", "Filtros", "Graves, medios y agudos",
            [P("low_db", "Graves", 0, -18, 18, " dB"), P("mid_db", "Medios", 0, -18, 18, " dB"),
                P("high_db", "Agudos", 0, -18, 18, " dB")]),
        new(CompressorId, "Compresor", "Dinámica", "Control de rango dinámico",
            [P("threshold_db", "Umbral", -18, -60, -1, " dB"), P("ratio", "Relación", 3, 1, 20),
                P("attack_ms", "Ataque", 20, 0.01, 2000, " ms"), P("release_ms", "Salida", 250, 0.01, 9000, " ms"),
                P("makeup_db", "Compensación", 0, 0, 24, " dB")]),
        new(LimiterId, "Limitador", "Dinámica", "Limitar picos", [P("ceiling_db", "Techo", -1, -24, 0, " dB")]),
        new(DeEsserId, "Reducción de sibilancias", "Voz", "Atenuar las eses", [P("intensity", "Intensidad", 0.5, 0, 1)]),
        new(GateId, "Puerta de ruido", "Dinámica", "Reducir ruido entre frases",
            [P("threshold_db", "Umbral", -38, -60, -6, " dB"), P("ratio", "Relación", 3, 1, 20)]),
        new(VoiceCleanId, "Limpiar y nivelar voz", "Voz", "Filtro, limpieza y nivelado",
            [P("strength", "Intensidad", 35, 0, 100, " %")]),
        new(PitchId, "Tono sin cambiar duración", "Creativos", "Cambiar altura de voz o música",
            [P("semitones", "Semitonos", 0, -12, 12)]),
        new(EchoId, "Eco", "Creativos", "Repeticiones con decaimiento",
            [P("delay_ms", "Retraso", 350, 10, 1500, " ms"), P("decay", "Decaimiento", 0.35, 0, 0.9)]),
        new(ChorusId, "Coro", "Creativos", "Modulación y duplicación",
            [P("depth", "Profundidad", 1.5, 0.1, 5), P("speed", "Velocidad", 0.4, 0.1, 5)]),
        new(TremoloId, "Trémolo", "Creativos", "Oscilación de amplitud",
            [P("frequency", "Frecuencia", 5, 0.1, 20, " Hz"), P("depth", "Profundidad", 0.5, 0, 1)])
    ];

    public static AudioEffectDefinition? Find(string id) => All.FirstOrDefault(effect => effect.Id == id);

    public static void Add(Clip clip, string id)
        => Add(clip.Effects, id);

    public static void Add(Track track, string id)
        => Add(track.Effects, id);

    private static void Add(List<EffectInstance> effects, string id)
    {
        var definition = Find(id) ?? throw new ArgumentException("Efecto de audio no disponible.", nameof(id));
        var existing = effects.FirstOrDefault(effect => effect.EffectId == id);
        if (existing != null) { existing.Enabled = true; return; }
        var instance = new EffectInstance { EffectId = id };
        foreach (var parameter in definition.Parameters)
            Set(instance, parameter.Key, parameter.Default);
        effects.Add(instance);
    }

    public static void Set(EffectInstance effect, string key, double value) =>
        effect.Parameters[key] = new EffectValue { Kind = "number", Number = value };

    public static double Read(EffectInstance effect, AudioEffectParameter parameter)
    {
        if (!effect.Parameters.TryGetValue(parameter.Key, out var value) ||
            value.Number is not { } number || !double.IsFinite(number)) return parameter.Default;
        return Math.Clamp(number, parameter.Minimum, parameter.Maximum);
    }
}
