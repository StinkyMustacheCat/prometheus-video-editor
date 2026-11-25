using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class KeyframeAnimationTests
{
    [Fact]
    public void SplitRemapsEveryMaskTargetAndEvaluationKeepsCompleteClipMetadata()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var effect = new EffectInstance { EffectId = "test" };
        var clip = new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "video").Id,
            Duration = Rational.Time(4), Effects = [effect], MaskLayersInitialized = true,
            MaskLayers = [new ClipMaskLayer { Target = "effect:" + effect.Id }],
            ShapeMask = new ShapeMask(), LabelId = "label", AvailableSourceDurationSeconds = 10,
            MgtOverrides = new() { ["color"] = System.Text.Json.JsonSerializer.SerializeToElement("#ff0000") } };
        KeyframeAnimation.Set(clip, KeyframeAnimation.EffectTarget(effect, "amount"), 0, .25);
        sequence.Clips.Add(clip);
        var right = CollisionEngine.SplitClip(sequence, clip.Id, 2).Right!;
        Assert.Equal("effect:" + right.Effects.Single().Id, right.MaskLayers.Single().Target);
        Assert.NotEqual(effect.Id, right.Effects.Single().Id);
        right.MaskLayers[0].Name = "Right";
        Assert.NotEqual("Right", clip.MaskLayers[0].Name);
        var result = KeyframeAnimation.EvaluateEffects(clip, 1);
        Assert.True(result.MaskLayersInitialized);
        Assert.Equal(clip.MaskLayers, result.MaskLayers);
        Assert.Equal(clip.ShapeMask, result.ShapeMask);
        Assert.Equal(clip.MgtOverrides, result.MgtOverrides);
        Assert.Equal(clip.LabelId, result.LabelId);
        Assert.Equal(.25, result.Effects[0].Parameters["amount"].Number);
        Assert.False(effect.Parameters.ContainsKey("amount"));
    }

    [Fact]
    public void McpSmoothAliasAndUiEaseEvaluateIdentically()
    {
        var clip = new Clip { Duration = Rational.Time(4) };
        KeyframeAnimation.Set(clip, "visual.opacity", 0, 0, "smooth");
        KeyframeAnimation.Set(clip, "visual.opacity", 4, 1);
        var smooth = KeyframeAnimation.Evaluate(clip, "visual.opacity", 1, 0);
        clip.KeyframeTracks.Single().Points[0].Interpolation = "ease";
        Assert.Equal(.15625, smooth, 8);
        Assert.Equal(smooth, KeyframeAnimation.Evaluate(clip, "visual.opacity", 1, 0));
        Assert.Contains("ease-in", KeyframeAnimation.Interpolations);
        Assert.Contains("ease-out", KeyframeAnimation.Interpolations);
    }

    [Fact]
    public void VisualValuesInterpolateAtClipLocalTimeAndSurviveSave()
    {
        var clip = new Clip { TimelineStart = Rational.Time(10), Duration = Rational.Time(4) };
        KeyframeAnimation.Enable(clip, "visual.scale_x", 10, 1);
        KeyframeAnimation.Set(clip, "visual.scale_x", 14, 2);
        KeyframeAnimation.Set(clip, "visual.rotation", 10, 0);
        KeyframeAnimation.Set(clip, "visual.rotation", 14, 90);
        var project = ProjectStore.CreateNew();
        clip.TrackId = project.ActiveSequence!.Tracks.First(t => t.Kind == "video").Id;
        project.ActiveSequence.Clips.Add(clip);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project)).ActiveSequence!.Clips.Single();
        var value = DynamicMotionEffect.Evaluate(restored, 12);
        Assert.Equal(1.5, value.ScaleX, 6);
        Assert.Equal(45, value.RotationDegrees, 6);
        Assert.Equal(1, DynamicMotionEffect.Evaluate(restored, 9).ScaleX);
        Assert.Equal(2, DynamicMotionEffect.Evaluate(restored, 15).ScaleX);
    }

    [Fact]
    public void HoldAndEaseInterpolationHaveDistinctResults()
    {
        var clip = new Clip { Duration = Rational.Time(4) };
        KeyframeAnimation.Set(clip, "visual.opacity", 0, 0, "hold");
        KeyframeAnimation.Set(clip, "visual.opacity", 4, 1);
        Assert.Equal(0, KeyframeAnimation.Evaluate(clip, "visual.opacity", 3, 1));
        KeyframeAnimation.Set(clip, "visual.opacity", 0, 0, "ease");
        Assert.Equal(0.15625, KeyframeAnimation.Evaluate(clip, "visual.opacity", 1, 1), 6);
        KeyframeAnimation.Disable(clip, "visual.opacity");
        Assert.Equal(0.7, KeyframeAnimation.Evaluate(clip, "visual.opacity", 1, 0.7));
    }

    [Fact]
    public void BezierAndAccumulatedRotationSurviveSaveAndPointMove()
    {
        var clip = new Clip { Duration = Rational.Time(4) };
        KeyframeAnimation.Set(clip, "visual.rotation", 0, 0);
        KeyframeAnimation.Set(clip, "visual.rotation", 4, 720);
        Assert.True(KeyframeAnimation.SetBezier(clip, "visual.rotation", 0, 0.8, 0, 0.9, 1));
        Assert.True(KeyframeAnimation.Evaluate(clip, "visual.rotation", 2, 0) < 360);
        KeyframeAnimation.Set(clip, "visual.rotation", 0, 0);
        Assert.Equal("bezier", KeyframeAnimation.Find(clip, "visual.rotation")!.Points[0].Interpolation);
        Assert.True(KeyframeAnimation.Move(clip, "visual.rotation", 0, 1));
        Assert.Equal(0.8, KeyframeAnimation.Find(clip, "visual.rotation")!.Points[0].BezierX1);
        var project = ProjectStore.CreateNew();
        clip.TrackId = project.ActiveSequence!.Tracks.First(t => t.Kind == "video").Id;
        project.ActiveSequence.Clips.Add(clip);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project)).ActiveSequence!.Clips.Single();
        Assert.Equal(720, KeyframeAnimation.Evaluate(restored, "visual.rotation", 4, 0));
        Assert.Equal("bezier", KeyframeAnimation.Find(restored, "visual.rotation")!.Points[0].Interpolation);
    }

    [Fact]
    public void MovingPointPreservesItsValueAndInterpolation()
    {
        var clip = new Clip { TimelineStart = Rational.Time(5), Duration = Rational.Time(5) };
        KeyframeAnimation.Set(clip, "visual.position_x", 6, 0.25, "hold");
        Assert.True(KeyframeAnimation.Move(clip, "visual.position_x", 6, 8));
        var point = Assert.Single(KeyframeAnimation.Find(clip, "visual.position_x")!.Points);
        Assert.Equal(3, point.TimeSeconds);
        Assert.Equal(0.25, point.Value);
        Assert.Equal("hold", point.Interpolation);
        Assert.True(KeyframeAnimation.Remove(clip, "visual.position_x", 8));
        Assert.Empty(KeyframeAnimation.Find(clip, "visual.position_x")!.Points);
    }

    [Fact]
    public void EffectAnimationUsesSnapshotWithoutChangingParentOrChild()
    {
        var parent = new Clip { Duration = Rational.Time(4) };
        var child = new Clip { Duration = Rational.Time(4) };
        BasicColorEffect.Set(parent, true, new BasicColorSettings(0, 1, 1));
        BasicColorEffect.Set(child, true, new BasicColorSettings(0.2, 1, 1));
        var effect = BasicColorEffect.Find(parent)!;
        var target = KeyframeAnimation.EffectTarget(effect, "brightness");
        KeyframeAnimation.Set(parent, target, 0, 0);
        KeyframeAnimation.Set(parent, target, 4, 0.8);
        var snapshot = KeyframeAnimation.EvaluateEffects(parent, 2);
        Assert.Equal(0.4, BasicColorEffect.Read(snapshot).Brightness, 6);
        Assert.Equal(0, BasicColorEffect.Read(parent).Brightness);
        Assert.Equal(0.2, BasicColorEffect.Read(child).Brightness);
    }

    [Fact]
    public void SplitKeepsAnimationAtTheSameTimelinePositionsAndRemapsEffectIds()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var clip = new Clip { TrackId = sequence.Tracks.First(track => track.Kind == "video").Id,
            TimelineStart = Rational.Time(2), Duration = Rational.Time(6) };
        BasicColorEffect.Set(clip, true, BasicColorSettings.Neutral);
        var target = KeyframeAnimation.EffectTarget(BasicColorEffect.Find(clip)!, "brightness");
        KeyframeAnimation.Set(clip, target, 2, 0);
        KeyframeAnimation.Set(clip, target, 8, 0.6);
        sequence.Clips.Add(clip);

        var (_, right) = CollisionEngine.SplitClip(sequence, clip.Id, 5);
        Assert.NotNull(right);
        Assert.Equal(-3, Assert.Single(right!.KeyframeTracks).Points[0].TimeSeconds);
        Assert.Equal(3, Assert.Single(right.KeyframeTracks).Points[1].TimeSeconds);
        var newTarget = KeyframeAnimation.EffectTarget(BasicColorEffect.Find(right)!, "brightness");
        Assert.Equal(newTarget, Assert.Single(right.KeyframeTracks).Target);
        Assert.Equal(0.3, BasicColorEffect.Read(KeyframeAnimation.EvaluateEffects(right, 5)).Brightness, 6);
        Assert.Equal(0.3, BasicColorEffect.Read(KeyframeAnimation.EvaluateEffects(clip, 5)).Brightness, 6);
    }

    [Fact]
    public void DuplicatingAndChangingSpeedPreservesEffectAnimation()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var clip = new Clip { TrackId = sequence.Tracks.First(track => track.Kind == "video").Id,
            Duration = Rational.Time(4) };
        BasicColorEffect.Set(clip, true, BasicColorSettings.Neutral);
        var target = KeyframeAnimation.EffectTarget(BasicColorEffect.Find(clip)!, "brightness");
        KeyframeAnimation.Set(clip, target, 0, 0);
        KeyframeAnimation.Set(clip, target, 4, 0.8);
        sequence.Clips.Add(clip);
        var copy = Assert.Single(CollisionEngine.DuplicateClips(sequence, [clip.Id], 5));
        Assert.NotEqual(BasicColorEffect.Find(clip)!.Id, BasicColorEffect.Find(copy)!.Id);
        Assert.Equal(KeyframeAnimation.EffectTarget(BasicColorEffect.Find(copy)!, "brightness"),
            Assert.Single(copy.KeyframeTracks).Target);
        CollisionEngine.SetClipSpeed(copy, 2);
        Assert.Equal(2, copy.Duration.Seconds, 6);
        Assert.Equal(2, copy.KeyframeTracks.Single().Points.Last().TimeSeconds, 6);
        Assert.Equal(0.4, BasicColorEffect.Read(KeyframeAnimation.EvaluateEffects(copy, 6)).Brightness, 6);
    }
}
