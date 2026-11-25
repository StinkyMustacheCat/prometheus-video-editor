using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public class ExposureEffectTests
{
    [Fact]
    public void ExposureKeepsStoredValuesWhenDisabledAndSurvivesProjectSave()
    {
        var project = ProjectStore.CreateNew();
        var clip = new Clip();
        project.ActiveSequence!.Clips.Add(clip);
        ExposureEffect.Set(clip, true, new ExposureSettings(1.5, -0.2));
        ExposureEffect.Set(clip, false, ExposureEffect.ReadStored(clip));

        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        var result = restored.ActiveSequence!.Clips.Single();
        Assert.Equal(ExposureSettings.Neutral, ExposureEffect.Read(result));
        Assert.Equal(new ExposureSettings(1.5, -0.2), ExposureEffect.ReadStored(result));
    }
}
