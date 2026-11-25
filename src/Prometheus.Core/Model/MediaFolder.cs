using System;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public class MediaFolder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Nueva carpeta";
}
