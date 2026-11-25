using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Layout;
using Prometheus.Client.Dialogs;
using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

public partial class ProjectController
{
    private readonly ProjectRecoveryStore _recoveries = new();
    private string _recoverySessionId = Guid.NewGuid().ToString("N");
    private string? _resourceProjectPath;
    private string? _activeRecoveryFile;
    private long _projectStateVersion;
    internal long StateVersion => _projectStateVersion;
    private long _openRequest;
    private sealed record OpenGuard(Project Project, int Revision, string? Path, long State, long Request);
    private OpenGuard BeginOpen() => new(CurrentProject, CurrentProject.Revision, CurrentProjectPath,
        _projectStateVersion, ++_openRequest);
    private void VerifyOpen(OpenGuard guard)
    {
        if (guard.Request != _openRequest || !ReferenceEquals(guard.Project, CurrentProject) ||
            guard.Revision != CurrentProject.Revision || guard.State != _projectStateVersion ||
            !string.Equals(guard.Path, CurrentProjectPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El proyecto cambió durante la apertura. Vuelve a abrir la sesión elegida.");
    }

    private OpenGuard AfterOpenConsent(OpenGuard guard)
    {
        // Saving from the confirmation may update paths/resources but must not
        // authorize replacement of a different or concurrently edited project.
        if (guard.Request != _openRequest || !ReferenceEquals(guard.Project, CurrentProject) ||
            guard.Revision != CurrentProject.Revision)
            throw new InvalidOperationException("El proyecto cambió durante la confirmación.");
        return guard with { Path = CurrentProjectPath, State = _projectStateVersion };
    }

    public IReadOnlyList<ProjectRecoveryEntry> RecoveryCandidates(string? originalPath = null) =>
        _recoveries.List(originalPath);

    public async Task OfferRecoveryAsync()
    {
        try
        {
            var guard = BeginOpen();
            var candidates = (await Task.Run(() => _recoveries.List())).ToList();
            VerifyOpen(guard);
            if (_currentProjectPath != null)
                candidates.AddRange(_recoveries.List(_currentProjectPath).Where(entry => entry.IsBackup));
            if (candidates.Count == 0) { ReportStatus("No hay copias de recuperación pendientes."); return; }
            var choice = await ChooseRecoveryAsync(candidates, "Elige la sesión o copia que quieres recuperar.");
            if (choice == null) return;
            VerifyOpen(guard);
            if (!await ConfirmDiscardChangesAsync("recuperar una sesión")) return;
            guard = AfterOpenConsent(guard);
            var project = await Task.Run(() => ProjectStore.LoadTransactional(choice.FilePath, out _));
            await AdoptWithDialogAsync(project, choice.OriginalPath, choice.ResourcePath,
                choice.IsBackup ? null : choice.FilePath, recoveredVersion: true, guard);
        }
        catch (Exception error) { ReportStatus("No se pudo recuperar la sesión: " + error.Message); }
    }

    private async Task<bool> AdoptWithDialogAsync(Project project, string? originalPath, string resourcePath,
        string? recoveryFile, bool recoveredVersion, OpenGuard? expected = null)
    {
        var guard = expected ?? BeginOpen();
        ProjectRecoveryStore.ResolveRelativeMedia(project, originalPath ?? resourcePath);
        var recovered = await MediaRelinkService.RecoverAsync(project, resourcePath, originalPath);
        var report = PreflightAuditor.Audit(project, Path.GetDirectoryName(resourcePath), autoPurgeOrphans: false);
        var relinked = recovered.Count;
        if (report.HasMissingAssets)
        {
            var dialog = new OfflineMediaDialog();
            dialog.Initialize(report.MissingAssets, project);
            await dialog.ShowDialog(_ownerWindow);
            if (dialog.WasCancelled) return false;
            foreach (var item in dialog.Items.Where(item => item.ResolvedPath != null && item.ProbedAsset != null))
            {
                var asset = project.Assets.Single(candidate => candidate.Id == item.AssetId);
                ProjectMediaRecovery.Relink(project, asset, item.ResolvedPath!, item.ProbedAsset!);
                relinked++;
            }
        }
        VerifyOpen(guard);
        AdoptProject(project, originalPath, resourcePath, recoveryFile, recoveredVersion || relinked > 0);
        RecordAction(recoveredVersion ? "Recuperar proyecto" : "Abrir proyecto",
            relinked > 0 ? $"{relinked} medios reconectados" : null);
        ReportStatus(recoveredVersion ? "Sesión recuperada · guarda para conservar los cambios" :
            relinked > 0 ? "Proyecto abierto · guarda las rutas reconectadas" : "Proyecto cargado: " + project.Name);
        return true;
    }

    private void AdoptProject(Project project, string? originalPath, string resourcePath,
        string? recoveryFile, bool dirty)
    {
        ForgetRecovery(recoveryFile);
        // Recovering this process' own autosave must not let its next autosave
        // overwrite the original recovery that remains available until save.
        if (recoveryFile != null) _recoverySessionId = Guid.NewGuid().ToString("N");
        project.Recovery = null;
        _currentProjectPath = originalPath;
        _resourceProjectPath = resourcePath;
        _activeRecoveryFile = recoveryFile;
        _undoRedoManager.Reset(project, isDirty: dirty);
        _history.DiscardUnsaved();
        _loggedAutosave = false;
        Transport.Reset();
        RememberProject(originalPath);
        StateChanged?.Invoke();
    }

    public async Task RecoverProjectWithoutDialogAsync(string filePath, bool discardUnsaved,
        CancellationToken cancellation = default)
    {
        if (IsDirty && !discardUnsaved)
            throw new InvalidOperationException("Hay cambios sin guardar; guarda o indica discard_unsaved.");
        var guard = BeginOpen();
        var entry = await Task.Run(() => _recoveries.Read(filePath, includeActive: true), cancellation)
            ?? throw new InvalidDataException("La copia de recuperación no es válida.");
        var project = await Task.Run(() => ProjectStore.LoadTransactional(entry.FilePath, out _), cancellation);
        ProjectRecoveryStore.ResolveRelativeMedia(project, entry.OriginalPath ?? entry.ResourcePath);
        await MediaRelinkService.RecoverAsync(project, entry.ResourcePath, entry.OriginalPath, cancellation);
        cancellation.ThrowIfCancellationRequested();
        VerifyOpen(guard);
        AdoptProject(project, entry.OriginalPath, entry.ResourcePath,
            entry.IsBackup ? null : entry.FilePath, dirty: true);
        RecordAction("Recuperar proyecto");
    }

    private async Task<ProjectRecoveryEntry?> ChooseRecoveryAsync(IReadOnlyList<ProjectRecoveryEntry> entries, string message)
    {
        var list = new ListBox { MinHeight = 150, SelectedIndex = 0,
            ItemsSource = entries.Select(entry => $"{entry.Name} · {entry.SavedUtc.ToLocalTime():g} · " +
                (entry.IsBackup ? "Copia de seguridad" : entry.FilePath == entry.OriginalPath ? "Último guardado" : "Recuperación")).ToArray() };
        var open = new Button { Content = "Abrir selección", MinWidth = 120 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 88 };
        open.Classes.Add("nle-accent");
        cancel.Classes.Add("nle-btn");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(open);
        var body = new Grid { Margin = new Avalonia.Thickness(16), RowDefinitions = new("Auto,*,Auto") };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 12) });
        Grid.SetRow(list, 1); body.Children.Add(list);
        Grid.SetRow(buttons, 2); body.Children.Add(buttons);
        var dialog = new Window { Title = "Recuperar proyecto", Width = 660, Height = 360,
            MinWidth = 460, MinHeight = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body };
        open.Click += (_, _) => { if (list.SelectedIndex >= 0) dialog.Close(entries[list.SelectedIndex]); };
        cancel.Click += (_, _) => dialog.Close();
        return await dialog.ShowDialog<ProjectRecoveryEntry?>(_ownerWindow);
    }

    private void ForgetRecovery(string? keep = null, bool saved = false)
    {
        var paths = new[] { _recoveries.FileFor(CurrentProject.Id, _recoverySessionId),
            saved ? _activeRecoveryFile : null };
        foreach (var path in paths.Where(path => path != null && !string.Equals(path, keep, StringComparison.OrdinalIgnoreCase)).Distinct())
        {
            try { _recoveries.Remove(path!); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ReportNotice("recovery-cleanup:" + CurrentProject.Id, "No se pudo retirar la copia de recuperación", error.Message);
            }
        }
        _activeRecoveryFile = null;
    }
}
