using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Mcp;

internal sealed partial class McpChangeService
{
    private static bool ExecuteProductionEdit(Project project, Sequence sequence, JsonElement action,
        string op, Dictionary<string, string> aliases)
    {
        switch (op)
        {
            case "clip.fit_speed_to_next":
                CollisionEngine.FitSpeedToNextClip(sequence, Clip(sequence, action, aliases).Id);
                break;
            case "transition.apply":
                ApplyTransition(project, sequence, action, aliases);
                break;
            case "transition.set_duration":
            case "transition.remove":
                EditTransition(project, sequence, action, aliases, remove: op == "transition.remove");
                break;
            case "mgt.set_parameter":
                SetMgtParameter(project, sequence, action, aliases);
                break;
            case "text.create":
            case "text.update":
                EditText(project, sequence, action, aliases, create: op == "text.create");
                break;
            case "transcript.import":
                var parsed = TranscriptOperations.Parse(Required(action, "content", aliases),
                    McpJson.String(action, "name") ?? "Transcripción importada", Time(action, "at_seconds"));
                sequence.Transcriptions.Add(parsed);
                Alias(action, aliases, parsed.Id);
                break;
            case "transcript.apply":
                var captions = TranscriptOperations.Apply(project, Required(action, "document_id", aliases));
                if (captions.Count > 0) Alias(action, aliases, captions[0]);
                break;
            case "transcript.set_word":
                var doc = sequence.Transcriptions.Single(d => d.Id == Required(action, "document_id", aliases));
                var word = doc.Words.Single(w => w.Id == Required(action, "word_id", aliases));
                word.Text = Required(action, "text", aliases);
                doc.Validate();
                break;
            case "transcript.set_mode":
                var transcript = sequence.Transcriptions.Single(d => d.Id == Required(action, "document_id", aliases));
                transcript.CaptionMode = Required(action, "mode", aliases);
                transcript.Validate();
                break;
            case "curve.move_keyframe":
            case "curve.remove_keyframe":
            case "curve.disable":
            case "curve.set_bezier":
                EditCurve(sequence, action, aliases, op);
                break;
            case "effect.set_lut":
                AttachLut(project, FindEffect(sequence, action, aliases).Effect, Required(action, "resource_path", aliases));
                break;
            case "mask.add_shape":
            case "mask.update":
            case "mask.remove":
                EditMask(sequence, action, aliases, op);
                break;
            default:
                return false;
        }
        return true;
    }

    private static void EditCurve(Sequence sequence, JsonElement action, Dictionary<string, string> aliases, string op)
    {
        var clip = Clip(sequence, action, aliases);
        Unlocked(sequence, clip);
        var target = KeyframeTarget(clip, Required(action, "parameter_id", aliases));
        if (op == "curve.disable") { KeyframeAnimation.Disable(clip, target); return; }
        var at = Time(action, "at_seconds");
        if (op == "curve.move_keyframe")
        {
            var to = Time(action, "to_seconds");
            if (to < clip.TimelineStart.Seconds || to > clip.EndSeconds ||
                !KeyframeAnimation.Move(clip, target, at, to))
                throw new ArgumentException("Punto o destino de fotograma clave inválido.");
            return;
        }
        var changed = op == "curve.remove_keyframe" ? KeyframeAnimation.Remove(clip, target, at) :
            KeyframeAnimation.SetBezier(clip, target, at, McpJson.Number(action, "x1"), McpJson.Number(action, "y1"),
                McpJson.Number(action, "x2"), McpJson.Number(action, "y2"));
        if (!changed) throw new ArgumentException("No existe un fotograma clave en esa posición.");
    }

    private static void PlaceEditedClip(Sequence sequence, Clip clip, JsonElement action)
    {
        switch (McpJson.String(action, "placement") ?? "empty")
        {
            case "empty":
                NoOverlap(sequence, clip);
                break;
            case "overwrite":
                CollisionEngine.Overwrite(sequence, [clip.Id]);
                break;
            case "insert":
                var at = clip.TimelineStart;
                var duration = clip.Duration;
                var displaced = sequence.Clips.Where(other => other.Id != clip.Id && other.TrackId == clip.TrackId &&
                    other.EndSeconds > at.Seconds).ToArray();
                var ids = displaced.Select(other => other.Id).ToHashSet();
                if (sequence.LinkGroups.Any(group => group.ClipIds.Any(ids.Contains) && group.ClipIds.Any(id => !ids.Contains(id))))
                    throw new InvalidOperationException("La inserción afectaría a clips vinculados fuera de la pista de destino.");
                foreach (var other in displaced)
                {
                    var tail = other;
                    if (other.TimelineStart < at)
                    {
                        tail = CollisionEngine.SplitClip(sequence, other.Id, at.Seconds).Right ??
                            throw new InvalidOperationException("No se puede abrir un corte en esa posición.");
                    }
                    tail.TimelineStart += duration;
                }
                NoOverlap(sequence, clip);
                break;
            default:
                throw new ArgumentException("placement debe ser empty, overwrite o insert.");
        }
    }

