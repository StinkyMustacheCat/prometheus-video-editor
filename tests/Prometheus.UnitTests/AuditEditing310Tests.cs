using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class AuditEditing310Tests
{
    private static Clip Add(Project project, string kind, double at, double duration)
    {
        var asset = new Asset { Id = Guid.NewGuid().ToString("N"), Streams = [new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = kind, Duration = Rational.Time(100) }] };
        project.Assets.Add(asset);
        var clip = new Clip { AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            TrackId = project.ActiveSequence!.Tracks.First(t => t.Kind == kind).Id,
            TimelineStart = Rational.Time(at), Duration = Rational.Time(duration) };
        project.ActiveSequence.Clips.Add(clip);
        return clip;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SilenceCutContractsInwardAndUsesTheSameRippleForLinkedMedia(bool ripple)
    {
        var project = ProjectStore.CreateNew();
        var audio = Add(project, "audio", 0, 1);
        var video = Add(project, "video", 0, 1);
        project.ActiveSequence!.LinkGroups.Add(new LinkGroup { ClipIds = [audio.Id, video.Id] });
        var manager = new UndoRedoManager(project);
        manager.Commit(p => SilenceCutOperations.Apply(p.ActiveSequence!, audio.Id,
            [new SilenceCut(.005, .5)], ripple), "Cut with off-grid edge");
        var seq = manager.CurrentProject.ActiveSequence!;
        Assert.True(PreflightAuditor.Audit(manager.CurrentProject).IsValid);
        foreach (var kind in new[] { "audio", "video" })
        {
            var parts = seq.Clips.Where(c => seq.Tracks.Any(t => t.Id == c.TrackId && t.Kind == kind))
                .OrderBy(c => c.TimelineStart.Seconds).ToArray();
            Assert.Equal(2, parts.Length);
            Assert.Equal(1d / 60, parts[0].Duration.Seconds, 6);
            Assert.Equal(ripple ? 1d / 60 : .5, parts[1].TimelineStart.Seconds, 6);
            Assert.Equal(.5, parts[1].SourceIn.Seconds, 6);
            Assert.Equal(.5, parts[1].Duration.Seconds, 6);
        }
        Assert.Equal(2, seq.LinkGroups.Count);
        Assert.True(manager.Undo());
        Assert.Equal(2, manager.CurrentProject.ActiveSequence!.Clips.Count);
        Assert.All(manager.CurrentProject.ActiveSequence.Clips, c => Assert.Equal(1, c.Duration.Seconds));
        Assert.True(manager.Redo());
    }

    [Fact]
    public void RangeTrimKeepsBothShortEdgesWithoutDeletingAdditionalSource()
    {
        var project = ProjectStore.CreateNew();
        Add(project, "video", 0, 1);
        var manager = new UndoRedoManager(project);
        manager.Commit(p => CollisionEngine.RangeTrim(p.ActiveSequence!, .005, .995), "Trim");
        var parts = manager.CurrentProject.ActiveSequence!.Clips.OrderBy(c => c.TimelineStart.Seconds).ToArray();
        Assert.Equal(2, parts.Length);
        Assert.All(parts, c => Assert.Equal(1d / 60, c.Duration.Seconds, 5));
        Assert.Equal(1 - 1d / 60, parts[1].SourceIn.Seconds, 5);
    }

    [Theory]
    [InlineData(0, .505, .505)]
    [InlineData(.495, 1, .495)]
    public void CaptureInsertionNearOffGridEdgePreservesSourceAndLinkedTiming(double start, double duration, double expected)
    {
        var project = ProjectStore.CreateNew();
        var video = Add(project, "video", start, duration);
        var audio = Add(project, "audio", start, duration);
        project.ActiveSequence!.LinkGroups.Add(new LinkGroup { ClipIds = [video.Id, audio.Id] });
        var originalIds = new[] { video.Id, audio.Id };
        var still = new Asset { Id = "capture", RecommendedDuration = Rational.Time(.1),
            Streams = [new StreamInfo { Id = "capture-image", Kind = "image" }] };
        var manager = new UndoRedoManager(project);
        manager.Commit(p => CapturedFramePlacement.Insert(p, still, .5), "Insert capture");
        var seq = manager.CurrentProject.ActiveSequence!;
        Assert.Equal(3, seq.Clips.Count);
        Assert.All(seq.Clips.Where(c => originalIds.Contains(c.Id)), c => {
            Assert.Equal(duration, c.Duration.Seconds, 6);
            Assert.Equal(start >= expected ? start + .1 : start, c.TimelineStart.Seconds, 6);
            Assert.Equal(0, c.SourceIn.Seconds);
        });
        Assert.Equal(expected, seq.Clips.Single(c => c.AssetId == still.Id).TimelineStart.Seconds, 6);
        Assert.Single(seq.LinkGroups);
        Assert.True(manager.Undo());
        Assert.Equal(2, manager.CurrentProject.ActiveSequence!.Clips.Count);
    }

    [Fact]
    public void UnnestComposesOverflowingFractionsWithinOneAudioSampleAndUndoIsExact()
    {
        var project = ProjectStore.CreateNew();
        var child = Add(project, "video", 0, 2);
        child.Speed = new Rational(166898812960, 349359654959);
        var parentId = NestedMediaOperations.Create(project, [child.Id]);
        var parent = project.ActiveSequence!.Clips.Single(c => c.Id == parentId);
        parent.Speed = new Rational(955946191375, 1879110867042);
        parent.Duration = Rational.One;
        var expected = child.Speed.Seconds * parent.Speed.Seconds;
        var before = ProjectStore.Serialize(project);
        Assert.True(PreflightAuditor.Audit(project).IsValid, string.Join("; ", PreflightAuditor.Audit(project).Errors));
        var manager = new UndoRedoManager(project);
        manager.Commit(p => NestedMediaOperations.Unnest(p, parentId), "Unnest");
        var revealed = Assert.Single(manager.CurrentProject.ActiveSequence!.Clips);
        Assert.True(Math.Abs(revealed.Speed.Seconds - expected) * revealed.Duration.Seconds <= 1d / 48000);
        Assert.True(manager.Undo());
        Assert.Equal(before, ProjectStore.Serialize(manager.CurrentProject));
    }

    [Fact]
    public void UnnestKeepsExactSmallFractionsAndAssignsUnusedGroupLabels()
    {
        var project = ProjectStore.CreateNew();
        var first = Add(project, "video", 0, 1);
        first.Speed = new Rational(3, 2);
        var second = Add(project, "video", 1, 1);
        project.ActiveSequence!.EditGroups.Add(new EditGroup { Number = 1, ClipIds = [first.Id, second.Id] });
        var parentId = NestedMediaOperations.Create(project, [first.Id, second.Id]);
        project.ActiveSequence.Clips.Single(c => c.Id == parentId).Speed = new Rational(2, 3);
        var a = Add(project, "video", 5, 1);
        var b = Add(project, "video", 6, 1);
        project.ActiveSequence.EditGroups.Add(new EditGroup { Number = 1, ClipIds = [a.Id, b.Id] });
        var manager = new UndoRedoManager(project);
        manager.Commit(p => NestedMediaOperations.Unnest(p, parentId), "Unnest groups");
        var seq = manager.CurrentProject.ActiveSequence!;
        Assert.Equal(2, seq.EditGroups.Count);
        Assert.Equal(2, seq.EditGroups.Select(g => g.Number).Distinct().Count());
        Assert.Contains(seq.Clips, c => c.Speed == Rational.One && c.Duration.Seconds == 1.5);
        Assert.All(seq.EditGroups, g => Assert.Equal(2, g.ClipIds.Count));
    }
}
