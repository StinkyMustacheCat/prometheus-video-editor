using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia.Threading;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public static class WebMediaService
{
    public sealed record ImportState(string Stage, string Message, int? Percent = null);
    private static readonly ConcurrentDictionary<string, ImportState> States = new();
    private static readonly ConcurrentDictionary<string, (string ProjectId, string AssetId)> PlaceholderJobs = new();
    public static event Action<string>? StateChanged;
    public static ImportState? GetState(string assetId) => States.TryGetValue(assetId, out var state) ? state : null;
    public static ImportState? GetPlaceholderState(string projectId, string clipId) =>
        PlaceholderJobs.TryGetValue(clipId, out var job) && job.ProjectId == projectId ? GetState(job.AssetId) : null;
    private static void SetState(string assetId, string stage, string message, int? percent = null)
    {
        States[assetId] = new ImportState(stage, message, percent);
        Dispatcher.UIThread.Post(() => StateChanged?.Invoke(assetId));
    }
    private static readonly SemaphoreSlim YouTubeDownload = new(1, 1);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Running = new();
    private static readonly string[] DirectExtensions = { ".mp4", ".webm", ".mov", ".mp3", ".m4a", ".wav", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".avif" };

    public static StreamInfo CreateProvisionalStream(string trackKind) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Kind = trackKind == "audio" ? "audio" : "video", Duration = Rational.Time(5)
    };

    public static bool TryUrl(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https") { uri = parsed; return true; }
        uri = null!; return false;
    }

    public static bool IsPending(Asset asset) => asset.Source.Kind == "web" && (asset.EffectiveLocator == null || !File.Exists(asset.EffectiveLocator.Path));

    public static async Task<bool> ImportIntoPlaceholderAsync(ProjectController controller, string url, string clipId, double sourceStart = 0, WebDownloadQuality? quality = null)
    {
        if (PlaceholderJobs.TryGetValue(clipId, out var active) && GetState(active.AssetId)?.Stage is "preparing" or "probing" or "downloading")
        { controller.ReportStatus("Ya se está conectando un enlace a este espacio"); return false; }
        if (!TryUrl(url, out var uri)) { controller.ReportStatus("Pega un enlace HTTP o HTTPS válido"); return false; }
        var project = controller.CurrentProject;
        var sequence = project.ActiveSequence;
        var clip = sequence?.Clips.FirstOrDefault(c => c.Id == clipId);
        var track = sequence?.Tracks.FirstOrDefault(t => t.Id == clip?.TrackId);
        if (clip == null || track == null || track.Locked || !PlaceholderService.IsPlaceholder(project.Assets.FirstOrDefault(a => a.Id == clip.AssetId)) ||
            !CompatibleWithKnownExtension(uri, track.Kind))
        { controller.ReportStatus("El enlace no es compatible con este espacio reservado"); return false; }

        var existing = project.Assets.FirstOrDefault(a => a.Source.Kind == "web" && a.Source.Uri == uri.ToString() && !IsPending(a));
        if (existing != null && PlaceholderService.Accepts(project, clip, existing))
        {
            controller.UndoRedo.Commit(p => {
                PlaceholderService.Replace(p, clipId, existing.Id, sourceStart);
                LinkShelfService.MarkPlaced(p, uri.ToString());
            }, "Conectar enlace a reserva");
            return true;
        }

        var asset = new Asset { Id = Guid.NewGuid().ToString("N"), Name = WebMediaNaming.InitialName(uri), WebAutoName = WebMediaNaming.InitialName(uri),
            WebPreferredQuality = WebDownloadSettings.QualityId(quality ?? WebDownloadSettings.Current.Quality),
            Source = new MediaSource { Kind = "web", Uri = uri.ToString(), PageUrl = uri.ToString(), ImportedAt = DateTime.UtcNow.ToString("O") } };
        asset.Streams.Add(CreateProvisionalStream(track.Kind));
        controller.UndoRedo.Commit(p => p.Assets.Add(asset), "Preparar enlace para reserva");
        PlaceholderJobs[clipId] = (project.Id, asset.Id);
        SetState(asset.Id, "preparing", "Preparando medio…");
        await ResolveAsync(controller, asset.Id, quality: quality,
            retryMissing: () => ImportIntoPlaceholderAsync(controller, url, clipId, sourceStart, quality));
        if (controller.CurrentProject.Id != project.Id) return false;
        var ready = GetState(asset.Id)?.Stage == "ready";
        var current = controller.CurrentProject.ActiveSequence?.Clips.FirstOrDefault(c => c.Id == clipId);
        if (ready && current != null && PlaceholderService.IsPlaceholder(controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == current.AssetId)))
        {
            try
            {
                controller.UndoRedo.Commit(p => {
                    PlaceholderService.Replace(p, clipId, asset.Id, sourceStart);
                    LinkShelfService.MarkPlaced(p, uri.ToString());
                }, "Conectar enlace a reserva");
                if (PlaceholderJobs.TryGetValue(clipId, out var completed) && completed.AssetId == asset.Id)
                    PlaceholderJobs.TryRemove(clipId, out _);
                return true;
            }
            catch (Exception ex) { controller.ReportStatus("No se pudo conectar el enlace: " + ex.Message); }
        }
        if (!ready && !controller.CurrentProject.Sequences.Any(s => s.Clips.Any(c => c.AssetId == asset.Id)))
            controller.UndoRedo.Commit(p => p.Assets.RemoveAll(a => a.Id == asset.Id), "Descartar enlace fallido");
        return false;
    }

    public static async Task<Asset?> ImportAsync(ProjectController controller, string url, string? trackId = null, double at = 0,
        WebMediaMetadata? knownMetadata = null, double? knownDuration = null, double sourceIn = 0, double? selectedDuration = null,
        WebDownloadQuality? quality = null)
    {
        if (!TryUrl(url, out var uri)) { controller.ReportStatus("Pega un enlace HTTP o HTTPS válido"); return null; }
        if (knownMetadata == null || knownDuration == null)
        {
            var shelf = controller.CurrentProject.LinkShelf.FirstOrDefault(x =>
                string.Equals(x.Source, uri.ToString(), StringComparison.OrdinalIgnoreCase));
            knownMetadata ??= shelf?.WebMetadata;
            knownDuration ??= shelf?.DurationSeconds;
        }
        var existing = controller.CurrentProject.Assets.FirstOrDefault(a => a.Source.Kind == "web" && a.Source.Uri == uri.ToString());
        if (existing != null)
        {
            if (knownMetadata != null && existing.WebMetadata == null)
                controller.UndoRedo.Commit(p => WebMediaMetadataUpdate.Apply(p, existing.Id, uri, knownMetadata, knownDuration), "Aplicar datos de bandeja");
            await ResolveAsync(controller, existing.Id, trackId, at, sourceIn: sourceIn, selectedDuration: selectedDuration, quality: quality);
            return controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == existing.Id);
        }
        var displayName = knownMetadata != null ? WebMediaNaming.DisplayName(knownMetadata) : WebMediaNaming.InitialName(uri);
        var initialDuration = knownDuration is > 0 ? knownDuration.Value : 5;
        var clipDuration = selectedDuration ?? Math.Max(1.0 / 60, initialDuration - sourceIn);
        var asset = new Asset { Id = Guid.NewGuid().ToString("N"), Name = displayName, WebAutoName = displayName,
            WebPreferredQuality = WebDownloadSettings.QualityId(quality ?? WebDownloadSettings.Current.Quality),
            WebMetadata = knownMetadata, RecommendedDuration = knownDuration is > 0 ? Rational.Time(knownDuration.Value) : null,
            Source = new MediaSource { Kind = "web", Uri = uri.ToString(), PageUrl = uri.ToString(), ImportedAt = DateTime.UtcNow.ToString("O") } };
        var sequenceId = controller.CurrentProject.ActiveSequenceId;
        var track = controller.CurrentProject.ActiveSequence?.Tracks.FirstOrDefault(t => t.Id == trackId && !t.Locked);
        if (trackId != null && track == null) { controller.ReportStatus("La pista de destino ya no está disponible"); return null; }
        if (track != null && !CompatibleWithKnownExtension(uri, track.Kind))
        { controller.ReportStatus("El enlace no contiene un medio compatible con esa pista"); return null; }
        var provisionalStream = track == null ? null : CreateProvisionalStream(track.Kind);
        if (provisionalStream != null) provisionalStream.Duration = Rational.Time(initialDuration);
        if (provisionalStream != null) asset.Streams.Add(provisionalStream);
        var provisionalClipId = Guid.NewGuid().ToString("N");
        try { controller.UndoRedo.Commit(p =>
        {
            p.Assets.Add(asset);
            if (track != null && provisionalStream != null)
            {
                var sequence = p.Sequences.First(s => s.Id == sequenceId);
                var destination = ImportPlacement.PreferredFreeTrack(sequence, track.Id, Math.Max(0, at), clipDuration);
                sequence.Clips.Add(new Clip
                {
                    Id = provisionalClipId, Name = "Preparando medio…", AssetId = asset.Id, StreamId = provisionalStream.Id,
                    TrackId = destination.Id, TimelineStart = Rational.Time(Math.Max(0, at)), SourceIn = Rational.Time(sourceIn),
                    Duration = Rational.Time(clipDuration), WebAutoDurationSeconds = clipDuration,
                    WebRequestedOutSeconds = selectedDuration.HasValue ? sourceIn + selectedDuration.Value : null
                });
                LinkShelfService.MarkPlaced(p, uri.ToString());
            }
        }, "Importar enlace web"); }
        catch (Exception ex) { controller.ReportStatus("No se pudo iniciar la importación: " + ex.Message); return null; }
        SetState(asset.Id, "preparing", "Preparando medio…");
        await ResolveAsync(controller, asset.Id, quality: quality);
        return controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == asset.Id);
    }

    public static void Cancel(string id)
    {
        if (Running.TryGetValue(id, out var token)) token.Cancel();
        SetState(id, "cancelled", "Descarga cancelada");
    }

    public static async Task ResolveAsync(ProjectController controller, string assetId, string? trackId = null, double at = 0,
        bool redownload = false, double sourceIn = 0, double? selectedDuration = null, WebDownloadQuality? quality = null,
        Func<Task>? retryMissing = null)
    {
        var project = controller.CurrentProject;
        var projectId = project.Id;
        var asset = project.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset == null || asset.Source.Kind != "web" || !TryUrl(asset.Source.Uri, out var uri)) return;
        if (trackId != null) Place(controller, assetId, trackId, at, sourceIn, selectedDuration);
        if (Running.ContainsKey(assetId)) { controller.ReportStatus("Este enlace ya se está descargando"); return; }
        var settings = WebDownloadSettings.Current;
        var requestedQuality = settings.QualityFor(asset, quality);
        if (!redownload && !IsPending(asset) &&
            (quality == null || asset.Representations.Any(r => r.Role == Role(requestedQuality) && File.Exists(r.Locator.Path)))) return;
        var token = new CancellationTokenSource();
        if (!Running.TryAdd(assetId, token)) { token.Dispose(); return; }
        SetState(assetId, "preparing", "Preparando " + WebDownloadSettings.QualityLabel(requestedQuality) + "…");
        controller.ReportStatus("Preparando enlace: " + uri.Host + " · " + WebDownloadSettings.QualityLabel(requestedQuality));
        string? downloadedPath = null;
        try
        {
            if (asset.WebPreferredQuality != WebDownloadSettings.QualityId(requestedQuality))
                controller.UndoRedo.Commit(p => p.Assets.First(a => a.Id == assetId).WebPreferredQuality =
                    WebDownloadSettings.QualityId(requestedQuality), "Elegir calidad web");
            var folder = controller.CurrentProjectPath is { Length: > 0 } file
                ? ProjectStorage.GetWebDirectory(file)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "WebMedia", projectId);
            Directory.CreateDirectory(folder);
            void MetadataReady(WebMediaMetadata metadata, double? duration)
            {
                if (token.IsCancellationRequested || controller.CurrentProject.Id != projectId ||
                    controller.CurrentProject.Assets.All(a => a.Id != assetId)) return;
                controller.UndoRedo.Commit(p => WebMediaMetadataUpdate.Apply(p, assetId, uri, metadata, duration),
                    "Actualizar datos web");
                SetState(assetId, "downloading", "Descargando medio…");
            }
            SetState(assetId, "probing", "Obteniendo datos del medio…");
            var (path, role) = await DownloadAsync(uri, folder, assetId + "-" + Guid.NewGuid().ToString("N")[..8],
                token.Token, assetId, MetadataReady, settings, requestedQuality);
            downloadedPath = path;
            SetState(assetId, "probing", "Analizando medio…");
            var probed = await LocalMediaService.ProbeAsync(path);
            if (token.IsCancellationRequested || controller.CurrentProject.Id != projectId || controller.CurrentProject.Assets.All(a => a.Id != assetId)) return;
            if (probed.RecommendedDuration is { } sourceDuration && sourceDuration.Seconds > 0 &&
                controller.CurrentProject.Sequences.SelectMany(s => s.Clips).Any(c => c.AssetId == assetId &&
                    c.WebAutoDurationSeconds.HasValue && c.SourceIn.Seconds >= sourceDuration.Seconds))
                throw new InvalidOperationException("El inicio del tramo está fuera de la duración del medio. Ajusta el inicio o vuelve a importar el enlace.");
            var incompatibleClips = 0;
            var downloadedName = controller.CurrentProject.Assets.First(a => a.Id == assetId).Name;
            controller.UndoRedo.Commit(p => {
                var target = p.Assets.First(a => a.Id == assetId);
                target.Representations.RemoveAll(r => r.Role == role);
                var downloadedFile = new FileInfo(path);
                var representation = new MediaRepresentation { Role = role, Locator = new Locator(path), SizeBytes = downloadedFile.Length,
                    ModifiedUtc = downloadedFile.LastWriteTimeUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture) };
                target.Representations.Add(representation);
                if (role == "preview" && target.Representations.Any(r => r.Role == "final" && File.Exists(r.Locator.Path)))
                {
                    foreach (var stream in target.Streams)
                        if (MatchStream(probed.Streams, stream) is { } incoming)
                            representation.StreamOrdinals[stream.Id] = incoming.Ordinal;
                    return;
                }
                var provisionalIds = p.Sequences.ToDictionary(s => s.Id,
                    s => s.Clips.Where(c => c.AssetId == assetId && c.WebAutoDurationSeconds.HasValue).Select(c => c.Id).ToArray());
                var previousStreams = target.Streams.ToArray();
                foreach (var incoming in probed.Streams)
                {
                    var previous = MatchStream(previousStreams, incoming);
                    if (previous != null) incoming.Id = previous.Id;
                }
                target.Streams = probed.Streams;
                foreach (var stream in target.Streams) representation.StreamOrdinals[stream.Id] = stream.Ordinal;
                foreach (var sequence in p.Sequences)
                {
                    var remove = new HashSet<string>();
                    foreach (var clip in sequence.Clips.Where(c => c.AssetId == assetId && target.Streams.All(s => s.Id != c.StreamId)))
                    {
                        var old = previousStreams.FirstOrDefault(s => s.Id == clip.StreamId);
                        var replacement = target.Streams.FirstOrDefault(s => s.Kind == old?.Kind)
                            ?? target.Streams.FirstOrDefault(s => old?.Kind == "video" && s.Kind == "image");
                        if (replacement != null) clip.StreamId = replacement.Id;
                        else remove.Add(clip.Id);
                    }
                    incompatibleClips += remove.Count;
                    sequence.Clips.RemoveAll(c => remove.Contains(c.Id));
                    foreach (var group in sequence.LinkGroups) group.ClipIds.RemoveAll(remove.Contains);
                    sequence.LinkGroups.RemoveAll(g => g.ClipIds.Count < 2);
                    foreach (var group in sequence.EditGroups) group.ClipIds.RemoveAll(remove.Contains);
                    sequence.EditGroups.RemoveAll(g => g.ClipIds.Count < 2);
                }
                foreach (var sequence in p.Sequences)
                    foreach (var clip in sequence.Clips.Where(c => c.AssetId == assetId && c.Name == "Preparando medio…"))
                        clip.Name = target.Name;
                target.RecommendedDuration = probed.RecommendedDuration;
                if (probed.RecommendedDuration.HasValue) WebClipDuration.Apply(p, assetId, probed.RecommendedDuration.Value.Seconds, true);
                var reference = p.LinkShelf.FirstOrDefault(x =>
                    string.Equals(x.Source, uri.ToString(), StringComparison.OrdinalIgnoreCase));
                if (reference != null)
                {
                    reference.Name = target.Name;
                    reference.DurationSeconds = probed.RecommendedDuration?.Seconds ?? reference.DurationSeconds;
                    reference.MediaKind = target.Streams.Any(s => s.Kind == "video") ? "video" :
                        target.Streams.Any(s => s.Kind == "image") ? "image" :
                        target.Streams.Any(s => s.Kind == "audio") ? "audio" : reference.MediaKind;
                    reference.MetadataStatus = "ready";
                    reference.MetadataError = null;
                }
                foreach (var sequence in p.Sequences)
                    foreach (var id in provisionalIds[sequence.Id])
                        ImportPlacement.PlaceCompanionAudio(sequence, target, id);
                target.ContainerFormat = probed.ContainerFormat;
            }, "Resolver medio web", downloadedName +
                (role == "preview" ? " · vista ligera" : " · vista completa"));
            downloadedPath = null;
            controller.ReportStatus("Medio web resuelto: " + controller.CurrentProject.Assets.First(a => a.Id == assetId).Name);
            if (incompatibleClips > 0) controller.ReportStatus($"{incompatibleClips} clip(s) retirados: el medio no corresponde a la pista elegida");
            SetState(assetId, "ready", role == "final" && requestedQuality == WebDownloadQuality.Light
                ? "Medio listo · archivo original (el enlace directo no ofrece variantes)"
                : "Medio listo · " + WebDownloadSettings.QualityLabel(requestedQuality));
        }
        catch (OperationCanceledException)
        {
            SetState(assetId, "cancelled", "Descarga cancelada"); controller.ReportStatus("Descarga cancelada");
        }
        catch (Exception ex)
        {
            var message = ex is HttpRequestException { StatusCode: { } status }
                ? $"HTTP {(int)status}: el servidor no permite acceder al medio"
                : ex is InvalidDataException ? "El enlace no entregó un medio válido: " + ex.Message
                : "No se pudo resolver el enlace: " + ex.Message;
            SetState(assetId, "failed", message);
            if (controller.CurrentProject.Id == projectId && controller.CurrentProject.LinkShelf.Any(x =>
                    string.Equals(x.Source, uri.ToString(), StringComparison.OrdinalIgnoreCase)))
                controller.UndoRedo.Commit(p =>
                {
                    var reference = p.LinkShelf.FirstOrDefault(x =>
                        string.Equals(x.Source, uri.ToString(), StringComparison.OrdinalIgnoreCase));
                    if (reference != null) { reference.MetadataStatus = "failed"; reference.MetadataError = message; }
                }, "Informar error de enlace");
            if (controller.CurrentProject.Id == projectId)
            {
                controller.ReportStatus(message + " · puedes reintentar desde Opciones web");
                controller.RecordAction("Error de descarga web", asset.Name + " · " + message);
                controller.Notifications.Add("web:" + projectId + ":" + assetId,
                    "Error de descarga web", asset.Name + " · " + message, "Reintentar", async () =>
                    {
                        if (controller.CurrentProject.Id != projectId) return;
                        if (controller.CurrentProject.Assets.Any(a => a.Id == assetId))
                            await ResolveAsync(controller, assetId, redownload: true, quality: requestedQuality, retryMissing: retryMissing);
                        else if (retryMissing != null) await retryMissing();
                        else controller.ReportStatus("El medio ya no está en el proyecto.");
                    });
            }
        }
        finally
        {
            Running.TryRemove(assetId, out _);
            token.Dispose();
            if (downloadedPath != null)
                try { File.Delete(downloadedPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Role(WebDownloadQuality quality) => quality == WebDownloadQuality.Light ? "preview" : "final";

    private static StreamInfo? MatchStream(IEnumerable<StreamInfo> streams, StreamInfo stream) =>
        streams.FirstOrDefault(s => s.Kind == stream.Kind && s.Ordinal == stream.Ordinal)
        ?? streams.FirstOrDefault(s => s.Kind == stream.Kind);

    private static async Task<(string Path, string Role)> DownloadAsync(Uri uri, string folder, string id, CancellationToken token,
        string assetId, Action<WebMediaMetadata, double?> metadataReady, WebDownloadSettings settings, WebDownloadQuality quality)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        var youtube = WebDownloadSettings.IsYouTube(uri);
        var knownDirect = !youtube && DirectExtensions.Contains(extension);
        HttpResponseMessage? response = null;
        try
        {
            using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (!knownDirect) headerTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            if (!youtube)
                response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !knownDirect) { }
        catch (HttpRequestException) when (!knownDirect) { }
        if (response != null)
        using (response)
        {
            if (!response.IsSuccessStatusCode && knownDirect)
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
            if (response.IsSuccessStatusCode)
            {
                var mime = response.Content.Headers.ContentType?.MediaType;
                var mimeExtension = WebDirectMedia.ExtensionForContentType(mime);
                await using var input = await response.Content.ReadAsStreamAsync(token);
                var header = new byte[32];
                var headerCount = 0;
                while (headerCount < header.Length)
                {
                    var read = await input.ReadAsync(header.AsMemory(headerCount), token);
                    if (read == 0) break;
                    headerCount += read;
                }
                var imageExtension = WebDirectMedia.ImageExtension(header.AsSpan(0, headerCount));
                if (knownDirect || mimeExtension != null || imageExtension != null)
                {
                    if (knownDirect && mime?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
                        throw new InvalidDataException("el servidor devolvió una página en lugar del archivo");
                    if ((mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
                        extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".avif") && imageExtension == null)
                        throw new InvalidDataException("la respuesta no contiene una imagen reconocible");
                    var actualExtension = imageExtension ?? mimeExtension ?? extension;
                    var path = Path.Combine(folder, id + actualExtension);
                    var partial = path + ".partial";
                    try
                    {
                        metadataReady(WebMediaNaming.FromDirectUrl(uri,
                            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName), null);
                        await using (var fileOutput = File.Create(partial))
                        {
                            await fileOutput.WriteAsync(header.AsMemory(0, headerCount), token);
                            var buffer = new byte[128 * 1024];
                            long received = headerCount;
                            int lastPercent = -1;
                            while (true)
                            {
                                var count = await input.ReadAsync(buffer, token);
                                if (count == 0) break;
                                await fileOutput.WriteAsync(buffer.AsMemory(0, count), token);
                                received += count;
                                var total = response.Content.Headers.ContentLength;
                                if (total is > 0)
                                {
                                    var percent = (int)Math.Clamp(received * 100 / total.Value, 0, 100);
                                    if (percent >= lastPercent + 5)
                                    {
                                        lastPercent = percent;
                                        SetState(assetId, "downloading", $"Descargando · {percent}%", percent);
                                    }
                                }
                            }
                        }
                        if (new FileInfo(partial).Length == 0) throw new InvalidDataException("el archivo recibido está vacío");
                        File.Move(partial, path, true);
                        return (path, "final");
                    }
                    catch { if (File.Exists(partial)) File.Delete(partial); throw; }
                }
            }
        }
        if (youtube) await YouTubeDownload.WaitAsync(token);
        try { return (await DownloadWithYtDlpAsync(uri, folder, id, token, assetId, metadataReady, settings, quality), Role(quality)); }
        finally { if (youtube) YouTubeDownload.Release(); }
    }

    private static async Task<string> DownloadWithYtDlpAsync(Uri uri, string folder, string id, CancellationToken token,
        string assetId, Action<WebMediaMetadata, double?> metadataReady, WebDownloadSettings settings, WebDownloadQuality quality)
    {
        var tool = LocalMediaService.Tool("yt-dlp");
        if (!File.Exists(tool)) throw new FileNotFoundException("No se encontró yt-dlp.exe");
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        using var credentials = settings.Configure(start, uri, quality);
        foreach (var arg in new[] { "--no-playlist", "--no-simulate", "--no-warnings", "--newline", "--progress",
                     "--print", "before_dl:PROMETHEUS_META:%()j",
                     "--print", "after_move:PROMETHEUS_FILE:%(filepath)s",
                     "--progress-template", "download:PROMETHEUS_PROGRESS:%(progress._percent_str)s",
                     "--restrict-filenames", "--ffmpeg-location", LocalMediaService.Tool("ffmpeg"),
                     "-o", Path.Combine(folder, id + ".%(ext)s"), uri.ToString() }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar yt-dlp");
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var errors = ExternalProcess.ReadDiagnosticsAsync(process.StandardError);
        string? completedPath = null;
        var lastYtPercent = -5;
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(token)) != null)
        {
            if (line.StartsWith("PROMETHEUS_META:", StringComparison.Ordinal))
            {
                try
                {
                    using var json = JsonDocument.Parse(line["PROMETHEUS_META:".Length..]);
                    var metadata = WebMediaNaming.FromYtDlp(json.RootElement, uri);
                    var duration = json.RootElement.TryGetProperty("duration", out var value) &&
                        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var seconds) &&
                        seconds > 0 ? seconds : (double?)null;
                    metadataReady(metadata, duration);
                }
                catch (JsonException) { /* Some sites omit fields; the file can still download. */ }
            }
            else if (line.StartsWith("PROMETHEUS_PROGRESS:", StringComparison.Ordinal))
            {
                var value = line["PROMETHEUS_PROGRESS:".Length..].Trim().TrimEnd('%');
                if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var percent))
                {
                    var rounded = (int)Math.Clamp(percent, 0, 100);
                    if (rounded >= lastYtPercent + 5 || rounded == 100 && lastYtPercent < 100)
                    {
                        lastYtPercent = rounded;
                        SetState(assetId, "downloading", $"Descargando · {rounded}%", rounded);
                    }
                }
            }
            else if (line.StartsWith("PROMETHEUS_FILE:", StringComparison.Ordinal))
                completedPath = line["PROMETHEUS_FILE:".Length..].Trim();
        }
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidOperationException(WebDownloadSettings.ExplainFailure(await errors, quality));
        await errors;
        if (!string.IsNullOrWhiteSpace(completedPath) && File.Exists(completedPath)) return completedPath;
        throw new FileNotFoundException("El enlace no produjo un archivo de medios.");
    }

    private static void Place(ProjectController controller, string assetId, string trackId, double at,
        double sourceIn = 0, double? selectedDuration = null)
    {
        var seq = controller.CurrentProject.ActiveSequence;
        var track = seq?.Tracks.FirstOrDefault(t => t.Id == trackId && !t.Locked);
        var asset = controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == assetId);
        if (track == null || asset == null) { controller.ReportStatus("La pista de destino no está disponible"); return; }
        if (TryUrl(asset.Source.Uri, out var uri) && !CompatibleWithKnownExtension(uri, track.Kind))
        { controller.ReportStatus("El enlace no contiene un medio compatible con esa pista"); return; }
        var pending = IsPending(asset);
        var sequenceId = seq!.Id;
        try { controller.UndoRedo.Commit(p =>
        {
            var target = p.Assets.First(a => a.Id == assetId);
            var stream = target.Streams.FirstOrDefault(s => track.Kind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image");
            if (stream == null && pending)
            {
                stream = CreateProvisionalStream(track.Kind);
                target.Streams.Add(stream);
            }
            if (stream == null) throw new InvalidOperationException("El enlace no es compatible con la pista seleccionada");
            var duration = selectedDuration ?? Math.Max(1.0 / 60, (target.RecommendedDuration?.Seconds ?? 5) - sourceIn);
            if (!pending)
            {
                ImportPlacement.PlaceAsset(p, assetId, trackId, at, sourceIn, selectedDuration);
                LinkShelfService.MarkPlaced(p, target.Source.Uri);
                return;
            }
            var sequence = p.Sequences.First(s => s.Id == sequenceId);
            var destination = ImportPlacement.PreferredFreeTrack(sequence, trackId, Math.Max(0, at), duration);
            sequence.Clips.Add(new Clip
            {
                Name = pending ? "Preparando medio…" : target.Name, AssetId = assetId, StreamId = stream.Id, TrackId = destination.Id,
                TimelineStart = Rational.Time(Math.Max(0, at)), SourceIn = Rational.Time(sourceIn), Duration = Rational.Time(duration),
                WebAutoDurationSeconds = pending ? duration : null,
                WebRequestedOutSeconds = pending && selectedDuration.HasValue ? sourceIn + selectedDuration.Value : null
            });
            LinkShelfService.MarkPlaced(p, target.Source.Uri);
        }, "Insertar medio web"); }
        catch (Exception ex) { controller.ReportStatus("No se pudo insertar el enlace: " + ex.Message); }
    }

    public static bool CompatibleWithKnownExtension(Uri uri, string trackKind)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".mov") return trackKind == "video";
        if (extension is ".mp3" or ".m4a" or ".wav") return trackKind == "audio";
        return true;
    }
}
