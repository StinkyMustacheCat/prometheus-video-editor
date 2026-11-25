using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Prometheus.Core.Model;

namespace Prometheus.Core.Storage;

public sealed record ProjectRecoveryEntry(string FilePath, string? OriginalPath, string Name,
    DateTime SavedUtc, int Revision, bool IsBackup, string? ProjectId = null)
{
    public string ResourcePath => IsBackup ? OriginalPath! : FilePath;
}

public sealed class ProjectRecoveryStore
{
    private readonly string _directory;
    private readonly string[] _directories;

    public ProjectRecoveryStore(string? directory = null)
    {
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory ?? ProjectStorage.RecoveryDirectory));
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prometheus", "Recovery");
        _directories = directory == null
            ? new[] { _directory, legacy }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [_directory];
    }

    public string FileFor(string projectId, string sessionId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectId + "|" + sessionId)));
        return Path.Combine(_directory, key + ProjectStorage.ProjectExtension);
    }

    public void Save(Project snapshot, string? originalPath, string sessionId)
    {
        using var process = Process.GetCurrentProcess();
        snapshot.Recovery = new(originalPath == null ? null : Path.GetFullPath(originalPath),
            DateTime.UtcNow, process.Id, process.StartTime.ToUniversalTime().Ticks);
        ProjectStore.SaveTransactional(snapshot, FileFor(snapshot.Id, sessionId));
    }

    public IReadOnlyList<ProjectRecoveryEntry> List(string? originalPath = null, bool includeActive = false)
    {
        var full = originalPath == null ? null : Path.GetFullPath(originalPath);
        var entries = new List<ProjectRecoveryEntry>();
        foreach (var directory in _directories.Where(Directory.Exists))
        {
            string[] files;
            try { files = Directory.GetFiles(directory, "*" + ProjectStorage.ProjectExtension); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (var path in files)
            {
                var entry = Read(path, includeActive);
                if (entry == null) continue;
                var legacyMatch = full != null && Path.GetFileName(path) == Path.GetFileName(ProjectStorage.GetAutosaveFile(full));
                if (full == null || string.Equals(entry.OriginalPath, full, StringComparison.OrdinalIgnoreCase))
                    entries.Add(entry);
                else if (legacyMatch) entries.Add(entry with { OriginalPath = full });
            }
        }
        if (full != null && Read(full + ".bak", includeActive: true) is { } backup)
            entries.Add(backup with { OriginalPath = full, IsBackup = true });
        return entries.OrderByDescending(entry => entry.SavedUtc).ToArray();
    }

    public ProjectRecoveryEntry? Read(string path, bool includeActive = false)
    {
        var full = Path.GetFullPath(path);
        var backup = full.EndsWith(ProjectStorage.ProjectExtension + ".bak", StringComparison.OrdinalIgnoreCase);
        if (!backup && !_directories.Any(directory =>
            string.Equals(Path.GetDirectoryName(full), directory, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            var project = ProjectStore.LoadTransactional(full, out _);
            var metadata = project.Recovery;
            if (!includeActive && metadata != null && ProcessAlive(metadata)) return null;
            return new(full, backup ? full[..^4] : metadata?.OriginalPath, project.Name,
                metadata?.SavedUtc ?? File.GetLastWriteTimeUtc(full), project.Revision, backup, project.Id);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool ProcessAlive(ProjectRecoveryInfo metadata)
    {
        try
        {
            using var process = Process.GetProcessById(metadata.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == metadata.ProcessStartedUtcTicks;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception) { return false; }
    }

    public void Remove(string path)
    {
        var full = Path.GetFullPath(path);
        if (!_directories.Any(directory => string.Equals(Path.GetDirectoryName(full), directory,
                StringComparison.OrdinalIgnoreCase)) || !ProjectStorage.IsProjectFile(full)) return;
        File.Delete(full);
        File.Delete(full + ".bak");
        var resources = Path.GetFullPath(ProjectStorage.GetDataDirectory(full));
        // Only the generated asset directory beside this recovery file can be removed.
        if (!string.Equals(Path.GetDirectoryName(resources), Path.GetDirectoryName(full),
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("La carpeta de recuperación está fuera de su ubicación.");
        if (Directory.Exists(resources)) Directory.Delete(resources, recursive: true);
    }

    public static void ResolveRelativeMedia(Project project, string? sourceProjectPath)
    {
        if (sourceProjectPath == null) return;
        var root = Path.GetDirectoryName(Path.GetFullPath(sourceProjectPath))!;
        foreach (var asset in project.Assets)
        {
            if (asset.Source.Kind is "local" or "mgt" && !string.IsNullOrWhiteSpace(asset.Source.Uri) &&
                (asset.Source.Relative || !Path.IsPathRooted(asset.Source.Uri)))
            {
                asset.Source.Uri = Path.GetFullPath(Path.Combine(root, asset.Source.Uri));
                asset.Source.Relative = false;
            }
            foreach (var representation in asset.Representations.Where(item =>
                !string.IsNullOrWhiteSpace(item.Locator.Path) &&
                (item.Locator.Relative || !Path.IsPathRooted(item.Locator.Path)) &&
                !(Uri.TryCreate(item.Locator.Path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")))
            {
                representation.Locator.Path = Path.GetFullPath(Path.Combine(root, representation.Locator.Path));
                representation.Locator.Relative = false;
            }
        }
    }
}
