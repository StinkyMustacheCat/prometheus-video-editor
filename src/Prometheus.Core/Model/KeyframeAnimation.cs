using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public sealed class KeyframeTrack
{
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public List<KeyframePoint> Points { get; set; } = new();
}

public sealed class KeyframePoint
{
    [JsonPropertyName("time_seconds")]
    public double TimeSeconds { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("interpolation")]
    public string Interpolation { get; set; } = "linear";

    [JsonPropertyName("bezier_x1")]
    public double BezierX1 { get; set; } = 0.42;
    [JsonPropertyName("bezier_y1")]
    public double BezierY1 { get; set; }
    [JsonPropertyName("bezier_x2")]
    public double BezierX2 { get; set; } = 0.58;
    [JsonPropertyName("bezier_y2")]
    public double BezierY2 { get; set; } = 1;
}

/// <summary>Clip-local parameter animation. Evaluation does not mutate the saved clip.</summary>
public static class KeyframeAnimation
{
    public static IReadOnlyList<string> Interpolations { get; } =
        Array.AsReadOnly(new[] { "linear", "hold", "ease", "ease-in", "ease-out", "bezier", "smooth" });

    public static readonly IReadOnlyDictionary<string, string> VisualTargets =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["visual.position_x"] = "Posición X", ["visual.position_y"] = "Posición Y",
            ["visual.scale_x"] = "Escala X", ["visual.scale_y"] = "Escala Y",
            ["visual.rotation"] = "Rotación", ["visual.opacity"] = "Opacidad",
            ["visual.anchor_x"] = "Anclaje X", ["visual.anchor_y"] = "Anclaje Y"
        };

    public static string EffectTarget(EffectInstance effect, string parameter) =>
        "effect." + effect.Id + "." + parameter;

    public static Clip EvaluateEffects(Clip clip, double absoluteSeconds)
    {
        var animated = clip.KeyframeTracks.Where(track => track.Target.StartsWith("effect.",
            StringComparison.Ordinal) && track.Points.Count > 0).ToArray();
        if (animated.Length == 0) return clip;
        var result = clip.ShallowCopy();
        result.KeyframeTracks = clip.KeyframeTracks.Where(track => !track.Target.StartsWith("effect.",
            StringComparison.Ordinal)).ToList();
        result.Effects = clip.Effects.Select(effect => new EffectInstance
        {
            Id = effect.Id, EffectId = effect.EffectId, Enabled = effect.Enabled,
            Parameters = new Dictionary<string, EffectValue>(effect.Parameters)
        }).ToList();
        foreach (var track in animated)
        {
            var remainder = track.Target[7..];
            var separator = remainder.IndexOf('.');
            if (separator <= 0) continue;
            var instanceId = remainder[..separator];
            var parameter = remainder[(separator + 1)..];
            var effect = result.Effects.FirstOrDefault(item => item.Id == instanceId);
            if (effect == null) continue;
            var original = clip.Effects.First(item => item.Id == instanceId);
            var fallback = original.Parameters.TryGetValue(parameter, out var stored) ? stored.Number ?? 0 : 0;
            var value = Evaluate(clip, track.Target, absoluteSeconds, fallback);
            effect.Parameters[parameter] = new EffectValue { Kind = "number", Number = value };
        }
        return result;
    }

    public static KeyframeTrack? Find(Clip clip, string target) =>
        clip.KeyframeTracks.FirstOrDefault(track => track.Target == target);

    public static KeyframeTrack Enable(Clip clip, string target, double absoluteSeconds, double value)
    {
        var track = Find(clip, target);
        if (track != null) return track;
        track = new KeyframeTrack { Target = target };
        clip.KeyframeTracks.Add(track);
        Set(clip, target, absoluteSeconds, value);
        return track;
    }

    public static void Disable(Clip clip, string target) =>
        clip.KeyframeTracks.RemoveAll(track => track.Target == target);

    public static void Set(Clip clip, string target, double absoluteSeconds, double value,
        string? interpolation = null)
    {
        if (!double.IsFinite(absoluteSeconds) || !double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var track = Find(clip, target) ?? new KeyframeTrack { Target = target };
        if (!clip.KeyframeTracks.Contains(track)) clip.KeyframeTracks.Add(track);
        var local = Math.Clamp(absoluteSeconds - clip.TimelineStart.Seconds, 0,
            Math.Max(0, clip.Duration.Seconds));
        var point = track.Points.FirstOrDefault(point => Math.Abs(point.TimeSeconds - local) < 1e-6);
        if (point == null) track.Points.Add(new KeyframePoint { TimeSeconds = local,
            Value = value, Interpolation = interpolation ?? "linear" });
        else { point.Value = value; if (interpolation != null) point.Interpolation = interpolation; }
        track.Points.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
    }

    public static bool Remove(Clip clip, string target, double absoluteSeconds)
    {
        var track = Find(clip, target);
        if (track == null) return false;
        var local = absoluteSeconds - clip.TimelineStart.Seconds;
        var removed = track.Points.RemoveAll(point => Math.Abs(point.TimeSeconds - local) < 1e-6) > 0;
        return removed;
    }

    public static bool Move(Clip clip, string target, double fromAbsoluteSeconds, double toAbsoluteSeconds)
    {
        var point = Find(clip, target)?.Points.FirstOrDefault(point =>
            Math.Abs(point.TimeSeconds - (fromAbsoluteSeconds - clip.TimelineStart.Seconds)) < 1e-6);
        if (point == null) return false;
        var value = point.Value;
        var interpolation = point.Interpolation;
        var x1 = point.BezierX1; var y1 = point.BezierY1;
        var x2 = point.BezierX2; var y2 = point.BezierY2;
        Remove(clip, target, fromAbsoluteSeconds);
        Set(clip, target, toAbsoluteSeconds, value, interpolation);
        var moved = Find(clip, target)?.Points.FirstOrDefault(item =>
            Math.Abs(item.TimeSeconds - (toAbsoluteSeconds - clip.TimelineStart.Seconds)) < 1e-6);
        if (moved != null)
        {
            moved.BezierX1 = x1; moved.BezierY1 = y1;
            moved.BezierX2 = x2; moved.BezierY2 = y2;
        }
        return true;
    }

    public static bool SetBezier(Clip clip, string target, double absoluteSeconds,
        double x1, double y1, double x2, double y2)
    {
        var point = Find(clip, target)?.Points.FirstOrDefault(item =>
            Math.Abs(item.TimeSeconds - (absoluteSeconds - clip.TimelineStart.Seconds)) < 1e-6);
        if (point == null || new[] { x1, y1, x2, y2 }.Any(value => !double.IsFinite(value))) return false;
        point.BezierX1 = Math.Clamp(x1, 0, 1);
        point.BezierY1 = Math.Clamp(y1, -2, 3);
        point.BezierX2 = Math.Clamp(x2, 0, 1);
        point.BezierY2 = Math.Clamp(y2, -2, 3);
        point.Interpolation = "bezier";
        return true;
    }

    /// <summary>Keeps animation at its original timeline positions when the clip's left edge is trimmed.</summary>
    public static void ShiftForLeftTrim(Clip clip, double newStartMinusOldStart)
    {
        if (Math.Abs(newStartMinusOldStart) < 1e-9) return;
        foreach (var point in clip.KeyframeTracks.SelectMany(track => track.Points))
            point.TimeSeconds -= newStartMinusOldStart;
    }

    public static void ScaleForDurationChange(Clip clip, double newDurationOverOldDuration)
    {
        if (!double.IsFinite(newDurationOverOldDuration) || newDurationOverOldDuration <= 0) return;
        foreach (var point in clip.KeyframeTracks.SelectMany(track => track.Points))
            point.TimeSeconds *= newDurationOverOldDuration;
    }

    public static void RemapEffectInstances(Clip clip, IReadOnlyDictionary<string, string> ids)
    {
        if (clip.SamMaskTarget?.StartsWith(SamMaskTarget.EffectPrefix, StringComparison.Ordinal) == true &&
            ids.TryGetValue(clip.SamMaskTarget[SamMaskTarget.EffectPrefix.Length..], out var targetId))
            clip.SamMaskTarget = SamMaskTarget.EffectPrefix + targetId;
        foreach (var layer in clip.MaskLayers)
            if (layer.Target.StartsWith(SamMaskTarget.EffectPrefix, StringComparison.Ordinal) &&
                ids.TryGetValue(layer.Target[SamMaskTarget.EffectPrefix.Length..], out var layerTargetId))
                layer.Target = SamMaskTarget.EffectPrefix + layerTargetId;
        foreach (var track in clip.KeyframeTracks)
        foreach (var (oldId, newId) in ids)
            if (track.Target.StartsWith("effect." + oldId + ".", StringComparison.Ordinal))
            { track.Target = "effect." + newId + track.Target[(7 + oldId.Length)..]; break; }
    }

    public static void RemoveForEffect(Clip clip, string instanceId) =>
        clip.KeyframeTracks.RemoveAll(track => track.Target.StartsWith(
            "effect." + instanceId + ".", StringComparison.Ordinal));

    public static double Evaluate(Clip clip, string target, double absoluteSeconds, double fallback)
    {
        var points = Find(clip, target)?.Points;
        if (points == null || points.Count == 0) return fallback;
        var local = absoluteSeconds - clip.TimelineStart.Seconds;
        var sorted = points.OrderBy(point => point.TimeSeconds).ToArray();
        if (local <= sorted[0].TimeSeconds) return sorted[0].Value;
        if (local >= sorted[^1].TimeSeconds) return sorted[^1].Value;
        for (var i = 0; i < sorted.Length - 1; i++)
        {
            var left = sorted[i]; var right = sorted[i + 1];
            if (local > right.TimeSeconds) continue;
            var t = Math.Clamp((local - left.TimeSeconds) /
                Math.Max(1e-9, right.TimeSeconds - left.TimeSeconds), 0, 1);
            t = left.Interpolation switch
            {
                "hold" => 0,
                "ease" or "smooth" => t * t * (3 - 2 * t),
                "ease-in" => CubicBezier(t, 0.42, 0, 1, 1),
                "ease-out" => CubicBezier(t, 0, 0, 0.58, 1),
                "bezier" => CubicBezier(t, left.BezierX1, left.BezierY1,
                    left.BezierX2, left.BezierY2),
                _ => t
            };
            return left.Value + (right.Value - left.Value) * t;
        }
        return sorted[^1].Value;
    }

    private static double CubicBezier(double progress, double x1, double y1, double x2, double y2)
    {
        static double Curve(double t, double first, double second)
        {
            var inverse = 1 - t;
            return 3 * inverse * inverse * t * first +
                3 * inverse * t * t * second + t * t * t;
        }
        var low = 0.0; var high = 1.0;
        for (var iteration = 0; iteration < 28; iteration++)
        {
            var middle = (low + high) / 2;
            if (Curve(middle, x1, x2) < progress) low = middle;
            else high = middle;
        }
        return Curve((low + high) / 2, y1, y2);
    }

    public static void ApplyVisual(Clip clip, double absoluteSeconds, ClipVisualState state)
    {
        state.PositionX = Evaluate(clip, "visual.position_x", absoluteSeconds, state.PositionX);
        state.PositionY = Evaluate(clip, "visual.position_y", absoluteSeconds, state.PositionY);
        state.ScaleX = Evaluate(clip, "visual.scale_x", absoluteSeconds, state.ScaleX);
        state.ScaleY = Evaluate(clip, "visual.scale_y", absoluteSeconds, state.ScaleY);
        state.RotationDegrees = Evaluate(clip, "visual.rotation", absoluteSeconds, state.RotationDegrees);
        state.Opacity = Evaluate(clip, "visual.opacity", absoluteSeconds, state.Opacity);
        state.AnchorX = Evaluate(clip, "visual.anchor_x", absoluteSeconds, state.AnchorX);
        state.AnchorY = Evaluate(clip, "visual.anchor_y", absoluteSeconds, state.AnchorY);
    }
}
