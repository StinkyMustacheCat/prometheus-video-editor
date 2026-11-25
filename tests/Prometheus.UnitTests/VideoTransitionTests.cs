using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class VideoTransitionTests
{
    [Fact]
    public void DissolveUsesRealSourceHandlesAtSequenceFrameRateAndRetainsTheCutOnSplit()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Fps = new Rational(30, 1);
        var track = sequence.Tracks.First(t => t.Kind == "video");
        var left = AddClip(project, track, 2, 1.8, 2, 4);
        var right = AddClip(project, track, 4, 0.4, 2, 4);
        var transition = VideoTransitionOperations.Apply(project, left.Id, right.Id, VideoTransitionOperations.Dissolve, 40);
        Assert.Equal(13, transition.DurationFrames);
        var window = VideoTransitionOperations.Window(sequence, transition)!;
        Assert.Equal(4, window.Cut);
        Assert.Equal(4 - 7d / 30, window.Start, 6);
        var layers = NestedRenderPlan.At(project, window.Start, "video");
        Assert.Equal(2, layers.Count);
        Assert.Equal(0.4 - 7d / 30, layers.Single(l => l.Clip.Id == right.Id).SourceSeconds, 6);
        Assert.Contains(window.Start, NestedRenderPlan.UpcomingStarts(project, 3, 4, "video"));
        Assert.True(NestedRenderTree.NeedsGroupComposition(layers, sequence));
        var node = Assert.Single(NestedRenderTree.Build(layers, sequence));
        Assert.Equal(1, node.Transition!.LeftWeight);
        Assert.Equal(0, node.Transition.RightWeight);
        Assert.Null(Assert.Single(NestedRenderPlan.At(project, window.End, "video")).Transition);

        var undo = new UndoRedoManager(project);
        undo.Commit(candidate => CollisionEngine.SplitClip(candidate.ActiveSequence!, left.Id, 3), "Dividir medio");
        var updated = undo.CurrentProject.ActiveSequence!;
        var updatedTransition = Assert.Single(updated.VideoTransitions);
        Assert.NotEqual(left.Id, updatedTransition.LeftClipId);
        Assert.Equal(4, VideoTransitionOperations.Window(updated, updatedTransition)!.Cut);
        Assert.True(undo.Undo());
        Assert.Equal(left.Id, Assert.Single(undo.CurrentProject.ActiveSequence!.VideoTransitions).LeftClipId);
    }

    [Fact]
    public void DipsWorkWithoutHandlesAndTransitionCopiesPersistWithTheRightClips()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var track = sequence.Tracks.First(t => t.Kind == "video");
        var left = AddClip(project, track, 0, 0, 2, 2);
        var right = AddClip(project, track, 2, 0, 2, 2);
        Assert.Throws<InvalidOperationException>(() =>
            VideoTransitionOperations.Apply(project, left.Id, right.Id, VideoTransitionOperations.Dissolve));
        var white = VideoTransitionOperations.Apply(project, left.Id, right.Id, VideoTransitionOperations.DipWhite, 24);
        var frame = Assert.Single(VideoTransitionOperations.FramesAt(sequence, 2));
        Assert.Equal(1, frame.SolidWeight);
        Assert.Equal(0, frame.LeftWeight + frame.RightWeight);

        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        var copy = SequenceOperations.Duplicate(restored, sequence.Id);
        var copied = Assert.Single(copy.VideoTransitions);
        Assert.NotEqual(white.Id, copied.Id);
        Assert.NotEqual(left.Id, copied.LeftClipId);
        Assert.Equal(24, copied.DurationFrames);
        Assert.Equal(2, VideoTransitionOperations.Window(copy, copied)!.Cut);

        var undo = new UndoRedoManager(restored);
        undo.Commit(p => p.ActiveSequence!.Clips.RemoveAll(c => c.Id == copied.LeftClipId), "Quitar medio");
        Assert.Empty(undo.CurrentProject.ActiveSequence!.VideoTransitions);
        Assert.True(undo.Undo());
        Assert.Single(undo.CurrentProject.ActiveSequence!.VideoTransitions);
    }

    private static Clip AddClip(Project project, Track track, double at, double sourceIn, double duration, double sourceDuration)
    {
        var stream = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "video", Duration = Rational.Time(sourceDuration) };
        var asset = new Asset { Id = Guid.NewGuid().ToString("N"), Streams = [stream] };
        project.Assets.Add(asset);
        var clip = new Clip { AssetId = asset.Id, StreamId = stream.Id, TrackId = track.Id, TimelineStart = Rational.Time(at),
            SourceIn = Rational.Time(sourceIn), Duration = Rational.Time(duration) };
        project.ActiveSequence!.Clips.Add(clip);
        return clip;
    }
}
