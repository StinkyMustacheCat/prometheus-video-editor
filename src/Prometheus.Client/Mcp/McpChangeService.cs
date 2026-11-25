using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Mcp;

internal sealed partial class McpChangeService
{
    private readonly ProjectController _controller;
    private readonly Dictionary<string, Plan> _plans = new();
    private sealed record Plan(Project Candidate, string ProjectId, int Revision, string BaseSnapshot,
        string[] Changes, Dictionary<string, string> References, string[] WebJobs, DateTime CreatedUtc);
    internal sealed record ActionSpec(string Op, string[] Required, string[] Optional);

    internal static readonly ActionSpec[] Actions =
    [
        new("sequence.create", ["name"], ["result"]),
        new("sequence.rename", ["sequence_id", "name"], []),
        new("sequence.activate", ["sequence_id"], []),
        new("sequence.duplicate", ["sequence_id"], ["result"]),
        new("sequence.delete", ["sequence_id"], []),
        new("sequence.set_canvas", ["width", "height"], ["sequence_id", "fit_all"]),
        new("sequence.set_export_in", ["at_seconds"], ["sequence_id"]),
        new("sequence.set_export_out", ["at_seconds"], ["sequence_id"]),
        new("sequence.clear_export_range", [], ["sequence_id"]),
        new("track.create", ["source_kind"], ["sequence_id", "name", "result"]),
        new("track.rename", ["track_id", "name"], ["sequence_id"]),
        new("track.set_locked", ["track_id", "enabled"], ["sequence_id"]),
        new("track.set_enabled", ["track_id", "enabled"], ["sequence_id"]),
        new("clip.cut", ["clip_id", "at_seconds"], ["sequence_id", "include_linked", "result"]),
        new("clip.delete", ["clip_id"], ["sequence_id", "ripple"]),
        new("clip.move", ["clip_id", "at_seconds", "track_id"], ["sequence_id", "placement"]),
        new("clip.duplicate", ["clip_id", "at_seconds", "track_id"], ["sequence_id", "placement", "result"]),
        new("clip.set_enabled", ["clip_id", "enabled"], ["sequence_id"]),
        new("gap.close", ["track_id", "at_seconds"], ["sequence_id"]),
        new("marker.create", ["at_seconds"], ["sequence_id", "name", "result"]),
        new("marker.move", ["marker_id", "at_seconds"], ["sequence_id"]),
        new("marker.rename", ["marker_id", "name"], ["sequence_id"]),
        new("marker.delete", ["marker_id"], ["sequence_id"]),
        new("media.import_local", ["resource_path"], ["sequence_id", "track_id", "at_seconds", "source_in_seconds", "duration_seconds", "result"]),
        new("media.rename", ["asset_id", "name"], []),
        new("folder.create", ["name"], ["result"]),
        new("folder.rename", ["folder_id", "name"], []),
        new("media.move_to_folder", ["asset_ids", "folder_id"], []),
        new("media.place_asset", ["asset_id", "track_id", "at_seconds"], ["sequence_id", "source_in_seconds", "duration_seconds", "result"]),
        new("media.import_url", ["url"], ["sequence_id", "track_id", "at_seconds", "source_in_seconds", "source_kind", "duration_seconds", "result"]),
        new("media.create_placeholder", ["duration_seconds"], ["sequence_id", "track_id", "at_seconds", "source_kind", "result"]),
        new("placeholder.fill_asset", ["clip_id", "asset_id"], ["sequence_id", "source_in_seconds"]),
        new("nested.create", ["clip_ids"], ["sequence_id", "name", "result"]),
        new("nested.unnest", ["clip_id"], ["sequence_id", "result"]),
        new("nested.update_child", ["clip_id", "other_clip_id", "at_seconds", "duration_seconds", "track_id"], ["sequence_id"]),
        new("nested.remove_child", ["clip_id", "other_clip_id"], ["sequence_id"]),
        new("clip.group", ["clip_ids"], ["sequence_id"]),
        new("clip.ungroup", ["clip_ids"], ["sequence_id"]),
        new("clip.set_transform", ["clip_id", "parameter_id"], ["sequence_id", "number", "value", "text"]),
        new("clip.set_audio", ["clip_id"], ["sequence_id", "gain_db", "balance"]),
        new("track.set_audio", ["track_id"], ["sequence_id", "gain_db", "balance"]),
        new("effect.apply", ["scope", "catalog_id"], ["sequence_id", "clip_id", "clip_ids", "track_id", "resource_path", "result"]),
        new("effect.remove", ["scope", "catalog_id"], ["sequence_id", "clip_id", "clip_ids", "track_id"]),
        new("effect.set_parameter", ["effect_id", "parameter_id"], ["sequence_id", "number", "text", "value"]),
        new("effect.set_enabled", ["effect_id", "enabled"], ["sequence_id"]),
        new("effect.move", ["effect_id", "index"], ["sequence_id"]),
        new("effect.remove_instance", ["effect_id"], ["sequence_id"]),
        new("curve.set_keyframe", ["clip_id", "parameter_id", "at_seconds", "number"], ["sequence_id"]),
        new("curve.set_interpolation", ["clip_id", "parameter_id", "text"], ["sequence_id"]),
        new("clip.trim", ["clip_id", "edge", "duration_seconds"], ["sequence_id", "ripple"]),
        new("clip.slip", ["clip_id", "source_in_seconds"], ["sequence_id"]),
        new("clip.roll", ["left_clip_id", "right_clip_id", "at_seconds"], ["sequence_id"]),
        new("clip.set_speed", ["clip_id", "number"], ["sequence_id"]),
        new("clip.link", ["clip_ids"], ["sequence_id"]),
        new("clip.unlink", ["clip_id"], ["sequence_id"]),
        new("track.delete", ["track_id"], ["sequence_id"]),
        new("clip.fit_speed_to_next", ["clip_id"], ["sequence_id"]),
        new("transition.apply", ["catalog_id", "clip_id"], ["sequence_id", "right_clip_id", "duration_seconds", "duration_frames", "result"]),
        new("transition.set_duration", ["transition_id"], ["sequence_id", "duration_seconds", "duration_frames"]),
        new("transition.remove", ["transition_id"], ["sequence_id"]),
        new("mgt.set_parameter", ["parameter_id", "value"], ["sequence_id", "clip_id", "transition_id"]),
        new("subtitle.set_animation", ["template_json"], ["sequence_id"]),
        new("subtitle.load_animation", ["resource_path"], ["sequence_id"]),
        new("subtitle.clear_animation", [], ["sequence_id"]),
        new("subtitle.set_animation_enabled", ["enabled"], ["sequence_id"]),
        new("subtitle.set_animation_parameter", ["parameter_id", "value"], ["sequence_id"]),
        new("text.create", ["content", "at_seconds"], ["sequence_id", "kind", "duration_seconds", "style", "result"]),
        new("text.update", ["clip_id"], ["sequence_id", "content", "style"]),
        new("transcript.import", ["content", "at_seconds"], ["sequence_id", "name", "result"]),
        new("transcript.apply", ["document_id"], ["sequence_id", "result"]),
        new("transcript.set_word", ["document_id", "word_id", "text"], ["sequence_id"]),
        new("effect.set_lut", ["effect_id", "resource_path"], ["sequence_id"]),
        new("mask.add_shape", ["clip_id", "shape"], ["sequence_id", "name", "result"]),
        new("mask.update", ["clip_id", "mask_id", "settings"], ["sequence_id"]),
        new("mask.remove", ["clip_id", "mask_id"], ["sequence_id"]),
        new("transcript.set_mode", ["document_id", "mode"], ["sequence_id"]),
        new("curve.move_keyframe", ["clip_id", "parameter_id", "at_seconds", "to_seconds"], ["sequence_id"]),
        new("curve.remove_keyframe", ["clip_id", "parameter_id", "at_seconds"], ["sequence_id"]),
        new("curve.disable", ["clip_id", "parameter_id"], ["sequence_id"]),
        new("curve.set_bezier", ["clip_id", "parameter_id", "at_seconds", "x1", "y1", "x2", "y2"], ["sequence_id"])
    ];

