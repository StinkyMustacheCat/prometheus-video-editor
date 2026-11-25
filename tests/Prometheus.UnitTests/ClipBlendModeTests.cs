using System.Text.Json;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class ClipBlendModeTests
{
    [Fact]
    public void OlderVisualStateDefaultsToNormalAndUnknownModesRenderSafely()
    {
        var state = JsonSerializer.Deserialize<ClipVisualState>("{\"opacity\":0.4}")!;
        Assert.Equal("normal", state.BlendMode);
        Assert.Equal(0, ClipBlendModes.GpuId("unknown-future-mode"));
        Assert.False(ClipBlendModes.IsSupported("unknown-future-mode"));
    }

    [Fact]
    public void MotionAndOpacityAnimationPreserveBlendMode()
    {
        var clip = new Clip { Duration = Rational.Time(4), Visual = new ClipVisualState { BlendMode = "screen" } };
        KeyframeAnimation.Set(clip, "visual.opacity", 0, 0);
        KeyframeAnimation.Set(clip, "visual.opacity", 2, 1);
        var evaluated = DynamicMotionEffect.Evaluate(clip, 1);
        Assert.Equal("screen", evaluated.BlendMode);
        Assert.InRange(evaluated.Opacity, .01, .99);
        Assert.Equal("screen", clip.Visual.BlendMode);
        Assert.Equal(1, clip.Visual.Opacity);
    }

    [Fact]
    public void ModePersistsThroughSaveMoveAndUndo()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Clips.Add(new Clip { Id = "clip", TrackId = sequence.Tracks.First(t => t.Kind == "video").Id });
        var undo = new UndoRedoManager(project);
        undo.Commit(p => p.ActiveSequence!.Clips[0].Visual.BlendMode = "multiply", "Blend");
        var saved = ProjectStore.Clone(undo.CurrentProject);
        Assert.Equal("multiply", saved.ActiveSequence!.Clips[0].Visual.BlendMode);
        var other = saved.ActiveSequence.Tracks.Last(t => t.Kind == "video").Id;
        saved.ActiveSequence.Clips[0].TrackId = other;
        Assert.Equal("multiply", saved.ActiveSequence.Clips[0].Visual.BlendMode);
        Assert.True(undo.Undo());
        Assert.Equal("normal", undo.CurrentProject.ActiveSequence!.Clips[0].Visual.BlendMode);
        Assert.True(undo.Redo());
        Assert.Equal("multiply", undo.CurrentProject.ActiveSequence!.Clips[0].Visual.BlendMode);
    }

    [Fact]
    public void NonNormalLeafAndContainerRequireGroupComposition()
    {
        var track = new Track();
        var leaf = new Clip { TrackId = track.Id, Visual = new ClipVisualState { BlendMode = "difference" } };
        var layers = new[] { new NestedRenderLayer(leaf, track, 0, 0, [], [], [0]) };
        Assert.True(NestedRenderTree.NeedsGroupComposition(layers));
        leaf.Visual.BlendMode = "normal";
        Assert.False(NestedRenderTree.NeedsGroupComposition(layers));
        var parent = new Clip { Visual = new ClipVisualState { BlendMode = "overlay" } };
        layers[0] = layers[0] with { Containers = new[] { parent }, ContainerTimelineSeconds = new double[] { 0 } };
        Assert.True(NestedRenderTree.NeedsGroupComposition(layers));
    }

    [Fact]
    public void TrackEffectsLiftSingleClipBlendWithoutChangingStoredClip()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var track = sequence.Tracks.First(t => t.Kind == "video");
        var clip = new Clip { TrackId = track.Id, Visual = new ClipVisualState { BlendMode = "screen" } };
        sequence.Clips.Add(clip);
        var fx = new Clip(); BasicColorEffect.Set(fx, true, new BasicColorSettings(.1, 1, 1)); track.Effects = fx.Effects;
        var tree = NestedRenderTree.Build(NestedRenderPlan.At(project, 0, "video"), sequence);
        var group = Assert.Single(tree);
        Assert.Equal("screen", group.Clip.Visual.BlendMode);
        Assert.Equal("normal", Assert.Single(group.Children).BlendModeOverride);
        Assert.Equal("screen", clip.Visual.BlendMode);
    }
}
