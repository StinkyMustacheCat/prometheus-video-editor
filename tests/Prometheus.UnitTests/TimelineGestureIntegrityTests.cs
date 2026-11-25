using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class TimelineGestureIntegrityTests
{
    [Fact]
    public void EmptyGestureDoesNotCreateRevisionOrUndoEntry()
    {
        var history = new UndoRedoManager(ProjectStore.CreateNew());
        history.Commit(_ => { }, "Gesto sin desplazamiento");
        Assert.Equal(0, history.CurrentProject.Revision);
        Assert.Equal(0, history.UndoCount);
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void ConfirmedTrimRestoresExactOriginalWithUndo()
    {
        var project = ProjectStore.CreateNew();
        var track = project.ActiveSequence!.Tracks[0];
        project.ActiveSequence.Clips.Add(new Clip { Id = "trim-target", TrackId = track.Id,
            TimelineStart = Rational.Time(1), Duration = Rational.Time(4), SourceIn = Rational.Time(0.5) });
        var history = new UndoRedoManager(project);
        history.Commit(p => {
            var clip = p.ActiveSequence!.Clips.Single(c => c.Id == "trim-target");
            clip.TimelineStart = Rational.Time(2);
            clip.Duration = Rational.Time(3);
            clip.SourceIn = Rational.Time(1.5);
        }, "Recortar");

        Assert.Equal(2, history.CurrentProject.ActiveSequence!.Clips[0].TimelineStart.Seconds);
        Assert.True(history.Undo());
        var original = history.CurrentProject.ActiveSequence!.Clips[0];
        Assert.Equal(1, original.TimelineStart.Seconds);
        Assert.Equal(4, original.Duration.Seconds);
        Assert.Equal(0.5, original.SourceIn.Seconds);
        Assert.True(history.Redo());
        Assert.Equal(2, history.CurrentProject.ActiveSequence!.Clips[0].TimelineStart.Seconds);
    }
}
