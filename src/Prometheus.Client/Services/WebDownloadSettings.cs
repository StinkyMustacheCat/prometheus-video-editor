using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

public enum WebDownloadQuality { Complete, Light }

public sealed record WebDownloadSettings(WebDownloadQuality Quality = WebDownloadQuality.Complete,
    bool WithoutPoToken = true, string? CookieBrowser = null, string? CookieProfile = null,
    bool PasteChooseRange = false, string? CookieFile = null, bool AllowPoTokenFallback = false)
{
    private static readonly string SettingsPath = Environment.GetEnvironmentVariable("PROMETHEUS_WEB_SETTINGS") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "web-downloads.json");

    public static WebDownloadSettings Current { get; private set; } = Load();

    internal static WebDownloadSettings Load(string? path = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<WebDownloadSettings>(File.ReadAllText(path ?? SettingsPath)) ?? new();
            return settings with { Quality = Enum.IsDefined(settings.Quality) ? settings.Quality : WebDownloadQuality.Complete };
        }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public static void Save(WebDownloadSettings settings)
    {
        if (!Enum.IsDefined(settings.Quality)) throw new ArgumentException("Calidad de descarga no válida.");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, SettingsPath, true);
        Current = settings;
    }

    internal bool HasSession => !string.IsNullOrWhiteSpace(CookieBrowser) || !string.IsNullOrWhiteSpace(CookieFile);

    internal static bool IsYouTube(Uri uri) => uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);

    internal static string QualityId(WebDownloadQuality quality) => quality == WebDownloadQuality.Light ? "light" : "complete";
    internal static string QualityLabel(WebDownloadQuality quality) => quality == WebDownloadQuality.Light ? "vista ligera" : "vista completa";
    internal WebDownloadQuality QualityFor(Asset asset, WebDownloadQuality? requested = null) => requested ??
        (asset.WebPreferredQuality == "light" ? WebDownloadQuality.Light :
            asset.WebPreferredQuality == "complete" ? WebDownloadQuality.Complete : Quality);

    // The caller owns this lease until yt-dlp exits. It never writes the user's cookie export.
    internal IDisposable? Configure(ProcessStartInfo start, Uri uri, WebDownloadQuality? quality = null)
    {
        var args = start.ArgumentList;
        args.Add("--ignore-config");
        if (WithoutPoToken) args.Add("--no-plugin-dirs");
        if (IsYouTube(uri))
        {
            if (string.IsNullOrWhiteSpace(CookieFile) && !string.IsNullOrWhiteSpace(CookieBrowser))
            {
                var profile = WebBrowserProfiles.Specifier(CookieBrowser, CookieProfile)
                    ?? throw new WebDownloadFailure(WebFailureKind.ProfileMissing);
                args.Add("--cookies-from-browser");
                args.Add(profile);
            }
            args.Add("--js-runtimes"); args.Add("node");
            args.Add("--sleep-requests"); args.Add("1");
            // The application queue owns retries for YouTube, including 429 and verification.
            args.Add("--retries"); args.Add("0");
            args.Add("--extractor-retries"); args.Add("0");
            args.Add("--fragment-retries"); args.Add("0");
            args.Add("--socket-timeout"); args.Add("25");
        }
        if (quality == WebDownloadQuality.Light)
        {
            args.Add("-f");
            args.Add("bv*[height<=720]+ba/b[height<=720]");
        }
        if (!IsYouTube(uri) || string.IsNullOrWhiteSpace(CookieFile)) return null;
        return WebCookieFile.Open(CookieFile, start);
    }

    internal IDisposable? ConfigureAttempt(ProcessStartInfo start, Uri uri, WebDownloadQuality? quality = null)
    {
        try { return Configure(start, uri, quality); }
        catch (Exception error) when (IsYouTube(uri) && !string.IsNullOrWhiteSpace(CookieFile) &&
            error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new WebDownloadFailure(WebFailureKind.CookieFile);
        }
    }

    internal static string ExplainFailure(string error, WebDownloadQuality? quality = null) =>
        WebDownloadFailure.FromDiagnostics(error, quality).Message;
}