    public McpChangeService(ProjectController controller) => _controller = controller;

    public object Evaluate(JsonElement args, IReadOnlyDictionary<string, Asset>? preparedAssets = null)
    {
        var project = _controller.CurrentProject;
        var revision = ExpectedRevision(args, project);
        var actions = McpJson.Array(args, "actions");
        if (actions.GetArrayLength() is < 1 or > 200)
            throw new ArgumentException("Se requieren entre 1 y 200 acciones.");
        var candidate = ProjectStore.Clone(project);
        var changes = new List<string>();
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var webJobs = new List<string>();
        var index = 0;
        try
        {
            foreach (var action in actions.EnumerateArray())
            {
                index++;
                if (action.ValueKind != JsonValueKind.Object) throw new ArgumentException("Acción inválida");
                var op = Required(action, "op", references);
                var spec = Actions.FirstOrDefault(a => a.Op == op)
                    ?? throw new ArgumentException("Operación no disponible: " + op);
                foreach (var field in spec.Required)
                    if (!action.TryGetProperty(field, out _)) throw new ArgumentException($"Falta {field} en {op}");
                Execute(candidate, action, op, references, preparedAssets, webJobs);
                changes.Add(op);
            }
            TranscriptOperations.FollowAudioEdits(project, candidate);
            var report = PreflightAuditor.Audit(candidate, autoPurgeOrphans: true);
            if (!report.IsValid) throw new InvalidOperationException(string.Join("; ", report.Errors));
            var planId = Guid.NewGuid().ToString("N");
            foreach (var expired in _plans.Where(p => DateTime.UtcNow - p.Value.CreatedUtc > TimeSpan.FromMinutes(15))
                         .Select(p => p.Key).ToArray()) _plans.Remove(expired);
            if (_plans.Count >= 32) _plans.Remove(_plans.OrderBy(p => p.Value.CreatedUtc).First().Key);
            _plans[planId] = new Plan(candidate, project.Id, revision, ProjectStore.Serialize(project),
                changes.ToArray(), references, webJobs.Distinct().ToArray(), DateTime.UtcNow);
            return new { valid = true, plan_id = planId, project_id = project.Id,
                base_revision = revision, changed_count = changes.Count,
                changes, references };
        }
        catch (Exception ex)
        {
            return new { valid = false, project_id = project.Id, base_revision = revision,
                changed_count = 0, error_index = index, error = ex.Message,
                changes, references };
        }
    }

