using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class PlaceholderService
{
    public static bool IsPlaceholder(Asset? asset) => asset?.Source.Kind == "generated" && asset.Source.Uri.StartsWith("placeholder:", StringComparison.Ordinal);

    public static bool Accepts(Project project, Clip clip, Asset asset)
    {
        var sequence = project.ActiveSequence;
        var track = sequence?.Tracks.FirstOrDefault(t => t.Id == clip.TrackId);
        return track != null && !track.Locked && IsPlaceholder(project.Assets.FirstOrDefault(a => a.Id == clip.AssetId)) &&
            !IsPlaceholder(asset) && asset.Streams.Any(s => track.Kind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image");
    }

    public static Clip Create(Project project, string trackId, double start, double duration = 5)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay secuencia activa");
        var track = sequence.Tracks.First(t => t.Id == trackId);
        if (track.Locked) throw new InvalidOperationException("La pista está bloqueada");
        var asset = CreateAsset(track.Kind == "audio", duration);
        var stream = asset.Streams[0];
        project.Assets.Add(asset);
        var clip = new Clip { Name = asset.Name, AssetId = asset.Id, StreamId = stream.Id, TrackId = trackId,
            TimelineStart = Rational.Time(Math.Max(0, start)), Duration = Rational.Time(Math.Max(1.0 / 60, duration)) };
        sequence.Clips.Add(clip);
        return clip;
    }

    public static Clip CreateInGap(Project project, string trackId, double gapStart, double gapEnd)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay secuencia activa");
        var gap = CollisionEngine.DetectGaps(sequence, trackId).FirstOrDefault(g =>
            Math.Abs(g.StartSeconds - gapStart) < 1.0 / 48000 && Math.Abs(g.EndSeconds - gapEnd) < 1.0 / 48000);
        if (gap == null) throw new InvalidOperationException("El hueco cambió; vuelve a seleccionarlo");
        var clip = Create(project, trackId, gap.StartSeconds, gap.EndSeconds - gap.StartSeconds);
        clip.Duration = Rational.Time(gap.EndSeconds) - clip.TimelineStart;
        return clip;
    }

    public static Asset CreateAsset(bool audio, double duration = 5)
    {
        var id = Guid.NewGuid().ToString("N");
        var asset = new Asset {
            Id = id, Name = audio ? "Audio reservado" : "Espacio reservado",
            Source = new MediaSource { Kind = "generated", Uri = "placeholder:" + id },
            RecommendedDuration = Rational.Time(duration)
        };
        asset.Streams.Add(new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = audio ? "audio" : "image", Duration = Rational.Time(duration) });
        return asset;
    }

    public static void Replace(Project project, string clipId, string assetId, double sourceStart = 0)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay secuencia activa");
        var clip = sequence.Clips.First(c => c.Id == clipId);
        var original = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
        if (!IsPlaceholder(original)) throw new InvalidOperationException("El clip no es un espacio reservado");
        var track = sequence.Tracks.First(t => t.Id == clip.TrackId);
        if (track.Locked) throw new InvalidOperationException("La pista está bloqueada");
        var asset = project.Assets.First(a => a.Id == assetId);
        if (!Accepts(project, clip, asset)) throw new InvalidOperationException("El medio no es compatible con este espacio reservado");
        var stream = asset.Streams.FirstOrDefault(s => track.Kind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image");
        if (stream == null) throw new InvalidOperationException("El medio no es compatible con esta pista");
        if (sourceStart < 0 || (stream.Duration.Seconds > 0 && sourceStart >= stream.Duration.Seconds && stream.Kind != "image"))
            throw new InvalidOperationException("El inicio está fuera del medio");
        clip.AssetId = asset.Id;
        clip.StreamId = stream.Id;
        clip.Name = asset.Name;
        clip.SourceIn = Rational.Time(stream.Kind == "image" ? 0 : sourceStart);
        clip.AvailableSourceDurationSeconds = stream.Duration.Seconds > 0 ? stream.Duration.Seconds : null;
    }
}
