using System.Linq;
using Avalonia.Media;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class MediaLabelPaletteTests
{
    [Fact]
    public void PlaceholderIsGrayAndClipLabelSurvivesProjectRoundTrip()
    {
        var project = ProjectStore.CreateNew();
        var track = project.ActiveSequence!.Tracks.First(t => t.Kind == "video");
        var clip = PlaceholderService.Create(project, track.Id, 0, 5);
        var asset = project.Assets.First(a => a.Id == clip.AssetId);
        Assert.Equal(Color.Parse("#B7B8B9"), MediaLabelPalette.ForClip(clip, asset, "video"));

        asset.LabelId = "blue";
        clip.LabelId = "coral";
        var encoded = ProjectStore.Serialize(project);
        var restored = ProjectStore.Deserialize(encoded);
        var copy = restored.ActiveSequence!.Clips.Single();
        Assert.Equal("coral", copy.LabelId);
        Assert.Equal("blue", restored.Assets.Single().LabelId);
        Assert.Equal(Color.Parse("#E2A08F"), MediaLabelPalette.ForClip(copy, restored.Assets.Single(), "video"));
    }
}
