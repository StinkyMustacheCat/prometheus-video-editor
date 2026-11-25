using System;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public sealed class AudioTransition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("clip_id")]
    public string ClipId { get; set; } = "";

    [JsonPropertyName("right_clip_id")]
    public string? RightClipId { get; set; }

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; set; }
}
