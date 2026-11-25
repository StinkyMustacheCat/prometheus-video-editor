using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

public partial class ProjectController
{
    private readonly RecentProjectStore _recentProjects = new();
    internal IReadOnlyList<string> RecentProjects => _recentProjects.List();

    private void RememberProject(string? path)
    {
        if (path == null) return;
        try { _recentProjects.Record(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { ReportStatus("El proyecto está listo, pero no se pudo actualizar la lista de recientes: " + error.Message); }
    }

    internal void ClearRecentProjects()
    {
        try { _recentProjects.Clear(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ReportStatus("No se pudieron limpiar los recientes: " + error.Message); }
    }

    internal async Task<bool> OpenRecentProjectAsync(string path)
    {
        BeforeHistoryNavigation?.Invoke();
        var guard = BeginOpen();
        if (!File.Exists(path)) { ReportStatus("El proyecto reciente ya no está disponible: " + path); return false; }
        if (!await ConfirmDiscardChangesAsync("abrir otro proyecto")) return false;
        try { AfterOpenConsent(guard); }
        catch (InvalidOperationException error) { ReportStatus(error.Message); return false; }
        return await OpenProjectFromPathAsync(path);
    }

    public async Task<bool> CloseProjectAsync()
    {
        BeforeHistoryNavigation?.Invoke();
        var guard = BeginOpen();
        if (!await ConfirmDiscardChangesAsync("cerrar el proyecto")) return false;
        try
        {
            AfterOpenConsent(guard);
            CloseProjectWithoutDialog(discardUnsaved: true);
            return true;
        }
        catch (InvalidOperationException error) { ReportStatus(error.Message); return false; }
    }

    public void CloseProjectWithoutDialog(bool discardUnsaved)
    {
        BeforeHistoryNavigation?.Invoke();
        if (IsDirty && !discardUnsaved)
            throw new InvalidOperationException("Hay cambios sin guardar; guarda o pasa discard_unsaved: true.");
        Transport.Pause();
        RecordAction("Cerrar proyecto");
        _history.Flush();
        ForgetRecovery();
        _openRequest++;
        _currentProjectPath = null;
        _resourceProjectPath = null;
        _activeRecoveryFile = null;
        SetSelectedClip(null);
        SamToolEnabled = false;
        ShapeMaskTool = null;
        TextToolEnabled = false;
        _undoRedoManager.Reset(ProjectStore.CreateNew(), isDirty: false);
        _history.DiscardUnsaved();
        _loggedAutosave = false;
        Transport.Reset();
        ReportStatus("Proyecto cerrado");
        StateChanged?.Invoke();
    }
}
