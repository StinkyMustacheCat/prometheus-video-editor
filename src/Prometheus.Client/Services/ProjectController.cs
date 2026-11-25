using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Prometheus.Client.Dialogs;
using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.Client.Services;

public partial class ProjectController
{
    private readonly Window _ownerWindow;
    private readonly UndoRedoManager _undoRedoManager;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _historyTimer;
    private Task? _historyFlush;
    private bool _loggedAutosave;
    private string? _currentProjectPath;
    private AudioMixPlan? _lastAudioPlan;

    public static ProjectController? Instance { get; private set; }

    public UndoRedoManager UndoRedo => _undoRedoManager;
    public PlaybackTransport Transport { get; }
    public Project CurrentProject => _undoRedoManager.CurrentProject;
    public string? CurrentProjectPath => _currentProjectPath;
    public string? ResourceProjectPath => _resourceProjectPath ?? _currentProjectPath;
    public bool IsDirty => _undoRedoManager.IsDirty;

    private static readonly string ClientVersion = typeof(ProjectController).Assembly.GetName().Version!.ToString(3);

    public string WindowTitle =>
        $"{CurrentProject.Name}{(IsDirty ? "*" : "")} - Prometheus {ClientVersion}";

    public event Action? StateChanged;
    public event Action? ClipSelectionChanged;
    public event Action? BeforeHistoryNavigation;
    public event Action? MaskSelectionChanged;
    public event Action? TextToolChanged;
    private bool _textToolEnabled;
    public bool TextToolEnabled
    {
        get => _textToolEnabled;
        set
        {
            if (value == _textToolEnabled) return;
            _textToolEnabled = value;
            TextToolChanged?.Invoke();
        }
    }

    public event Action<string>? TitleCreated;

    public void CreateTitle(TitleBox box, double atSeconds)
    {
        Clip? clip = null;
        try
        {
            UndoRedo.Commit(project => clip = TitlePlacement.Add(project, atSeconds, box), "Crear texto");
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            ReportStatus(error.Message);
            return;
        }
        if (clip == null) return;
        SetSelectedClip(clip.Id);
        TitleCreated?.Invoke(clip.Id);
        ReportStatus("Texto creado · edita su contenido en Controles de medio");
    }
    public string? SelectedMaskId { get; private set; }
    public void SelectMask(string? id)
    {
        if (SelectedMaskId == id) return;
        SelectedMaskId = id;
        MaskSelectionChanged?.Invoke();
    }
    public event Action? SamToolChanged;
    public event Action? ShapeMaskToolChanged;
    private string? _shapeMaskTool;
    public string? ShapeMaskTool
    {
        get => _shapeMaskTool;
        set
        {
            if (_shapeMaskTool == value) return;
            _shapeMaskTool = value;
            ShapeMaskToolChanged?.Invoke();
        }
    }
    private bool _samToolEnabled;
    public bool SamToolEnabled
    {
        get => _samToolEnabled;
        set
        {
            if (_samToolEnabled == value) return;
            _samToolEnabled = value;
            SamToolChanged?.Invoke();
        }
    }
    public event Action<string, double>? ClipLocateRequested;
    public string? SelectedClipId { get; private set; }
    public string? SelectedTransitionId { get; private set; }

    public void SetSelectedTransition(string id)
    {
        if (SelectedTransitionId == id) return;
        SelectedTransitionId = id;
        SelectedClipId = SelectedNestedRootId = SelectedMaskId = null;
        ClipSelectionChanged?.Invoke();
        MaskSelectionChanged?.Invoke();
    }
    public string? SelectedNestedRootId { get; private set; }
    public void SetSelectedClip(string? id, string? nestedRootId = null)
    {
        if (SelectedClipId == id && SelectedNestedRootId == nestedRootId && SelectedTransitionId == null) return;
        SelectedTransitionId = null;
        SelectedClipId = id;
        SelectedNestedRootId = nestedRootId;
        SelectedMaskId = null;
        ClipSelectionChanged?.Invoke();
        MaskSelectionChanged?.Invoke();
    }
    public void LocateClip(string sequenceId, string rootClipId, double atSeconds)
    {
        if (CurrentProject.ActiveSequenceId != sequenceId)
        {
            Transport.Pause();
            _undoRedoManager.Commit(project => project.ActiveSequenceId = sequenceId, "Ubicar medio");
        }
        Transport.Seek(atSeconds);
        SetSelectedClip(rootClipId);
        ClipLocateRequested?.Invoke(rootClipId, atSeconds);
    }
    public event Action<string>? StatusMessageRequested;
    public Func<(double Playhead, double Offset, double Zoom)>? CaptureSequenceView { get; set; }
    public void ReportStatus(string message) => StatusMessageRequested?.Invoke(message);

