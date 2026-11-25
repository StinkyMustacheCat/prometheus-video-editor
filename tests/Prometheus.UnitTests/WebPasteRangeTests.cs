using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class WebPasteRangeTests
{
    [Theory]
    [InlineData("1.5", "4", 1.5, 2.5)]
    [InlineData("1,5", "4", 1.5, 2.5)]
    [InlineData("01:02", "01:04.5", 62, 2.5)]
    [InlineData("01:02:03", "01:02:05", 3723, 2)]
    public void RangeSupportsSecondsAndTimecodes(string from, string to, double start, double duration)
    {
        Assert.True(WebLinkDialog.TryRange(from, to, null, 60, out var actualStart, out var actualDuration));
        Assert.Equal(start, actualStart); Assert.Equal(duration, actualDuration);
    }

    [Theory]
    [InlineData("-1", "4")]
    [InlineData("3", "2")]
    [InlineData("1", "1.001")]
    [InlineData("NaN", "3")]
    [InlineData("0", "Infinity")]
    [InlineData("01:99", "03:00")]
    [InlineData("bad", "3")]
    public void InvalidRangeCannotCreateClip(string from, string to) =>
        Assert.False(WebLinkDialog.TryRange(from, to, null, 60, out _, out _));

    [Fact]
    public void RangeMustFitKnownSourceAndAllowOneFrame()
    {
        Assert.False(WebLinkDialog.TryRange("3", "6", 5, 60, out _, out _));
        Assert.True(WebLinkDialog.TryRange("3", "3.02", 5, 60, out _, out _));
        Assert.False(WebLinkDialog.TryRange("3", "3.02", 5, 24, out _, out _));
    }
}
