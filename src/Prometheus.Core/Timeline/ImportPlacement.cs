using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class ImportPlacement
{
    public static Clip? PlaceCompanionAudio(Sequence sequence, Asset asset, string videoClipId)
    {
        var video = sequence.Clips.FirstOrDefault(c => c.Id == videoClipId && c.AssetId == asset.Id);
        var videoTrack = sequence.Tracks.FirstOrDefault(t => t.Id == video?.TrackId);
        var stream = asset.Streams.FirstOrDefault(s => s.Kind == "audio");
        if (video == null || videoTrack?.Kind != "video" || stream == null ||
            sequence.LinkGroups.Any(g => g.ClipIds.Contains(videoClipId) && g.ClipIds.Any(id =>
                sequence.Clips.Any(c => c.Id == id && sequence.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "audio"))))) return null;
        var track = LowestFreeTrack(sequence, "audio", video.TimelineStart.Seconds, video.Duration.Seconds);
        var audio = new Clip { Name = asset.Name, AssetId = asset.Id, StreamId = stream.Id, TrackId = track.Id,
            TimelineStart = video.TimelineStart, SourceIn = video.SourceIn, Duration = video.Duration,
            AvailableSourceDurationSeconds = stream.Duration.Seconds > 0 ? stream.Duration.Seconds : null };
        sequence.Clips.Add(audio);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = new System.Collections.Generic.List<string> { video.Id, audio.Id } });
        return audio;
    }

    public static (string PrimaryClipId, string PrimaryTrackId) PlaceAsset(Project project, string assetId,
        string preferredTrackId, double at, double sourceIn = 0, double? selectedDuration = null)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo");
        var asset = project.Assets.First(a => a.Id == assetId);
        var preferred = sequence.Tracks.First(t => t.Id == preferredTrackId);
        if (preferred.Role == "subtitle") throw new InvalidOperationException("La pista admite únicamente subtítulos.");
        if (preferred.Locked) throw new InvalidOperationException("La pista está bloqueada");
        var main = asset.Streams.FirstOrDefault(s => preferred.Kind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image")
            ?? throw new InvalidOperationException("El medio no corresponde a esta pista");
        var start = Math.Max(0, at);
        var created = new System.Collections.Generic.List<string>();
        Clip Place(StreamInfo stream, string kind, Track? selectedTrack)
        {
            var offset = Math.Max(0, stream.PtsOrigin.Seconds - asset.PresentationOrigin.Seconds);
            var begin = start + offset;
            var sourceDuration = stream.Duration.Seconds > 0 ? stream.Duration.Seconds : asset.RecommendedDuration?.Seconds ?? 5;
            var duration = selectedDuration ?? (stream.Kind == "image" ? asset.RecommendedDuration?.Seconds ?? 5 : sourceDuration - sourceIn);
            if (duration < 1.0 / Math.Max(1, sequence.Fps.Seconds))
                throw new InvalidOperationException("El medio es demasiado corto para insertarlo");
            var track = selectedTrack == null ? LowestFreeTrack(sequence, kind, begin, duration) :
                PreferredFreeTrack(sequence, selectedTrack.Id, begin, duration);
            var clip = new Clip { Name = asset.Name, AssetId = asset.Id, StreamId = stream.Id, TrackId = track.Id,
                TimelineStart = Rational.Time(begin), SourceIn = Rational.Time(stream.Kind == "image" ? 0 : sourceIn),
                Duration = Rational.Time(duration), AvailableSourceDurationSeconds = stream.Duration.Seconds > 0 ? stream.Duration.Seconds : null };
            sequence.Clips.Add(clip);
            created.Add(clip.Id);
            return clip;
        }
        var primary = Place(main, preferred.Kind, preferred);
        if (main.Kind == "video" && asset.Streams.FirstOrDefault(s => s.Kind == "audio") is { } audio)
            Place(audio, "audio", null);
        if (created.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = created });
        return (primary.Id, primary.TrackId);
    }

    public static Track PreferredFreeTrack(Sequence sequence, string requestedTrackId, double start, double duration)
    {
        var requested = sequence.Tracks.First(t => t.Id == requestedTrackId && !t.Locked);
        var end = start + Math.Max(1.0 / 60, duration);
        if (!sequence.Clips.Any(c => c.TrackId == requested.Id && c.TimelineStart.Seconds < end && start < c.EndSeconds))
            return requested;
        var above = sequence.Tracks.Where(t => t.Kind == requested.Kind && t.Role != "subtitle" &&
                !t.Locked && t.Order > requested.Order)
            .OrderBy(t => t.Order).FirstOrDefault(t => !sequence.Clips.Any(c =>
                c.TrackId == t.Id && c.TimelineStart.Seconds < end && start < c.EndSeconds));
        return above ?? CollisionEngine.AddTrack(sequence, requested.Kind);
    }

    public static Track LowestFreeTrack(Sequence sequence, string kind, double start, double duration)
    {
        var end = start + Math.Max(1.0 / 60, duration);
        foreach (var track in sequence.Tracks.Where(t => t.Kind == kind && t.Role != "subtitle" && !t.Locked)
                     .OrderBy(t => t.Order))
            if (!sequence.Clips.Any(c => c.TrackId == track.Id && c.TimelineStart.Seconds < end && start < c.EndSeconds))
                return track;
        return CollisionEngine.AddTrack(sequence, kind);
    }
}
