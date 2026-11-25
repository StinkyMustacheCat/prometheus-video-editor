namespace Prometheus.Core.Model;

public sealed record ProjectRecoveryInfo(string? OriginalPath, DateTime SavedUtc,
    int ProcessId, long ProcessStartedUtcTicks);
