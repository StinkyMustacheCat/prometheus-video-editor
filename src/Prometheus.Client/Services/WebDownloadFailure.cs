using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Prometheus.Client.Services;

internal enum WebFailureKind
{
    RateLimit, Verification, CookiesLocked, CookiesUnreadable, ProfileMissing, CookieFile,
    Authentication, PoToken, Network, Format, Forbidden, Unsupported, Unavailable, Other
}

internal sealed class WebDownloadFailure : InvalidOperationException
{
    internal WebFailureKind Kind { get; }
    internal DateTimeOffset? RetryAfter { get; }
    internal bool IsCookieFailure => Kind is WebFailureKind.CookiesLocked or WebFailureKind.CookiesUnreadable or
        WebFailureKind.ProfileMissing or WebFailureKind.CookieFile;

    internal WebDownloadFailure(WebFailureKind kind, string? message = null, DateTimeOffset? retryAfter = null)
        : base(message ?? Describe(kind))
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    internal static WebDownloadFailure FromDiagnostics(string diagnostic, WebDownloadQuality? quality = null)
    {
        var lines = diagnostic.Split('\n');
        var errors = string.Join("\n", lines.Where(line => line.TrimStart().StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)));
        var detail = errors.Length != 0 ? errors : diagnostic;
        bool Found(params string[] terms) => terms.Any(term => detail.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (Found("not a bot", "captcha", "verify you are human", "confirm you're human"))
            return new(WebFailureKind.Verification);
        if (diagnostic.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase) ||
            Found("HTTP 429", "too many requests", "this content isn't available, try again later"))
            return new(WebFailureKind.RateLimit, retryAfter: ReadRetryAfter(diagnostic, DateTimeOffset.UtcNow));
        if (Found("failed to decrypt", "could not decrypt", "decryption failed", "DPAPI", "app-bound", "failed to read the key"))
            return new(WebFailureKind.CookiesUnreadable);
        if (Found("could not find") && Found("cookies", "cookie database", "profile"))
            return new(WebFailureKind.ProfileMissing);
        if (Found("could not copy", "database is locked", "permissionerror") && Found("cookie", "database"))
            return new(WebFailureKind.CookiesLocked);
        if (Found("cookies database", "cookie database", "failed to load cookies"))
            return new(WebFailureKind.CookiesUnreadable);
        if (Found("age-restricted", "age restricted", "confirm your age", "age verification"))
            return new(WebFailureKind.Authentication, "El vídeo tiene restricción de edad. Elige una sesión autorizada para verlo.");
        if (Found("private video", "members-only", "members only", "sign in", "login required", "authentication required"))
            return new(WebFailureKind.Authentication);
        if (Found("po token", "po-token provider", "po_token provider") ||
            Found("requested format is not available") &&
            diagnostic.Contains("require a PO Token", StringComparison.OrdinalIgnoreCase))
            return new(WebFailureKind.PoToken);
        if (Found("requested format is not available") && quality == WebDownloadQuality.Light)
            return new(WebFailureKind.Format, "El sitio no ofrece una vista ligera compatible. Puedes elegir la vista completa.");
        if (Found("HTTP Error 403", "HTTP 403")) return new(WebFailureKind.Forbidden);
        if (Found("timed out", "timeout", "connection reset", "connection aborted", "temporary failure",
            "name resolution", "network is unreachable", "failed to resolve", "HTTP Error 500", "HTTP Error 502",
            "HTTP Error 503", "HTTP Error 504", "incomplete read"))
            return new(WebFailureKind.Network, retryAfter: ReadRetryAfter(diagnostic, DateTimeOffset.UtcNow));
        if (Found("unsupported url")) return new(WebFailureKind.Unsupported);
        if (Found("video unavailable", "has been removed", "does not exist")) return new(WebFailureKind.Unavailable);
        return new(WebFailureKind.Other);
    }

    internal static DateTimeOffset? ReadRetryAfter(string diagnostic, DateTimeOffset now)
    {
        var value = Regex.Match(diagnostic, @"(?im)(?:^|[\s,])Retry-After\s*:\s*([^\r\n]+)").Groups[1].Value.Trim();
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return now.AddSeconds(seconds);
        return DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var absolute) && absolute > now ? absolute : null;
    }

    private static string Describe(WebFailureKind kind) => kind switch
    {
        WebFailureKind.RateLimit => "El sitio limitó las solicitudes. La cola de YouTube está en espera.",
        WebFailureKind.Verification => "YouTube solicita verificar la sesión. Completa la verificación en el navegador.",
        WebFailureKind.CookiesLocked => "No se pudieron abrir las cookies del navegador. Cierra el navegador y vuelve a probar el perfil.",
        WebFailureKind.CookiesUnreadable => "No se pudieron descifrar las cookies del navegador. Prueba una sesión accesible, por ejemplo Firefox, o un archivo de cookies.",
        WebFailureKind.ProfileMissing => "No se encontró la base de cookies del perfil seleccionado. Elige el perfil donde iniciaste sesión.",
        WebFailureKind.CookieFile => "El archivo de cookies no está disponible o no tiene formato Netscape.",
        WebFailureKind.Authentication => "El vídeo requiere una sesión con permiso para verlo.",
        WebFailureKind.PoToken => "El formato solicitado requiere un PO Token.",
        WebFailureKind.Network => "No se pudo completar la conexión. Comprueba la red y vuelve a intentar.",
        WebFailureKind.Forbidden => "El sitio rechazó el acceso (HTTP 403). Comprueba que el enlace se abre en el navegador.",
        WebFailureKind.Unsupported => "El sitio del enlace no es compatible.",
        WebFailureKind.Unavailable => "El vídeo no está disponible.",
        _ => "El sitio no entregó el medio. Comprueba que el enlace se abre en el navegador."
    };
}
