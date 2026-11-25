using System;
using System.Collections.Generic;
using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Core.Undo;

public interface IUndoRedoManager
{
    Project CurrentProject { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool IsDirty { get; }
    int UndoCount { get; }
    int RedoCount { get; }

    event Action<Project>? ProjectChanged;
    event Action<bool>? DirtyStateChanged;

    void Commit(Action<Project> mutation, string description);
    bool Undo();
    bool Redo();
    void Reset(Project newProject, bool isDirty = false);
    void MarkClean();
}

public class UndoRedoManager : IUndoRedoManager
{
    private sealed record Entry(HistorySnapshot Snapshot, string Description, string? Details)
    {
        public long Bytes => Snapshot.Data.LongLength + 2L * (Description.Length + (Details?.Length ?? 0));
        public string Summary => Details == null ? Description : Description + " · " + Details;
    }

    private readonly LinkedList<Entry> _undoStack = new();
    private readonly LinkedList<Entry> _redoStack = new();
    private Project _currentProject;
    private readonly long _historyByteLimit;
    private readonly int _historyEntryLimit;
    public long HistoryBytes { get; private set; }
    private bool _isDirty;
    private string? _lastCoalesceKey;
    private DateTime _lastCoalesceAt;
    private bool _gestureActive;
    private Entry? _gestureEntry;

    public void BeginGesture()
    {
        EndGesture();
        _gestureActive = true;
    }

    public void EndGesture()
    {
        _gestureActive = false;
        _gestureEntry = null;
        _lastCoalesceKey = null;
    }

