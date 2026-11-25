using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public class LevelsEffectTests
{
    [Fact]
    public void LevelsKeepAValidRangeAndMonochromePersists()
    {
        var project = ProjectStore.CreateNew();
        var clip = new Clip();
        project.ActiveSequence!.Clips.Add(clip);
        LevelsEffect.Set(clip, true, new LevelsSettings(0.8, 0.2, 2));
        MonochromeEffect.Set(clip, true);

        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        var result = restored.ActiveSequence!.Clips.Single();
        var levels = LevelsEffect.Read(result);
        Assert.True(levels.White > levels.Black);
        Assert.Equal(2, levels.Gamma);
        Assert.True(MonochromeEffect.IsEnabled(result));
    }
}
