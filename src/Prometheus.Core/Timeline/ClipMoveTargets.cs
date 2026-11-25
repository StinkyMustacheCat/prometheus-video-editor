using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public static class ClipMoveTargets
{
    public static bool TryResolve(Sequence sequence, IEnumerable<string> clipIds, string anchorId,
        string targetTrackId, out Dictionary<string, string> targets)
    {
        targets = new();
        var anchor = sequence.Clips.FirstOrDefault(c => c.Id == anchorId);
        var origin = sequence.Tracks.FirstOrDefault(t => t.Id == anchor?.TrackId);
        var destination = sequence.Tracks.FirstOrDefault(t => t.Id == targetTrackId);
        if (origin == null || destination == null || origin.Kind != destination.Kind || origin.Role != destination.Role)
            return false;
        var delta = destination.Order - origin.Order;
        foreach (var id in clipIds)
        {
            var clip = sequence.Clips.FirstOrDefault(c => c.Id == id);
            var source = sequence.Tracks.FirstOrDefault(t => t.Id == clip?.TrackId);
            if (clip == null || source == null || source.Locked) return false;
            var target = source.Kind != origin.Kind ? source : sequence.Tracks.FirstOrDefault(t =>
                t.Kind == source.Kind && t.Order == source.Order + delta);
            if (target == null || target.Locked || target.Role != source.Role) return false;
            targets[id] = target.Id;
        }
        return targets.Count > 0;
    }
}