    public Project CurrentProject => _currentProject;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                DirtyStateChanged?.Invoke(_isDirty);
            }
        }
    }

    public event Action<Project>? ProjectChanged;
    public event Action<bool>? DirtyStateChanged;

    public event Action<string, string?>? ActionRecorded;

    public UndoRedoManager(Project? initialProject = null) : this(initialProject, 256 * 1024 * 1024, 500) { }

    public UndoRedoManager(Project? initialProject, long historyByteLimit, int historyEntryLimit = 500)
    {
        if (historyByteLimit < 1024 || historyEntryLimit < 1) throw new ArgumentOutOfRangeException(nameof(historyByteLimit));
        _historyByteLimit = historyByteLimit;
        _historyEntryLimit = historyEntryLimit;
        _currentProject = initialProject ?? ProjectStore.CreateNew();
    }

    public void Commit(Action<Project> mutation, string description) => Commit(mutation, description, null);

    public void Commit(Action<Project> mutation, string description, string? details)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        // 1. Clona el proyecto para aplicar la mutación atómicamente
        var priorSnapshot = ProjectStore.SerializeSnapshotBytes(_currentProject);
        var candidate = ProjectStore.DeserializeSnapshot(priorSnapshot);
        mutation(candidate);
        if (AcceptNavigationOrNoChange(candidate, priorSnapshot)) return;
        _lastCoalesceKey = null;
        TranscriptOperations.FollowAudioEdits(_currentProject, candidate);

        // 2. Valida y evita crear revisiones para gestos sin cambio real.
        var report = PreflightAuditor.Audit(candidate, autoPurgeOrphans: true);

        if (!report.IsValid)
        {
            throw new InvalidOperationException($"La operación '{description}' fue abortada por violar la integridad estructural: {string.Join("; ", report.Errors)}");
        }

        if (ProjectStore.SerializeSnapshotBytes(candidate).AsSpan().SequenceEqual(priorSnapshot)) return;

        candidate.Revision = _currentProject.Revision + 1;

        // 3. Almacena el estado previo solo después de confirmar un cambio.
        Clear(_redoStack);
        var merged = PushEdit(priorSnapshot, description, details);

        _currentProject = candidate;
        IsDirty = true;

        if (!merged) ActionRecorded?.Invoke(description, details);
        ProjectChanged?.Invoke(_currentProject);
    }

    /// <summary>Groups consecutive live control changes into one undo gesture.</summary>
    public void CommitCoalesced(Action<Project> mutation, string description, string key,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Falta la clave de gesto.", nameof(key));
        var priorSnapshot = ProjectStore.SerializeSnapshotBytes(_currentProject);
        var candidate = ProjectStore.DeserializeSnapshot(priorSnapshot);
        mutation(candidate);
        if (AcceptNavigationOrNoChange(candidate, priorSnapshot)) return;
        TranscriptOperations.FollowAudioEdits(_currentProject, candidate);
        var report = PreflightAuditor.Audit(candidate, autoPurgeOrphans: true);
        if (!report.IsValid)
            throw new InvalidOperationException($"La operación '{description}' fue abortada: {string.Join("; ", report.Errors)}");
        if (ProjectStore.SerializeSnapshotBytes(candidate).AsSpan().SequenceEqual(priorSnapshot)) return;
        var now = DateTime.UtcNow;
        var continuous = _lastCoalesceKey == key && now - _lastCoalesceAt <= (interval ?? TimeSpan.FromMilliseconds(650))
            && _undoStack.Count > 0;
        Clear(_redoStack);
        var merged = PushEdit(priorSnapshot, description, null, continuous);
        candidate.Revision = _currentProject.Revision + 1;
        _currentProject = candidate;
        _lastCoalesceKey = key;
        _lastCoalesceAt = now;
        IsDirty = true;
        if (!merged) ActionRecorded?.Invoke(description, null);
        ProjectChanged?.Invoke(_currentProject);
    }

    /// <summary>Confirma exactamente el proyecto ya evaluado, con guardia de revisión y un solo Undo.</summary>
    public bool CommitPrepared(Project candidate, int expectedRevision, string description,
        string? expectedSnapshot = null, string? details = null)
    {
        EndGesture();
        ArgumentNullException.ThrowIfNull(candidate);
        if (_currentProject.Revision != expectedRevision || _currentProject.Id != candidate.Id)
            throw new InvalidOperationException("El proyecto cambió desde la evaluación; vuelve a evaluar el lote.");
        var prior = ProjectStore.Serialize(_currentProject);
        if (expectedSnapshot != null && prior != expectedSnapshot)
            throw new InvalidOperationException("El proyecto ya no coincide con el estado evaluado; vuelve a evaluar.");
        var validated = ProjectStore.Clone(candidate);
        var beforeNavigation = _currentProject;
        if (AcceptNavigationOrNoChange(validated, ProjectStore.SerializeSnapshotBytes(_currentProject)))
            return !ReferenceEquals(beforeNavigation, _currentProject);
        var report = PreflightAuditor.Audit(validated, autoPurgeOrphans: true);
        if (!report.IsValid)
            throw new InvalidOperationException($"Lote '{description}' inválido: {string.Join("; ", report.Errors)}");
        if (ProjectStore.Serialize(validated) == prior) return false;
        validated.Revision = expectedRevision + 1;
        Clear(_redoStack);
        Push(_undoStack, new(HistorySnapshot.Pack(prior), description, details));
        _currentProject = validated;
        IsDirty = true;
        ActionRecorded?.Invoke(description, details);
        ProjectChanged?.Invoke(_currentProject);
        return true;
    }

    public bool Undo()
    {
        EndGesture();
        if (_undoStack.Count == 0)
            return false;

        var prior = Pop(_undoStack);
        Push(_redoStack, prior with { Snapshot = HistorySnapshot.Pack(ProjectStore.SerializeSnapshotBytes(_currentProject)) });

        var view = new ProjectViewState(_currentProject);
        _currentProject = ProjectStore.DeserializeSnapshot(prior.Snapshot.Unpack());
        view.Apply(_currentProject);
        IsDirty = true;

        ActionRecorded?.Invoke("Deshacer", prior.Summary);
        ProjectChanged?.Invoke(_currentProject);
        return true;
    }

    public bool Redo()
    {
        EndGesture();
        if (_redoStack.Count == 0)
            return false;

        var next = Pop(_redoStack);
        Push(_undoStack, next with { Snapshot = HistorySnapshot.Pack(ProjectStore.SerializeSnapshotBytes(_currentProject)) });

        var view = new ProjectViewState(_currentProject);
        _currentProject = ProjectStore.DeserializeSnapshot(next.Snapshot.Unpack());
        view.Apply(_currentProject);
        IsDirty = true;

        ActionRecorded?.Invoke("Rehacer", next.Summary);
        ProjectChanged?.Invoke(_currentProject);
        return true;
    }

    public void Reset(Project newProject, bool isDirty = false)
    {
        EndGesture();
        ArgumentNullException.ThrowIfNull(newProject);

        _currentProject = newProject;
        Clear(_undoStack);
        Clear(_redoStack);
        IsDirty = isDirty;

        ProjectChanged?.Invoke(_currentProject);
    }

    private void Push(LinkedList<Entry> stack, Entry entry)
    {
        stack.AddLast(entry);
        HistoryBytes += entry.Bytes;
        while ((_undoStack.Count + _redoStack.Count > _historyEntryLimit || HistoryBytes > _historyByteLimit) &&
            _undoStack.Count + _redoStack.Count > 1)
        {
            var oldest = _undoStack.Count > 0 ? _undoStack : _redoStack;
            HistoryBytes -= oldest.First!.Value.Bytes;
            oldest.RemoveFirst();
        }
    }

    private Entry Pop(LinkedList<Entry> stack)
    {
        var entry = stack.Last!.Value;
        stack.RemoveLast();
        HistoryBytes -= entry.Bytes;
        return entry;
    }

    private void Clear(LinkedList<Entry> stack)
    {
        foreach (var entry in stack) HistoryBytes -= entry.Bytes;
        stack.Clear();
    }

    public void MarkClean()
    {
        EndGesture();
        IsDirty = false;
    }

    private bool AcceptNavigationOrNoChange(Project candidate, byte[] prior)
    {
        if (ProjectStore.SerializeSnapshotBytes(candidate).AsSpan().SequenceEqual(prior)) return true;
        if (candidate.ActiveSequence == null) return false;
        var priorView = new ProjectViewState(_currentProject);
        if (priorView.Matches(candidate)) return false;
        var nextView = new ProjectViewState(candidate);
        priorView.Apply(candidate);
        var navigationOnly = ProjectStore.SerializeSnapshotBytes(candidate).AsSpan().SequenceEqual(prior);
        nextView.Apply(candidate);
        if (!navigationOnly) return false;
        _currentProject = candidate;
        ProjectChanged?.Invoke(_currentProject);
        return true;
    }

    private bool PushEdit(byte[] prior, string description, string? details, bool timedContinuous = false)
    {
        var merge = _gestureActive
            ? _gestureEntry != null && ReferenceEquals(_undoStack.Last?.Value, _gestureEntry)
            : timedContinuous;
        if (!merge) Push(_undoStack, new(HistorySnapshot.Pack(prior), description, details));
        if (_gestureActive) _gestureEntry = _undoStack.Last?.Value;
        return merge;
    }
}
