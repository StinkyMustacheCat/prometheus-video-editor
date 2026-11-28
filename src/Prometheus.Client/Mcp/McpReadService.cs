using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Mcp;

internal static class McpReadService
{
    private static object? AnimationSummary(SubtitleAnimationSettings? settings)
    {
        if (settings == null) return null;
        using var document = JsonDocument.Parse(settings.TemplateJson);
        return new { enabled = settings.Enabled, name = document.RootElement.GetProperty("name").GetString(),
            parameters = SubtitleAnimation.Parameters(settings), overrides = settings.Parameters };
    }
    public static object GetState(ProjectController controller, JsonElement args)
    {
        var project = controller.CurrentProject;
        var scope = McpJson.String(args, "scope") ?? "summary";
        if (scope == "processing") return ProcessingService.Describe();
        if (scope == "playback") return controller.Transport.Diagnostics.Describe();
        var sequence = project.ActiveSequence;
        var offset = Math.Max(0, McpJson.Int(args, "offset", 0));
        var limit = Math.Clamp(McpJson.Int(args, "limit", 250), 1, 1000);
        var selected = controller.SelectedClipId;
        var requestedTracks = args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("track_ids", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        object TrackSummary(Track t, bool includeClips = true)
        {
            var clips = sequence!.Clips.Where(c => c.TrackId == t.Id)
                .OrderBy(c => c.TimelineStart.Seconds).ToArray();
            return new { t.Id, t.Name, t.Kind, t.Order, t.Locked, t.Enabled, t.Solo,
                t.GainDb, t.Balance, role = t.Role, effects = t.Effects, clip_count = clips.Length,
                clips = includeClips ? clips.Skip(offset).Take(limit).Select(ClipSummary).ToArray() : [] };
        }
        return scope switch
        {
            "summary" => new { project = new { project.Id, project.Name, project.Revision },
                project_id = project.Id, revision = project.Revision, host_mode = "live-gui",
                project_file = controller.CurrentProjectPath,
                active_sequence_id = project.ActiveSequenceId, sequence_count = project.Sequences.Count,
                asset_count = project.Assets.Count, dirty = controller.IsDirty,
                width = sequence?.Width, height = sequence?.Height, fps = sequence?.Fps.Seconds,
                duration_seconds = sequence?.DurationSeconds,
                subtitle_animation = AnimationSummary(sequence?.SubtitleAnimation),
                track_count = sequence?.Tracks.Count ?? 0, clip_count = sequence?.Clips.Count ?? 0,
                marker_count = sequence?.Markers.Count ?? 0,
                in_seconds = sequence?.MarkInSeconds, out_seconds = sequence?.MarkOutSeconds,
                selected_clip_id = selected, selected_clip_ids = selected == null ? [] : new[] { selected },
                playhead_seconds = controller.Transport.PositionSeconds,
                is_playing = controller.Transport.IsPlaying,
                can_undo = controller.UndoRedo.CanUndo, can_redo = controller.UndoRedo.CanRedo },
            "project" => new { project.Id, project.Name, project.Revision,
                folders = project.MediaFolders.Select(f => new { f.Id, f.Name }).ToArray(),
                sequences = project.Sequences.Select(s => new { s.Id, s.Name, s.Width, s.Height,
                    fps = s.Fps.Seconds, clip_count = s.Clips.Count }).ToArray(),
                total_assets = project.Assets.Count, offset, limit,
                assets = project.Assets.Skip(offset).Take(limit).Select(a => new
                { a.Id, a.Name, a.FolderId, a.WebPreferredQuality, a.Source.Kind, a.Source.Uri,
                    duration = a.RecommendedDuration?.Seconds,
                    streams = a.Streams.Select(x => new { x.Id, x.Kind, x.Duration,
                        x.Width, x.Height }).ToArray() }).ToArray() },
            "media_pool" => MediaPoolState(project, args, offset, limit),
            "sequence" => sequence == null ? new { error = "No hay montaje activo" } : new
                { sequence.Id, sequence.Name, sequence.Width, sequence.Height, fps = sequence.Fps.Seconds,
                    sequence.DurationSeconds, sequence.MarkInSeconds, sequence.MarkOutSeconds,
                    offset, limit, total_clips = sequence.Clips.Count,
                    tracks = sequence.Tracks.Select(t => TrackSummary(t)).ToArray(),
                    video_transitions = sequence.VideoTransitions.Select(t => new { t.Id, t.Kind, t.LeftClipId, t.RightClipId,
                        t.DurationFrames, mgt_id = t.Mgt?.Id, parameters = t.Mgt?.Parameters, overrides = t.MgtOverrides }),
                    audio_transitions = sequence.AudioTransitions,
                    subtitle_animation = AnimationSummary(sequence.SubtitleAnimation),
                    link_groups = sequence.LinkGroups, edit_groups = sequence.EditGroups,
                    transcriptions = sequence.Transcriptions.Select(d => new { d.Id, d.Name, d.SourceClipId, d.CaptionMode, words_count = d.Words.Count }),
                    markers = sequence.Markers.Select(m => new { m.Id, m.Name,
                        time_seconds = m.Time.Seconds, m.ColorArgb }).ToArray() },
            "links" => new { total = project.LinkShelf.Count, offset, limit,
                links = project.LinkShelf.Skip(offset).Take(limit).ToArray() },
            "transcriptions" => new { offset, limit, documents = (sequence?.Transcriptions ?? [])
                .Where(d => McpJson.String(args, "document_id") is not { } id || d.Id == id)
                .Select(d => new { d.Id, d.Name, d.SourceClipId, d.CaptionMode, d.Language, words_count = d.Words.Count,
                    words = McpJson.String(args, "document_id") == d.Id ? d.Words.Skip(offset).Take(limit).ToArray() : [] }).ToArray() },
            "selection" => new { selected_clip_id = selected,
                selected_nested_root_id = controller.SelectedNestedRootId,
                clip = sequence?.Clips.FirstOrDefault(c => c.Id == selected) is { } selectedClip
                    ? ClipSummary(selectedClip) : null },
            "io" => new { project_path = controller.CurrentProjectPath,
                dirty = controller.IsDirty, can_undo = controller.UndoRedo.CanUndo,
                can_redo = controller.UndoRedo.CanRedo,
                in_seconds = sequence?.MarkInSeconds ?? 0,
                out_seconds = sequence?.MarkOutSeconds ?? sequence?.DurationSeconds ?? 0,
                clips = sequence?.Clips.Where(c => c.TimelineStart.Seconds <
                    (sequence.MarkOutSeconds ?? sequence.DurationSeconds) &&
                    c.EndSeconds > (sequence.MarkInSeconds ?? 0)).Skip(offset).Take(limit)
                    .Select(ClipSummary).ToArray() ?? [],
                markers = sequence?.Markers.Where(m => m.Time.Seconds >= (sequence.MarkInSeconds ?? 0) &&
                    m.Time.Seconds < (sequence.MarkOutSeconds ?? sequence.DurationSeconds))
                    .Select(m => new { m.Id, m.Name, time_seconds = m.Time.Seconds }).ToArray() ?? [],
                web = project.Assets.Where(a => a.Source.Kind == "web").Select(a => new
                { a.Id, a.Name, a.Source.Uri, preferred_quality = a.WebPreferredQuality, pending = WebMediaService.IsPending(a),
                    stage = WebMediaService.GetState(a.Id)?.Stage,
                    message = WebMediaService.GetState(a.Id)?.Message,
                    percent = WebMediaService.GetState(a.Id)?.Percent }).ToArray() },
            "tracks" => sequence == null ? new { error = "No hay montaje activo" } : new
                { sequence_id = sequence.Id, offset, limit,
                    tracks = sequence.Tracks.Where(t => requestedTracks == null || requestedTracks.Contains(t.Id))
                        .Select(t => TrackSummary(t)).ToArray() },
            _ => throw new ArgumentException("scope inválido: " + scope)
        };
    }

