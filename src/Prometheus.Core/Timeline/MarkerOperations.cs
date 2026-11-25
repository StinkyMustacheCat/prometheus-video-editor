using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class MarkerOperations
{
    public static double FrameTime(Sequence sequence, double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        var fps = Math.Max(1, sequence.Fps.Seconds);
        return Math.Round(Math.Max(0, seconds) * fps) / fps;
    }

    public static SequenceMarker Add(Sequence sequence, double seconds, string? name = null)
    {
        var marker = new SequenceMarker { Time = Rational.Time(FrameTime(sequence, seconds)),
            Name = string.IsNullOrWhiteSpace(name) ? $"Marcador {sequence.Markers.Count + 1}" : name.Trim() };
        sequence.Markers.Add(marker);
        return marker;
    }

    public static void Move(Sequence sequence, string id, double seconds) =>
        sequence.Markers.Single(m => m.Id == id).Time = Rational.Time(FrameTime(sequence, seconds));

    public static void Rename(Sequence sequence, string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El marcador necesita un nombre");
        sequence.Markers.Single(m => m.Id == id).Name = name.Trim();
    }

    public static void SetIn(Sequence sequence, double seconds)
    {
        sequence.MarkInSeconds = FrameTime(sequence, seconds);
        if (sequence.MarkOutSeconds <= sequence.MarkInSeconds) sequence.MarkOutSeconds = null;
    }

    public static void SetOut(Sequence sequence, double seconds)
    {
        sequence.MarkOutSeconds = FrameTime(sequence, seconds);
        if (sequence.MarkInSeconds >= sequence.MarkOutSeconds) sequence.MarkInSeconds = null;
    }

    public static void ClearRange(Sequence sequence)
    {
        sequence.MarkInSeconds = null;
        sequence.MarkOutSeconds = null;
    }
}
