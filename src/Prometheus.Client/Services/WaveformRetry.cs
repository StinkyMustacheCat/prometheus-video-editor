using System;

namespace Prometheus.Client.Services;

internal readonly record struct WaveformFailure(int Attempts, long RetryAt)
{
    public long Remaining(long now) => Math.Max(0, RetryAt - now);

    public static WaveformFailure Next(WaveformFailure? previous, long now)
    {
        var attempts = Math.Min(32, (previous?.Attempts ?? 0) + 1);
        var delay = Math.Min(60_000, 2_000L << Math.Min(5, attempts - 1));
        return new(attempts, now + delay);
    }
}
