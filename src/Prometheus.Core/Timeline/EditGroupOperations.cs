using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

/// <summary>Grupos editoriales; independientes de los vínculos de audio y vídeo.</summary>
public static class EditGroupOperations
{
    public static IReadOnlyList<string> Expand(Sequence sequence, IEnumerable<string> seedIds, bool includeLinks = true)
    {
        var ids = seedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool changed;
        do
        {
            var count = ids.Count;
            foreach (var group in sequence.EditGroups.Where(g => g.ClipIds.Any(ids.Contains))) ids.UnionWith(group.ClipIds);
            if (includeLinks)
                foreach (var group in sequence.LinkGroups.Where(g => g.ClipIds.Any(ids.Contains))) ids.UnionWith(group.ClipIds);
            changed = ids.Count != count;
        } while (changed);
        return sequence.Clips.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToArray();
    }

    public static EditGroup? Group(Sequence sequence, IEnumerable<string> selectedIds)
    {
        var live = sequence.Clips.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = selectedIds.Where(live.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count < 2) return null;
        var touched = sequence.EditGroups.Where(g => g.ClipIds.Any(ids.Contains)).ToArray();
        foreach (var group in touched) ids.UnionWith(group.ClipIds.Where(live.Contains));
        sequence.EditGroups.RemoveAll(touched.Contains);
        var number = sequence.EditGroups.Count == 0 ? 1 : sequence.EditGroups.Max(g => g.Number) + 1;
        var result = new EditGroup { Number = number, ClipIds = sequence.Clips.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToList() };
        sequence.EditGroups.Add(result);
        return result;
    }

    public static void Ungroup(Sequence sequence, IEnumerable<string> selectedIds)
    {
        var ids = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        sequence.EditGroups.RemoveAll(g => g.ClipIds.Any(ids.Contains));
    }

    public static IReadOnlyList<string> AlignedTrimTargets(Sequence sequence, string anchorId, bool leftEdge)
    {
        var anchor = sequence.Clips.FirstOrDefault(c => c.Id == anchorId);
        if (anchor == null) return Array.Empty<string>();
        var members = Expand(sequence, new[] { anchorId });
        var edge = leftEdge ? anchor.TimelineStart.Seconds : anchor.EndSeconds;
        var tolerance = 0.5 / Math.Max(1, sequence.Fps.Seconds);
        return sequence.Clips.Where(c => members.Contains(c.Id) &&
            Math.Abs((leftEdge ? c.TimelineStart.Seconds : c.EndSeconds) - edge) <= tolerance)
            .Select(c => c.Id).ToArray();
    }
}
