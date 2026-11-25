using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class EffectControlsPanelView
{
    private ProjectController? _transitionController;
    private string? _librarySignature;

    private void AttachTransitionLibrary()
    {
        DetachTransitionLibrary();
        _transitionController = ProjectController.Instance;
        if (_transitionController != null) _transitionController.StateChanged += RefreshTransitionLibrary;
        RefreshTransitionLibrary();
    }

    private void DetachTransitionLibrary()
    {
        if (_transitionController != null) _transitionController.StateChanged -= RefreshTransitionLibrary;
        _transitionController = null;
    }

    private void RefreshTransitionLibrary()
    {
        var project = _transitionController?.CurrentProject;
        var key = project?.Id + ":" + string.Join('|', project?.MgtTransitionLibrary.Select(t => t.Id) ?? []);
        if (key == _librarySignature) return;
        _librarySignature = key;
        RefreshEffects();
    }

    private async Task ImportMgtTransitionAsync()
    {
        if (!MgtTransitionCatalog.ImportEnabled || ProjectController.Instance is not { } controller ||
            TopLevel.GetTopLevel(this) is not { } top) return;
        var projectId = controller.CurrentProject.Id;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Importar transición MGT GPU",
                FileTypeFilter = [new FilePickerFileType("Transición MGT") { Patterns = ["*.json"] }]
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            var definition = await Task.Run(() =>
            {
                if (new FileInfo(path).Length > 256 * 1024)
                    throw new InvalidDataException("La plantilla MGT supera 256 KiB.");
                var template = MgtGpuTransition.Parse(File.ReadAllText(path));
                MgtGpuCanvasProgram.Check(template);
                return template;
            });
            if (controller.CurrentProject.Id != projectId) return;
            if (MgtTransitionCatalog.Supports(controller.CurrentProject, definition.Id) ||
                AudioTransitionOperations.Supports(definition.Id))
                throw new InvalidDataException("Ya existe una transición con ese identificador.");
            controller.UndoRedo.Commit(p => p.MgtTransitionLibrary.Add(definition), "Importar transición MGT");
            controller.ReportStatus("Transición MGT añadida: " + definition.Name);
            RefreshEffects();
        }
        catch (Exception error) { controller.ReportStatus("Transiciones MGT: " + error.Message); }
    }
}
