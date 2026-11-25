using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Prometheus.Core.Model;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MgtGpuTransition
{
    public const string Profile = "gpu-canvas2d-v1";
    private static readonly Regex IdPattern = new(@"^[a-z][a-z0-9.-]{2,79}$", RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new(@"^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.CultureInvariant);

    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("contractVersion")] public int ContractVersion { get; init; }
    [JsonPropertyName("renderProfile")] public string RenderProfile { get; init; } = "";
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("seed")] public uint Seed { get; init; } = 1;
    [JsonPropertyName("recommended")] public MgtGpuRecommended Recommended { get; init; } = new();
    [JsonPropertyName("duration")] public MgtGpuDuration Duration { get; init; } = new();
    [JsonPropertyName("output")] public MgtGpuOutput Output { get; init; } = new();
    [JsonPropertyName("parameters")] public List<MgtParameter> Parameters { get; init; } = [];
    [JsonPropertyName("optionLabels")] public Dictionary<string, Dictionary<string, string>> OptionLabels { get; init; } = new();
    [JsonPropertyName("render")] public string Render { get; init; } = "";

    public static MgtGpuTransition Parse(string json)
    {
        if (json.Length > 256 * 1024) throw new InvalidDataException("La plantilla MGT supera 256 KiB.");
        var value = JsonSerializer.Deserialize<MgtGpuTransition>(json) ??
            throw new InvalidDataException("El documento MGT está vacío.");
        value.Validate();
        return value;
    }

    public void Validate()
    {
        if (Kind != "transition" || ContractVersion != 1 || RenderProfile != Profile ||
            Version is < 2 or > 4 || !IdPattern.IsMatch(Id ?? "") || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 ||
            string.IsNullOrWhiteSpace(Render) || Render.Length > 96 * 1024 || Duration == null || Recommended == null || Output == null ||
            Duration.Adaptation != "stretch" || !double.IsFinite(Duration.Min) || !double.IsFinite(Duration.Max) ||
            !double.IsFinite(Recommended.Duration) || Duration.Min < 0.1 || Duration.Max > 30 || Duration.Max < Duration.Min ||
            Recommended.Duration < Duration.Min || Recommended.Duration > Duration.Max ||
            Output.Alpha != "straight" || Output.ColorSpace != "srgb-bt709-sdr" || Parameters == null || Parameters.Count > 48 || OptionLabels == null)
            throw new InvalidDataException("Se requiere una transición MGT del perfil GPU Canvas 2D v1 con duración y salida SDR válidas.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Parameters)
        {
            if (parameter == null || !KeyPattern.IsMatch(parameter.Id ?? "") || !keys.Add(parameter.Id!) ||
                string.IsNullOrWhiteSpace(parameter.Label) || parameter.Label.Length > 80 || !Accepts(parameter, parameter.Default))
                throw new InvalidDataException("La transición MGT contiene un parámetro inválido.");
        }
    }

    public static bool Accepts(MgtParameter parameter, JsonElement value) => parameter.Type switch
    {
        "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
            double.IsFinite(number) && number >= (parameter.Min ?? -1e8) && number <= (parameter.Max ?? 1e8) &&
            (parameter.Min == null || double.IsFinite(parameter.Min.Value) && Math.Abs(parameter.Min.Value) <= 1e8) &&
            (parameter.Max == null || double.IsFinite(parameter.Max.Value) && Math.Abs(parameter.Max.Value) <= 1e8) &&
            (parameter.Step == null || double.IsFinite(parameter.Step.Value) && parameter.Step.Value is >= 1e-28 and <= 1e8),
        "color" => value.ValueKind == JsonValueKind.String &&
            Regex.IsMatch(value.GetString() ?? "", @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"),
        "select" => value.ValueKind == JsonValueKind.String && parameter.Options?.Count is > 0 and <= 30 &&
            parameter.Options.All(option => !string.IsNullOrWhiteSpace(option) && option.Length <= 100) &&
            parameter.Options.Distinct(StringComparer.Ordinal).Count() == parameter.Options.Count &&
            parameter.Options.Contains(value.GetString()!),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "text" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 300,
        _ => false
    };
}

public sealed record MgtGpuRecommended([property: JsonPropertyName("duration")] double Duration = 0);
public sealed record MgtGpuDuration([property: JsonPropertyName("min")] double Min = 0,
    [property: JsonPropertyName("max")] double Max = 0,
    [property: JsonPropertyName("adaptation")] string Adaptation = "");
public sealed record MgtGpuOutput([property: JsonPropertyName("alpha")] string Alpha = "",
    [property: JsonPropertyName("colorSpace")] string ColorSpace = "");
