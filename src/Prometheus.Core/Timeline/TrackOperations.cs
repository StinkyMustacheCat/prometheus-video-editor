using System;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public static class TrackOperations
{
    public static void ValidateDelete(Sequence sequence, string trackId)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == trackId)
            ?? throw new ArgumentException("La pista ya no existe.");
        if (track.Role == "subtitle") throw new InvalidOperationException("La pista de subtítulos se puede ocultar o replegar.");
        if (track.Locked) throw new InvalidOperationException("Desbloquea la pista antes de eliminarla.");
        if (sequence.Tracks.Count(t => t.Kind == track.Kind && t.Role != "subtitle") <= 1)
            throw new InvalidOperationException("Debe quedar al menos una pista de cada tipo, además de la pista de subtítulos.");
    }

    public static void Delete(Sequence sequence, string trackId)
    {
        ValidateDelete(sequence, trackId);
        var track = sequence.Tracks.First(t => t.Id == trackId);
        var removed = sequence.Clips.Where(c => c.TrackId == trackId).Select(c => c.Id).ToHashSet();
        sequence.Clips.RemoveAll(c => removed.Contains(c.Id));
        sequence.Tracks.Remove(track);
        foreach (var group in sequence.LinkGroups) group.ClipIds.RemoveAll(removed.Contains);
        sequence.LinkGroups.RemoveAll(g => g.ClipIds.Count < 2);
        foreach (var group in sequence.EditGroups) group.ClipIds.RemoveAll(removed.Contains);
        sequence.EditGroups.RemoveAll(g => g.ClipIds.Count < 2);
        var order = 0;
        foreach (var lane in sequence.Tracks.Where(t => t.Kind == track.Kind && t.Role != "subtitle").OrderBy(t => t.Order))
            lane.Order = order++;
        foreach (var lane in sequence.Tracks.Where(t => t.Kind == track.Kind && t.Role == "subtitle")) lane.Order = -1;
        // The transaction's common preflight reconciles transitions after all mutations.
    }
}
