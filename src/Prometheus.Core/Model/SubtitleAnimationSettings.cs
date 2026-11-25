using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public sealed class SubtitleAnimationSettings
{
    [JsonPropertyName("template_json")] public string TemplateJson { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("parameters")] public Dictionary<string, JsonElement> Parameters { get; set; } = new();
}

/// <summary>Times in the original caption's source clock, before trimming or speed changes.</summary>
public sealed record CaptionWord(string WordId, string Text, double Start, double End);
