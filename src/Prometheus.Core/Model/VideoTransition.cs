using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public sealed class VideoTransition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "dissolve";
    [JsonPropertyName("left_clip_id")]
    public string LeftClipId { get; set; } = "";
    [JsonPropertyName("right_clip_id")]
    public string RightClipId { get; set; } = "";
    [JsonPropertyName("mgt")]
    public MgtGpuTransition? Mgt { get; set; }

    [JsonPropertyName("mgt_parameters")]
    public Dictionary<string, JsonElement> MgtOverrides { get; set; } = new();

    [JsonPropertyName("duration_frames")]
    public int DurationFrames { get; set; }
}
