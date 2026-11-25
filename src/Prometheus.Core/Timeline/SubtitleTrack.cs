using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class SubtitleTrack
{
    public const string Name = "Subtítulos";

    public static Track? Find(Sequence sequence) => sequence.Tracks
        .FirstOrDefault(track => track.Kind == "video" && track.Role == "subtitle");

    public static Track Ensure(Sequence sequence)
    {
        if (Find(sequence) is { } existing) return existing;
        var track = new Track { Name = Name, Kind = "video", Role = "subtitle",
            Order = sequence.Tracks.Where(item => item.Kind == "video")
                .Select(item => item.Order).DefaultIfEmpty(0).Min() - 1,
            PresentationHeight = 38 };
        sequence.Tracks.Add(track);
        return track;
    }

    public static Clip Add(Project project, string content, double start, double duration)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        if (!double.IsFinite(start) || !double.IsFinite(duration) || start < 0 ||
            duration + 1e-7 < 1.0 / Math.Max(1, sequence.Fps.Seconds) || duration > 600)
            throw new ArgumentException("El subtítulo necesita un intervalo válido.");
        var track = Ensure(sequence);
        if (track.Locked) throw new InvalidOperationException("La pista de subtítulos está bloqueada.");
        if (sequence.Clips.Any(clip => clip.TrackId == track.Id &&
            clip.TimelineStart.Seconds < start + duration - 0.000001 &&
            clip.EndSeconds > start + 0.000001))
            throw new InvalidOperationException("Ya hay un subtítulo en ese intervalo.");
        var asset = GeneratedText.CreateAsset(content, "subtitle", sequence.Width, sequence.Height);
        project.Assets.Add(asset);
        var cue = new Clip { Name = asset.Name, TrackId = track.Id,
            AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            TimelineStart = Rational.Time(start), Duration = Rational.Time(duration) };
        sequence.Clips.Add(cue);
        return cue;
    }
}
