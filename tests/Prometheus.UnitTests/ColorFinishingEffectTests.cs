using Prometheus.Core.Model;

namespace Prometheus.UnitTests;

public class ColorFinishingEffectTests
{
    [Fact]
    public void ReadsActiveEffectsAndIgnoresDisabledOnes()
    {
        var clip = new Clip();
        Assert.Equal(ColorFinishingSettings.Neutral, ColorFinishingEffect.Read(clip));
        ColorFinishingEffect.Add(clip, ColorFinishingEffect.WhiteBalanceId);
        ColorFinishingEffect.Add(clip, ColorFinishingEffect.HueId);
        var balance = clip.Effects.Single(e => e.EffectId == ColorFinishingEffect.WhiteBalanceId);
        ColorFinishingEffect.Number(balance, "temperature", 4200);
        var hue = clip.Effects.Single(e => e.EffectId == ColorFinishingEffect.HueId);
        ColorFinishingEffect.Number(hue, "degrees", 90);
        hue.Enabled = false;
        var state = ColorFinishingEffect.Read(clip);
        Assert.Equal(4200, state.Temperature);
        Assert.Equal(0, state.HueDegrees);
        hue.Enabled = true;
        Assert.Equal(90, ColorFinishingEffect.Read(clip).HueDegrees);
    }

    [Fact]
    public void ReaddingEffectKeepsStoredValues()
    {
        var clip = new Clip();
        ColorFinishingEffect.Add(clip, ColorFinishingEffect.VignetteId);
        var effect = Assert.Single(clip.Effects);
        ColorFinishingEffect.Number(effect, "amount", 0.8);
        effect.Enabled = false;
        ColorFinishingEffect.Add(clip, ColorFinishingEffect.VignetteId);
        Assert.Single(clip.Effects);
        Assert.Equal(0.8, ColorFinishingEffect.Read(clip).Vignette);
    }
}
