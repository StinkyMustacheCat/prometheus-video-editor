using System;

namespace Prometheus.Client.Services;

public static class PreviewUserSettings
{
    public static int QualityIndex { get; private set; } = 2;
    public static int RefreshIndex { get; private set; } = 2;
    public static event Action? Changed;

    public static void Set(int qualityIndex, int refreshIndex)
    {
        qualityIndex = Math.Clamp(qualityIndex, 0, 2);
        refreshIndex = Math.Clamp(refreshIndex, 0, 2);
        if (QualityIndex == qualityIndex && RefreshIndex == refreshIndex) return;
        QualityIndex = qualityIndex;
        RefreshIndex = refreshIndex;
        Changed?.Invoke();
    }
}
