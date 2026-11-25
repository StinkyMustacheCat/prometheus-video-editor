using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public sealed class ShapeMaskKeyframe
{
    [JsonPropertyName("source_seconds")]
    public double SourceSeconds { get; set; }

    [JsonPropertyName("shape")]
    public ShapeMask Shape { get; set; } = new();
}

/// <summary>Interpolate a vector mask at source time; topology changes hold the earlier shape.</summary>
public static class ShapeMaskAnimation
{
    public static ShapeMask? Evaluate(ClipMaskLayer layer, double sourceSeconds, double fps)
    {
        var keys = layer.ShapeKeyframes.OrderBy(key => key.SourceSeconds).ToArray();
        if (keys.Length == 0) return layer.Shape;
        if (!double.IsFinite(sourceSeconds)) return keys[0].Shape;
        sourceSeconds = Math.Round(sourceSeconds * Math.Max(1, fps)) /
            Math.Max(1, fps);
        if (sourceSeconds <= keys[0].SourceSeconds) return keys[0].Shape;
        if (sourceSeconds >= keys[^1].SourceSeconds) return keys[^1].Shape;
        var next = Array.FindIndex(keys, key => key.SourceSeconds > sourceSeconds);
        var a = keys[next - 1]; var b = keys[next];
        if (a.Shape.Kind != b.Shape.Kind ||
            a.Shape.Points.Count != b.Shape.Points.Count) return a.Shape;
        var ratio = Math.Clamp((sourceSeconds - a.SourceSeconds) /
            Math.Max(0.000001, b.SourceSeconds - a.SourceSeconds), 0, 1);
        return new ShapeMask { Id = layer.Id + ":" +
            Math.Round(sourceSeconds * Math.Max(1, fps)), Kind = a.Shape.Kind,
            Points = a.Shape.Points.Zip(b.Shape.Points, (left, right) =>
                new ShapeMaskPoint(left.X + (right.X - left.X) * ratio,
                    left.Y + (right.Y - left.Y) * ratio)).ToList() };
    }

    public static void Upsert(ClipMaskLayer layer, double sourceSeconds, double fps,
        ShapeMask shape)
    {
        shape.Validate();
        if (!double.IsFinite(sourceSeconds) || sourceSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSeconds));
        var tolerance = 0.5 / Math.Max(1, fps) + 0.000001;
        layer.ShapeKeyframes.RemoveAll(key =>
            Math.Abs(key.SourceSeconds - sourceSeconds) <= tolerance);
        layer.ShapeKeyframes.Add(new ShapeMaskKeyframe {
            SourceSeconds = sourceSeconds, Shape = Clone(shape) });
        layer.ShapeKeyframes.Sort((left, right) =>
            left.SourceSeconds.CompareTo(right.SourceSeconds));
    }

    public static ShapeMask Clone(ShapeMask shape) => new()
    {
        Kind = shape.Kind,
        Points = shape.Points.Select(point => new ShapeMaskPoint(point.X, point.Y)).ToList()
    };
}
