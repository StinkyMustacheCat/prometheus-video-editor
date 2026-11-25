using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

internal static class MediaRelinkService
{
    public static async Task<Asset> CheckAsync(Project project, Asset original, string path, CancellationToken cancellation = default)
    {
        var candidate = await LocalMediaService.ProbeAsync(path, cancellation);
        ProjectMediaRecovery.ValidateReplacement(project, original, candidate);
        return candidate;
    }

    public static async Task<IReadOnlyList<string>> RecoverAsync(Project project, string projectPath, string? originalProjectPath = null,
        CancellationToken cancellation = default)
    {
        var missing = project.Assets.Where(asset => ProjectMediaRecovery.NeedsRecovery(asset, projectPath)).ToArray();
        if (missing.Length == 0) return System.Array.Empty<string>();
        var roots = new[] { projectPath, originalProjectPath }.Where(path => path != null)
            .Select(path => ProjectStorage.GetDataDirectory(path!)).Distinct(System.StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = await Task.Run(() => roots.Select(root =>
            (Root: root, Files: ProjectMediaRecovery.EnumerateFiles(root))).ToArray(), cancellation);
        var recovered = new List<string>();
        foreach (var asset in missing)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var matches = await Task.Run(() => candidates.Select(candidate =>
                        ProjectMediaRecovery.FindUnique(asset, candidate.Files, candidate.Root))
                    .Where(path => path != null).Distinct(System.StringComparer.OrdinalIgnoreCase).ToArray(), cancellation);
                if (matches.Length != 1) continue;
                var path = matches[0]!;
                var candidate = await CheckAsync(project, asset, path, cancellation);
                ProjectMediaRecovery.Relink(project, asset, path, candidate);
                recovered.Add(asset.Id);
            }
            catch (System.Exception error) when (error is IOException or System.InvalidOperationException or System.ArgumentException or
                System.NotSupportedException or System.TimeoutException or System.Text.Json.JsonException)
            {
                // Keep unresolved media available for explicit reconnection.
            }
        }
        return recovered;
    }
}
