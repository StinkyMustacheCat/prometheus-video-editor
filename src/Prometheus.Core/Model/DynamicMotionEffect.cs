using System;
using System.Linq;

namespace Prometheus.Core.Model;

/// <summary>Duration-relative motion. Endpoints are clip-local and require no keyframe timeline.</summary>
public static class DynamicMotionEffect
{
    public const string ZoomInId = "prometheus.motion.zoom-in-dynamic";
    public const string ZoomOutId = "prometheus.motion.zoom-out-dynamic";
    public const string PanId = "prometheus.motion.pan-dynamic";
    public const string JourneyId = "prometheus.motion.journey";

    public static bool Supports(string id) => id is ZoomInId or ZoomOutId or PanId or JourneyId;

    public static void Add(Clip clip, string id)
    {
        if (!Supports(id)) throw new ArgumentException("Movimiento no disponible.", nameof(id));
        var existing = clip.Effects.FirstOrDefault(e => e.EffectId == id);
        if (existing != null) { existing.Enabled = true; return; }
        var effect = new EffectInstance { EffectId = id };
        if (id is ZoomInId or ZoomOutId) Number(effect, "zoom_percent", 15);
        else if (id == PanId)
        {
            Number(effect, "distance_percent", 15);
            Text(effect, "direction", "left-to-right");
        }
        else
        {
            Number(effect, "start_x", 0); Number(effect, "start_y", 0); Number(effect, "start_scale", 100);
            Number(effect, "end_x", 0); Number(effect, "end_y", 0); Number(effect, "end_scale", 115);
        }
        Text(effect, "motion", "ease-in-out");
        clip.Effects.Add(effect);
    }

    public static void Number(EffectInstance effect, string key, double value) =>
        effect.Parameters[key] = new EffectValue { Kind = "number", Number = value };

    public static void Text(EffectInstance effect, string key, string value) =>
        effect.Parameters[key] = new EffectValue { Kind = "text", Text = value };

    public static double ReadNumber(EffectInstance effect, string key, double fallback) =>
        effect.Parameters.TryGetValue(key, out var value) && value.Number is { } number && double.IsFinite(number)
            ? number : fallback;

    public static string ReadText(EffectInstance effect, string key, string fallback) =>
        effect.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.Text)
            ? value.Text! : fallback;

    public static ClipVisualState Evaluate(Clip clip, double localTimelineSeconds)
    {
        var evaluatedEffects = KeyframeAnimation.EvaluateEffects(clip, localTimelineSeconds).Effects;
        var baseState = clip.Visual ?? new ClipVisualState();
        var state = new ClipVisualState
        {
            Framing = baseState.Framing, PositionX = baseState.PositionX, PositionY = baseState.PositionY,
            ScaleX = baseState.ScaleX, ScaleY = baseState.ScaleY,
            ScaleLinked = baseState.ScaleLinked, FlipX = baseState.FlipX,
            FlipY = baseState.FlipY, AnchorX = baseState.AnchorX,
            AnchorY = baseState.AnchorY, RotationDegrees = baseState.RotationDegrees,
            Opacity = baseState.Opacity, BlendMode = baseState.BlendMode
        };
        KeyframeAnimation.ApplyVisual(clip, localTimelineSeconds, state);
        var duration = Math.Max(1e-6, clip.Duration.Seconds);
        var progress = Math.Clamp((localTimelineSeconds - clip.TimelineStart.Seconds) / duration, 0, 1);
        foreach (var effect in evaluatedEffects.Where(e => e.Enabled && Supports(e.EffectId)))
        {
            var t = Ease(progress, ReadText(effect, "motion", "ease-in-out"));
            if (effect.EffectId is ZoomInId or ZoomOutId)
            {
                var amount = Math.Clamp(ReadNumber(effect, "zoom_percent", 15), 1, 100) / 100;
                var factor = effect.EffectId == ZoomInId ? 1 + amount * t : 1 + amount * (1 - t);
                state.ScaleX *= factor;
                state.ScaleY *= factor;
            }
            else if (effect.EffectId == PanId)
            {
                var distance = Math.Clamp(ReadNumber(effect, "distance_percent", 15), 1, 100) / 100;
                var (dx, dy) = Direction(ReadText(effect, "direction", "left-to-right"));
                state.PositionX += dx * distance * (t - 0.5);
                state.PositionY += dy * distance * (t - 0.5);
            }
            else
            {
                var startX = Math.Clamp(ReadNumber(effect, "start_x", 0), -100, 100) / 100;
                var startY = Math.Clamp(ReadNumber(effect, "start_y", 0), -100, 100) / 100;
                var endX = Math.Clamp(ReadNumber(effect, "end_x", 0), -100, 100) / 100;
                var endY = Math.Clamp(ReadNumber(effect, "end_y", 0), -100, 100) / 100;
                var startScale = Math.Clamp(ReadNumber(effect, "start_scale", 100), 1, 400) / 100;
                var endScale = Math.Clamp(ReadNumber(effect, "end_scale", 115), 1, 400) / 100;
                state.PositionX += startX + (endX - startX) * t;
                state.PositionY += startY + (endY - startY) * t;
                var factor = startScale + (endScale - startScale) * t;
                state.ScaleX *= factor;
                state.ScaleY *= factor;
            }
        }
        return state;
    }

    public static double Ease(double t, string mode) => mode switch
    {
        "linear" or "Lineal" => t,
        "ease-in" or "Acelerado al final" => t * t,
        "ease-out" => 1 - (1 - t) * (1 - t),
        _ => t * t * (3 - 2 * t)
    };

    private static (double X, double Y) Direction(string value) => value switch
    {
        "right-to-left" or "Derecha → izquierda" => (-1, 0),
        "top-to-bottom" or "Arriba → abajo" => (0, 1),
        "bottom-to-top" or "Abajo → arriba" => (0, -1),
        "down-right" or "Diagonal ↘" => (0.70710678, 0.70710678),
        "up-right" or "Diagonal ↗" => (0.70710678, -0.70710678),
        "down-left" or "Diagonal ↙" => (-0.70710678, 0.70710678),
        "up-left" or "Diagonal ↖" => (-0.70710678, -0.70710678),
        _ => (1, 0)
    };
}
