using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>Maps the legacy audio catalog to the FFmpeg stream used by timeline preview.</summary>
public static class AudioEffectFilters
{
    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static string Linear(double db) => N(Math.Pow(10, db / 20));

    public static string Build(IEnumerable<EffectInstance> effects) => string.Concat(effects
        .Where(effect => effect.Enabled && AudioEffects.Find(effect.EffectId) != null)
        .Select(Filter));

    private static string Filter(EffectInstance effect)
    {
        var definition = AudioEffects.Find(effect.EffectId)!;
        double V(string key) => AudioEffects.Read(effect, definition.Parameters.First(p => p.Key == key));
        return effect.EffectId switch
        {
            AudioEffects.HighPassId => $",highpass=f={N(V("frequency"))}",
            AudioEffects.LowPassId => $",lowpass=f={N(V("frequency"))}",
            AudioEffects.EqualizerId => $",bass=g={N(V("low_db"))},equalizer=f=1000:t=q:w=1:g={N(V("mid_db"))},treble=g={N(V("high_db"))}",
            AudioEffects.CompressorId => $",acompressor=threshold={Linear(V("threshold_db"))}:ratio={N(V("ratio"))}:attack={N(V("attack_ms"))}:release={N(V("release_ms"))}:makeup={Linear(V("makeup_db"))}",
            AudioEffects.LimiterId => $",alimiter=limit={Linear(V("ceiling_db"))}:level=false:latency=true",
            AudioEffects.DeEsserId => $",deesser=i={N(V("intensity"))}:m=0.6:f=0.5",
            AudioEffects.GateId => $",agate=threshold={Linear(V("threshold_db"))}:ratio={N(V("ratio"))}",
            AudioEffects.VoiceCleanId => $",highpass=f=75,afftdn=nr={N(3 + V("strength") * 0.17)}:nf=-50,adeclick=t={N(4 - V("strength") * 0.03)},acompressor=threshold=0.1:ratio=2.5:attack=15:release=180:makeup=1.2,alimiter=limit=0.84:level=false:latency=true",
            AudioEffects.PitchId => $",rubberband=tempo=1:pitch={N(Math.Pow(2, V("semitones") / 12))}:formant=preserved",
            AudioEffects.EchoId => $",aecho=in_gain=0.8:out_gain=0.6:delays={N(V("delay_ms"))}:decays={N(V("decay"))}",
            AudioEffects.ChorusId => $",chorus=in_gain=0.65:out_gain=0.7:delays=40:decays=0.4:speeds={N(V("speed"))}:depths={N(V("depth"))}",
            AudioEffects.TremoloId => $",tremolo=f={N(V("frequency"))}:d={N(V("depth"))}",
            _ => ""
        };
    }
}