    private static int? TransitionFrames(JsonElement action, Sequence sequence)
    {
        if (action.TryGetProperty("duration_frames", out _))
        {
            var frames = McpJson.Int(action, "duration_frames", 0);
            if (frames < 1) throw new ArgumentException("duration_frames debe ser un entero positivo.");
            return frames;
        }
        var duration = OptionalPositive(action, "duration_seconds");
        return duration == null ? null : Math.Max(1, checked((int)Math.Round(duration.Value * sequence.Fps.Seconds)));
    }

    private static void ApplyTransition(Project project, Sequence sequence, JsonElement action, Dictionary<string, string> aliases)
    {
        var kind = Required(action, "catalog_id", aliases);
        var left = Clip(sequence, action, aliases);
        Unlocked(sequence, left);
        var right = Optional(action, "right_clip_id", aliases);
        var frames = TransitionFrames(action, sequence);
        if (AudioTransitionOperations.Supports(kind))
        {
            var added = AudioTransitionOperations.Apply(project, kind, left.Id, right,
                frames == null ? null : frames.Value / sequence.Fps.Seconds);
            Alias(action, aliases, added.Id);
            return;
        }
        var mgt = MgtTransitionCatalog.Find(project, kind);
        if (!VideoTransitionOperations.Supports(kind) && mgt == null)
            throw new ArgumentException("La transición no está en el catálogo.");
        var transition = VideoTransitionOperations.Apply(project, left.Id,
            right ?? throw new ArgumentException("Falta right_clip_id."), kind, frames, mgt);
        Alias(action, aliases, transition.Id);
    }

    private static void EditTransition(Project project, Sequence sequence, JsonElement action,
        Dictionary<string, string> aliases, bool remove)
    {
        var id = Required(action, "transition_id", aliases);
        int? frames = remove ? null : TransitionFrames(action, sequence) ??
            throw new ArgumentException("Indica duration_frames o duration_seconds.");
        if (sequence.VideoTransitions.FirstOrDefault(item => item.Id == id) is { } video)
        {
            Unlocked(sequence, sequence.Clips.Single(clip => clip.Id == video.LeftClipId));
            if (remove) sequence.VideoTransitions.Remove(video);
            else VideoTransitionOperations.Apply(project, video.LeftClipId, video.RightClipId, video.Kind, frames, video.Mgt);
        }
        else if (sequence.AudioTransitions.FirstOrDefault(item => item.Id == id) is { } audio)
        {
            Unlocked(sequence, sequence.Clips.Single(clip => clip.Id == audio.ClipId));
            if (remove) sequence.AudioTransitions.Remove(audio);
            else AudioTransitionOperations.Apply(project, audio.Kind, audio.ClipId, audio.RightClipId,
                frames!.Value / sequence.Fps.Seconds);
        }
        else throw new ArgumentException("Transición no encontrada.");
    }

