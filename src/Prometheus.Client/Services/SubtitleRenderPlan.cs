using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public sealed record SubtitleSceneRequest(string Signature, SubtitleAnimation.Transcript Data, double Fps, double Duration);

/// <summary>One virtual source for the subtitle track. No model assets or rendered frame cache.</summary>
public static class SubtitleRenderPlan
{
    private const string Prefix = "subtitle-scene:";
    private sealed record State(Asset Asset, Clip Clip, SubtitleSceneRequest Request);
    private sealed record Source(Project Project, int Revision, string Id, int Width, int Height, Rational Fps,
        List<Asset> Assets, int AssetCount, List<Clip> Clips, int ClipCount, List<Track> Tracks,
        List<TranscriptDocument> Documents, int DocumentCount, SubtitleAnimationSettings Settings, string Template);
    private sealed class Entry { public Source? Source; public State? State; }
    private static readonly ConditionalWeakTable<Sequence, Entry> States = new();
    public static bool IsScene(Clip clip) => clip.Id.StartsWith(Prefix, StringComparison.Ordinal);
    public static bool Enabled(Project project) => project.ActiveSequence?.SubtitleAnimation is { Enabled: true } &&
        SubtitleTrack.Find(project.ActiveSequence) is { Enabled: true };

    /// <summary>Call after editing an existing model in place. Normal transactions replace the sequence.
    /// Invalidation belongs to the edit boundary, never to the per-frame render loop.</summary>
    public static void Invalidate(Project project)
    {
        foreach (var sequence in project.Sequences) States.Remove(sequence);
    }

    private static State Get(Project project)
    {
        var sequence = project.ActiveSequence!;
        var settings = sequence.SubtitleAnimation!;
        var source = new Source(project, project.Revision, sequence.Id, sequence.Width, sequence.Height, sequence.Fps,
            project.Assets, project.Assets.Count, sequence.Clips, sequence.Clips.Count, sequence.Tracks,
            sequence.Transcriptions, sequence.Transcriptions.Count, settings, settings.TemplateJson);
        var entry = States.GetValue(sequence, _ => new Entry());
        lock (entry)
        {
            if (entry.Source == source && entry.State != null) return entry.State;
            var state = Build(project, sequence);
            entry.Source = source; entry.State = state;
            return state;
        }
    }

    private static State Build(Project project, Sequence sequence)
    {
        var settings = sequence.SubtitleAnimation!;
        var normalized = SubtitleAnimation.NormalizeTemplate(settings.TemplateJson);
        var shortSide = Math.Min(sequence.Width, sequence.Height);
        var longSide = Math.Max(sequence.Width, sequence.Height);
        var size = CanvasResolutions.AtShortEdge(sequence.Width, sequence.Height, Math.Min(1080, Math.Max(64, 8192 * shortSide / longSide)));
        var folder = Path.Combine(AppContext.BaseDirectory, "Cache", "SubtitleTemplates");
        Directory.CreateDirectory(folder);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized + "|" + size.Width + "x" + size.Height)));
        var path = Path.Combine(folder, hash + ".mgt.json");
        if (!File.Exists(path))
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, normalized); try { File.Move(temporary, path); } catch (IOException) when (File.Exists(path)) { } }
            finally { File.Delete(temporary); }
        }
        var asset = MgtLiveAsset.Inspect(path);
        asset.Id = Prefix + sequence.Id;
        asset.Streams[0].Width = size.Width; asset.Streams[0].Height = size.Height;
        var data = SubtitleAnimation.BuildTranscript(project);
        var signature = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data)));
        var duration = Math.Max(.1, sequence.DurationSeconds);
        var clip = new Clip { Id = Prefix + sequence.Id, AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            Name = "Subtítulos · animación MGT", Duration = Rational.Time(duration) };
        return new(asset, clip, new(signature, data, sequence.Fps.Seconds, duration));
    }

    public static Asset? Asset(Project project, Clip clip) => IsScene(clip) ? Get(project).Asset :
        project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
    public static SubtitleSceneRequest? Request(Project project, Clip clip) => IsScene(clip) ? Get(project).Request : null;

    public static IReadOnlyList<NestedRenderLayer> At(Project project, double time, string kind)
    {
        var original = NestedRenderPlan.At(project, time, kind, includeSubtitles: !Enabled(project));
        if (kind != "video" || !Enabled(project)) return original;
        var sequence = project.ActiveSequence!;
        var track = SubtitleTrack.Find(sequence)!;
        var state = Get(project);
        if (state.Request.Data.cues.Count == 0) return original;
        var result = original.Where(layer => layer.Track.Id != track.Id).ToList();
        var clip = state.Clip;
        clip.TrackId = track.Id;
        clip.MgtOverrides = sequence.SubtitleAnimation!.Parameters;
        result.Add(new NestedRenderLayer(clip, track, time, time, [], [], [int.MaxValue / 2]));
        return result;
    }
}
