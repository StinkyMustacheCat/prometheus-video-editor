using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>Extracts the declared WAV stream; rendered video frames are never cached.</summary>
public static class MgtProjectAudio
{
    private static readonly object Gate = new();

    public static string? Resolve(Asset? asset, string? resolvedPath = null)
    {
        if (asset?.Source.Kind != "mgt" || asset.ContainerFormat != "mgtproj" ||
            !asset.Streams.Exists(stream => stream.Kind == "audio")) return null;
        var path = resolvedPath ?? asset.EffectiveLocator?.Path ?? throw new FileNotFoundException("Falta el proyecto MGT.");
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Falta el proyecto MGT.", path);
        var drive = Path.GetPathRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("No se encontró el disco de la aplicación.");
        var directory = Path.Combine(drive, "Prometheus", "ExtractedMgtAudio");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks)));
        var destination = Path.Combine(directory, key + ".wav");
        lock (Gate)
        {
            if (File.Exists(destination) && new FileInfo(destination).Length > 0) return destination;
            Directory.CreateDirectory(directory);
            using var zip = ZipFile.OpenRead(path);
            var document = zip.GetEntry("project.json") ?? throw new InvalidDataException("Falta project.json.");
            using var source = JsonDocument.Parse(document.Open());
            var entryName = MgtLiveAsset.ProjectAudioEntry(source.RootElement)
                ?? throw new InvalidDataException("Falta el audio del MGT.");
            var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException("Falta el WAV del MGT.");
            if (entry.Length is <= 0 or > 200_000_000)
                throw new InvalidDataException("El audio del MGT supera el límite permitido.");
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var input = entry.Open())
                using (var output = File.Create(temporary)) input.CopyTo(output);
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return destination;
        }
    }
}
