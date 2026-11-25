using Prometheus.Core.Storage;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class HistoryPerformance310Tests
{
    [Fact]
    public void CompressedHistoryRestoresTheCompleteUnicodeProjectWithoutLosingMetadata()
    {
        var project = ProjectStore.CreateNew(string.Concat(Enumerable.Repeat("Edición ñ 東京 ", 8000)));
        var before = ProjectStore.Serialize(project);
        var undo = new UndoRedoManager(project);
        undo.Commit(p => p.Name = "Changed", "Change");
        Assert.True(undo.HistoryBytes < before.Length / 10);
        Assert.True(undo.Undo());
        Assert.Equal(before, ProjectStore.Serialize(undo.CurrentProject));
        Assert.True(undo.Redo());
        Assert.Equal("Changed", undo.CurrentProject.Name);
        undo.CurrentProject.Name = "External metadata";
        undo.Commit(p => p.ActiveSequence!.Name = "Edited sequence", "Edit");
        Assert.Equal("External metadata", undo.CurrentProject.Name);
        Assert.True(undo.Undo());
        Assert.Equal("External metadata", undo.CurrentProject.Name);
    }

    [Fact]
    public void EntryAndMemoryLimitsPreserveRecentUndoAndRedo()
    {
        var undo = new UndoRedoManager(ProjectStore.CreateNew(), 1_000_000, 3);
        for (var i = 0; i < 10; i++) undo.Commit(p => p.Name = "State " + i, "Change");
        Assert.Equal(3, undo.UndoCount);
        Assert.True(undo.Undo()); Assert.True(undo.Undo()); Assert.True(undo.Undo());
        Assert.False(undo.Undo());
        Assert.Equal("State 6", undo.CurrentProject.Name);
        Assert.True(undo.Redo()); Assert.True(undo.Redo()); Assert.True(undo.Redo());
        Assert.Equal("State 9", undo.CurrentProject.Name);
        var limited = new UndoRedoManager(ProjectStore.CreateNew(), 4096);
        for (var i = 0; i < 10; i++) limited.Commit(p => p.Name = "Entry " + i, "Edit");
        Assert.True(limited.HistoryBytes <= 4096 || limited.UndoCount == 1);
        Assert.True(limited.Undo());
        Assert.Equal("Entry 8", limited.CurrentProject.Name);
        limited.Reset(ProjectStore.CreateNew());
        Assert.Equal(0, limited.HistoryBytes);
    }

    [Fact]
    public void CoalescingAndBranchingKeepAccountingAndAtomicValidation()
    {
        var undo = new UndoRedoManager(ProjectStore.CreateNew());
        undo.CommitCoalesced(p => p.Name = "A", "Rename", "name");
        undo.CommitCoalesced(p => p.Name = "B", "Rename", "name");
        Assert.Equal(1, undo.UndoCount);
        Assert.True(undo.Undo());
        Assert.True(undo.Redo());
        undo.Commit(p => p.Name = "Branch", "Edit");
        var bytes = undo.HistoryBytes;
        var revision = undo.CurrentProject.Revision;
        Assert.Throws<InvalidOperationException>(() => undo.Commit(p => p.ActiveSequence!.Tracks[0].Id = "", "Invalid"));
        Assert.Equal(bytes, undo.HistoryBytes);
        Assert.Equal(revision, undo.CurrentProject.Revision);
    }
}