    private static object MediaPoolState(Project project, JsonElement args, int offset, int limit)
    {
        var visible = project.Assets.Where(MediaPoolOperations.AppearsInPool).ToArray();
        var filtered = visible.AsEnumerable();
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("folder_id", out var folder))
        {
            if (folder.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new ArgumentException("folder_id debe ser una carpeta existente o null para Sin carpeta.");
            var id = folder.ValueKind == JsonValueKind.Null ? null : folder.GetString();
            if (id != null && project.MediaFolders.All(f => f.Id != id)) throw new ArgumentException("Carpeta no encontrada.");
            filtered = filtered.Where(a => a.FolderId == id);
        }
        var assets = filtered.ToArray();
        return new { total_assets = assets.Length, offset, limit, unfiled_count = visible.Count(a => a.FolderId == null),
            folders = project.MediaFolders.Select(f => new { id = f.Id, name = f.Name,
                asset_count = visible.Count(a => a.FolderId == f.Id) }).ToArray(),
            assets = assets.Skip(offset).Take(limit).Select(a => new { id = a.Id, name = a.Name,
                folder_id = a.FolderId, preferred_quality = a.WebPreferredQuality, kind = a.Source.Kind, uri = a.Source.Uri }).ToArray() };
    }

    public static object InspectClip(ProjectController controller, JsonElement args)
    {
        var id = McpJson.String(args, "clip_id") ?? controller.SelectedClipId
            ?? throw new ArgumentException("Falta clip_id y no hay clip seleccionado");
        foreach (var sequence in controller.CurrentProject.Sequences)
        {
            var located = Find(sequence.Clips, sequence.Tracks, id, null);
            if (located != null)
            {
                var (clip, track, parent) = located.Value;
                var asset = controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
                return new { sequence_id = sequence.Id, parent_nested_clip_id = parent,
                    clip = McpJson.Bool(args, "compact") ? ClipSummary(clip) : (object)clip,
                    clip_id = clip.Id, asset_name = asset?.Name,
                    text = asset?.TextOverlay, mgt_parameters = asset?.MgtParameters, mgt_overrides = clip.MgtOverrides,
                    masks = ClipMaskStack.Read(clip).Select(mask => new { mask.Id, mask.Name, mask.Kind, mask.Target,
                        mask.Enabled, mask.Blend, mask.FeatherPixels, mask.ExpandPixels, mask.Inverted,
                        seed_times = mask.SamMasks.Select(seed => seed.SourceSeconds).ToArray(), tracks = mask.SamMaskTracks.Count }),
                    asset_kind = asset?.Source.Kind, track = track == null ? null : new
                    { track.Id, track.Name, track.Kind, track.Locked },
                    start_seconds = clip.TimelineStart.Seconds, duration_seconds = clip.Duration.Seconds,
                    end_seconds = clip.EndSeconds, source_in_seconds = clip.SourceIn.Seconds,
                    speed = clip.Speed.Seconds };
            }
        }
        throw new ArgumentException("No existe el clip: " + id);
    }

