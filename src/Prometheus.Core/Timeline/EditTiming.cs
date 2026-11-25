using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

/// <summary>Use the same representable fragment floor as project validation.</summary>
internal static class EditTiming
{
    public static double MinimumDuration(Sequence sequence) =>
        Math.Min(1d / 60, 1 / Math.Max(1, sequence.Fps.Seconds));

    // Contract removal inward, never remove extra source to absorb a short remnant.
    // Callers that ripple must use the returned interval, not the requested interval.
    public static (double Start, double End) RemovalInterval(Sequence sequence, double start, double end,
        IEnumerable<Clip> clips)
    {
        var minimum = MinimumDuration(sequence);
        var items = clips.ToArray();
        foreach (var clip in items.OrderBy(c => c.TimelineStart.Seconds))
            if (start > clip.TimelineStart.Seconds + 1e-7 &&
                start < clip.TimelineStart.Seconds + minimum - 1e-7 && clip.EndSeconds > start)
                start = clip.TimelineStart.Seconds + minimum;
        foreach (var clip in items.OrderByDescending(c => c.EndSeconds))
            if (end < clip.EndSeconds - 1e-7 &&
                end > clip.EndSeconds - minimum + 1e-7 && clip.TimelineStart.Seconds < end)
                end = clip.EndSeconds - minimum;
        return (start, end);
    }

    // A capture inserted very close to an off-grid edge uses the nearest safe
    // boundary. Preserve all source and apply one insertion point to all tracks.
    public static double InsertionTime(Sequence sequence, double requested)
    {
        var minimum = MinimumDuration(sequence);
        var frame = 1 / Math.Max(1, sequence.Fps.Seconds);
        var clips = sequence.Clips.Where(c => TranscriptOperations.AudioSourceId(sequence, c) == null).ToArray();
        bool Safe(double time) => clips.All(c =>
            time <= c.TimelineStart.Seconds + 1e-7 || time >= c.EndSeconds - 1e-7 ||
            time >= c.TimelineStart.Seconds + minimum - 1e-7 && time <= c.EndSeconds - minimum + 1e-7);
        if (Safe(requested)) return requested;
        var candidates = clips.SelectMany(c => new[] { c.TimelineStart.Seconds, c.EndSeconds,
                c.TimelineStart.Seconds + minimum, c.EndSeconds - minimum })
            .Where(time => time >= 0 && Math.Abs(time - requested) <= frame + 1e-7)
            .OrderBy(time => Math.Abs(time - requested)).ThenBy(time => time);
        foreach (var candidate in candidates)
        {
            var time = Prometheus.Core.Primitives.Rational.Time(candidate).Seconds;
            if (Safe(time)) return time;
        }
        throw new InvalidOperationException("No hay un punto de inserción cercano que conserve todos los fragmentos. Mueve el cabezal un fotograma.");
    }
}
