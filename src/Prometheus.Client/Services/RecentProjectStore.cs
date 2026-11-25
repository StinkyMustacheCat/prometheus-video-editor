using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

internal sealed class RecentProjectStore
{
    private static readonly object Gate = new();
    private readonly string _path;
    private const int Limit = 10;

    public RecentProjectStore(string? path = null) => _path = path ??
        Environment.GetEnvironmentVariable("PROMETHEUS_RECENT_PROJECTS") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "recent-projects.json");

    public IReadOnlyList<string> List()
    {
        lock (Gate)
        {
            try
            {
                return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)) ?? [])
                    .Where(p => !string.IsNullOrWhiteSpace(p) && ProjectStorage.IsProjectFile(p) && Path.IsPathFullyQualified(p))
                    .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(Limit).ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            { return Array.Empty<string>(); }
        }
    }

    public void Record(string path)
    {
        path = Path.GetFullPath(path);
        if (!ProjectStorage.IsProjectFile(path)) throw new ArgumentException("El archivo debe ser un proyecto .pthproj.");
        lock (Gate) Write(new[] { path }.Concat(List().Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase))).Take(Limit));
    }

    public void Clear() { lock (Gate) Write(Array.Empty<string>()); }

    private void Write(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(paths.ToArray(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
