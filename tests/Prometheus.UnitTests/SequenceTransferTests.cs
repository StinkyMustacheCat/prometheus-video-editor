using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class SequenceTransferTests
{
    [Fact]
    public void CopyMapsTracksAndKeepsLinkedPair()
    {
        var project = ProjectStore.CreateNew();
        var source = project.ActiveSequence!;
        var a = new Clip { TrackId = source.Tracks[0].Id, TimelineStart = Rational.Time(3), Duration = Rational.Time(2) };
        var b = new Clip { TrackId = source.Tracks[1].Id, TimelineStart = Rational.Time(3), Duration = Rational.Time(2) };
        source.Clips.AddRange(new[] { a, b });
        source.LinkGroups.Add(new LinkGroup { ClipIds = new() { a.Id, b.Id } });
        var destination = SequenceOperations.Create(project, "Otra", source);

        var inserted = SequenceTransfer.Copy(source, destination, new[] { a.Id, b.Id }, 10);

        Assert.Equal(2, inserted.Count);
        Assert.Equal(2, destination.Clips.Count);
        Assert.All(destination.Clips, c => Assert.Equal(10, c.TimelineStart.Seconds));
        Assert.All(destination.Clips, c => Assert.Contains(destination.Tracks, t => t.Id == c.TrackId));
        Assert.Equal(2, destination.LinkGroups.Single().ClipIds.Count);
    }

    [Fact]
    public void MoveIsOneUndoableProjectChange()
    {
        var project = ProjectStore.CreateNew();
        var sourceId = project.ActiveSequenceId;
        var source = project.ActiveSequence!;
        var clip = new Clip { TrackId = source.Tracks[0].Id, TimelineStart = Rational.Time(5), Duration = Rational.Time(2) };
        source.Clips.Add(clip);
        var destination = SequenceOperations.Create(project, "Otra", source);
        var destinationId = destination.Id;
        project.ActiveSequenceId = sourceId;
        var history = new UndoRedoManager(project);

        history.Commit(p => SequenceTransfer.Move(p.Sequences.Single(s => s.Id == sourceId),
            p.Sequences.Single(s => s.Id == destinationId), new[] { clip.Id }), "Mover");
        Assert.Empty(history.CurrentProject.Sequences.Single(s => s.Id == sourceId).Clips);
        Assert.Single(history.CurrentProject.Sequences.Single(s => s.Id == destinationId).Clips);
        Assert.True(history.Undo());
        Assert.Single(history.CurrentProject.Sequences.Single(s => s.Id == sourceId).Clips);
        Assert.Empty(history.CurrentProject.Sequences.Single(s => s.Id == destinationId).Clips);
    }
}
