using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public sealed record SilenceCut(double Start, double End)
{
    public double Duration => End - Start;
}

public static class SilenceCutOperations
{
    public static void Apply(Sequence sequence, string audioId, IReadOnlyCollection<SilenceCut> cuts,
        bool closeGaps = true)
    {
        var audio = sequence.Clips.FirstOrDefault(c => c.Id == audioId &&
            sequence.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "audio"));
        if (audio == null) throw new InvalidOperationException("Ya no está disponible el clip de audio analizado.");
        var linked = sequence.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(audioId));
        var ids = (linked?.ClipIds ?? [audioId]).ToHashSet();
        var trackIds = sequence.Clips.Where(c => ids.Contains(c.Id)).Select(c => c.TrackId).ToHashSet();
        if (sequence.Tracks.Any(t => trackIds.Contains(t.Id) && t.Locked))
            throw new InvalidOperationException("Desbloquea las pistas del audio y sus medios vinculados antes de aplicar los cortes.");

        const double tolerance = 0.0001;
        var laterBoundary = audio.EndSeconds;
        var frame = 1 / Math.Max(1, sequence.Fps.Seconds);
        var ordered = cuts.OrderByDescending(c => c.Start).ToArray();
        foreach (var cut in ordered)
        {
            if (!double.IsFinite(cut.Start) || !double.IsFinite(cut.End) ||
                cut.Start < audio.TimelineStart.Seconds - tolerance || cut.End > laterBoundary + tolerance ||
                cut.Duration + tolerance < frame)
                throw new ArgumentException("Los cortes exceden el audio o se solapan. Vuelve a analizar el clip.");
            laterBoundary = cut.Start;
        }

        // Starting at the end keeps all the earlier, reviewed timestamps valid.
        foreach (var cut in ordered)
        {
            var interval = EditTiming.RemovalInterval(sequence, cut.Start, cut.End,
                sequence.Clips.Where(c => ids.Contains(c.Id)));
            if (!CollisionEngine.RangeTrim(sequence, interval.Start, interval.End, ids))
                throw new InvalidOperationException("El tramo analizado ya no se puede cortar. Vuelve a analizar el clip.");
            if (closeGaps)
                CollisionEngine.CloseInterval(sequence, trackIds, interval.End, interval.End - interval.Start, followCaptionsAfterEdit: true);
        }
    }
}
