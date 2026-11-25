using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public static class SequenceOperations
{
    public static Sequence Create(Project project, string name, Sequence? template = null)
    {
        var sequence = new Sequence { Name = UniqueName(project, name) };
        if (template != null)
        {
            sequence.Width = template.Width;
            sequence.Height = template.Height;
            sequence.Fps = template.Fps;
            sequence.ColorSpace = template.ColorSpace;
            sequence.MixSampleRate = template.MixSampleRate;
            sequence.MixChannels = template.MixChannels;
            foreach (var track in template.Tracks)
            {
                var copy = Clone(track);
                copy.Id = NewId();
                foreach (var effect in copy.Effects) effect.Id = NewId();
                sequence.Tracks.Add(copy);
            }
        }
        else
        {
            sequence.Tracks.Add(new Track { Name = "V1", Kind = "video" });
            sequence.Tracks.Add(new Track { Name = "A1", Kind = "audio" });
        }
        project.Sequences.Add(sequence);
        project.ActiveSequenceId = sequence.Id;
        return sequence;
    }

    public static Sequence Duplicate(Project project, string sourceId)
    {
        var source = project.Sequences.Single(s => s.Id == sourceId);
        var copy = Clone(source);
        copy.Id = NewId();
        copy.Name = UniqueName(project, source.Name + " copia");
        var tracks = copy.Tracks.ToDictionary(t => t.Id, t => NewId());
        foreach (var track in copy.Tracks)
        {
            track.Id = tracks[track.Id];
            foreach (var effect in track.Effects) effect.Id = NewId();
        }
        var clips = copy.Clips.ToDictionary(c => c.Id, c => NewId());
        var transcripts = copy.Transcriptions.ToDictionary(document => document.Id, _ => NewId());
        string RemapSource(string id)
        {
            // A transcript can outlive its original audio piece and still reference its split descendants.
            if (!clips.TryGetValue(id, out var mapped)) clips[id] = mapped = NewId();
            return mapped;
        }
        foreach (var document in copy.Transcriptions)
        {
            document.Id = transcripts[document.Id];
            if (document.SourceClipId is { } sourceClip) document.SourceClipId = RemapSource(sourceClip);
        }
        foreach (var clip in copy.Clips)
        {
            if (clip.TranscriptId is { } transcriptId) clip.TranscriptId = transcripts.GetValueOrDefault(transcriptId);
            if (clip.SubtitleAudioClipId is { } subtitleAudio) clip.SubtitleAudioClipId = RemapSource(subtitleAudio);
            if (clip.AudioSplitAncestors is { } ancestors) clip.AudioSplitAncestors = ancestors.Select(RemapSource).ToList();
            clip.TrackId = tracks[clip.TrackId];
            clip.Id = clips[clip.Id];
            var effectIds = new Dictionary<string, string>();
            foreach (var effect in clip.Effects)
            {
                var oldId = effect.Id;
                effect.Id = NewId();
                effectIds[oldId] = effect.Id;
            }
            KeyframeAnimation.RemapEffectInstances(clip, effectIds);
        }
        copy.AudioTransitions.Clear();
        AudioTransitionOperations.CopyBetween(source.AudioTransitions, copy.AudioTransitions, clips);
        foreach (var transition in copy.VideoTransitions)
        {
            transition.Id = NewId();
            transition.LeftClipId = RemapSource(transition.LeftClipId);
            transition.RightClipId = RemapSource(transition.RightClipId);
        }
        foreach (var group in copy.LinkGroups)
        {
            group.Id = NewId();
            group.ClipIds = group.ClipIds.Where(clips.ContainsKey).Select(id => clips[id]).ToList();
        }
        foreach (var group in copy.EditGroups)
        {
            group.Id = NewId();
            group.ClipIds = group.ClipIds.Where(clips.ContainsKey).Select(id => clips[id]).ToList();
        }
        foreach (var marker in copy.Markers) marker.Id = NewId();
        project.Sequences.Add(copy);
        project.ActiveSequenceId = copy.Id;
        return copy;
    }

    public static void Delete(Project project, string id)
    {
        if (project.Sequences.Count <= 1) throw new InvalidOperationException("Debe quedar al menos un montaje.");
        var index = project.Sequences.FindIndex(s => s.Id == id);
        if (index < 0) return;
        project.Sequences.RemoveAt(index);
        if (project.ActiveSequenceId == id)
            project.ActiveSequenceId = project.Sequences[Math.Min(index, project.Sequences.Count - 1)].Id;
    }

    public static void Rename(Project project, string id, string name)
    {
        var sequence = project.Sequences.Single(s => s.Id == id);
        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("El nombre no puede estar vacío.");
        if (project.Sequences.Any(s => s.Id != id && s.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ya existe una secuencia con ese nombre.");
        sequence.Name = trimmed;
    }

    public static void SetCanvas(Project project, string id, int width, int height, bool fitAllVisualClips)
    {
        CanvasResolutions.ValidateDimensions(width, height);
        var sequence = project.Sequences.Single(s => s.Id == id);
        sequence.Width = width;
        sequence.Height = height;
        if (!fitAllVisualClips) return;
        FitVisualClips(sequence.Tracks, sequence.Clips);
    }

    private static void FitVisualClips(List<Track> tracks, List<Clip> clips)
    {
        var videoTracks = tracks.Where(t => t.Kind == "video").Select(t => t.Id).ToHashSet();
        foreach (var clip in clips)
        {
            if (videoTracks.Contains(clip.TrackId)) clip.Visual.Framing = "fit";
            if (clip.NestedContent != null) FitVisualClips(clip.NestedContent.Tracks, clip.NestedContent.Clips);
        }
    }

    private static string UniqueName(Project project, string requested)
    {
        var name = string.IsNullOrWhiteSpace(requested) ? "Secuencia" : requested.Trim();
        var result = name;
        var number = 2;
        while (project.Sequences.Any(s => s.Name.Equals(result, StringComparison.OrdinalIgnoreCase)))
            result = $"{name} {number++}";
        return result;
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static string NewId() => Guid.NewGuid().ToString("N");
}
