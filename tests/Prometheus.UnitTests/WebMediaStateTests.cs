using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Xunit;

namespace Prometheus.UnitTests;

public class WebMediaStateTests
{
    [Theory]
    [InlineData("https://example.com/video", true)]
    [InlineData("http://example.com/image.png", true)]
    [InlineData("file:///C:/private.mp4", false)]
    [InlineData("javascript:alert(1)", false)]
    public void OnlyHttpLinksAreAccepted(string input, bool expected)
    {
        Assert.Equal(expected, WebMediaService.TryUrl(input, out _));
    }

    [Fact]
    public void PendingWebAssetSurvivesProjectSerialization()
    {
        var project = ProjectStore.CreateNew();
        project.Assets.Add(new Asset { Id = "web1", Name = "Pendiente", Source = new MediaSource { Kind = "web", Uri = "https://example.com/video" } });
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        Assert.True(WebMediaService.IsPending(loaded.Assets[0]));
        Assert.Equal("https://example.com/video", loaded.Assets[0].Source.Uri);
    }
}
