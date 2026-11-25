using System;
using Prometheus.Core.Storage;
using Prometheus.Core.Undo;
using Xunit;

namespace Prometheus.UnitTests;

public class McpCommitTests
{
    [Fact]
    public void PreparedBatchCreatesOneUndoAndRejectsStaleRevision()
    {
        var manager = new UndoRedoManager(ProjectStore.CreateNew());
        var before = manager.CurrentProject.Name;
        var candidate = ProjectStore.Clone(manager.CurrentProject);
        candidate.Name = "Cambio MCP";
        candidate.ActiveSequence!.Name = "Montaje MCP";

        Assert.True(manager.CommitPrepared(candidate, 0, "Lote MCP"));
        Assert.Equal(1, manager.UndoCount);
        Assert.Equal(1, manager.CurrentProject.Revision);
        Assert.Throws<InvalidOperationException>(() => manager.CommitPrepared(candidate, 0, "Obsoleto"));
        Assert.Equal(1, manager.UndoCount);
        Assert.True(manager.Undo());
        Assert.Equal(before, manager.CurrentProject.Name);
        Assert.Equal("Montaje 1", manager.CurrentProject.ActiveSequence!.Name);
    }

    [Fact]
    public void PreparedBatchRejectsDifferentBranchWithSameRevision()
    {
        var manager = new UndoRedoManager(ProjectStore.CreateNew());
        manager.Commit(p => p.Name = "Rama A", "A");
        var snapshot = ProjectStore.Serialize(manager.CurrentProject);
        var candidate = ProjectStore.Clone(manager.CurrentProject);
        candidate.Name = "Plan sobre A";
        Assert.True(manager.Undo());
        manager.Commit(p => p.Name = "Rama B", "B");
        Assert.Equal(1, manager.CurrentProject.Revision);
        Assert.Throws<InvalidOperationException>(() =>
            manager.CommitPrepared(candidate, 1, "Plan de A", snapshot));
        Assert.Equal("Rama B", manager.CurrentProject.Name);
    }
}
