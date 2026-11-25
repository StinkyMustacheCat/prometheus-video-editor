using System;

namespace Prometheus.Client.Timeline;

internal static class TimelineRulerStep
{
    private static readonly double[] Steps =
    {
        1.0 / 60, 1.0 / 30, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30,
        60, 120, 300, 600, 900, 1800, 3600, 7200, 14400, 28800, 86400
    };

    public static double ForZoom(double pixelsPerSecond, double targetSpacing = 90)
    {
        var minimum = targetSpacing / Math.Max(0.1, pixelsPerSecond);
        foreach (var step in Steps)
            if (step >= minimum) return step;
        return Math.Ceiling(minimum / 86400) * 86400;
    }
}