    private readonly ProjectHistory _history = new();
    internal EditorNotifications Notifications { get; } = new();

    internal sealed record HistoryTarget(string ProjectId, string? ProjectPath, int Revision);

    internal HistoryTarget CaptureHistoryTarget() => new(CurrentProject.Id, CurrentProjectPath, CurrentProject.Revision);

    internal void RecordAction(string action, string? details = null) =>
        _history.Add(CurrentProjectPath, CurrentProject.Revision, action, details);

    internal void RecordAction(HistoryTarget target, string action, string? details = null)
    {
        var path = target.ProjectPath;
        if (path == null)
        {
            if (CurrentProject.Id != target.ProjectId) return;
            path = CurrentProjectPath;
        }
        _history.Add(path, target.Revision, action, details);
    }

    internal void CloseHistory()
    {
        _projectStateVersion++;
        _openRequest++;
        _historyTimer.Stop();
        _autosaveTimer.Stop();
        ForgetRecovery();
        if (CurrentProjectPath != null) RecordAction("Cerrar proyecto");
        _history.Flush();
    }

    internal void ReportNotice(string key, string title, string message, string? actionLabel = null, Func<Task>? action = null)
    {
        if (Notifications.Add(key, title, message, actionLabel, action)) RecordAction(title, message);
    }

    internal void ReportPlaybackFailure(string projectId, string key, string message, string? clipId = null)
    {
        if (CurrentProject.Id != projectId) return;
        var sequence = CurrentProject.ActiveSequence;
        var clip = sequence?.Clips.FirstOrDefault(item => item.Id == clipId);
        ReportStatus("Previsualización: " + message);
        ReportNotice("playback:" + projectId + ":" + key, "Error de reproducción",
            clip == null ? message : clip.Name + " · " + message,
            clip == null ? null : "Ubicar clip", clip == null ? null : () =>
            {
                if (CurrentProject.Id == projectId && CurrentProject.Sequences.FirstOrDefault(s => s.Id == sequence!.Id)?.Clips.FirstOrDefault(item => item.Id == clip.Id) is { } current)
                    LocateClip(sequence!.Id, current.Id, current.TimelineStart.Seconds);
                return Task.CompletedTask;
            });
    }

    internal event Func<string, ExportFormat?, Task>? OpenExportRequested;

    internal void ReportExportFailure(string projectId, string outputPath, string reason, HistoryTarget? target = null, ExportFormat? format = null)
    {
        var details = Path.GetFileName(outputPath) + " · " + reason;
        if (target != null) RecordAction(target, "Error de exportación", details);
        else if (CurrentProject.Id == projectId) RecordAction("Error de exportación", details);
        if (CurrentProject.Id != projectId) return;
        Notifications.Add("export:" + projectId + ":" + outputPath, "Error de exportación", details, "Abrir exportación",
            () => CurrentProject.Id == projectId ? OpenExportRequested?.Invoke(outputPath, format) ?? Task.CompletedTask : Task.CompletedTask);
    }