    public object Apply(JsonElement args, IReadOnlyDictionary<string, Asset>? preparedAssets = null)
    {
        var mode = McpJson.String(args, "mode") ?? "apply";
        if (mode != "apply") throw new ArgumentException("Usa evaluate_changelist o el modo de revisión del Panel de Instrucciones");
        var id = McpJson.String(args, "plan_id");
        if (id == null)
        {
            var evaluated = Evaluate(args, preparedAssets);
            var serialized = JsonSerializer.SerializeToElement(evaluated);
            if (!serialized.GetProperty("valid").GetBoolean()) return evaluated;
            id = serialized.GetProperty("plan_id").GetString();
        }
        if (id == null || !_plans.TryGetValue(id, out var plan))
            throw new ArgumentException("Plan no encontrado; evalúa de nuevo.");
        var current = _controller.CurrentProject;
        var expected = ExpectedRevision(args, current);
        if (expected != plan.Revision || current.Id != plan.ProjectId ||
            DateTime.UtcNow - plan.CreatedUtc > TimeSpan.FromMinutes(15))
            throw new InvalidOperationException("La revisión o el plan han caducado; evalúa de nuevo.");
        _controller.Transport.Pause();
        var committed = _controller.UndoRedo.CommitPrepared(plan.Candidate, plan.Revision,
            "Instrucciones MCP", plan.BaseSnapshot, string.Join(" → ", plan.Changes));
        _plans.Remove(id);
        if (committed)
            foreach (var assetId in plan.WebJobs)
                _ = ResolveWebAfterCommitAsync(assetId);
        return new { applied = committed, prior_revision = plan.Revision,
            new_revision = _controller.CurrentProject.Revision,
            changed_count = committed ? plan.Changes.Length : 0,
            changes = plan.Changes, references = plan.References };
    }

    private async System.Threading.Tasks.Task ResolveWebAfterCommitAsync(string assetId)
    {
        try { await WebMediaService.ResolveAsync(_controller, assetId); }
        catch (Exception ex) { _controller.ReportStatus("Importación web MCP: " + ex.Message); }
    }

