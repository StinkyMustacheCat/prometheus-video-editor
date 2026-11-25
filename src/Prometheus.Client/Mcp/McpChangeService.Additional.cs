using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Prometheus.Client.Services;
using Prometheus.Client.Panels;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Mcp;

internal sealed partial class McpChangeService
{
    public static async Task<IReadOnlyDictionary<string, Asset>> PrepareAssetsAsync(JsonElement args)
    {
        var prepared = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("actions", out var actions) ||
            actions.ValueKind != JsonValueKind.Array) return prepared;
        foreach (var action in actions.EnumerateArray())
        {
            if (McpJson.String(action, "op") != "media.import_local") continue;
            var requested = McpJson.String(action, "resource_path")
                ?? throw new ArgumentException("media.import_local requiere resource_path");
            var path = Path.GetFullPath(requested);
            if (!File.Exists(path)) throw new FileNotFoundException("Medio local no encontrado", path);
            if (!prepared.ContainsKey(path)) prepared[path] = await LocalMediaService.ProbeAsync(path).ConfigureAwait(false);
        }
        return prepared;
    }

    private static void ExecuteAdditional(Project p, Sequence s, JsonElement a, string op,
        Dictionary<string, string> refs, IReadOnlyDictionary<string, Asset>? prepared, List<string> webJobs)
    {
        var originalSequenceId = p.ActiveSequenceId;
        p.ActiveSequenceId = s.Id;
        try { ExecuteAdditionalInSequence(p, s, a, op, refs, prepared, webJobs); }
        finally { p.ActiveSequenceId = originalSequenceId; }
    }

    private static void ExecuteAdditionalInSequence(Project p, Sequence s, JsonElement a, string op,
        Dictionary<string, string> refs, IReadOnlyDictionary<string, Asset>? prepared, List<string> webJobs)
    {
        if (ExecuteProductionEdit(p, s, a, op, refs)) return;
        switch (op)
        {
            case "media.import_local":
            {
                var path = Path.GetFullPath(Required(a, "resource_path", refs));
                if (prepared == null || !prepared.TryGetValue(path, out var probed))
                    throw new InvalidOperationException("El archivo requiere preparación antes de evaluar el lote");
                var asset = p.Assets.FirstOrDefault(x => x.Source.Kind == "local" &&
                    string.Equals(x.Source.Uri, path, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    asset = JsonSerializer.Deserialize<Asset>(JsonSerializer.Serialize(probed))!;
                    p.Assets.Add(asset);
                }
                var trackId = Optional(a, "track_id", refs);
                string? clipId = null;
                if (trackId != null)
                {
                    var at = OptionalTime(a, "at_seconds", 0);
                    clipId = ImportPlacement.PlaceAsset(p, asset.Id, trackId, at,
                        OptionalTime(a, "source_in_seconds", 0), OptionalPositive(a, "duration_seconds")).PrimaryClipId;
                }
                AliasMedia(a, refs, clipId ?? asset.Id, asset.Id, clipId);
                break;
            }
            case "media.place_asset":
            {
                var assetId = Required(a, "asset_id", refs);
                var asset = p.Assets.FirstOrDefault(x => x.Id == assetId)
                    ?? throw new ArgumentException("Medio no encontrado");
                if (asset.Source.Kind == "web" && asset.Streams.Count == 0)
                {
                    var preferred = s.Tracks.FirstOrDefault(t => t.Id == Required(a, "track_id", refs))
                        ?? throw new ArgumentException("Pista no encontrada");
                    var provisional = WebMediaService.CreateProvisionalStream(preferred.Kind);
                    provisional.Duration = Rational.Time(5);
                    asset.Streams.Add(provisional);
                }
                var placed = ImportPlacement.PlaceAsset(p, asset.Id, Required(a, "track_id", refs),
                    Time(a, "at_seconds"), OptionalTime(a, "source_in_seconds", 0),
                    OptionalPositive(a, "duration_seconds"));
                if (asset.Source.Kind == "web" && WebMediaService.IsPending(asset))
                {
                    var provisional = s.Clips.Single(c => c.Id == placed.PrimaryClipId);
                    provisional.Name = "Preparando medio…";
                    provisional.WebAutoDurationSeconds = provisional.Duration.Seconds;
                }
                AliasMedia(a, refs, placed.PrimaryClipId, asset.Id, placed.PrimaryClipId);
                if (asset.Source.Kind == "web" && WebMediaService.IsPending(asset)) webJobs.Add(asset.Id);
                break;
            }
            case "media.import_url":
            {
                var url = Required(a, "url", refs);
                if (!WebMediaService.TryUrl(url, out var uri)) throw new ArgumentException("Enlace HTTP/HTTPS inválido");
                var source = uri.ToString();
                var asset = p.Assets.FirstOrDefault(x => x.Source.Kind == "web" && x.Source.Uri == source);
                if (asset == null)
                {
                    var name = WebMediaNaming.InitialName(uri);
                    asset = new Asset { Id = Guid.NewGuid().ToString("N"), Name = name, WebAutoName = name,
                        WebPreferredQuality = WebDownloadSettings.QualityId(WebDownloadSettings.Current.Quality),
                        Source = new MediaSource { Kind = "web", Uri = source, PageUrl = source,
                            ImportedAt = DateTime.UtcNow.ToString("O") } };
                    p.Assets.Add(asset);
                }
                var trackId = Optional(a, "track_id", refs);
                string? clipId = null;
                if (trackId != null)
                {
                    var track = s.Tracks.FirstOrDefault(t => t.Id == trackId && !t.Locked)
                        ?? throw new ArgumentException("Pista destino no encontrada o bloqueada");
                    if (!WebMediaService.CompatibleWithKnownExtension(uri, track.Kind))
                        throw new ArgumentException("Enlace incompatible con la pista destino");
                    if (!WebMediaService.IsPending(asset))
                    {
                        clipId = ImportPlacement.PlaceAsset(p, asset.Id, track.Id,
                            OptionalTime(a, "at_seconds", 0), OptionalTime(a, "source_in_seconds", 0),
                            OptionalPositive(a, "duration_seconds")).PrimaryClipId;
                        AliasMedia(a, refs, clipId, asset.Id, clipId);
                        break;
                    }
                    var stream = asset.Streams.FirstOrDefault(x => track.Kind == "audio" ? x.Kind == "audio" : x.Kind is "video" or "image");
                    if (stream == null)
                    {
                        stream = WebMediaService.CreateProvisionalStream(track.Kind);
                        stream.Duration = Rational.Time(5);
                        asset.Streams.Add(stream);
                    }
                    var at = OptionalTime(a, "at_seconds", 0);
                    var sourceIn = OptionalTime(a, "source_in_seconds", 0);
                    var requestedDuration = OptionalPositive(a, "duration_seconds");
                    var duration = requestedDuration ?? Math.Max(1 / Math.Max(1, s.Fps.Seconds),
                        (asset.RecommendedDuration?.Seconds ?? 5) - sourceIn);
                    stream.Duration = Rational.Time(Math.Max(stream.Duration.Seconds, sourceIn + duration));
                    var destination = ImportPlacement.PreferredFreeTrack(s, track.Id, at, duration);
                    var clip = new Clip { Name = "Preparando medio…", AssetId = asset.Id,
                        StreamId = stream.Id, TrackId = destination.Id,
                        TimelineStart = Rational.Time(at), SourceIn = Rational.Time(sourceIn), Duration = Rational.Time(duration),
                        WebAutoDurationSeconds = duration,
                        WebRequestedOutSeconds = requestedDuration.HasValue ? sourceIn + requestedDuration.Value : null };
                    s.Clips.Add(clip);
                    clipId = clip.Id;
                }
                else if (asset.Streams.Count == 0 && McpJson.String(a, "source_kind") is { } sourceKind)
                {
                    if (sourceKind is not ("video" or "audio")) throw new ArgumentException("source_kind inválido");
                    asset.Streams.Add(WebMediaService.CreateProvisionalStream(sourceKind));
                }
                AliasMedia(a, refs, clipId ?? asset.Id, asset.Id, clipId);
                if (WebMediaService.IsPending(asset)) webJobs.Add(asset.Id);
                break;
            }
            case "media.create_placeholder":
            {
                var duration = Positive(McpJson.Number(a, "duration_seconds"), "duration_seconds");
                var requested = Optional(a, "track_id", refs);
                var kind = McpJson.String(a, "source_kind") ??
                    (requested == null ? "video" : s.Tracks.FirstOrDefault(t => t.Id == requested)?.Kind ?? "video");
                if (kind is not ("video" or "audio")) throw new ArgumentException("source_kind inválido");
                var at = OptionalTime(a, "at_seconds", 0);
                var track = requested == null ? ImportPlacement.LowestFreeTrack(s, kind, at, duration)
                    : ImportPlacement.PreferredFreeTrack(s, requested, at, duration);
                if (track.Kind != kind) throw new ArgumentException("Tipo de pista incompatible");
                var clip = PlaceholderService.Create(p, track.Id, at, duration);
                AliasMedia(a, refs, clip.Id, clip.AssetId, clip.Id);
                break;
            }
            case "placeholder.fill_asset":
                PlaceholderService.Replace(p, Required(a, "clip_id", refs), Required(a, "asset_id", refs),
                    OptionalTime(a, "source_in_seconds", 0)); break;
            case "nested.create":
            {
                var id = NestedMediaOperations.Create(p, Ids(a, "clip_ids", refs),
                    McpJson.String(a, "name") ?? "Medio anidado");
                var clip = s.Clips.Single(c => c.Id == id);
                AliasMedia(a, refs, id, clip.AssetId, id); break;
            }
            case "nested.unnest":
            {
                var ids = NestedMediaOperations.Unnest(p, Required(a, "clip_id", refs));
                if (ids.Count == 0) throw new InvalidOperationException("El medio anidado no devolvió hijos");
                var alias = McpJson.String(a, "result");
                if (alias != null)
                {
                    Alias(a, refs, ids[0]);
                    for (var i = 0; i < ids.Count; i++) refs[$"${alias}.{i + 1}"] = ids[i];
                }
                break;
            }
            case "nested.update_child":
            {
                var outerId = Required(a, "clip_id", refs);
                var childId = Required(a, "other_clip_id", refs);
                var child = NestedMediaOperations.FindChild(p, outerId, childId)
                    ?? throw new ArgumentException("Hijo anidado no encontrado");
                NestedMediaOperations.UpdateChild(p, outerId, childId, Time(a, "at_seconds"),
                    Positive(McpJson.Number(a, "duration_seconds"), "duration_seconds"),
                    Optional(a, "track_id", refs) ?? child.Track.Id);
                break;
            }
            case "nested.remove_child":
                NestedMediaOperations.RemoveChild(p, Required(a, "clip_id", refs),
                    Required(a, "other_clip_id", refs)); break;
            case "clip.group":
                if (EditGroupOperations.Group(s, Ids(a, "clip_ids", refs)) == null)
                    throw new ArgumentException("Se requieren dos medios para agrupar"); break;
            case "clip.ungroup": EditGroupOperations.Ungroup(s, Ids(a, "clip_ids", refs)); break;
            case "clip.set_transform": SetTransform(s, a, refs); break;
            case "clip.set_audio": SetAudio(s, Clip(s, a, refs), null, a); break;
            case "track.set_audio": SetAudio(s, null, Track(s, a, refs), a); break;
            case "effect.apply": ApplyEffect(p, s, a, refs); break;
            case "effect.remove": RemoveEffect(s, a, refs); break;
            case "effect.set_parameter": SetEffectParameter(s, a, refs); break;
            case "effect.set_enabled": FindEffect(s, a, refs).Effect.Enabled = McpJson.Bool(a, "enabled"); break;
            case "effect.move": MoveEffect(s, a, refs); break;
            case "effect.remove_instance":
            {
                var (effects, clip, effect) = FindEffect(s, a, refs);
                effects.Remove(effect);
                if (clip != null) KeyframeAnimation.RemoveForEffect(clip, effect.Id);
                break;
            }
            case "curve.set_keyframe": SetKeyframe(s, a, refs); break;
            case "curve.set_interpolation": SetInterpolation(s, a, refs); break;
            case "clip.trim": Trim(s, p, a, refs); break;
            case "clip.slip": Slip(s, p, a, refs); break;
            case "clip.roll":
            {
                var left = Required(a, "left_clip_id", refs);
                var right = Required(a, "right_clip_id", refs);
                if (!RollEditOperations.Apply(p, s, left, right, Time(a, "at_seconds")))
                    throw new ArgumentException("El punto de unión no admite roll edit");
                break;
            }
            case "clip.set_speed":
                CollisionEngine.SetLinkedClipSpeed(s, Required(a, "clip_id", refs),
                    McpJson.Number(a, "number") / 100); break;
            case "clip.link": Link(s, a, refs); break;
            case "clip.unlink": Unlink(s, a, refs); break;
            case "track.delete": DeleteTrack(s, a, refs); break;
            default: throw new ArgumentException("Operación no implementada: " + op);
        }
    }

    private static void AliasMedia(JsonElement a, Dictionary<string, string> refs,
        string primary, string assetId, string? clipId)
    {
        var name = McpJson.String(a, "result");
        if (name == null) return;
        Alias(a, refs, primary);
        refs["$" + name + ".asset_id"] = assetId;
        if (clipId != null) refs["$" + name + ".clip_id"] = clipId;
    }

    private static string[] Ids(JsonElement a, string field, Dictionary<string, string> refs) =>
        McpJson.Array(a, field).EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String
            ? Resolve(x.GetString()!, refs) : throw new ArgumentException("ID inválido en " + field)).ToArray();
    private static double OptionalTime(JsonElement a, string field, double fallback) =>
        a.TryGetProperty(field, out _) ? Time(a, field) : fallback;
    private static double? OptionalPositive(JsonElement a, string field) =>
        a.TryGetProperty(field, out _) ? Positive(McpJson.Number(a, field), field) : null;
    private static double Positive(double value, string field) =>
        double.IsFinite(value) && value >= 1.0 / 60 && value <= 24 * 3600
            ? value : throw new ArgumentOutOfRangeException(field);

    private static void SetTransform(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var clip = Clip(s, a, refs); Unlocked(s, clip);
        var parameter = Required(a, "parameter_id", refs);
        double Number()
        {
            var value = McpJson.Number(a, "number");
            if (!double.IsFinite(value)) throw new ArgumentException("Valor no finito");
            return value;
        }
        switch (parameter)
        {
            case "x": clip.Visual.PositionX = Number(); break;
            case "y": clip.Visual.PositionY = Number(); break;
            case "scale": clip.Visual.ScaleX = Number(); if (clip.Visual.ScaleLinked) clip.Visual.ScaleY = clip.Visual.ScaleX; break;
            case "scale_y": clip.Visual.ScaleY = Number(); break;
            case "rotation": clip.Visual.RotationDegrees = Number(); break;
            case "opacity": clip.Visual.Opacity = Math.Clamp(Number(), 0, 1); break;
            case "blend_mode":
                if (s.Tracks.First(t => t.Id == clip.TrackId).Kind != "video")
                    throw new ArgumentException("El modo de fusión requiere un medio visual.");
                var blendMode = McpJson.String(a, "text") ?? McpJson.String(a, "value")
                    ?? throw new ArgumentException("Falta text con el modo de fusión.");
                if (!ClipBlendModes.IsSupported(blendMode)) throw new ArgumentException("Modo de fusión no disponible: " + blendMode);
                clip.Visual.BlendMode = blendMode; break;
            case "anchor_x": clip.Visual.AnchorX = Number(); break;
            case "anchor_y": clip.Visual.AnchorY = Number(); break;
            case "scale_linked":
                if (!a.TryGetProperty("value", out var linked) || linked.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("Falta value booleano");
                clip.Visual.ScaleLinked = linked.GetBoolean(); break;
            case "mode":
                var mode = McpJson.String(a, "text") ?? throw new ArgumentException("Falta text");
                if (mode is not ("fit" or "width" or "height" or "fill" or "stretch"))
                    throw new ArgumentException("Encuadre no disponible");
                clip.Visual.Framing = mode; break;
            default: throw new ArgumentException("Parámetro visual no disponible: " + parameter);
        }
    }

    private static void SetAudio(Sequence s, Clip? clip, Track? track, JsonElement a)
    {
        var lane = track ?? s.Tracks.First(t => t.Id == clip!.TrackId);
        if (lane.Kind != "audio" || lane.Locked) throw new InvalidOperationException("Requiere pista de audio desbloqueada");
        if (!a.TryGetProperty("gain_db", out _) && !a.TryGetProperty("balance", out _))
            throw new ArgumentException("Indica gain_db o balance");
        if (a.TryGetProperty("gain_db", out _))
        {
            var gain = McpJson.Number(a, "gain_db");
            if (!double.IsFinite(gain) || gain is < -60 or > 12) throw new ArgumentOutOfRangeException("gain_db");
            if (clip != null) clip.GainDb = gain; else lane.GainDb = gain;
        }
        if (a.TryGetProperty("balance", out _))
        {
            var balance = McpJson.Number(a, "balance");
            if (!double.IsFinite(balance) || balance is < -1 or > 1) throw new ArgumentOutOfRangeException("balance");
            if (clip != null) clip.Balance = balance; else lane.Balance = balance;
        }
    }

    private static Clip[] EffectTargets(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var scope = Required(a, "scope", refs);
        var ids = scope switch
        {
            "clip" => [Required(a, "clip_id", refs)],
            "selection" => Ids(a, "clip_ids", refs),
            _ => throw new ArgumentException("Los efectos de pista/secuencia aún no están disponibles")
        };
        if (ids.Length == 0) throw new ArgumentException("Selección vacía");
        var clips = ids.Select(id => s.Clips.FirstOrDefault(c => c.Id == id)
            ?? throw new ArgumentException("Medio no encontrado: " + id)).DistinctBy(c => c.Id).ToArray();
        foreach (var c in clips) Unlocked(s, c);
        return clips;
    }

    private static void ApplyEffect(Project p, Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var id = Required(a, "catalog_id", refs);
        var entry = EffectCatalog.Available.FirstOrDefault(e => e.Id == id && e.Kind != "transitions")
            ?? throw new ArgumentException("Efecto no disponible: " + id);
        if (McpJson.String(a, "scope") == "track")
        {
            var track = Track(s, a, refs);
            if (track.Locked || track.Role == "subtitle" || !TimelinePanelView.SupportsTrackEffect(track, entry))
                throw new ArgumentException("Efecto incompatible con la pista o pista bloqueada.");
            var draft = new Clip { Effects = track.Effects };
            if (EffectApplicationService.ApplyToClip(draft, [id]) == 0)
                throw new InvalidOperationException("El efecto ya está activo en la pista.");
            Alias(a, refs, track.Effects.Last(effect => effect.EffectId == id).Id);
            return;
        }
        var lutPath = id == GpuLutEffect.Id ? Required(a, "resource_path", refs) : null;
        var targets = EffectTargets(s, a, refs);
        var added = new List<string>();
        foreach (var clip in targets)
        {
            var track = s.Tracks.First(t => t.Id == clip.TrackId);
            if (track.Kind != entry.Kind || PlaceholderService.IsPlaceholder(p.Assets.FirstOrDefault(x => x.Id == clip.AssetId)))
                throw new ArgumentException("Efecto incompatible con el medio o espacio reservado");
            var previous = clip.Effects.Select(e => e.Id).ToHashSet();
            if (EffectApplicationService.ApplyToClip(clip, [id]) == 0)
                throw new InvalidOperationException("El efecto ya está activo en el medio");
            var instance = clip.Effects.FirstOrDefault(e => !previous.Contains(e.Id));
            if (instance != null)
            {
                if (lutPath != null) AttachLut(p, instance, lutPath);
                added.Add(instance.Id);
            }
        }
        if (added.Count == 1) Alias(a, refs, added[0]);
    }

    private static void RemoveEffect(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var id = Required(a, "catalog_id", refs);
        if (McpJson.String(a, "scope") == "track")
        {
            var track = Track(s, a, refs);
            if (track.Locked) throw new InvalidOperationException("Pista bloqueada.");
            if (track.Effects.RemoveAll(effect => id == "*" || effect.EffectId == id) == 0)
                throw new ArgumentException("No hay efecto que eliminar.");
            return;
        }
        var count = 0;
        foreach (var clip in EffectTargets(s, a, refs))
        {
            var removed = clip.Effects.Where(e => id == "*" || e.EffectId == id).Select(e => e.Id).ToArray();
            count += clip.Effects.RemoveAll(e => removed.Contains(e.Id));
            foreach (var instanceId in removed) KeyframeAnimation.RemoveForEffect(clip, instanceId);
        }
        if (count == 0) throw new ArgumentException("No hay efecto que eliminar");
    }

    private static (List<EffectInstance> Effects, Clip? Clip, EffectInstance Effect) FindEffect(Sequence s, JsonElement a,
        Dictionary<string, string> refs)
    {
        var id = Required(a, "effect_id", refs);
        foreach (var clip in s.Clips)
        {
            var effect = clip.Effects.FirstOrDefault(e => e.Id == id);
            if (effect == null) continue;
            Unlocked(s, clip);
            return (clip.Effects, clip, effect);
        }
        foreach (var track in s.Tracks)
        {
            var effect = track.Effects.FirstOrDefault(item => item.Id == id);
            if (effect == null) continue;
            if (track.Locked) throw new InvalidOperationException("Pista bloqueada.");
            return (track.Effects, null, effect);
        }
        throw new ArgumentException("Instancia de efecto no encontrada");
    }

    private static void SetEffectParameter(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var (_, _, effect) = FindEffect(s, a, refs);
        var id = Required(a, "parameter_id", refs);
        if (!effect.Parameters.TryGetValue(id, out var value))
            throw new ArgumentException("Parámetro del efecto no encontrado");
        if (a.TryGetProperty("number", out _))
        {
            var n = McpJson.Number(a, "number");
            if (!double.IsFinite(n) || value.Kind != "number") throw new ArgumentException("Valor numérico inválido");
            value.Number = n;
        }
        else if (a.TryGetProperty("text", out _))
        {
            if (value.Kind != "text") throw new ArgumentException("El parámetro no acepta texto");
            value.Text = Required(a, "text", refs);
        }
        else if (a.TryGetProperty("value", out var boolean) && boolean.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            if (value.Kind != "boolean") throw new ArgumentException("El parámetro no acepta booleanos");
            value.Boolean = boolean.GetBoolean();
        }
        else throw new ArgumentException("Indica number, text o value");
    }

    private static void MoveEffect(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var (effects, _, effect) = FindEffect(s, a, refs);
        var index = (int)McpJson.Number(a, "index");
        if (index < 0 || index >= effects.Count) throw new ArgumentOutOfRangeException("index");
        effects.Remove(effect); effects.Insert(index, effect);
    }

    private static string KeyframeTarget(Clip clip, string parameter)
    {
        if (KeyframeAnimation.VisualTargets.ContainsKey(parameter)) return parameter;
        var parts = parameter.Split('.', 3);
        if (parts.Length == 3 && parts[0] == "effect" &&
            clip.Effects.Any(e => e.Id == parts[1] && e.Parameters.TryGetValue(parts[2], out var v) && v.Kind == "number"))
            return parameter;
        throw new ArgumentException("Parámetro animable no disponible: " + parameter);
    }

    private static void SetKeyframe(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var clip = Clip(s, a, refs); Unlocked(s, clip);
        var at = Time(a, "at_seconds");
        if (at < clip.TimelineStart.Seconds || at > clip.EndSeconds)
            throw new ArgumentException("Fotograma clave fuera del clip");
        var value = McpJson.Number(a, "number");
        if (!double.IsFinite(value)) throw new ArgumentException("Valor no finito");
        KeyframeAnimation.Set(clip, KeyframeTarget(clip, Required(a, "parameter_id", refs)), at, value);
    }

    private static void SetInterpolation(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var clip = Clip(s, a, refs); Unlocked(s, clip);
        var target = KeyframeTarget(clip, Required(a, "parameter_id", refs));
        var interpolation = Required(a, "text", refs);
        if (!KeyframeAnimation.Interpolations.Contains(interpolation))
            throw new ArgumentException("Interpolación no disponible");
        var track = KeyframeAnimation.Find(clip, target)
            ?? throw new ArgumentException("No hay fotogramas clave para este parámetro");
        foreach (var point in track.Points) point.Interpolation = interpolation;
    }

    private static void Trim(Sequence s, Project p, JsonElement a, Dictionary<string, string> refs)
    {
        var clip = Clip(s, a, refs); Unlocked(s, clip);
        var edge = Required(a, "edge", refs);
        var duration = Positive(McpJson.Number(a, "duration_seconds"), "duration_seconds");
        if (edge is not ("left" or "right")) throw new ArgumentException("edge debe ser left o right");
        var old = clip.Duration.Seconds;
        if (Math.Abs(duration - old) < 1.0 / 48000) return;
        var image = p.Assets.FirstOrDefault(x => x.Id == clip.AssetId)?.Streams
            .Any(stream => stream.Id == clip.StreamId && stream.Kind == "image") == true;
        if (McpJson.Bool(a, "ripple"))
        {
            if (duration >= old) throw new ArgumentException("El recorte con rizo solo admite acortar");
            var cut = edge == "left" ? clip.EndSeconds - duration : clip.TimelineStart.Seconds + duration;
            if (!CollisionEngine.RippleTrimClip(s, clip.Id, edge == "left", cut))
                throw new InvalidOperationException("No se pudo aplicar el recorte con rizo");
            return;
        }
        var start = edge == "left" ? clip.EndSeconds - duration : clip.TimelineStart.Seconds;
        var sourceIn = edge == "left" && !image
            ? clip.SourceIn.Seconds + (start - clip.TimelineStart.Seconds) * clip.Speed.Seconds
            : clip.SourceIn.Seconds;
        if (start < 0 || sourceIn < -0.0001)
            throw new ArgumentException("El recorte supera el inicio del medio");
        if (!image && clip.AvailableSourceDurationSeconds is { } sourceLength &&
            sourceIn + duration * clip.Speed.Seconds > sourceLength + 0.0001)
            throw new ArgumentException("El recorte supera la duración de origen");
        var oldStart = clip.TimelineStart.Seconds;
        clip.TimelineStart = Rational.Time(start);
        clip.Duration = Rational.Time(duration);
        clip.SourceIn = Rational.Time(image ? 0 : Math.Max(0, sourceIn));
        if (edge == "left") KeyframeAnimation.ShiftForLeftTrim(clip, start - oldStart);
        NoOverlap(s, clip);
    }

    private static void Slip(Sequence s, Project p, JsonElement a, Dictionary<string, string> refs)
    {
        var clip = Clip(s, a, refs); Unlocked(s, clip);
        var stream = p.Assets.FirstOrDefault(x => x.Id == clip.AssetId)?.Streams
            .FirstOrDefault(x => x.Id == clip.StreamId);
        if (stream?.Kind == "image") throw new ArgumentException("Una imagen fija no requiere slip");
        var sourceIn = Time(a, "source_in_seconds");
        var available = clip.AvailableSourceDurationSeconds ?? stream?.Duration.Seconds;
        if (available is > 0 && sourceIn + clip.Duration.Seconds * clip.Speed.Seconds > available + 0.0001)
            throw new ArgumentException("El rango excede el medio de origen");
        clip.SourceIn = Rational.Time(sourceIn);
    }

    private static void Link(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var ids = Ids(a, "clip_ids", refs).Distinct().ToArray();
        if (ids.Length < 2 || ids.Any(id => !s.Clips.Any(c => c.Id == id)))
            throw new ArgumentException("Se requieren dos o más medios existentes");
        if (ids.Any(id => s.LinkGroups.Any(g => g.ClipIds.Contains(id))))
            throw new InvalidOperationException("Desvincula los medios antes de crear otro vínculo");
        foreach (var id in ids) Unlocked(s, s.Clips.Single(c => c.Id == id));
        s.LinkGroups.Add(new LinkGroup { ClipIds = ids.ToList() });
    }

    private static void Unlink(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var clip = Clip(s, a, refs); Unlocked(s, clip);
        var group = s.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(clip.Id))
            ?? throw new ArgumentException("El medio no está vinculado");
        group.ClipIds.Remove(clip.Id);
        if (group.ClipIds.Count < 2) s.LinkGroups.Remove(group);
    }

    private static void DeleteTrack(Sequence s, JsonElement a, Dictionary<string, string> refs)
    {
        var track = Track(s, a, refs);
        TrackOperations.Delete(s, track.Id);
    }
}