    public ProjectController(Window ownerWindow)
    {
        Instance = this;
        _ownerWindow = ownerWindow;
        _undoRedoManager = new UndoRedoManager();

        var initialProj = ProjectStore.CreateNew();
        _undoRedoManager.Reset(initialProj, isDirty: false);
        RecordAction("Crear proyecto");
        _lastAudioPlan = AudioMixPlan.Create(initialProj, 0);
        Transport = new PlaybackTransport(() => CurrentProject);
        Transport.AudioError += message =>
        {
            var projectId = CurrentProject.Id;
            Dispatcher.UIThread.Post(() => ReportPlaybackFailure(projectId, "audio", "Audio · " + message));
        };
        _undoRedoManager.ActionRecorded += (description, details) =>
        {
            _loggedAutosave = false;
            RecordAction(description, details);
        };

        _undoRedoManager.ProjectChanged += _ =>
        {
            _projectStateVersion++;
            if (SelectedTransitionId is { } transitionId &&
                CurrentProject.ActiveSequence?.VideoTransitions.Any(t => t.Id == transitionId) != true &&
                CurrentProject.ActiveSequence?.AudioTransitions.Any(t => t.Id == transitionId) != true)
                SetSelectedClip(null);
            var nextAudio = AudioMixPlan.Create(CurrentProject, 0);
            if (_lastAudioPlan == null || nextAudio.MasterGainDb != _lastAudioPlan.MasterGainDb ||
                !nextAudio.Inputs.SequenceEqual(_lastAudioPlan.Inputs) ||
                !nextAudio.Groups.SequenceEqual(_lastAudioPlan.Groups))
                Transport.RefreshAudio();
            _lastAudioPlan = nextAudio;
            StateChanged?.Invoke();
        };
        _undoRedoManager.DirtyStateChanged += _ => StateChanged?.Invoke();

        // Temporizador de autoguardado cada 60 segundos si dirty == true
        _autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _autosaveTimer.Tick += (_, _) => PerformAutosave();
        _autosaveTimer.Start();

        _historyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _historyTimer.Tick += (_, _) =>
        {
            if (_history.HasPending && _historyFlush?.IsCompleted != false)
                _historyFlush = Task.Run(_history.Flush);
        };
        _historyTimer.Start();
    }

    public async Task<bool> NewProjectAsync()
    {
        if (IsDirty)
        {
            var shouldProceed = await ConfirmDiscardChangesAsync("crear un nuevo proyecto");
            if (!shouldProceed)
                return false;
        }

        var newProject = ProjectStore.CreateNew();
        ForgetRecovery();
        _currentProjectPath = null;
        _resourceProjectPath = null;
        _undoRedoManager.Reset(newProject, isDirty: false);
        _history.DiscardUnsaved();
        _loggedAutosave = false;
        RecordAction("Crear proyecto");
        Transport.Reset();

        StatusMessageRequested?.Invoke("Nuevo proyecto creado (1920x1080 @ 60 FPS)");
        StateChanged?.Invoke();
        return true;
    }

