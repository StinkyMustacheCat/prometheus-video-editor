using System;
using System.Linq;
using System.Text.Json.Serialization;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Model;

/// <summary>Editable text data, independent of its cached transparent image.</summary>
public sealed class TextOverlay
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = "Texto";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "title"; // title | subtitle

    [JsonPropertyName("font_family")]
    public string FontFamily { get; set; } = "Arial";

    [JsonPropertyName("font_size")]
    public double FontSize { get; set; } = 72;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";

    [JsonPropertyName("outline_color")]
    public string OutlineColor { get; set; } = "#090B10";

    [JsonPropertyName("outline_pixels")]
    public double OutlinePixels { get; set; } = 4;

    [JsonPropertyName("shadow_opacity")]
    public double ShadowOpacity { get; set; } = 0.35;

    [JsonPropertyName("alignment")]
    public string Alignment { get; set; } = "center"; // left | center | right

    [JsonPropertyName("position_x")]
    public double PositionX { get; set; } = 0.5;

    [JsonPropertyName("position_y")]
    public double PositionY { get; set; } = 0.5;

    [JsonPropertyName("box_width")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BoxWidth { get; set; }

    [JsonIgnore]
    public double WrapWidth => BoxWidth ?? (Kind == "subtitle" ? 0.86 : 0.88);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Content) || Content.Length > 2000 ||
            Kind is not ("title" or "subtitle") ||
            string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 100 ||
            !double.IsFinite(FontSize) || FontSize is < 8 or > 400 ||
            !double.IsFinite(OutlinePixels) || OutlinePixels is < 0 or > 32 ||
            !double.IsFinite(ShadowOpacity) || ShadowOpacity is < 0 or > 1 ||
            !double.IsFinite(PositionX) || PositionX is < 0 or > 1 ||
            !double.IsFinite(PositionY) || PositionY is < 0 or > 1 ||
            BoxWidth is { } width && (!double.IsFinite(width) || width is < 0.02 or > 1) ||
            Alignment is not ("left" or "center" or "right") ||
            !IsColor(Color) || !IsColor(OutlineColor))
            throw new ArgumentException("El texto o su formato no son válidos.");
    }

    private static bool IsColor(string value) => value.Length == 7 && value[0] == '#' &&
        value.Skip(1).All(Uri.IsHexDigit);
}

public static class GeneratedText
{
    public static Asset CreateAsset(string content, string kind, int width, int height)
    {
        var id = Guid.NewGuid().ToString("N");
        var text = new TextOverlay { Content = content, Kind = kind,
            FontSize = kind == "subtitle" ? 54 : 80,
            PositionY = kind == "subtitle" ? 0.84 : 0.5 };
        text.Validate();
        return new Asset { Id = id, Name = DisplayName(text),
            Source = new MediaSource { Kind = "generated", Uri = "text:" + id },
            TextOverlay = text, RecommendedDuration = Rational.Time(kind == "subtitle" ? 3 : 5),
            Streams = [new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "image",
                Width = width, Height = height, Codec = "prometheus-text" }] };
    }

    public static string DisplayName(TextOverlay text)
    {
        var clean = string.Join(" ", text.Content.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
        return (text.Kind == "subtitle" ? "Subtítulo · " : "Texto · ") +
            (clean.Length > 34 ? clean[..34] + "…" : clean);
    }
}
