using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class TrackDeletionTests
{
    [Fact]
    public void DeletionRetainsSurvivingLinksAndGroupsAndRestoresEverythingWithUndo()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var videos = seq.Tracks.Where(t => t.Kind == "video").OrderBy(t => t.Order).ToArray();
        var extra = new Track { Kind = "video", Name = "Mi pista", Order = 2 };
        var subtitles = new Track { Kind = "video", Role = "subtitle", Name = "Subtítulos", Order = -1 };
        seq.Tracks.AddRange([extra, subtitles]);
        var gone = RollEditSafetyTests.Add(project, videos[1], 0, 1, 2, 8);
        var goneRight = RollEditSafetyTests.Add(project, videos[1], 2, 1, 2, 8);
        var survivor = RollEditSafetyTests.Add(project, videos[0], 0, 1, 2, 8);
        var second = RollEditSafetyTests.Add(project, extra, 0, 1, 2, 8);
        seq.LinkGroups.Add(new LinkGroup { ClipIds = [gone.Id, survivor.Id, second.Id] });
        seq.EditGroups.Add(new EditGroup { ClipIds = [gone.Id, survivor.Id, second.Id] });
        seq.LinkGroups.Add(new LinkGroup { ClipIds = [gone.Id, goneRight.Id] });
        VideoTransitionOperations.Apply(project, gone.Id, goneRight.Id, VideoTransitionOperations.Dissolve, 24);
        var initial = ProjectStore.Serialize(project);
        var undo = new UndoRedoManager(project);
        undo.Commit(p => TrackOperations.Delete(p.ActiveSequence!, videos[1].Id), "Eliminar pista");
        var changed = undo.CurrentProject.ActiveSequence!;
        Assert.Equal(new[] { 0, 1 }, changed.Tracks.Where(t => t.Kind == "video" && t.Role != "subtitle").OrderBy(t => t.Order).Select(t => t.Order));
        Assert.Equal("Mi pista", changed.Tracks.Single(t => t.Id == extra.Id).Name);
        Assert.Equal(-1, changed.Tracks.Single(t => t.Id == subtitles.Id).Order);
        Assert.Equal(new[] { survivor.Id, second.Id }, Assert.Single(changed.LinkGroups).ClipIds);
        Assert.Equal(new[] { survivor.Id, second.Id }, Assert.Single(changed.EditGroups).ClipIds);
        Assert.Empty(changed.VideoTransitions);
        Assert.Equal(project.Assets.Count, undo.CurrentProject.Assets.Count);
        Assert.True(undo.Undo());
        Assert.Equal(initial, ProjectStore.Serialize(undo.CurrentProject));
        Assert.True(undo.Redo());
        var saved = ProjectStore.Deserialize(ProjectStore.Serialize(undo.CurrentProject));
        Assert.DoesNotContain(saved.ActiveSequence!.Tracks, t => t.Id == videos[1].Id);
    }

    [Fact]
    public void GroupCanMoveBetweenSurvivingTracksAfterDeletingAnIntermediateLane()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var regular = seq.Tracks.Where(t => t.Kind == "video").OrderBy(t => t.Order).ToList();
        regular.Add(new Track { Kind = "video", Name = "V3", Order = 2 });
        regular.Add(new Track { Kind = "video", Name = "V4", Order = 3 });
        seq.Tracks.AddRange(regular.Skip(2));
        var first = RollEditSafetyTests.Add(project, regular[0], 0, 1, 2, 8);
        var second = RollEditSafetyTests.Add(project, regular[2], 0, 1, 2, 8);
        seq.EditGroups.Add(new EditGroup { ClipIds = [first.Id, second.Id] });
        TrackOperations.Delete(seq, regular[1].Id);
        Assert.True(ClipMoveTargets.TryResolve(seq, [first.Id, second.Id], first.Id, regular[2].Id, out var targets));
        Assert.Equal(regular[2].Id, targets[first.Id]);
        Assert.Equal(regular[3].Id, targets[second.Id]);
    }

    [Fact]
    public void SubtitleLaneDoesNotPermitDeletingTheLastRegularVideoTrack()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var videos = seq.Tracks.Where(t => t.Kind == "video").ToArray();
        TrackOperations.Delete(seq, videos[0].Id);
        var subtitle = new Track { Kind = "video", Role = "subtitle", Order = -1 };
        seq.Tracks.Add(subtitle);
        var snapshot = ProjectStore.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => TrackOperations.Delete(seq, videos[1].Id));
        Assert.Throws<InvalidOperationException>(() => TrackOperations.Delete(seq, subtitle.Id));
        Assert.Equal(snapshot, ProjectStore.Serialize(project));
        var audio = seq.Tracks.First(t => t.Kind == "audio");
        audio.Locked = true;
        Assert.Throws<InvalidOperationException>(() => TrackOperations.Delete(seq, audio.Id));
    }
}