    public async Task<bool> OpenProjectAsync()
    {
        var guard = BeginOpen();
        if (IsDirty)
        {
            var shouldProceed = await ConfirmDiscardChangesAsync("abrir otro proyecto");
            if (!shouldProceed)
                return false;
        }

        try { guard = AfterOpenConsent(guard); }
        catch (InvalidOperationException error) { ReportStatus(error.Message); return false; }
        var files = await _ownerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir Proyecto Prometheus",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Proyecto Prometheus (*.pthproj)")
                {
                    Patterns = new[] { "*.pthproj" }
                }
            }
        });

        if (files.Count == 0)
            return false;

        try { VerifyOpen(guard); }
        catch (InvalidOperationException error) { ReportStatus(error.Message); return false; }
        string path = files[0].Path.LocalPath;
        return await OpenProjectFromPathAsync(path);
    }

    public async Task<bool> OpenProjectFromPathAsync(string filePath)
    {
        try
        {
            var guard = BeginOpen();
            var full = Path.GetFullPath(filePath);
            Project? saved = null;
            Exception? failure = null;
            try { saved = await Task.Run(() => ProjectStore.LoadTransactional(full, out _)); }
            catch (Exception error) { failure = error; }
            var candidates = (await Task.Run(() => _recoveries.List(full)))
                .Where(entry => saved == null || !entry.IsBackup && entry.ProjectId == saved.Id &&
                    entry.SavedUtc > File.GetLastWriteTimeUtc(full)).ToList();
            VerifyOpen(guard);
            if (candidates.Count == 0)
            {
                if (failure != null) throw failure;
                return await AdoptWithDialogAsync(saved!, full, full, recoveryFile: null, recoveredVersion: false, expected: guard);
            }
            if (saved != null)
                candidates.Add(new(full, full, saved.Name, File.GetLastWriteTimeUtc(full), saved.Revision, false));
            var choice = await ChooseRecoveryAsync(candidates, saved == null
                ? "El proyecto principal no se pudo abrir. Elige una copia válida."
                : "Hay una copia de recuperación más reciente. Elige qué versión abrir.");
            if (choice == null) return false;
            VerifyOpen(guard);
            var project = await Task.Run(() => ProjectStore.LoadTransactional(choice.FilePath, out _));
            var recovery = !string.Equals(choice.FilePath, full, StringComparison.OrdinalIgnoreCase);
            return await AdoptWithDialogAsync(project, full, choice.ResourcePath,
                recovery && !choice.IsBackup ? choice.FilePath : null, recovery, expected: guard);
        }
        catch (Exception ex)
        {
            StatusMessageRequested?.Invoke("Error al cargar proyecto: " + ex.Message);
            return false;
        }
    }

    public async Task<bool> SaveProjectAsync(bool saveAs = false)
    {
        string? targetPath = _currentProjectPath;

        if (string.IsNullOrEmpty(targetPath) || saveAs)
        {
            var file = await _ownerWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar Proyecto Prometheus",
                DefaultExtension = "pthproj",
                SuggestedFileName = CurrentProject.Name,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Proyecto Prometheus (*.pthproj)")
                    {
                        Patterns = new[] { "*.pthproj" }
                    }
                }
            });

            if (file == null)
                return false;

            targetPath = file.Path.LocalPath;
        }

        try
        {
            SaveProjectToPath(targetPath);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessageRequested?.Invoke($"Error al guardar proyecto: {ex.Message}");
            return false;
        }
    }

    public void NewProjectWithoutDialog(string? name, bool discardUnsaved)
    {
        if (IsDirty && !discardUnsaved)
            throw new InvalidOperationException("Hay cambios sin guardar; guarda o pasa discard_unsaved: true.");
        var project = ProjectStore.CreateNew(string.IsNullOrWhiteSpace(name) ? "Sin título" : name.Trim());
        ForgetRecovery();
        _currentProjectPath = null;
        _resourceProjectPath = null;
        _undoRedoManager.Reset(project, isDirty: false);
        _history.DiscardUnsaved();
        _loggedAutosave = false;
        RecordAction("Crear proyecto");
        Transport.Reset();
        StateChanged?.Invoke();
    }

    public async Task<(int Recovered, int Missing)> OpenProjectWithoutDialogAsync(string path, bool discardUnsaved,
        CancellationToken cancellation = default)
    {
        if (IsDirty && !discardUnsaved)
            throw new InvalidOperationException("Hay cambios sin guardar; guarda o pasa discard_unsaved: true.");
        var guard = BeginOpen();
        var full = Path.GetFullPath(path);
        var project = await Task.Run(() => ProjectStore.LoadTransactional(full, out _), cancellation);
        ProjectRecoveryStore.ResolveRelativeMedia(project, full);
        var recovered = await MediaRelinkService.RecoverAsync(project, full, cancellation: cancellation);
        var report = PreflightAuditor.Audit(project, Path.GetDirectoryName(full), autoPurgeOrphans: false);
        cancellation.ThrowIfCancellationRequested();
        VerifyOpen(guard);
        AdoptProject(project, full, full, recoveryFile: null, dirty: recovered.Count > 0);
        RecordAction("Abrir proyecto", recovered.Count > 0 ? $"{recovered.Count} medios reconectados" : null);
        return (recovered.Count, report.MissingAssets.Count);
    }

    public void SaveProjectToPath(string path)
    {
        if (!ProjectStorage.IsProjectFile(path))
            throw new ArgumentException("La ruta debe terminar en .pthproj", nameof(path));
        var full = Path.GetFullPath(path);
        var snapshot = ProjectStore.Clone(CurrentProject);
        snapshot.Name = ProjectStorage.GetProjectStem(full);
        snapshot.Recovery = null;
        ProjectRecoveryStore.ResolveRelativeMedia(snapshot, ResourceProjectPath);
        try
        {
            CopyLutResources(snapshot, ResourceProjectPath, full);
            SamMaskTrackStore.CopyReferenced(snapshot, ResourceProjectPath, full);
            ProjectStore.SaveTransactional(snapshot, full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecordAction("Error al guardar proyecto", Path.GetFileName(full) + " · " + ex.Message);
            throw;
        }
        _projectStateVersion++;
        CurrentProject.Name = snapshot.Name;
        CurrentProject.EffectResources = snapshot.EffectResources;
        CurrentProject.Assets = snapshot.Assets;
        SubtitleRenderPlan.Invalidate(CurrentProject);
        var previousPath = _currentProjectPath;
        var saveAs = previousPath != null && !string.Equals(previousPath, full, StringComparison.OrdinalIgnoreCase);
        if (saveAs)
            _history.CopyTo(previousPath!, full);
        ForgetRecovery(saved: true);
        _currentProjectPath = full;
        _resourceProjectPath = full;
        _history.AdoptUnsaved(full);
        RecordAction(saveAs ? "Guardar como" : "Guardar proyecto", Path.GetFileName(full));
        _history.FlushProject(full);
        _undoRedoManager.MarkClean();
        StatusMessageRequested?.Invoke($"Proyecto guardado: {Path.GetFileName(full)}");
        RememberProject(full);
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        BeforeHistoryNavigation?.Invoke();
        if (_undoRedoManager.CanUndo)
        {
            _undoRedoManager.Undo();
            StatusMessageRequested?.Invoke("Acción deshecha (Ctrl+Z)");
            StateChanged?.Invoke();
        }
    }

    public void Redo()
    {
        BeforeHistoryNavigation?.Invoke();
        if (_undoRedoManager.CanRedo)
        {
            _undoRedoManager.Redo();
            StatusMessageRequested?.Invoke("Acción rehecha (Ctrl+Shift+Z)");
            StateChanged?.Invoke();
        }
    }

    internal bool PerformAutosave()
    {
        if (!IsDirty) return false;
        try
        {
            var recovery = ProjectStore.Clone(CurrentProject);
            var autosavePath = _recoveries.FileFor(recovery.Id, _recoverySessionId);
            ProjectRecoveryStore.ResolveRelativeMedia(recovery, ResourceProjectPath);
            CopyLutResources(recovery, ResourceProjectPath, autosavePath);
            SamMaskTrackStore.CopyReferenced(recovery, ResourceProjectPath, autosavePath);
            _recoveries.Save(recovery, _currentProjectPath, _recoverySessionId);
            if (!_loggedAutosave)
            {
                RecordAction("Autoguardar recuperación");
                _loggedAutosave = true;
            }
            StatusMessageRequested?.Invoke("Autoguardado de recuperación generado");
            return true;
        }
        catch (Exception error)
        {
            ReportNotice("autosave-failed:" + CurrentProject.Id, "No se pudo autoguardar", error.Message);
            return false;
        }
    }

    private static void CopyLutResources(Project project, string? sourceProjectPath, string targetProjectPath)
    {
        foreach (var resource in project.EffectResources.Where(r => r.Kind == "lut3d" && !string.IsNullOrWhiteSpace(r.Uri)))
        {
            var source = resource.Relative && sourceProjectPath != null
                ? Path.Combine(ProjectStorage.GetDataDirectory(sourceProjectPath), resource.Uri)
                : resource.Uri;
            if (!File.Exists(source)) continue;
            var relative = Path.Combine("recursos", "luts", resource.Id + ".cube");
            var destination = Path.Combine(ProjectStorage.GetDataDirectory(targetProjectPath), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                ProjectStorage.CopyResourceTransactional(source, destination);
            resource.Uri = relative;
            resource.Relative = true;
        }
    }

    public async Task<bool> ConfirmDiscardChangesAsync(string actionContext)
    {
        if (!IsDirty) return true;
        var dialog = new Window { Title = "Cambios sin guardar", Width = 390, Height = 170,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var message = new TextBlock { Text = $"Hay cambios sin guardar en «{CurrentProject.Name}».\n¿Quieres guardarlos antes de {actionContext}?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Content = "Guardar", MinWidth = 88 };
        var discard = new Button { Content = actionContext == "salir" ? "Salir sin guardar" :
            "Continuar sin guardar", MinWidth = 125 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 88 };
        save.Click += (_, _) => dialog.Close("save");
        discard.Click += (_, _) => dialog.Close("discard");
        cancel.Click += (_, _) => dialog.Close("cancel");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(cancel); buttons.Children.Add(discard); buttons.Children.Add(save);
        var content = new StackPanel { Margin = new Avalonia.Thickness(15), Spacing = 20 };
        content.Children.Add(message); content.Children.Add(buttons);
        dialog.Content = content;
        var choice = await dialog.ShowDialog<string?>(_ownerWindow);
        return choice == "discard" || choice == "save" && await SaveProjectAsync();
    }
}