    private static (Clip Clip, Track? Track, string? Parent)? Find(IEnumerable<Clip> clips,
        IEnumerable<Track> tracks, string id, string? parent)
    {
        foreach (var clip in clips)
        {
            if (clip.Id == id) return (clip, tracks.FirstOrDefault(t => t.Id == clip.TrackId), parent);
            if (clip.NestedContent != null && Find(clip.NestedContent.Clips,
                clip.NestedContent.Tracks, id, clip.Id) is { } child) return child;
        }
        return null;
    }

    private static object ClipSummary(Clip c) => new { c.Id, c.Name, c.TrackId, c.AssetId,
        start = c.TimelineStart.Seconds, duration = c.Duration.Seconds,
        c.Enabled, source_in_seconds = c.SourceIn.Seconds, speed = c.Speed.Seconds,
        effects_count = c.Effects.Count, keyframe_tracks_count = c.KeyframeTracks.Count,
        is_nested = c.NestedContent != null,
        nested_children_count = c.NestedContent?.Clips.Count ?? 0 };
}

internal static class McpJson
{
    public static string? String(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;
    public static double Number(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) && p.TryGetDouble(out var n)
            ? n : throw new ArgumentException("Falta número: " + name);
    public static bool Bool(JsonElement args, string name, bool fallback = false) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) &&
        (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False) ? p.GetBoolean() : fallback;
    public static JsonElement Array(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
            ? p : throw new ArgumentException("Falta matriz: " + name);
    public static int Int(JsonElement args, string name, int fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) && p.TryGetInt32(out var n)
            ? n : fallback;
}
