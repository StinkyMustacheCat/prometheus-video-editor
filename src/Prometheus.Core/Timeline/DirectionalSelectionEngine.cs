using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public static class DirectionalSelectionEngine
{
    public static List<string> Select(Sequence sequence, string direction, double anchorTime,
        IReadOnlyList<string> orderedTrackIds, string? anchorTrackId)
    {
        if (direction is "up" or "down")
        {
            var index = orderedTrackIds.ToList().FindIndex(id => string.Equals(id, anchorTrackId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return new List<string>();
            var tracks = (direction == "up" ? orderedTrackIds.Take(index + 1) : orderedTrackIds.Skip(index))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return sequence.Clips.Where(c => tracks.Contains(c.TrackId)).Select(c => c.Id).ToList();
        }
        return sequence.Clips.Where(c => direction == "backward"
                ? c.TimelineStart.Seconds <= anchorTime + 0.05
                : c.EndSeconds >= anchorTime - 0.05)
            .Select(c => c.Id).ToList();
    }
}
