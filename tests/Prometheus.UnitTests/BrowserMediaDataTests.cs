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

    private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jH2kAAAAASUVORK5CYII=";

    [Fact]
    public void WindowsHtmlFragmentReturnsRasterBytesWithAnAlternativeWebUrl()
    {
        var html = $"SourceURL:https://example.org/docs\r\n<html><body>" +
            "<img src='https://example.org/outside.png'><!--StartFragment-->" +
            $"<img alt='figure > demo' data-src='https://example.org/original.png' src='data:image/png;base64,{Pixel}'>" +
            "<!--EndFragment--></body></html>";
        var data = new DataObject();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        data.Set("HTML Format", stream);
        Assert.True(BrowserMediaData.HasImage(data));
        Assert.Equal(Convert.FromBase64String(Pixel), BrowserMediaData.ReadInlineImage(BrowserMediaData.ReadText(stream)));
        Assert.Equal(0, stream.Position);
        Assert.True(BrowserMediaData.TryGetUrl(data, out var url));
        Assert.Equal("https://example.org/original.png", url.ToString());
    }

    [Theory]
    [InlineData("<img src='data:image/png;base64,%%%'>")]
    [InlineData("<img src='data:image/png;base64,PGh0bWw+VGV4dDwvaHRtbD4='>")]
    [InlineData("<img src='data:image/svg+xml;base64,PHN2Zz48L3N2Zz4='>")]
    [InlineData("<img alt=\"src='data:image/png;base64,{0}'\">")]
    public void EmbeddedImagesRejectMalformedNonRasterOrUnrelatedAttributes(string html)
    {
        Assert.Null(BrowserMediaData.ReadInlineImage(string.Format(html, Pixel)));
    }

    [Fact]
    public void BrowserOnlyReferenceDoesNotResolveToTheDocumentPage()
    {
        var data = new DataObject();
        data.Set("HTML Format", "SourceURL:https://example.org/docs\r\n<img src='blob:https://example.org/123'>");
        data.Set("text/uri-list", "https://example.org/docs");
        Assert.True(BrowserMediaData.HasHtmlImage(data));
        Assert.False(BrowserMediaData.TryGetUrl(data, out _));
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
