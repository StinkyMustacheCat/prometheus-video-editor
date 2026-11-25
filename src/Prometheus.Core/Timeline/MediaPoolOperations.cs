using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public static class MediaPoolOperations
{
    public static bool AppearsInPool(Asset asset) => asset.TextOverlay == null &&
        !PlaceholderService.IsPlaceholder(asset) && !asset.Source.Uri.StartsWith("nested:", StringComparison.Ordinal);

    private static string Name(string value)
    {
        var name = value.Trim();
        if (name.Length == 0 || name.Any(char.IsControl))
            throw new ArgumentException("El nombre no puede estar vacío ni contener caracteres de control.");
        return name;
    }

    public static void RenameAsset(Project project, string assetId, string name)
    {
        var asset = project.Assets.FirstOrDefault(a => a.Id == assetId)
            ?? throw new ArgumentException("Medio no encontrado.");
        asset.Name = Name(name);
    }

    public static MediaFolder CreateFolder(Project project, string name)
    {
        var folder = new MediaFolder { Name = FolderName(project, name) };
        project.MediaFolders.Add(folder);
        return folder;
    }

    public static void RenameFolder(Project project, string folderId, string name)
    {
        var folder = project.MediaFolders.FirstOrDefault(f => f.Id == folderId)
            ?? throw new ArgumentException("Carpeta no encontrada.");
        folder.Name = FolderName(project, name, folderId);
    }

    private static string FolderName(Project project, string name, string? except = null)
    {
        name = Name(name);
        if (project.MediaFolders.Any(f => f.Id != except && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ya existe una carpeta con ese nombre.");
        return name;
    }

    public static void MoveAssets(Project project, IEnumerable<string> assetIds, string? folderId)
    {
        if (folderId != null && project.MediaFolders.All(f => f.Id != folderId))
            throw new ArgumentException("Carpeta de destino no encontrada.");
        var ids = assetIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) throw new ArgumentException("Selecciona al menos un medio.");
        var assets = ids.Select(id => project.Assets.FirstOrDefault(a => a.Id == id)
            ?? throw new ArgumentException("Medio no encontrado: " + id)).ToArray();
        foreach (var asset in assets) asset.FolderId = folderId;
    }
}
