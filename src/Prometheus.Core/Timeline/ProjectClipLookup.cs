using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public sealed record ClipLocation(Clip Clip, Track Track, IReadOnlyList<Clip> Siblings,
    IReadOnlyList<Track> Tracks, IReadOnlyList<LinkGroup> LinkGroups, double LocalTime,
    double Rate, double Offset)
{
    public double ToRootTime(double localTime) => (localTime - Offset) / Rate;
}

public static class ProjectClipLookup
{
    public static ClipLocation? Find(Project project, string? clipId, double sequenceTime,
        string? nestedRootId = null)
    {
        var sequence = project.ActiveSequence;
        if (sequence == null || clipId == null) return null;
        ClipLocation? Visit(IReadOnlyList<Clip> clips, IReadOnlyList<Track> tracks,
            IReadOnlyList<LinkGroup> links, double time, double rate, double offset, int depth)
        {
            if (depth > 16) return null;
            foreach (var clip in clips)
            {
                var track = tracks.FirstOrDefault(track => track.Id == clip.TrackId);
                if (clip.Id == clipId && track != null)
                    return new ClipLocation(clip, track, clips, tracks, links, time, rate, offset);
                if (clip.NestedContent == null) continue;
                var childTime = clip.SourceIn.Seconds +
                    (time - clip.TimelineStart.Seconds) * clip.Speed.Seconds;
                var childRate = rate * clip.Speed.Seconds;
                var childOffset = clip.SourceIn.Seconds +
                    (offset - clip.TimelineStart.Seconds) * clip.Speed.Seconds;
                var found = Visit(clip.NestedContent.Clips, clip.NestedContent.Tracks,
                    clip.NestedContent.LinkGroups, childTime, childRate, childOffset, depth + 1);
                if (found != null) return found;
            }
            return null;
        }
        if (nestedRootId != null)
        {
            var root = sequence.Clips.FirstOrDefault(clip => clip.Id == nestedRootId);
            if (root?.NestedContent == null) return null;
            var childTime = root.SourceIn.Seconds +
                (sequenceTime - root.TimelineStart.Seconds) * root.Speed.Seconds;
            var childOffset = root.SourceIn.Seconds - root.TimelineStart.Seconds * root.Speed.Seconds;
            return Visit(root.NestedContent.Clips, root.NestedContent.Tracks,
                root.NestedContent.LinkGroups, childTime, root.Speed.Seconds, childOffset, 1);
        }
        return Visit(sequence.Clips, sequence.Tracks, sequence.LinkGroups, sequenceTime, 1, 0, 0);
    }
}
