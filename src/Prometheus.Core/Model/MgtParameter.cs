using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public sealed class MgtParameter
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("default")] public JsonElement Default { get; set; }
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
    [JsonPropertyName("step")] public double? Step { get; set; }
    [JsonPropertyName("options")] public List<string> Options { get; set; } = [];
}