    private static void SetMgtParameter(Project project, Sequence sequence, JsonElement action, Dictionary<string, string> aliases)
    {
        var parameterId = Required(action, "parameter_id", aliases);
        var value = action.GetProperty("value");
        List<MgtParameter> parameters;
        Dictionary<string, JsonElement> current;
        if (Optional(action, "transition_id", aliases) is { } transitionId)
        {
            var transition = sequence.VideoTransitions.Single(t => t.Id == transitionId);
            Unlocked(sequence, sequence.Clips.Single(clip => clip.Id == transition.LeftClipId));
            parameters = transition.Mgt?.Parameters ?? throw new ArgumentException("La transición no es MGT.");
            current = transition.MgtOverrides;
        }
        else
        {
            var clip = Clip(sequence, action, aliases);
            Unlocked(sequence, clip);
            var asset = project.Assets.Single(item => item.Id == clip.AssetId);
            parameters = asset.MgtParameters;
            current = clip.MgtOverrides;
        }
        var parameter = parameters.SingleOrDefault(item => item.Id == parameterId)
            ?? throw new ArgumentException("Parámetro MGT no encontrado.");
        if (value.ValueKind == JsonValueKind.Null) { current.Remove(parameterId); return; }
        var valid = parameter.Type switch
        {
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) &&
                double.IsFinite(n) && n >= (parameter.Min ?? double.NegativeInfinity) &&
                n <= (parameter.Max ?? double.PositiveInfinity),
            "boolean" or "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "select" or "choice" => value.ValueKind == JsonValueKind.String && parameter.Options.Contains(value.GetString()!),
            "color" => value.ValueKind == JsonValueKind.String && value.GetString() is { } color &&
                color.Length is 7 or 9 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit),
            "text" or "string" => value.ValueKind == JsonValueKind.String,
            _ => false
        };
        if (!valid) throw new ArgumentException("El valor no coincide con el tipo o rango de " + parameterId);
        current[parameterId] = value.Clone();
    }

    private static void EditText(Project project, Sequence sequence, JsonElement action,
        Dictionary<string, string> aliases, bool create)
    {
        var content = McpJson.String(action, "content");
        Clip clip;
        if (create)
        {
            var at = Time(action, "at_seconds");
            var kind = McpJson.String(action, "kind") ?? "title";
            var duration = OptionalPositive(action, "duration_seconds");
            clip = kind switch
            {
                "title" => TitlePlacement.Add(project, at, new TitleBox(0.5, 0.5, 0.88)),
                "subtitle" => SubtitleTrack.Add(project, content!, at, duration ?? 3),
                _ => throw new ArgumentException("kind debe ser title o subtitle.")
            };
            if (kind == "title" && duration.HasValue)
            {
                clip.Duration = Rational.Time(duration.Value);
                NoOverlap(sequence, clip);
            }
            Alias(action, aliases, clip.Id);
        }
        else clip = Clip(sequence, action, aliases);
        Unlocked(sequence, clip);
        var asset = project.Assets.Single(item => item.Id == clip.AssetId);
        var text = asset.TextOverlay ?? throw new ArgumentException("El clip seleccionado no contiene texto.");
        if (content != null) text.Content = content;
        if (action.TryGetProperty("style", out var style))
        {
            if (style.ValueKind != JsonValueKind.Object) throw new ArgumentException("style debe ser un objeto.");
            var fields = JsonSerializer.SerializeToNode(text)!.AsObject();
            foreach (var property in style.EnumerateObject())
            {
                if (property.Name is "content" or "kind" ||
                    !fields.ContainsKey(property.Name) && property.Name != "box_width")
                    throw new ArgumentException("Estilo de texto desconocido: " + property.Name);
                fields[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
            text = fields.Deserialize<TextOverlay>()!;
        }
        text.Validate();
        asset.TextOverlay = text;
        asset.Name = GeneratedText.DisplayName(text);
        foreach (var other in sequence.Clips.Where(item => item.AssetId == asset.Id)) other.Name = asset.Name;
    }

    private static void AttachLut(Project project, EffectInstance effect, string value)
    {
        if (effect.EffectId != GpuLutEffect.Id) throw new ArgumentException("La instancia no es una LUT 3D.");
        var path = Path.GetFullPath(value);
        _ = GpuLutEffect.ParseCube(path);
        var resource = project.EffectResources.FirstOrDefault(item => item.Kind == "lut3d" &&
            !item.Relative && string.Equals(item.Uri, path, StringComparison.OrdinalIgnoreCase));
        if (resource == null)
        {
            resource = new EffectResource { Kind = "lut3d", Uri = path };
            project.EffectResources.Add(resource);
        }
        effect.Parameters["lut"] = new EffectValue { Kind = "resource", ResourceId = resource.Id };
        effect.Enabled = true;
    }

    private static void EditMask(Sequence sequence, JsonElement action, Dictionary<string, string> aliases, string op)
    {
        var clip = Clip(sequence, action, aliases);
        Unlocked(sequence, clip);
        if (sequence.Tracks.Single(track => track.Id == clip.TrackId).Kind != "video")
            throw new ArgumentException("Las máscaras necesitan un clip visual.");
        var masks = ClipMaskStack.Edit(clip);
        if (op == "mask.add_shape")
        {
            var shape = action.GetProperty("shape").Deserialize<ShapeMask>() ?? throw new ArgumentException("Forma requerida.");
            shape.Validate();
            var mask = new ClipMaskLayer { Kind = "shape", Shape = shape, Name = McpJson.String(action, "name") ?? "Máscara" };
            masks.Add(mask);
            Alias(action, aliases, mask.Id);
            return;
        }
        var target = masks.Single(item => item.Id == Required(action, "mask_id", aliases));
        if (op == "mask.remove") { masks.Remove(target); return; }
        foreach (var property in action.GetProperty("settings").EnumerateObject())
        {
            switch (property.Name)
            {
                case "name": target.Name = property.Value.GetString() ?? target.Name; break;
                case "enabled": target.Enabled = property.Value.GetBoolean(); break;
                case "inverted": target.Inverted = property.Value.GetBoolean(); break;
                case "blend": target.Blend = property.Value.GetString() ?? target.Blend; break;
                case "target": target.Target = property.Value.GetString() ?? target.Target; break;
                case "feather_pixels": target.FeatherPixels = property.Value.GetDouble(); break;
                case "expand_pixels": target.ExpandPixels = property.Value.GetDouble(); break;
                default: throw new ArgumentException("Ajuste de máscara desconocido: " + property.Name);
            }
        }
    }
}
