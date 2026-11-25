using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public readonly struct SnapResult
{
    public bool IsSnapped { get; }
    public double SnappedTime { get; }
    public double OriginalTime { get; }
    public string Reason { get; }

    public SnapResult(bool isSnapped, double snappedTime, double originalTime, string reason = "")
    {
        IsSnapped = isSnapped;
        SnappedTime = snappedTime;
        OriginalTime = originalTime;
        Reason = reason;
    }
}

public static class SnappingEngine
{
    public const double DefaultTolerancePixels = 8.0;

    public static SnapResult Snap(
        double targetTimeSeconds,
        Sequence sequence,
        double playheadSeconds,
        double zoomPixelsPerSecond,
        double tolerancePixels = DefaultTolerancePixels,
        IEnumerable<string>? excludedClipIds = null,
        Func<Clip, bool>? includeClip = null)
    {
        if (zoomPixelsPerSecond <= 0)
        {
            return new SnapResult(false, targetTimeSeconds, targetTimeSeconds);
        }

        double thresholdSeconds = tolerancePixels / zoomPixelsPerSecond;
        double bestTime = targetTimeSeconds;
        double minDelta = double.MaxValue;
        string bestReason = string.Empty;

        var excluded = excludedClipIds != null
            ? new HashSet<string>(excludedClipIds, StringComparer.OrdinalIgnoreCase)
            : null;

        void TestPoint(double snapPoint, string reason)
        {
            double delta = Math.Abs(targetTimeSeconds - snapPoint);
            if (delta <= thresholdSeconds && delta < minDelta)
            {
                minDelta = delta;
                bestTime = snapPoint;
                bestReason = reason;
            }
        }

        // 1. Origen de secuencia
        TestPoint(0.0, "Inicio de secuencia (00:00:00:00)");

        // 2. Cabezal de reproducción
        if (playheadSeconds >= 0)
        {
            TestPoint(playheadSeconds, "Cabezal de reproducción");
        }

        // 3. Bordes de clips en pistas no bloqueadas
        var lockedTracks = new HashSet<string>(
            sequence.Tracks.Where(t => t.Locked).Select(t => t.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var clip in sequence.Clips)
        {
            if (includeClip != null && !includeClip(clip)) continue;
            if (excluded != null && excluded.Contains(clip.Id))
                continue;

            if (lockedTracks.Contains(clip.TrackId))
                continue;

            TestPoint(clip.TimelineStart.Seconds, $"Inicio de clip: {clip.Name}");
            TestPoint(clip.EndSeconds, $"Fin de clip: {clip.Name}");
        }

        if (minDelta <= thresholdSeconds)
        {
            return new SnapResult(true, bestTime, targetTimeSeconds, bestReason);
        }

        return new SnapResult(false, targetTimeSeconds, targetTimeSeconds);
    }

    /// <summary>
    /// Snapping bidireccional para clips completos: evalúa tanto la cabeza (inicio)
    /// como la cola (fin) del clip contra los puntos de referencia magnéticos (adelante y atrás).
    /// </summary>
    public static SnapResult SnapClip(
        double proposedStartSeconds,
        double durationSeconds,
        Sequence sequence,
        double playheadSeconds,
        double zoomPixelsPerSecond,
        double tolerancePixels = DefaultTolerancePixels,
        IEnumerable<string>? excludedClipIds = null,
        Func<Clip, bool>? includeClip = null)
    {
        if (zoomPixelsPerSecond <= 0)
        {
            return new SnapResult(false, proposedStartSeconds, proposedStartSeconds);
        }

        double thresholdSeconds = tolerancePixels / zoomPixelsPerSecond;
        double bestStart = proposedStartSeconds;
        double minDelta = double.MaxValue;
        string bestReason = string.Empty;

        var excluded = excludedClipIds != null
            ? new HashSet<string>(excludedClipIds, StringComparer.OrdinalIgnoreCase)
            : null;

        var snapPoints = new List<(double Time, string Label)>
        {
            (0.0, "Inicio de secuencia (00:00:00:00)")
        };

        if (playheadSeconds >= 0)
        {
            snapPoints.Add((playheadSeconds, "Cabezal de reproducción"));
        }

        var lockedTracks = new HashSet<string>(
            sequence.Tracks.Where(t => t.Locked).Select(t => t.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var clip in sequence.Clips)
        {
            if (includeClip != null && !includeClip(clip)) continue;
            if (excluded != null && excluded.Contains(clip.Id))
                continue;

            if (lockedTracks.Contains(clip.TrackId))
                continue;

            snapPoints.Add((clip.TimelineStart.Seconds, $"Inicio de clip: {clip.Name}"));
            snapPoints.Add((clip.EndSeconds, $"Fin de clip: {clip.Name}"));
        }

        double proposedTail = proposedStartSeconds + durationSeconds;

        foreach (var pt in snapPoints)
        {
            // 1. Probar cabeza del clip con punto de referencia (snapping hacia adelante o hacia atrás)
            double headDelta = Math.Abs(proposedStartSeconds - pt.Time);
            if (headDelta <= thresholdSeconds && headDelta < minDelta)
            {
                minDelta = headDelta;
                bestStart = pt.Time;
                bestReason = $"Cabeza del clip imantada a {pt.Label}";
            }

            // 2. Probar cola del clip con punto de referencia (snapping hacia adelante cuando la cola topa con un corte)
            double tailDelta = Math.Abs(proposedTail - pt.Time);
            if (tailDelta <= thresholdSeconds && tailDelta < minDelta)
            {
                double candidateStart = pt.Time - durationSeconds;
                if (candidateStart >= 0)
                {
                    minDelta = tailDelta;
                    bestStart = candidateStart;
                    bestReason = $"Cola del clip imantada a {pt.Label}";
                }
            }
        }

        if (minDelta <= thresholdSeconds)
        {
            return new SnapResult(true, Math.Max(0, bestStart), proposedStartSeconds, bestReason);
        }

        return new SnapResult(false, proposedStartSeconds, proposedStartSeconds);
    }
}
