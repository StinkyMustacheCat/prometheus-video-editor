using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.UnitTests;

public class DynamicMotionEffectTests
{
    [Fact]
    public void ZoomUsesVisibleClipDurationAfterTrim()
    {
        var clip = new Clip { TimelineStart = Rational.Time(10), Duration = Rational.Time(4) };
        DynamicMotionEffect.Add(clip, DynamicMotionEffect.ZoomInId);
        Assert.Equal(1, DynamicMotionEffect.Evaluate(clip, 10).ScaleX, 5);
        Assert.Equal(1.15, DynamicMotionEffect.Evaluate(clip, 14).ScaleX, 5);
        clip.Duration = Rational.Time(2);
        Assert.Equal(1.15, DynamicMotionEffect.Evaluate(clip, 12).ScaleX, 5);
    }

    [Fact]
    public void PanAndJourneyCombineWithoutChangingSavedBaseTransform()
    {
        var clip = new Clip { Duration = Rational.Time(10), Visual = new ClipVisualState { PositionX = 0.1, ScaleX = 2, ScaleY = 2 } };
        DynamicMotionEffect.Add(clip, DynamicMotionEffect.PanId);
        DynamicMotionEffect.Add(clip, DynamicMotionEffect.JourneyId);
        var middle = DynamicMotionEffect.Evaluate(clip, 5);
        Assert.Equal(0.1, middle.PositionX, 5);
        Assert.Equal(2.15, middle.ScaleX, 5);
        Assert.Equal(0.1, clip.Visual.PositionX, 5);
        Assert.Equal(2, clip.Visual.ScaleX, 5);
    }

    [Fact]
    public void DisabledMotionHasNoVisualEffect()
    {
        var clip = new Clip { Duration = Rational.Time(5) };
        DynamicMotionEffect.Add(clip, DynamicMotionEffect.ZoomOutId);
        clip.Effects[0].Enabled = false;
        Assert.Equal(1, DynamicMotionEffect.Evaluate(clip, 0).ScaleX, 5);
        Assert.Equal(1, DynamicMotionEffect.Evaluate(clip, 5).ScaleX, 5);
    }
}
