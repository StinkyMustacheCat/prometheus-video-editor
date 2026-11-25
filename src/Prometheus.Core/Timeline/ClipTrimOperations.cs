using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class ClipTrimOperations
{
    public sealed record Plan(double Delta, IReadOnlyList<string> ClipIds);

    // Preview and release use the same intersection of every aligned member's limits.
    public static Plan PlanEdge(Project project, string anchorId, bool left, double requestedEdge)
    {
        if (!double.IsFinite(requestedEdge)) throw new ArgumentException("Borde no válido.");
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var anchor = sequence.Clips.First(c => c.Id == anchorId);
        var ids = EditGroupOperations.AlignedTrimTargets(sequence, anchorId, left);
        var selected = ids.ToHashSet();
        double lower = double.NegativeInfinity, upper = double.PositiveInfinity;
        foreach (var clip in sequence.Clips.Where(c => selected.Contains(c.Id)))
        {
            var track = sequence.Tracks.First(t => t.Id == clip.TrackId);
            if (track.Locked) throw new InvalidOperationException("Una pista vinculada está bloqueada.");
            var minimum = Math.Min(clip.Duration.Seconds, 1 / Math.Max(1, sequence.Fps.Seconds));
            var still = StillImageRules.IsStill(project, clip);
            var speed = clip.Speed.Seconds;
            if (speed <= 0) throw new InvalidOperationException("Velocidad no válida.");
            if (left)
            {
                lower = Math.Max(lower, -clip.TimelineStart.Seconds);
                if (!still) lower = Math.Max(lower, -clip.SourceIn.Seconds / speed);
                upper = Math.Min(upper, clip.Duration.Seconds - minimum);
                var previous = sequence.Clips.Where(c => !selected.Contains(c.Id) && c.TrackId == clip.TrackId &&
                    c.EndSeconds <= clip.TimelineStart.Seconds + 1e-7).MaxBy(c => c.EndSeconds);
                if (previous != null) lower = Math.Max(lower, previous.EndSeconds - clip.TimelineStart.Seconds);
            }
            else
            {
                lower = Math.Max(lower, minimum - clip.Duration.Seconds);
                var asset = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
                var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId);
                var available = clip.AvailableSourceDurationSeconds ?? (stream?.Duration.Seconds > 0 ? stream.Duration.Seconds : null);
                if (!still && available is { } end)
                    upper = Math.Min(upper, Math.Max(0, (end - clip.SourceIn.Seconds) / speed - clip.Duration.Seconds));
                var next = sequence.Clips.Where(c => !selected.Contains(c.Id) && c.TrackId == clip.TrackId &&
                    c.TimelineStart.Seconds >= clip.EndSeconds - 1e-7).MinBy(c => c.TimelineStart.Seconds);
                if (next != null) upper = Math.Min(upper, next.TimelineStart.Seconds - clip.EndSeconds);
            }
        }
        var requested = requestedEdge - (left ? anchor.TimelineStart.Seconds : anchor.EndSeconds);
        return new Plan(lower > upper ? 0 : Math.Clamp(requested, lower, upper), ids);
    }

    public static void ApplyEdge(Project project, string anchorId, bool left, double edge)
    {
        var plan = PlanEdge(project, anchorId, left, edge);
        if (Math.Abs(plan.Delta) < 1e-7) return;
        var sequence = project.ActiveSequence!;
        foreach (var id in plan.ClipIds)
        {
            var clip = sequence.Clips.First(c => c.Id == id);
            if (left)
            {
                clip.TimelineStart = Rational.Time(clip.TimelineStart.Seconds + plan.Delta);
                KeyframeAnimation.ShiftForLeftTrim(clip, plan.Delta);
                clip.SourceIn = Rational.Time(StillImageRules.IsStill(project, clip) && clip.CaptionWords == null ? 0 :
                    TranscriptOperations.ClampCaptionSourceIn(clip, clip.SourceIn.Seconds + plan.Delta * clip.Speed.Seconds));
            }
            clip.Duration = Rational.Time(clip.Duration.Seconds + (left ? -plan.Delta : plan.Delta));
        }
        CollisionEngine.Overwrite(sequence, plan.ClipIds);
    }
}
