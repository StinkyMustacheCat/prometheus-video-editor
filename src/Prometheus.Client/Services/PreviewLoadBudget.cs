using System;

namespace Prometheus.Client.Services;

internal sealed class PreviewLoadBudget
{
    private int _slowFrames;
    public bool Reduce { get; private set; }

    public void Observe(bool eligible, bool completed, TimeSpan elapsed, int requestedFps)
    {
        if (!eligible)
        {
            _slowFrames = 0;
            Reduce = false;
            return;
        }
        if (!completed || Reduce) return;
        // Let the first texture allocation and occasional slow frame pass at full quality.
        if (elapsed.TotalSeconds > 1.2 / requestedFps)
            Reduce = ++_slowFrames >= 5;
        else
            _slowFrames = 0;
    }
}
