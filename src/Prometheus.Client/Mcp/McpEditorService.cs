using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Prometheus.Client.Services;

namespace Prometheus.Client.Mcp;

internal static class McpEditorService
{
    public static readonly string[] ActionIds =
    [
        "transport.play", "transport.pause", "transport.toggle",
        "playhead.seek", "playhead.step_frames", "playhead.step_seconds",
        "playhead.start", "playhead.end", "playhead.boundary.next", "playhead.boundary.previous",
        "playhead.marker.next", "playhead.marker.previous", "selection.by_id", "selection.clear", "selection.at_playhead",
        "state.current_time", "state.selected_clip", "state.active_sequence"
    ];

    public static object Action(ProjectController controller, JsonElement args)
    {
        var id = McpJson.String(args, "id") ?? throw new ArgumentException("Falta id");
        var transport = controller.Transport;
        switch (id)
        {
            case "transport.play": if (!transport.IsPlaying) transport.TogglePlayback(); break;
            case "transport.pause": transport.Pause(); break;
            case "transport.toggle": transport.TogglePlayback(); break;
            case "playhead.seek": transport.Seek(McpJson.Number(args, "seconds")); break;
            case "playhead.step_frames": transport.StepFrames((int)McpJson.Number(args, "frames")); break;
            case "playhead.step_seconds":
                transport.Pause(); transport.Seek(transport.PositionSeconds + McpJson.Number(args, "seconds")); break;
            case "playhead.start": transport.GoToStart(); break;
            case "playhead.end": transport.GoToEnd(); break;
            case "playhead.boundary.next":
            case "playhead.boundary.previous":
                var clips = controller.CurrentProject.ActiveSequence?.Clips;
                SeekBoundary(clips?.SelectMany(c => new[] { c.TimelineStart.Seconds, c.EndSeconds }) ?? [],
                    id.EndsWith(".next", StringComparison.Ordinal));
                break;
            case "playhead.marker.next":
            case "playhead.marker.previous":
                SeekBoundary(controller.CurrentProject.ActiveSequence?.Markers.Select(marker => marker.Time.Seconds) ?? [],
                    id.EndsWith(".next", StringComparison.Ordinal));
                break;
            case "selection.at_playhead":
                var sequence = controller.CurrentProject.ActiveSequence;
                var at = transport.PositionSeconds;
                var selected = sequence?.Clips.Where(c => c.Enabled && c.TimelineStart.Seconds <= at && c.EndSeconds > at)
                    .OrderByDescending(c => sequence.Tracks.FirstOrDefault(t => t.Id == c.TrackId)?.Kind == "video")
                    .ThenByDescending(c => sequence.Tracks.FirstOrDefault(t => t.Id == c.TrackId)?.Order ?? 0).FirstOrDefault();
                controller.SetSelectedClip(selected?.Id);
                break;
            case "selection.clip.by_id":
            case "selection.by_id":
                var clipId = McpJson.String(args, "clip_id") ?? throw new ArgumentException("Falta clip_id");
                if (!controller.CurrentProject.ActiveSequence!.Clips.Any(c => c.Id == clipId))
                    throw new ArgumentException("Clip no encontrado en el montaje activo");
                controller.SetSelectedClip(clipId); break;
            case "selection.clear": controller.SetSelectedClip(null); break;
            case "state.current_time": break;
            case "state.selected_clip": break;
            case "state.active_sequence": break;
            default: throw new ArgumentException("editor_action no disponible: " + id);
        }
        return new { action = id, playhead_seconds = transport.PositionSeconds,
            is_playing = transport.IsPlaying, selected_clip_id = controller.SelectedClipId,
            active_sequence_id = controller.CurrentProject.ActiveSequenceId,
            revision = controller.CurrentProject.Revision };

        void SeekBoundary(IEnumerable<double> positions, bool forward)
        {
            var current = transport.PositionSeconds;
            var target = forward
                ? positions.Where(time => time > current + 1e-6).DefaultIfEmpty(current).Min()
                : positions.Where(time => time < current - 1e-6).DefaultIfEmpty(current).Max();
            transport.Seek(target);
        }
    }

    public static object History(ProjectController controller, JsonElement args)
    {
        var operation = McpJson.String(args, "operation") ?? throw new ArgumentException("Falta operation");
        var prior = controller.CurrentProject.Revision;
        var changed = operation switch
        {
            "undo" => controller.UndoRedo.Undo(),
            "redo" => controller.UndoRedo.Redo(),
            _ => throw new ArgumentException("operation debe ser undo o redo")
        };
        return new { operation, changed, prior_revision = prior,
            new_revision = controller.CurrentProject.Revision,
            can_undo = controller.UndoRedo.CanUndo, can_redo = controller.UndoRedo.CanRedo };
    }
}
