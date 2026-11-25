using Prometheus.Client.Services;
using Xunit;

namespace Prometheus.UnitTests;

public class WebDirectMediaTests
{
    [Fact]
    public void ExtensionlessImageResponseUsesItsActualFormat()
    {
        Assert.Equal(".jpg", WebDirectMedia.ExtensionForContentType("image/jpeg"));
        Assert.Equal(".jpg", WebDirectMedia.ImageExtension(new byte[] { 0xff, 0xd8, 0xff, 0xe0 }));
        Assert.Equal(".webp", WebDirectMedia.ImageExtension("RIFF\0\0\0\0WEBP"u8));
        Assert.Null(WebDirectMedia.ImageExtension("<html>error</html>"u8));
    }

    [Fact]
    public void CommonImageFormatsAreRecognizedWithoutFilenameExtension()
    {
        Assert.Equal(".png", WebDirectMedia.ImageExtension(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.Equal(".gif", WebDirectMedia.ImageExtension("GIF89a"u8));
        Assert.Equal(".avif", WebDirectMedia.ImageExtension(new byte[]
            { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f' }));
    }
}
