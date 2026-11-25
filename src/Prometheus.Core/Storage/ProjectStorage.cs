using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Prometheus.Core.Storage;

public static class ProjectStorage
{
    public const string ProjectExtension = ".pthproj";

    public static string RecoveryDirectory =>
        Environment.GetEnvironmentVariable("PROMETHEUS_RECOVERY_ROOT") ??
        Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory)!, "Prometheus", "Recovery");

    public static bool IsProjectFile(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && path.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetProjectStem(string projectFile)
    {
        string name = Path.GetFileName(projectFile);
        if (IsProjectFile(name))
            return name.Substring(0, name.Length - ProjectExtension.Length);

        return Path.GetFileNameWithoutExtension(name);
    }

    public static string GetDataDirectory(string projectFile)
    {
        string full = Path.GetFullPath(projectFile);
        string? dir = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
        return Path.Combine(dir, GetProjectStem(full) + ".assets");
    }

    public static string GetWebDirectory(string projectFile) => Path.Combine(GetDataDirectory(projectFile), "medios", "web");
    public static string GetImportDirectory(string projectFile) => Path.Combine(GetDataDirectory(projectFile), "medios", "importados");
    public static string GetLutDirectory(string projectFile) => Path.Combine(GetDataDirectory(projectFile), "recursos", "luts");
    public static string GetProxyDirectory(string projectFile) => Path.Combine(GetDataDirectory(projectFile), "cache", "vistas-ligeras");
    public static string GetSamDirectory(string projectFile) => Path.Combine(GetDataDirectory(projectFile), "recursos", "sam");

    public static string GetAutosaveFile(string projectFile)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(projectFile).ToUpperInvariant());
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 24);
        return Path.Combine(RecoveryDirectory, hash + ProjectExtension);
    }

    public static void CopyResourceTransactional(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void EnsureDirectories(string projectFile)
    {
        Directory.CreateDirectory(GetDataDirectory(projectFile));
        Directory.CreateDirectory(GetWebDirectory(projectFile));
        Directory.CreateDirectory(GetImportDirectory(projectFile));
        Directory.CreateDirectory(GetLutDirectory(projectFile));
        Directory.CreateDirectory(GetProxyDirectory(projectFile));
        Directory.CreateDirectory(GetSamDirectory(projectFile));
        Directory.CreateDirectory(RecoveryDirectory);
    }
}
