using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

public enum WebDownloadQuality { Complete, Light }

public sealed record WebDownloadSettings(WebDownloadQuality Quality = WebDownloadQuality.Complete,
    bool WithoutPoToken = true, string? CookieBrowser = null, string? CookieProfile = null,
    bool PasteChooseRange = false, string? CookieFile = null)
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
            if (string.IsNullOrWhiteSpace(CookieFile) && CookieBrowser is "chrome" or "edge" or "firefox")
            {
                args.Add("--cookies-from-browser");
                args.Add(string.IsNullOrWhiteSpace(CookieProfile) ? CookieBrowser : CookieBrowser + ":" + CookieProfile);
            }
            args.Add("--js-runtimes");
            args.Add("node");
            args.Add("--sleep-requests");
            args.Add("1");
            args.Add("--retry-sleep");
            args.Add("http:exp=2:15");
            args.Add("--retries"); args.Add("3");
            args.Add("--fragment-retries"); args.Add("3");
            args.Add("--socket-timeout"); args.Add("30");
        }
        if (quality == WebDownloadQuality.Light)
        {
            args.Add("-f");
            args.Add("bv*[height<=720]+ba/b[height<=720]");
        }
        if (!IsYouTube(uri) || string.IsNullOrWhiteSpace(CookieFile)) return null;
        return WebCookieFile.Open(CookieFile, start);
    }

    internal static string ExplainFailure(string error, WebDownloadQuality? quality = null)
    {
        if (Contains("cookies database") || Contains("cookie database") || Contains("decrypt") || Contains("DPAPI") || Contains("app-bound") || Contains("could not find chrome") ||
            Contains("could not find edge") || Contains("could not find firefox"))
            return "No se pudieron leer las cookies del navegador. Comprueba el perfil y cierra el navegador. Si Chrome/Edge impide descifrarlas, usa Firefox o un archivo de cookies en Descargas web.";
        if (Contains("429") || Contains("too many requests") || Contains("try again later"))
            return "El sitio limitó las solicitudes. Espera unos minutos antes de reintentar.";
        if (Contains("age-restricted") || Contains("age restricted") || Contains("confirm your age") || Contains("age verification"))
            return "El vídeo tiene restricción de edad. Inicia sesión con una cuenta autorizada en el navegador y elige su sesión o un archivo de cookies en Descargas web. El sitio puede exigir verificar la edad.";
        if (Contains("not a bot") || Contains("confirm you're not a bot"))
            return "YouTube solicita verificar la sesión. Abre el vídeo en tu navegador y completa la verificación; después configura la sesión opcional en Descargas web y reintenta.";
        if (Contains("private video") || Contains("members-only") || Contains("members only"))
            return "El vídeo es privado o exclusivo para miembros. Se necesita una sesión con permiso para verlo; configura la cuenta opcional en Descargas web.";
        if (Contains("sign in") || Contains("not a bot") || Contains("login required"))
            return "El sitio solicita iniciar sesión. Puedes habilitar las cookies del navegador en Descargas web.";
        if (Contains("po token"))
            return "El sitio exige un PO Token para los formatos disponibles. Revisa Descargas web o reintenta más tarde.";
        if (Contains("requested format is not available") && quality == WebDownloadQuality.Light)
            return "El sitio no ofrece una vista ligera compatible. Prueba descargar la vista completa.";
        if (Contains("403")) return "El sitio rechazó el acceso (HTTP 403). Comprueba el enlace y los ajustes de descarga.";
        if (Contains("timed out") || Contains("timeout") || Contains("unable to download webpage") || Contains("name resolution"))
            return "No se pudo completar la conexión con el sitio. Comprueba la red y reintenta.";
        if (Contains("unsupported url")) return "El sitio del enlace no es compatible.";
        if (Contains("private video") || Contains("video unavailable"))
            return "El vídeo está privado o no está disponible.";
        return "El sitio no entregó el medio. Comprueba que el enlace se abre en el navegador y reintenta.";

        bool Contains(string value) => error.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
