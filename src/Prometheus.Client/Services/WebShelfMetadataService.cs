using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>Resolves shelf labels without starting a media download.</summary>
public static class WebShelfMetadataService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(18) };
    private static readonly SemaphoreSlim Probes = new(2);
    private static readonly ConcurrentDictionary<string, byte> InFlight = new();

    public static async Task ProbeAsync(ProjectController controller, string itemId)
    {
        var projectId = controller.CurrentProject.Id;
        var item = controller.CurrentProject.LinkShelf.FirstOrDefault(x => x.Id == itemId);
        if (item?.Kind != "web" || !WebMediaService.TryUrl(item.Source, out var uri)) return;
        var jobId = projectId + ":" + itemId;
        if (!InFlight.TryAdd(jobId, 0)) return;
        await Probes.WaitAsync();
        try
        {
            if (controller.CurrentProject.Id != projectId || controller.CurrentProject.LinkShelf.All(x => x.Id != itemId)) return;
            await Dispatcher.UIThread.InvokeAsync(() => UpdateStatus(controller, projectId, itemId, "probing", null));
            var (metadata, duration, thumbnail, kind) = await ReadAsync(uri);
            string? thumbnailPath = null;
            try { thumbnailPath = await DownloadThumbnailAsync(thumbnail); }
            catch (Exception) { /* Thumbnail availability must not discard title and duration. */ }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (controller.CurrentProject.Id != projectId || controller.CurrentProject.LinkShelf.All(x => x.Id != itemId)) return;
                controller.UndoRedo.Commit(p =>
                {
                    var target = p.LinkShelf.First(x => x.Id == itemId);
                    target.WebMetadata = metadata;
                    target.DurationSeconds = duration;
                    target.MediaKind = kind;
                    target.ThumbnailPath = thumbnailPath;
                    target.Name = WebMediaNaming.DisplayName(metadata);
                    target.MetadataStatus = "ready";
                    target.MetadataError = null;
                }, "Actualizar datos de enlace");
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateStatus(controller, projectId, itemId, "failed", ex.Message);
                if (controller.CurrentProject.Id == projectId && controller.CurrentProject.LinkShelf.Any(x => x.Id == itemId))
                    controller.ReportNotice("web-info:" + projectId + ":" + itemId, "Error al consultar enlace",
                        item.Name + " · " + ex.Message, "Reintentar", () => ProbeAsync(controller, itemId));
            });
        }
        finally { Probes.Release(); InFlight.TryRemove(jobId, out _); }
    }

    private static void UpdateStatus(ProjectController controller, string projectId, string itemId, string status, string? error)
    {
        if (controller.CurrentProject.Id != projectId || controller.CurrentProject.LinkShelf.All(x => x.Id != itemId)) return;
        controller.UndoRedo.Commit(p =>
        {
            var target = p.LinkShelf.First(x => x.Id == itemId);
            target.MetadataStatus = status;
            target.MetadataError = error;
        }, "Consultar datos de enlace");
    }

    private static async Task<(WebMediaMetadata Metadata, double? Duration, string? Thumbnail, string Kind)> ReadAsync(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is ".mp4" or ".webm" or ".mov" or ".mp3" or ".m4a" or ".wav" or ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            string? name = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
            }
            catch (HttpRequestException) { /* Some servers do not support HEAD. The URL still supplies a useful name. */ }
            var directKind = extension is ".mp3" or ".m4a" or ".wav" ? "audio" :
                extension is ".png" or ".jpg" or ".jpeg" or ".webp" ? "image" : "video";
            return (WebMediaNaming.FromDirectUrl(uri, name), null,
                directKind == "image" ? uri.ToString() : null, directKind);
        }
        var tool = LocalMediaService.Tool("yt-dlp");
        if (!File.Exists(tool)) throw new FileNotFoundException("No se encontró yt-dlp.exe");
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        using var credentials = WebDownloadSettings.Current.Configure(start, uri);
        foreach (var arg in new[] { "--dump-single-json", "--skip-download", "--no-playlist", "--quiet", "--no-warnings", uri.ToString() })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo consultar yt-dlp");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var jsonText = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(WebDownloadSettings.ExplainFailure(error));
        using var document = JsonDocument.Parse(jsonText);
        var root = document.RootElement;
        var metadata = WebMediaNaming.FromYtDlp(root, uri);
        var duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) && seconds > 0
            ? seconds : (double?)null;
        var thumbnail = root.TryGetProperty("thumbnail", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var videoCodec = root.TryGetProperty("vcodec", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var audioCodec = root.TryGetProperty("acodec", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        var kind = videoCodec == "none" && !string.IsNullOrWhiteSpace(audioCodec) && audioCodec != "none" ? "audio" : "video";
        return (metadata, duration, thumbnail, kind);
    }

    private static async Task<string?> DownloadThumbnailAsync(string? url)
    {
        if (url == null || !WebMediaService.TryUrl(url, out var uri)) return null;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "ShelfThumbnails");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString()))) + ".img");
        if (File.Exists(path)) return path;
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 3_000_000) return null;
        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) return null;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(path);
        var buffer = new byte[32 * 1024];
        var total = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer);
            if (count == 0) break;
            total += count;
            if (total > 3_000_000) { output.Close(); File.Delete(path); return null; }
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
        return path;
    }
}
