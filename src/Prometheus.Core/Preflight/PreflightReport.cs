using System.Collections.Generic;

namespace Prometheus.Core.Preflight;

public class MissingAssetInfo
{
    public string AssetId { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string ExpectedPath { get; set; } = string.Empty;
    public string Kind { get; set; } = "local";
}

public class PreflightReport
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> ActionsTaken { get; } = new();
    public List<MissingAssetInfo> MissingAssets { get; } = new();

    public bool HasMissingAssets => MissingAssets.Count > 0;
}
