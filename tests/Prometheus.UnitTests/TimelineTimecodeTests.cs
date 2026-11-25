using Prometheus.Client.Panels;

namespace Prometheus.UnitTests;

public class TimelineTimecodeTests
{
    [Theory]
    [InlineData("00:00:01:12", 24.0, 1.5)]
    [InlineData("00:01:00:15", 30.0, 60.5)]
    [InlineData("00:00:00:59", 60.0, 59.0 / 60.0)]
    public void ParseUsesSequenceFrameRate(string value, double fps, double expected)
    {
        Assert.True(TimelinePanelView.TryParseTimecode(value, fps, out var seconds));
        Assert.Equal(expected, seconds, 6);
    }

    [Theory]
    [InlineData("00:00:01:24", 24.0)]
    [InlineData("00:60:00:00", 60.0)]
    [InlineData("00:00:ab:00", 60.0)]
    public void RejectsInvalidTimecode(string value, double fps) =>
        Assert.False(TimelinePanelView.TryParseTimecode(value, fps, out _));
}
