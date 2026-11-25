using System;
using System.Diagnostics;
using System.IO;

namespace Prometheus.Client.Services;

internal sealed class WebCookieFile : IDisposable
{
    internal string PathOnDisk { get; }
    private WebCookieFile(string path) => PathOnDisk = path;

    internal static WebCookieFile Open(string source, ProcessStartInfo start)
    {
        if (!File.Exists(source)) throw new InvalidOperationException("El archivo de cookies no está disponible. Vuelve a seleccionarlo en Descargas web.");
        using (var reader = File.OpenText(source))
        {
            var header = reader.ReadLine()?.Trim();
            if (header is not ("# Netscape HTTP Cookie File" or "# HTTP Cookie File"))
                throw new InvalidOperationException("El archivo de cookies debe estar en formato Netscape (.txt). Selecciona una exportación válida en Descargas web.");
        }
        var folder = Environment.GetEnvironmentVariable("PROMETHEUS_AUTH_TEMP") ?? Path.Combine(Path.GetTempPath(), "Prometheus-WebAuth");
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".txt");
        File.Copy(source, temporary, false);
        start.ArgumentList.Add("--cookies"); start.ArgumentList.Add(temporary);
        return new WebCookieFile(temporary);
    }

    public void Dispose()
    {
        try { File.Delete(PathOnDisk); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
