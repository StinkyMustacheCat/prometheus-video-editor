using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class SequenceTransfer
{
    public static IReadOnlyList<string> Copy(Sequence source, Sequence destination, IEnumerable<string> selectedIds, double atSeconds)
    {
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clips = source.Clips.Where(c => selected.Contains(c.Id)).ToList();
        if (clips.Count == 0) return Array.Empty<string>();
        var origin = clips.Min(c => c.TimelineStart.Seconds);
        var trackMap = new Dictionary<string, string>();
        var clipMap = new Dictionary<string, string>();
        foreach (var clip in clips)
        {
            if (!trackMap.TryGetValue(clip.TrackId, out var targetTrackId))
            {
                var sourceTrack = source.Tracks.Single(t => t.Id == clip.TrackId);
                var destinationTrack = destination.Tracks.FirstOrDefault(t => t.Kind == sourceTrack.Kind &&
                    t.Role == sourceTrack.Role && t.Order == sourceTrack.Order);
                if (destinationTrack == null)
                {
                    destinationTrack = Clone(sourceTrack);
                    destinationTrack.Id = NewId();
                    destinationTrack.Name = sourceTrack.Name;
                    destinationTrack.Locked = false;
                    foreach (var effect in destinationTrack.Effects) effect.Id = NewId();
                    destination.Tracks.Add(destinationTrack);
                }
                if (destinationTrack.Locked)
                    throw new InvalidOperationException($"La pista de destino {destinationTrack.Name} está bloqueada.");
                targetTrackId = destinationTrack.Id;
                trackMap[clip.TrackId] = targetTrackId;
            }
            var copy = Clone(clip);
            copy.Id = NewId();
            copy.TranscriptId = null;
            copy.SubtitleAudioClipId = null;
            copy.AudioSplitAncestors = null;
            copy.TrackId = targetTrackId;
            copy.TimelineStart = Rational.Time(Math.Max(0, atSeconds + clip.TimelineStart.Seconds - origin));
            var effectIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var effect in copy.Effects)
            {
                var oldId = effect.Id;
                effect.Id = NewId();
                effectIds[oldId] = effect.Id;
            }
            KeyframeAnimation.RemapEffectInstances(copy, effectIds);
            destination.Clips.Add(copy);
            clipMap[clip.Id] = copy.Id;
        }
        foreach (var group in source.LinkGroups)
        {
            var members = group.ClipIds.Where(clipMap.ContainsKey).Select(id => clipMap[id]).ToList();
            if (members.Count > 1) destination.LinkGroups.Add(new LinkGroup { ClipIds = members });
        }
        foreach (var group in source.EditGroups)
        {
            var members = group.ClipIds.Where(clipMap.ContainsKey).Select(id => clipMap[id]).ToList();
            if (members.Count > 1) destination.EditGroups.Add(new EditGroup
            {
                Number = destination.EditGroups.Count == 0 ? 1 : destination.EditGroups.Max(g => g.Number) + 1,
                ClipIds = members
            });
        }
        CollisionEngine.Overwrite(destination, clipMap.Values);
        VideoTransitionOperations.CopyBetween(source, destination, clipMap);
        AudioTransitionOperations.CopyBetween(source.AudioTransitions, destination.AudioTransitions, clipMap);
        return clipMap.Values.ToArray();
    }

    public static IReadOnlyList<string> Move(Sequence source, Sequence destination, IEnumerable<string> selectedIds)
    {
        if (source.Id == destination.Id) throw new ArgumentException("El destino debe ser otra secuencia.");
        var ids = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clips = source.Clips.Where(c => ids.Contains(c.Id)).ToList();
        if (clips.Count == 0) return Array.Empty<string>();
        var result = Copy(source, destination, ids, clips.Min(c => c.TimelineStart.Seconds));
        source.Clips.RemoveAll(c => ids.Contains(c.Id));
        source.VideoTransitions.RemoveAll(t => ids.Contains(t.LeftClipId) || ids.Contains(t.RightClipId));
        source.AudioTransitions.RemoveAll(t => ids.Contains(t.ClipId) ||
            t.RightClipId != null && ids.Contains(t.RightClipId));
        foreach (var group in source.LinkGroups) group.ClipIds.RemoveAll(ids.Contains);
        source.LinkGroups.RemoveAll(g => g.ClipIds.Count < 2);
        foreach (var group in source.EditGroups) group.ClipIds.RemoveAll(ids.Contains);
        source.EditGroups.RemoveAll(g => g.ClipIds.Count < 2);
        return result;
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static string NewId() => Guid.NewGuid().ToString("N");
}
