using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public static class LinkShelfService
{
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>\""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int AddText(ProjectController controller, string? text)
    {
        var sources = new List<string>();
        foreach (Match match in UrlPattern.Matches(text ?? string.Empty))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ')', ']');
            if (WebMediaService.TryUrl(candidate, out var uri)) sources.Add(uri.ToString());
        }
        foreach (var line in (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Trim().Trim('"');
            if (File.Exists(candidate)) sources.Add(Path.GetFullPath(candidate));
        }
        var distinct = sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = controller.CurrentProject.LinkShelf.GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var restore = distinct.Where(x => existing.TryGetValue(x, out var item) && item.PlacedAt != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = distinct.Where(x => !existing.ContainsKey(x)).Select(source => {
            var matching = controller.CurrentProject.Assets.FirstOrDefault(a => a.Source.Uri == source);
            var web = WebMediaService.TryUrl(source, out var uri);
            return new LinkShelfItem {
                Source = source, Kind = web ? "web" : "local",
                Name = matching?.Name ?? (web ? WebMediaNaming.InitialName(uri) : Path.GetFileName(source)),
                WebMetadata = matching?.WebMetadata,
                DurationSeconds = matching?.RecommendedDuration?.Seconds,
                MediaKind = matching == null ? null : MediaKindFor(matching),
                MetadataStatus = matching?.WebMetadata != null ? "ready" : null
            };
        }).ToArray();
        if (additions.Length > 0 || restore.Count > 0) controller.UndoRedo.Commit(p => {
            foreach (var item in p.LinkShelf.Where(x => restore.Contains(x.Source))) item.PlacedAt = null;
            p.LinkShelf.AddRange(additions);
        }, "Añadir referencias a la bandeja");
        foreach (var item in additions.Where(x => x.Kind == "web" && x.MetadataStatus == null)) _ = WebShelfMetadataService.ProbeAsync(controller, item.Id);
        var added = additions.Length + restore.Count;
        controller.ReportStatus(added > 0 ? $"Bandeja: {added} referencia(s) pendientes" : "No se encontraron enlaces o archivos nuevos");
        return added;
    }

    public static void MarkPlaced(Project project, string source)
    {
        var item = project.LinkShelf.FirstOrDefault(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase));
        if (item == null && WebMediaService.TryUrl(source, out var uri))
        {
            var asset = project.Assets.FirstOrDefault(a => string.Equals(a.Source.Uri, uri.ToString(), StringComparison.OrdinalIgnoreCase));
            item = new LinkShelfItem
            {
                Source = uri.ToString(), Kind = "web",
                Name = asset?.Name ?? WebMediaNaming.InitialName(uri),
                WebMetadata = asset?.WebMetadata,
                DurationSeconds = asset?.RecommendedDuration?.Seconds,
                MediaKind = asset == null ? null : MediaKindFor(asset),
                MetadataStatus = asset != null && WebMediaService.IsPending(asset) ? "probing" : "ready"
            };
            project.LinkShelf.Add(item);
        }
        if (item != null) item.PlacedAt = DateTime.UtcNow;
    }

    public static void Restore(ProjectController controller, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet();
        if (selected.Count == 0) return;
        controller.UndoRedo.Commit(p => {
            foreach (var item in p.LinkShelf.Where(x => selected.Contains(x.Id))) item.PlacedAt = null;
        }, "Devolver referencias a pendientes");
    }

    public static async Task<int> InsertBatchAtAsync(ProjectController controller, IEnumerable<string> ids, double at)
    {
        var projectId = controller.CurrentProject.Id;
        var sequenceId = controller.CurrentProject.ActiveSequence?.Id;
        if (sequenceId == null) return 0;
        var wanted = ids.ToHashSet();
        var ordered = controller.CurrentProject.LinkShelf.Where(x => wanted.Contains(x.Id)).ToArray();
        var plans = new List<(string Id, double Duration)>();
        foreach (var initial in ordered)
        {
            if (initial.Kind == "local" && initial.DurationSeconds is not > 0)
                await PromoteAsync(controller, initial.Id);
            if (controller.CurrentProject.Id != projectId || controller.CurrentProject.ActiveSequence?.Id != sequenceId) return 0;
            var item = controller.CurrentProject.LinkShelf.FirstOrDefault(x => x.Id == initial.Id);
            var duration = item?.DurationSeconds ?? controller.CurrentProject.Assets
                .FirstOrDefault(a => a.Source.Uri == initial.Source)?.RecommendedDuration?.Seconds;
            if (duration is not > 0 || !double.IsFinite(duration.Value))
            {
                controller.ReportStatus("Falta la duración de " + initial.Name + "; espera sus datos antes de colocar varios");
                return 0;
            }
            plans.Add((initial.Id, duration.Value));
        }
        var cursor = Math.Max(0, at);
        var tasks = new List<Task<Asset?>>();
        foreach (var plan in plans)
        {
            tasks.Add(InsertAtAsync(controller, plan.Id, cursor));
            cursor += plan.Duration;
        }
        var results = await Task.WhenAll(tasks);
        return results.Count(x => x != null);
    }

    public static void ProbeMissing(ProjectController controller)
    {
        foreach (var item in controller.CurrentProject.LinkShelf.Where(x => x.Kind == "web" && x.MetadataStatus == null))
            _ = WebShelfMetadataService.ProbeAsync(controller, item.Id);
    }

    public static async Task<Asset?> PromoteAsync(ProjectController controller, string id)
    {
        var item = controller.CurrentProject.LinkShelf.FirstOrDefault(x => x.Id == id);
        if (item == null) return null;
        var projectId = controller.CurrentProject.Id;
        Asset? asset;
        if (item.Kind == "web")
        {
            asset = await WebMediaService.ImportAsync(controller, item.Source, knownMetadata: item.WebMetadata,
                knownDuration: item.DurationSeconds);
        }
        else
        {
            if (!File.Exists(item.Source)) { controller.ReportStatus("Archivo de la bandeja no disponible"); return null; }
            asset = controller.CurrentProject.Assets.FirstOrDefault(a => string.Equals(a.Source.Uri, item.Source, StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                asset = await LocalMediaService.ProbeAsync(item.Source);
                if (controller.CurrentProject.Id != projectId) return null;
                var imported = asset;
                controller.UndoRedo.Commit(p => p.Assets.Add(imported), "Importar referencia local");
            }
        }
        if (controller.CurrentProject.Id != projectId) return null;
        if (asset == null) return null;
        if (controller.CurrentProject.LinkShelf.Any(x => x.Id == item.Id) &&
            (item.Name != asset.Name || item.Kind == "local" &&
                (item.DurationSeconds != asset.RecommendedDuration?.Seconds || item.MediaKind != MediaKindFor(asset))))
        {
            var shelfId = item.Id;
            var resolvedName = asset.Name;
            var duration = asset.RecommendedDuration?.Seconds;
            var mediaKind = MediaKindFor(asset);
            controller.UndoRedo.Commit(p => {
                var target = p.LinkShelf.First(x => x.Id == shelfId);
                target.Name = resolvedName;
                if (target.Kind == "local")
                {
                    target.DurationSeconds = duration;
                    target.MediaKind = mediaKind;
                }
            }, "Actualizar datos en Bandeja");
        }
        return asset;
    }

    public static async Task<Asset?> InsertAtAsync(ProjectController controller, string id, double at,
        double sourceIn = 0, double? sourceOut = null)
    {
        var item = controller.CurrentProject.LinkShelf.FirstOrDefault(x => x.Id == id);
        if (item == null || controller.CurrentProject.ActiveSequence == null) return null;
        var total = item.DurationSeconds ?? controller.CurrentProject.Assets
            .FirstOrDefault(a => a.Source.Uri == item.Source)?.RecommendedDuration?.Seconds;
        if (!double.IsFinite(at) || !double.IsFinite(sourceIn) || sourceIn < 0 ||
            sourceOut.HasValue && (!double.IsFinite(sourceOut.Value) || sourceOut.Value <= sourceIn) ||
            total is > 0 && (sourceIn >= total || sourceOut > total + 1.0 / 60))
        { controller.ReportStatus("El tramo debe estar dentro de la duración del medio"); return null; }
        var selectedDuration = sourceOut - sourceIn;
        var projectId = controller.CurrentProject.Id;
        var sequenceId = controller.CurrentProject.ActiveSequence!.Id;
        if (item.Kind == "web")
        {
            WebMediaService.TryUrl(item.Source, out var uri);
            var kind = item.MediaKind == "audio" || item.MediaKind == null && uri != null && new[] { ".mp3", ".m4a", ".wav" }
                .Contains(Path.GetExtension(uri.AbsolutePath).ToLowerInvariant()) ? "audio" : "video";
            var trackId = EnsureBaseTrack(controller, kind);
            return await WebMediaService.ImportAsync(controller, item.Source, trackId, Math.Max(0, at),
                item.WebMetadata, item.DurationSeconds, sourceIn, selectedDuration);
        }
        if (!File.Exists(item.Source)) { controller.ReportStatus("Archivo de la bandeja no disponible"); return null; }
        var asset = controller.CurrentProject.Assets.FirstOrDefault(a =>
            string.Equals(a.Source.Uri, item.Source, StringComparison.OrdinalIgnoreCase));
        if (asset == null) asset = await LocalMediaService.ProbeAsync(item.Source);
        if (controller.CurrentProject.Id != projectId || controller.CurrentProject.ActiveSequence?.Id != sequenceId) return null;
        if (asset.Streams.Any(s => s.Kind is "video" or "audio") && asset.Streams.All(s => s.Kind != "image") &&
            sourceOut > (asset.RecommendedDuration?.Seconds ?? double.MaxValue) + 1.0 / 60)
        { controller.ReportStatus("El Final supera la duración del archivo"); return null; }
        var imported = asset;
        try
        {
            controller.UndoRedo.Commit(p =>
            {
                if (p.Assets.All(a => a.Id != imported.Id)) p.Assets.Add(imported);
                var sequence = p.ActiveSequence!;
                var kind = imported.Streams.Any(s => s.Kind is "video" or "image") ? "video" : "audio";
                var track = sequence.Tracks.Where(t => t.Kind == kind && !t.Locked)
                    .OrderBy(t => t.Order).FirstOrDefault() ?? CollisionEngine.AddTrack(sequence, kind);
                ImportPlacement.PlaceAsset(p, imported.Id, track.Id, at, sourceIn, selectedDuration);
                MarkPlaced(p, item.Source);
            }, "Insertar referencia sin solapamiento");
            controller.ReportStatus("Referencia colocada en la línea de tiempo: " + imported.Name);
            return imported;
        }
        catch (Exception ex) { controller.ReportStatus("No se pudo insertar la referencia: " + ex.Message); return null; }
    }

    private static string EnsureBaseTrack(ProjectController controller, string kind)
    {
        var sequence = controller.CurrentProject.ActiveSequence!;
        var track = sequence.Tracks.Where(t => t.Kind == kind && !t.Locked).OrderBy(t => t.Order).FirstOrDefault();
        if (track != null) return track.Id;
        string? id = null;
        controller.UndoRedo.Commit(p => id = CollisionEngine.AddTrack(p.ActiveSequence!, kind).Id,
            "Crear pista para referencia");
        return id!;
    }

    private static string? MediaKindFor(Asset asset) =>
        asset.Streams.Any(s => s.Kind == "video") ? "video" :
        asset.Streams.Any(s => s.Kind == "image") ? "image" :
        asset.Streams.Any(s => s.Kind == "audio") ? "audio" : null;

}
