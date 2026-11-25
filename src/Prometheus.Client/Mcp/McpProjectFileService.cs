using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Client.Services;

namespace Prometheus.Client.Mcp;

internal static class McpProjectFileService
{
    public static async Task<object> ExecuteAsync(ProjectController controller, JsonElement args, CancellationToken cancellation = default)
    {
        var operation = McpJson.String(args, "operation")
            ?? throw new ArgumentException("Falta operation");
        var path = McpJson.String(args, "file_path");
        var discard = McpJson.Bool(args, "discard_unsaved");
        var recovered = 0;
        var missing = 0;
        switch (operation)
        {
            case "new":
                controller.NewProjectWithoutDialog(McpJson.String(args, "name"), discard);
                break;
            case "close":
                controller.CloseProjectWithoutDialog(discard);
                break;
            case "open":
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Falta file_path");
                (recovered, missing) = await controller.OpenProjectWithoutDialogAsync(path, discard, cancellation);
                break;
            case "recoveries":
                return new { recoveries = (await Task.Run(() => controller.RecoveryCandidates(path), cancellation)).Select(entry => new {
                    file_path = entry.FilePath, original_path = entry.OriginalPath, name = entry.Name,
                    saved_utc = entry.SavedUtc, revision = entry.Revision, is_backup = entry.IsBackup }) };
            case "recover":
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Falta file_path");
                await controller.RecoverProjectWithoutDialogAsync(path, discard, cancellation);
                break;
            case "save":
                path ??= controller.CurrentProjectPath;
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Falta file_path para el primer guardado");
                controller.SaveProjectToPath(path);
                break;
            default: throw new ArgumentException("operation debe ser new, open, close, save, recoveries o recover");
        }
        var project = controller.CurrentProject;
        return new { operation, project_id = project.Id, project_name = project.Name,
            revision = project.Revision, file_path = controller.CurrentProjectPath,
            dirty = controller.IsDirty, recovered_assets = recovered, missing_assets = missing };
    }
}
