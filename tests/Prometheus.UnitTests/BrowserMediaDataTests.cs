using Prometheus.Client.Services;
using Avalonia.Input;
using System.Text;
using Xunit;

namespace Prometheus.UnitTests;

public class BrowserMediaDataTests
{
    [Theory]
    [InlineData("https://example.org/photo.png", "https://example.org/photo.png")]
    [InlineData("# browser URL\nhttps://example.org/photo.png", "https://example.org/photo.png")]
    [InlineData("image/png:photo.png:https://example.org/photo.png", "https://example.org/photo.png")]
    [InlineData("<img src=\"https://example.org/photo.png?x=1&amp;y=2\">", "https://example.org/photo.png?x=1&y=2")]
    public void BrowserFormats_ResolveOriginalUrl(string input, string expected)
    {
        Assert.True(BrowserMediaData.TryGetUrl(input, out var url));
        Assert.Equal(expected, url.ToString());
    }

    [Fact]
    public void Mgt_IsExcludedFromBrowserMediaImport()
    {
        Assert.False(BrowserMediaData.TryGetUrl("https://example.org/template.mgt", out _));
    }

    [Fact]
    public void HtmlImageTakesPriorityOverDraggedPageUrl()
    {
        var data = new DataObject();
        data.Set("HTML Format", new MemoryStream(Encoding.UTF8.GetBytes("SourceURL:https://example.org/story\r\n<img data-src='/media/photo.jpg'>")));
        data.Set("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://example.org/story\0")));
        Assert.True(BrowserMediaData.TryGetUrl(data, out var url));
        Assert.Equal("https://example.org/media/photo.jpg", url.ToString());
    }

    [Fact]
    public void UnicodeUrlStreamIsRead()
    {
        var data = new DataObject();
        data.Set("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://example.org/movie.mp4\0")));
        Assert.True(BrowserMediaData.TryGetUrl(data, out var url));
        Assert.Equal("https://example.org/movie.mp4", url.ToString());
    }

    [Fact]
    public void ProvisionalWebStreamsHaveUniqueIds()
    {
        var first = WebMediaService.CreateProvisionalStream("video");
        var second = WebMediaService.CreateProvisionalStream("video");
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.NotEqual(first.Id, second.Id);
    }
}