    private static int ExpectedRevision(JsonElement args, Project current)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("expected_revision", out var p))
        {
            if (!p.TryGetInt32(out var value) || value != current.Revision)
                throw new InvalidOperationException("La revisión del proyecto cambió; vuelve a consultar get_state.");
            return value;
        }
        return current.Revision;
    }

    private static string Required(JsonElement action, string name, Dictionary<string, string> refs) =>
        Resolve(McpJson.String(action, name) ?? throw new ArgumentException("Falta " + name), refs);
    private static string Resolve(string text, Dictionary<string, string> refs) =>
        text.StartsWith('$') ? refs.TryGetValue(text, out var resolved) ? resolved
            : throw new ArgumentException("Alias desconocido: " + text) : text;
    private static string? Optional(JsonElement a, string name, Dictionary<string, string> refs) =>
        McpJson.String(a, name) is { } value ? Resolve(value, refs) : null;
    private static double Time(JsonElement a, string field)
    {
        var n = McpJson.Number(a, field);
        if (!double.IsFinite(n) || n < 0 || n > 24 * 3600) throw new ArgumentOutOfRangeException(field);
        return n;
    }
    private static void Alias(JsonElement a, Dictionary<string, string> refs, string id)
    {
        var alias = McpJson.String(a, "result");
        if (alias == null) return;
        if (alias.Length == 0 || alias.StartsWith('$') || refs.ContainsKey("$" + alias))
            throw new ArgumentException("Alias vacío o repetido");
        refs["$" + alias] = id;
    }
    private static Sequence Sequence(Project p, JsonElement a, Dictionary<string, string> refs) =>
        p.Sequences.FirstOrDefault(s => s.Id == (Optional(a, "sequence_id", refs) ?? p.ActiveSequenceId))
            ?? throw new ArgumentException("Montaje no encontrado");
    private static Track Track(Sequence s, JsonElement a, Dictionary<string, string> refs) =>
        s.Tracks.FirstOrDefault(t => t.Id == Required(a, "track_id", refs))
            ?? throw new ArgumentException("Pista no encontrada");
    private static Clip Clip(Sequence s, JsonElement a, Dictionary<string, string> refs) =>
        s.Clips.FirstOrDefault(c => c.Id == Required(a, "clip_id", refs))
            ?? throw new ArgumentException("Medio no encontrado");
    private static void Unlocked(Sequence s, Clip c)
    {
        if (s.Tracks.Any(t => t.Id == c.TrackId && t.Locked))
            throw new InvalidOperationException("La pista del medio está bloqueada");
    }
    private static void NoOverlap(Sequence s, Clip c)
    {
        if (s.Clips.Any(x => x.Id != c.Id && x.TrackId == c.TrackId &&
            x.TimelineStart.Seconds < c.EndSeconds - 0.001 &&
            c.TimelineStart.Seconds < x.EndSeconds - 0.001))
            throw new InvalidOperationException("La colocación superpone medios; usa un espacio libre");
    }

    private static void Execute(Project p, JsonElement a, string op, Dictionary<string, string> refs,
        IReadOnlyDictionary<string, Asset>? preparedAssets, List<string> webJobs)
    {
        var s = Sequence(p, a, refs);
        switch (op)
        {
            case "subtitle.set_animation":
                SubtitleAnimation.Set(s, Required(a, "template_json", refs)); break;
            case "subtitle.load_animation":
                var subtitleTemplate = Required(a, "resource_path", refs);
                if (new System.IO.FileInfo(subtitleTemplate).Length > 4 * 1024 * 1024) throw new ArgumentException("La plantilla supera 4 MB.");
                SubtitleAnimation.Set(s, System.IO.File.ReadAllText(subtitleTemplate)); break;
            case "subtitle.clear_animation": s.SubtitleAnimation = null; break;
            case "subtitle.set_animation_enabled":
                if (s.SubtitleAnimation == null) throw new ArgumentException("No hay plantilla de subtítulos.");
                s.SubtitleAnimation.Enabled = McpJson.Bool(a, "enabled"); break;
            case "subtitle.set_animation_parameter":
                if (s.SubtitleAnimation == null) throw new ArgumentException("No hay plantilla de subtítulos.");
                SubtitleAnimation.SetParameter(s.SubtitleAnimation, Required(a, "parameter_id", refs), a.GetProperty("value")); break;
            case "media.rename":
                MediaPoolOperations.RenameAsset(p, Required(a, "asset_id", refs), Required(a, "name", refs)); break;
            case "folder.create":
                Alias(a, refs, MediaPoolOperations.CreateFolder(p, Required(a, "name", refs)).Id); break;
            case "folder.rename":
                MediaPoolOperations.RenameFolder(p, Required(a, "folder_id", refs), Required(a, "name", refs)); break;
            case "media.move_to_folder":
                if (!a.TryGetProperty("folder_id", out var folderDestination) ||
                    folderDestination.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                    throw new ArgumentException("folder_id debe ser una carpeta existente o null para Sin carpeta.");
                var assetIds = McpJson.Array(a, "asset_ids").EnumerateArray().Select(item =>
                    item.ValueKind == JsonValueKind.String ? Resolve(item.GetString()!, refs) :
                        throw new ArgumentException("asset_ids debe contener identificadores de medios.")).ToArray();
                MediaPoolOperations.MoveAssets(p, assetIds,
                    folderDestination.ValueKind == JsonValueKind.Null ? null : Required(a, "folder_id", refs)); break;
            case "sequence.create":
                Alias(a, refs, SequenceOperations.Create(p, Required(a, "name", refs), s).Id); break;
            case "sequence.rename":
                SequenceOperations.Rename(p, Required(a, "sequence_id", refs), Required(a, "name", refs)); break;
            case "sequence.activate":
                p.ActiveSequenceId = Required(a, "sequence_id", refs);
                if (!p.Sequences.Any(x => x.Id == p.ActiveSequenceId))
                    throw new ArgumentException("Montaje no encontrado"); break;
            case "sequence.duplicate":
                Alias(a, refs, SequenceOperations.Duplicate(p, Required(a, "sequence_id", refs)).Id); break;
            case "sequence.delete":
                var sequenceId = Required(a, "sequence_id", refs);
                if (!p.Sequences.Any(x => x.Id == sequenceId)) throw new ArgumentException("Montaje no encontrado");
                SequenceOperations.Delete(p, sequenceId); break;
            case "sequence.set_canvas":
                if (!a.GetProperty("width").TryGetInt32(out var canvasWidth) || !a.GetProperty("height").TryGetInt32(out var canvasHeight))
                    throw new ArgumentException("El ancho y el alto deben ser enteros.");
                SequenceOperations.SetCanvas(p, s.Id, canvasWidth, canvasHeight, McpJson.Bool(a, "fit_all")); break;
            case "sequence.set_export_in": s.MarkInSeconds = Time(a, "at_seconds"); break;
            case "sequence.set_export_out": s.MarkOutSeconds = Time(a, "at_seconds"); break;
            case "sequence.clear_export_range": s.MarkInSeconds = s.MarkOutSeconds = null; break;
            case "track.create":
                var kind = Required(a, "source_kind", refs);
                if (kind is not ("video" or "audio")) throw new ArgumentException("source_kind debe ser video o audio");
                var created = CollisionEngine.AddTrack(s, kind);
                if (McpJson.String(a, "name") is { } trackName) created.Name = trackName.Trim();
                if (string.IsNullOrWhiteSpace(created.Name) || s.Tracks.Any(t => t.Id != created.Id &&
                        t.Name.Equals(created.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Nombre de pista vacío o repetido");
                Alias(a, refs, created.Id); break;
            case "track.rename":
                var renamedTrack = Track(s, a, refs);
                if (renamedTrack.Role == "subtitle") throw new ArgumentException("La pista de subtítulos tiene un nombre fijo.");
                var newName = Required(a, "name", refs).Trim();
                if (newName.Length == 0 || s.Tracks.Any(t => t.Id != renamedTrack.Id &&
                        t.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Nombre de pista vacío o repetido");
                renamedTrack.Name = newName; break;
            case "track.set_locked":
                var lockedTrack = Track(s, a, refs);
                if (lockedTrack.Role == "subtitle") throw new ArgumentException("La pista de subtítulos no se puede bloquear.");
                lockedTrack.Locked = McpJson.Bool(a, "enabled"); break;
            case "track.set_enabled": Track(s, a, refs).Enabled = McpJson.Bool(a, "enabled"); break;
            case "clip.cut":
                var cutClip = Clip(s, a, refs); Unlocked(s, cutClip);
                var cut = Time(a, "at_seconds");
                var right = McpJson.Bool(a, "include_linked", true)
                    ? CollisionEngine.SplitLinkedClips(s, cutClip.Id, cut).FirstOrDefault(c => c.TrackId == cutClip.TrackId)
                    : CollisionEngine.SplitClip(s, cutClip.Id, cut).Right;
                if (right == null) throw new ArgumentException("El corte debe estar dentro del medio");
                if (McpJson.String(a, "result") is { } cutAlias)
                {
                    Alias(a, refs, cutClip.Id);
                    refs["$" + cutAlias + ".left"] = cutClip.Id;
                    refs["$" + cutAlias + ".right"] = right.Id;
                }
                break;
            case "clip.delete":
                var removed = Clip(s, a, refs); Unlocked(s, removed);
                if (McpJson.Bool(a, "ripple")) CollisionEngine.RippleDeleteClip(s, removed.Id);
                else s.Clips.Remove(removed); break;
            case "clip.move":
                var moving = Clip(s, a, refs); Unlocked(s, moving);
                var target = Track(s, a, refs);
                if (target.Locked) throw new InvalidOperationException("Pista destino bloqueada");
                if (s.Tracks.First(t => t.Id == moving.TrackId) is { } movingTrack &&
                    (movingTrack.Kind != target.Kind || (movingTrack.Role == "subtitle") != (target.Role == "subtitle")))
                    throw new ArgumentException("El tipo de pista destino debe coincidir con el origen");
                moving.TrackId = target.Id; moving.TimelineStart = Rational.Time(Time(a, "at_seconds"));
                PlaceEditedClip(s, moving, a); break;
            case "clip.duplicate":
                var source = Clip(s, a, refs); Unlocked(s, source);
                var destination = Track(s, a, refs);
                if (destination.Locked) throw new InvalidOperationException("Pista destino bloqueada");
                if (s.Tracks.First(t => t.Id == source.TrackId) is { } sourceTrack &&
                    (sourceTrack.Kind != destination.Kind || (sourceTrack.Role == "subtitle") != (destination.Role == "subtitle")))
                    throw new ArgumentException("El tipo de pista destino debe coincidir con el origen");
                var duplicated = CollisionEngine.DuplicateClips(s, [source.Id],
                    Time(a, "at_seconds") - source.TimelineStart.Seconds).Single();
                duplicated.TrackId = destination.Id;
                PlaceEditedClip(s, duplicated, a); Alias(a, refs, duplicated.Id); break;
            case "clip.set_enabled":
                var clip = Clip(s, a, refs); Unlocked(s, clip); clip.Enabled = McpJson.Bool(a, "enabled"); break;
            case "gap.close":
                var lane = Track(s, a, refs);
                if (lane.Locked) throw new InvalidOperationException("Pista bloqueada");
                var time = Time(a, "at_seconds");
                var gap = CollisionEngine.DetectGaps(s, lane.Id)
                    .FirstOrDefault(g => g.StartSeconds <= time && time < g.EndSeconds);
                if (gap == null || !CollisionEngine.RippleDeleteGap(s, lane.Id, gap.StartSeconds, gap.EndSeconds))
                    throw new ArgumentException("No hay hueco cerrable en ese punto"); break;
            case "marker.create":
                var marker = new SequenceMarker { Time = Rational.Time(Time(a, "at_seconds")),
                    Name = McpJson.String(a, "name") ?? "Marcador" };
                s.Markers.Add(marker); Alias(a, refs, marker.Id); break;
            case "marker.move":
                s.Markers.Single(m => m.Id == Required(a, "marker_id", refs)).Time =
                    Rational.Time(Time(a, "at_seconds")); break;
            case "marker.rename":
                s.Markers.Single(m => m.Id == Required(a, "marker_id", refs)).Name = Required(a, "name", refs); break;
            case "marker.delete":
                if (s.Markers.RemoveAll(m => m.Id == Required(a, "marker_id", refs)) == 0)
                    throw new ArgumentException("Marcador no encontrado"); break;
            default: ExecuteAdditional(p, s, a, op, refs, preparedAssets, webJobs); break;
        }
        if (s.MarkInSeconds is { } start && s.MarkOutSeconds is { } end && end <= start)
            throw new ArgumentException("La marca Out debe ser posterior a In");
    }
}
