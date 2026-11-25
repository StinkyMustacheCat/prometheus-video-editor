using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class CapturedFramePlacement
{
    public static Clip Overlay(Project project, Asset asset, double at)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo");
        var time = MarkerOperations.FrameTime(sequence, at);
        var duration = Duration(asset);
        var track = ImportPlacement.LowestFreeTrack(sequence, "video", time, duration);
        project.Assets.Add(asset);
        var placed = ImportPlacement.PlaceAsset(project, asset.Id, track.Id, time, selectedDuration: duration);
        return sequence.Clips.Single(c => c.Id == placed.PrimaryClipId);
    }

    public static Clip Insert(Project project, Asset asset, double at)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo");
        var time = EditTiming.InsertionTime(sequence, MarkerOperations.FrameTime(sequence, at));
        var duration = Duration(asset);
        if (sequence.Clips.Any(c => c.EndSeconds > time + 0.000001 &&
            sequence.Tracks.Any(t => t.Id == c.TrackId && t.Locked)))
            throw new InvalidOperationException("Desbloquea las pistas posteriores para insertar el fotograma.");

        var originalGroups = sequence.LinkGroups.Select(g => g.ClipIds.ToArray()).ToArray();
        var rightIds = new Dictionary<string, string>();
        foreach (var clip in sequence.Clips.Where(c => c.TimelineStart.Seconds < time - 0.000001 &&
                     c.EndSeconds > time + 0.000001 &&
                     TranscriptOperations.AudioSourceId(sequence, c) == null).ToArray())
        {
            var right = CollisionEngine.CopySplitPiece(sequence, clip);
            var offset = time - clip.TimelineStart.Seconds;
            right.TimelineStart = Rational.Time(time);
            right.Duration = Rational.Time(clip.EndSeconds - time);
            right.SourceIn = Rational.Time(clip.SourceIn.Seconds + offset * clip.Speed.Seconds);
            KeyframeAnimation.ShiftForLeftTrim(right, offset);
            clip.Duration = Rational.Time(offset);
            sequence.Clips.Add(right);
            rightIds[clip.Id] = right.Id;
        }

        sequence.LinkGroups.Clear();
        foreach (var ids in originalGroups)
        {
            var left = ids.Where(id => sequence.Clips.Any(c => c.Id == id && c.TimelineStart.Seconds < time)).ToList();
            var right = ids.Select(id => rightIds.TryGetValue(id, out var newId) ? newId :
                    sequence.Clips.FirstOrDefault(c => c.Id == id && c.TimelineStart.Seconds >= time)?.Id)
                .Where(id => id != null).Select(id => id!).ToList();
            if (left.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = left });
            if (right.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = right });
        }
        foreach (var group in sequence.EditGroups)
        foreach (var pair in rightIds)
            if (group.ClipIds.Contains(pair.Key)) group.ClipIds.Add(pair.Value);

        foreach (var clip in sequence.Clips.Where(c => c.TimelineStart.Seconds >= time - 0.000001))
            clip.TimelineStart = Rational.Time(clip.TimelineStart.Seconds + duration);
        foreach (var marker in sequence.Markers.Where(m => m.Time.Seconds >= time - 0.000001))
            marker.Time = Rational.Time(marker.Time.Seconds + duration);
        if (sequence.MarkInSeconds >= time) sequence.MarkInSeconds += duration;
        if (sequence.MarkOutSeconds >= time) sequence.MarkOutSeconds += duration;

        var track = sequence.Tracks.Where(t => t.Kind == "video" && t.Role != "subtitle" && !t.Locked).OrderBy(t => t.Order).FirstOrDefault()
            ?? CollisionEngine.AddTrack(sequence, "video");
        project.Assets.Add(asset);
        var placed = ImportPlacement.PlaceAsset(project, asset.Id, track.Id, time, selectedDuration: duration);
        return sequence.Clips.Single(c => c.Id == placed.PrimaryClipId);
    }

    private static double Duration(Asset asset)
    {
        if (!asset.Streams.Any(s => s.Kind is "image" or "video"))
            throw new InvalidOperationException("El fotograma capturado no contiene imagen.");
        var duration = asset.RecommendedDuration?.Seconds ?? 5;
        if (!double.IsFinite(duration) || duration < 1.0 / 240 || duration > 48 * 3600)
            throw new InvalidOperationException("Duración del fotograma inválida.");
        return duration;
    }
}
