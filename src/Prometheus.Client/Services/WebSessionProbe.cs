using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

internal static class WebSessionProbe
{
    internal static async Task CheckAsync(Uri uri, WebDownloadSettings settings, CancellationToken token)
    {
        if (!WebDownloadSettings.IsYouTube(uri)) throw new ArgumentException("Introduce un enlace de YouTube.");
        await YouTubeRequestQueue.Shared.RunAsync(async () =>
        {
            var start = CreateProcess();
            using var credentials = settings.ConfigureAttempt(start, uri);
            foreach (var arg in new[] { "--no-playlist", "--skip-download", "--print", "%(id)s", uri.ToString() })
                start.ArgumentList.Add(arg);
            var result = await ExternalProcess.RunAsync(start, TimeSpan.FromSeconds(40), token);
            if (result.ExitCode != 0) throw WebDownloadFailure.FromDiagnostics(result.Error);
            if (string.IsNullOrWhiteSpace(result.Output)) throw new WebDownloadFailure(WebFailureKind.Other);
            if (settings.HasSession)
            {
                var warning = WebDownloadFailure.FromDiagnostics(result.Error);
                if (warning.IsCookieFailure) throw warning;
            }
            return true;
        }, token);
    }

    internal static ProcessStartInfo CreateProcess()
    {
        var tool = LocalMediaService.Tool("yt-dlp");
        if (!File.Exists(tool)) throw new FileNotFoundException("No se encontró yt-dlp.exe");
        return new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
    }
}
