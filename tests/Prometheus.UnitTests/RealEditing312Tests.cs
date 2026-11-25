using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class RealEditing312Tests
{
    private static Project LinkedProject()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Clips.AddRange([
            new Clip { Id = "video", TrackId = sequence.Tracks.First(t => t.Name == "V1").Id,
                TimelineStart = Rational.Time(2), SourceIn = Rational.Time(1), Duration = Rational.Time(4), AvailableSourceDurationSeconds = 10 },
            new Clip { Id = "audio", TrackId = sequence.Tracks.First(t => t.Name == "A1").Id,
                TimelineStart = Rational.Time(2), SourceIn = Rational.Time(.5), Duration = Rational.Time(4), AvailableSourceDurationSeconds = 6 }
        ]);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = ["video", "audio"] });
        return project;
    }

    [Fact]
    public void SlowSliderDragAndSeparateFastDragHaveExplicitUndoBoundaries()
    {
        var history = new UndoRedoManager(ProjectStore.CreateNew());
        history.BeginGesture();
        history.Commit(p => p.Name = "A", "Slider");
        history.Commit(p => p.ActiveSequence!.Tracks[0].PresentationHeight = 110, "View");
        history.CommitCoalesced(p => p.Name = "B", "Slider", "param", TimeSpan.Zero);
        history.Commit(p => p.Name = "C", "Slider");
        history.EndGesture();
        Assert.Equal(1, history.UndoCount);
        history.BeginGesture();
        history.Commit(p => p.Name = "D", "Slider");
        history.EndGesture();
        Assert.Equal(2, history.UndoCount);
        history.Undo(); Assert.Equal("C", history.CurrentProject.Name);
        history.Undo(); Assert.Equal("Sin título", history.CurrentProject.Name);
        history.Redo(); Assert.Equal("C", history.CurrentProject.Name);
    }

    [Fact]
    public void NavigationDoesNotDirtyProjectOrConsumeUndoAndKeepsRedo()
    {
        var history = new UndoRedoManager(ProjectStore.CreateNew());
        history.Commit(p => { p.ActiveSequence!.Tracks[0].PresentationHeight = 120; p.ActiveSequence.VideoAreaHeight = 200; }, "Resize");
        Assert.False(history.IsDirty);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.CurrentProject.Revision);
        history.Commit(p => p.Name = "Edited", "Rename");
        history.Undo();
        history.Commit(p => { p.ActiveSequence!.Tracks[0].Compact = true; p.ActiveSequence.ViewZoomPixelsPerSecond = 180; }, "View");
        Assert.True(history.CanRedo);
        history.Redo();
        Assert.True(history.CurrentProject.ActiveSequence!.Tracks[0].Compact);
        Assert.Equal(180, history.CurrentProject.ActiveSequence.ViewZoomPixelsPerSecond);
        Assert.Equal("Edited", history.CurrentProject.Name);
    }

    [Fact]
    public void UndoPreservesCurrentTrackLayoutInsteadOfRestoringOldView()
    {
        var history = new UndoRedoManager(LinkedProject());
        history.Commit(p => ClipTrimOperations.ApplyEdge(p, "video", false, 5), "Trim");
        history.Commit(p => { p.ActiveSequence!.Tracks[0].PresentationHeight = 160; p.ActiveSequence.Tracks[0].ShowNames = false; }, "View");
        Assert.Equal(1, history.UndoCount);
        history.Undo();
        Assert.All(history.CurrentProject.ActiveSequence!.Clips, c => Assert.Equal(4, c.Duration.Seconds));
        Assert.Equal(160, history.CurrentProject.ActiveSequence.Tracks[0].PresentationHeight);
        Assert.False(history.CurrentProject.ActiveSequence.Tracks[0].ShowNames);
    }

    [Fact]
    public void ChangingSequenceIsNavigationAndContentEditStillUndoes()
    {
        var project = ProjectStore.CreateNew();
        project.Sequences.Add(new Sequence { Id = "other" });
        var history = new UndoRedoManager(project);
        history.Commit(p => p.Name = "Changed", "Edit");
        history.Commit(p => p.ActiveSequenceId = "other", "Navigate");
        Assert.Equal(1, history.UndoCount);
        history.Undo();
        Assert.Equal("other", history.CurrentProject.ActiveSequenceId);
        Assert.Equal(project.Name, history.CurrentProject.Name);
    }

    [Fact]
    public void PreparedNavigationReportsAppliedWithoutAnUndoStep()
    {
        var project = ProjectStore.CreateNew();
        project.Sequences.Add(new Sequence { Id = "other" });
        var history = new UndoRedoManager(project);
        var candidate = ProjectStore.Clone(project);
        candidate.ActiveSequenceId = "other";
        Assert.True(history.CommitPrepared(candidate, 0, "Navigate"));
        Assert.Equal("other", history.CurrentProject.ActiveSequenceId);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.CurrentProject.Revision);
        Assert.False(history.CommitPrepared(ProjectStore.Clone(history.CurrentProject), 0, "No change"));
    }

    [Fact]
    public void NoOpClickDoesNotReconcileUnrelatedLegacyState()
    {
        var project = ProjectStore.CreateNew();
        project.ActiveSequence!.LinkGroups.Add(new LinkGroup { ClipIds = ["old-missing-id"] });
        var history = new UndoRedoManager(project);
        history.Commit(_ => { }, "Click");
        Assert.Equal(0, history.UndoCount);
        Assert.Single(history.CurrentProject.ActiveSequence!.LinkGroups);
    }

    [Fact]
    public void NoOpAndViewChangesDoNotBreakContinuousAdjustment()
    {
        var history = new UndoRedoManager(ProjectStore.CreateNew());
        history.CommitCoalesced(p => p.Name = "A", "Typing", "name", TimeSpan.FromSeconds(5));
        history.Commit(_ => { }, "Click");
        history.Commit(p => p.ActiveSequence!.Tracks[0].PresentationHeight = 150, "View");
        history.CommitCoalesced(p => p.Name = "B", "Typing", "name", TimeSpan.FromSeconds(5));
        Assert.Equal(1, history.UndoCount);
        history.Undo();
        Assert.Equal("Sin título", history.CurrentProject.Name);
    }

    [Fact]
    public void SavedControlAdjustmentStartsANewUndoGesture()
    {
        var history = new UndoRedoManager(ProjectStore.CreateNew());
        history.CommitCoalesced(p => p.Name = "A", "Typing", "name");
        history.MarkClean();
        history.CommitCoalesced(p => p.Name = "B", "Typing", "name");
        Assert.Equal(2, history.UndoCount);
    }

    [Theory]
    [InlineData(true, 1.5, 1.5, 4.5)]
    [InlineData(false, 20, 2, 5.5)]
    public void LinkedEdgesShareTheMostRestrictiveSourceLimit(bool left, double edge, double expectedStart, double expectedDuration)
    {
        var project = LinkedProject();
        var plan = ClipTrimOperations.PlanEdge(project, "video", left, edge);
        Assert.Equal(2, plan.ClipIds.Count);
        ClipTrimOperations.ApplyEdge(project, "video", left, edge);
        Assert.All(project.ActiveSequence!.Clips, c => {
            Assert.Equal(expectedStart, c.TimelineStart.Seconds);
            Assert.Equal(expectedDuration, c.Duration.Seconds);
        });
        Assert.Single(project.ActiveSequence.LinkGroups);
    }

    [Fact]
    public void LeftPreviewStopsAtTheOtherLinkedSourcesBeginning()
    {
        var project = LinkedProject();
        Assert.Equal(-.5, ClipTrimOperations.PlanEdge(project, "video", true, -100).Delta);
        ClipTrimOperations.ApplyEdge(project, "video", true, -100);
        Assert.Equal(0, project.ActiveSequence!.Clips.Single(c => c.Id == "audio").SourceIn.Seconds);
        Assert.Equal(.5, project.ActiveSequence.Clips.Single(c => c.Id == "video").SourceIn.Seconds);
    }

    [Fact]
    public void NeighborOnLinkedTrackConstrainsTheWholeTrim()
    {
        var project = LinkedProject();
        var sequence = project.ActiveSequence!;
        sequence.Clips.Add(new Clip { Id = "next", TrackId = sequence.Clips[1].TrackId, TimelineStart = Rational.Time(6.5), Duration = Rational.One });
        Assert.Equal(.5, ClipTrimOperations.PlanEdge(project, "video", false, 10).Delta);
        ClipTrimOperations.ApplyEdge(project, "video", false, 10);
        Assert.Equal(6.5, sequence.Clips[0].EndSeconds);
        Assert.Equal(3, sequence.Clips.Count);
    }

    [Fact]
    public void LockedPartnerPreventsMutationAndHistoryEntry()
    {
        var project = LinkedProject();
        project.ActiveSequence!.Tracks.Single(t => t.Id == project.ActiveSequence.Clips[1].TrackId).Locked = true;
        var before = ProjectStore.Serialize(project);
        var history = new UndoRedoManager(project);
        Assert.Throws<InvalidOperationException>(() => history.Commit(p => ClipTrimOperations.ApplyEdge(p, "video", false, 5), "Trim"));
        Assert.Equal(before, ProjectStore.Serialize(history.CurrentProject));
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void StaggeredLinkedEdgesRemainIndependent()
    {
        var project = LinkedProject();
        project.ActiveSequence!.Clips[1].TimelineStart = Rational.Time(3);
        Assert.Equal(new[] { "video" }, ClipTrimOperations.PlanEdge(project, "video", true, 1).ClipIds);
    }

    [Fact]
    public void SourceLimitAccountsForSpeed()
    {
        var project = LinkedProject();
        project.ActiveSequence!.LinkGroups.Clear();
        var clip = project.ActiveSequence.Clips[0];
        clip.Speed = new Rational(2, 1);
        Assert.Equal(.5, ClipTrimOperations.PlanEdge(project, "video", false, 100).Delta);
    }

    [Fact]
    public void GeneratedStillCanExtendBeyondRecommendedDuration()
    {
        var project = LinkedProject();
        project.ActiveSequence!.LinkGroups.Clear();
        var clip = project.ActiveSequence.Clips[0];
        clip.AssetId = "still"; clip.StreamId = "image";
        project.Assets.Add(new Asset { Id = "still", Streams = [new StreamInfo { Id = "image", Kind = "image", Duration = Rational.One }] });
        ClipTrimOperations.ApplyEdge(project, "video", false, 100);
        Assert.Equal(100, clip.EndSeconds);
    }

    [Fact]
    public void DraggingTopClipCannotMoveLowerMemberIntoSubtitles()
    {
        var project = LinkedProject();
        var sequence = project.ActiveSequence!;
        var lower = sequence.Tracks.Single(t => t.Name == "V1");
        var upper = sequence.Tracks.Single(t => t.Name == "V2");
        SubtitleTrack.Ensure(sequence);
        sequence.Clips.Add(new Clip { Id = "upper", TrackId = upper.Id });
        Assert.False(ClipMoveTargets.TryResolve(sequence, ["upper", "video"], "upper", lower.Id, out _));
        Assert.False(ClipMoveTargets.TryResolve(sequence, ["upper"], "upper", SubtitleTrack.Find(sequence)!.Id, out _));
    }

    [Fact]
    public void MixedVideoAudioMoveKeepsAudioTrackAndRejectsLockedDestination()
    {
        var project = LinkedProject();
        var sequence = project.ActiveSequence!;
        var upper = sequence.Tracks.Single(t => t.Name == "V2");
        Assert.True(ClipMoveTargets.TryResolve(sequence, ["video", "audio"], "video", upper.Id, out var targets));
        Assert.Equal(upper.Id, targets["video"]);
        Assert.Equal(sequence.Clips[1].TrackId, targets["audio"]);
        upper.Locked = true;
        Assert.False(ClipMoveTargets.TryResolve(sequence, ["video", "audio"], "video", upper.Id, out _));
    }

    [Fact]
    public void MissingDestinationForAnyMemberRejectsWholeMove()
    {
        var project = LinkedProject();
        var sequence = project.ActiveSequence!;
        var lower = sequence.Tracks.Single(t => t.Name == "V1");
        var upper = sequence.Tracks.Single(t => t.Name == "V2");
        sequence.Clips.Add(new Clip { Id = "upper", TrackId = upper.Id });
        Assert.False(ClipMoveTargets.TryResolve(sequence, ["video", "upper"], "video", upper.Id, out _));
        Assert.True(ClipMoveTargets.TryResolve(sequence, ["video", "upper"], "video", lower.Id, out _));
    }
}
