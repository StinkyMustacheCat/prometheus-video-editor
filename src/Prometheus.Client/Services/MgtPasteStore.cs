using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>Stores a pasted MGT as a regular durable media source outside versioned releases.</summary>
public static class MgtPasteStore
{
    public static Asset Import(string content, string aspect = "horizontal")
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("El portapapeles no contiene texto MGT.");
        var trimmed = content.Trim();
        if (trimmed.Length > 10_000_000)
            throw new InvalidDataException("El MGT pegado supera el límite permitido.");
        var isComposition = false;
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var parsed = JsonDocument.Parse(trimmed);
                isComposition = parsed.RootElement.ValueKind == JsonValueKind.Object &&
                    parsed.RootElement.TryGetProperty("render", out _);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("El MGT JSON pegado no es válido: " + error.Message, error);
            }
            if (!isComposition)
                throw new InvalidDataException("El texto pegado parece JSON, pero no contiene una composición MGT completa.");
        }
        var document = isComposition ? trimmed : MgtPastedText.Create(trimmed, aspect);
        var drive = Path.GetPathRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("No se encontró el disco de la aplicación.");
        var directory = Path.Combine(drive, "Prometheus", "ImportedMGT");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pegado-" + Guid.NewGuid().ToString("N") + ".mgt.json");
        try
        {
            File.WriteAllText(path, document, new UTF8Encoding(false));
            return MgtLiveAsset.Inspect(path);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }
}
