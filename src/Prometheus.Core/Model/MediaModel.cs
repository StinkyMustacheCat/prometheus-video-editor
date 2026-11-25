using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Model;

public class Locator
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("relative")]
    public bool Relative { get; set; }

    public Locator() { }
    public Locator(string path, bool relative = false)
    {
        Path = path;
        Relative = relative;
    }
}

public class MediaSource
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "local"; // "local", "web", "mgt", "generated"

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("relative")]
    public bool Relative { get; set; }

    [JsonPropertyName("page_url")]
    public string? PageUrl { get; set; }

    [JsonPropertyName("imported_at")]
    public string? ImportedAt { get; set; }
}

public class WebMediaMetadata
{
    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("creator_name")]
    public string? CreatorName { get; set; }

    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }

    [JsonPropertyName("account_handle")]
    public string? AccountHandle { get; set; }

    [JsonPropertyName("account_url")]
    public string? AccountUrl { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }
}

public class MediaRepresentation
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "original"; // "original", "preview", "proxy", "final"

    [JsonPropertyName("locator")]
    public Locator Locator { get; set; } = new();

    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("modified_utc")]
    public string? ModifiedUtc { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("stream_ordinals")]
    public Dictionary<string, int> StreamOrdinals { get; set; } = new();
}

public class StreamInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("ordinal")]
    public int Ordinal { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "video"; // "video", "audio", "image"

    [JsonPropertyName("codec")]
    public string? Codec { get; set; }

    [JsonPropertyName("pts_origin")]
    public Rational PtsOrigin { get; set; } = Rational.Zero;

    [JsonPropertyName("time_base")]
    public Rational TimeBase { get; set; } = new(1, 60);

    [JsonPropertyName("duration")]
    public Rational Duration { get; set; } = Rational.Zero;

    [JsonPropertyName("nominal_fps")]
    public Rational NominalFps { get; set; } = new(60, 1);

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int? Channels { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("pixel_format")]
    public string? PixelFormat { get; set; }
}

public class Asset
{
    [JsonPropertyName("web_preferred_quality")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebPreferredQuality { get; set; }
    [JsonPropertyName("mgt_parameters")]
    public List<MgtParameter> MgtParameters { get; set; } = [];

    [JsonPropertyName("web_metadata")]
    public WebMediaMetadata? WebMetadata { get; set; }

    [JsonPropertyName("web_auto_name")]
    public string? WebAutoName { get; set; }

    [JsonPropertyName("folder_id")]
    public string? FolderId { get; set; }
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label_id")]
    public string? LabelId { get; set; }

    [JsonPropertyName("source")]
    public MediaSource Source { get; set; } = new();

    [JsonPropertyName("text_overlay")]
    public TextOverlay? TextOverlay { get; set; }

    [JsonPropertyName("representations")]
    public List<MediaRepresentation> Representations { get; set; } = new();

    [JsonPropertyName("container_format")]
    public string? ContainerFormat { get; set; }

    [JsonPropertyName("bitrate")]
    public long? Bitrate { get; set; }

    [JsonPropertyName("recommended_duration")]
    public Rational? RecommendedDuration { get; set; }

    [JsonPropertyName("presentation_origin")]
    public Rational PresentationOrigin { get; set; } = Rational.Zero;

    [JsonPropertyName("streams")]
    public List<StreamInfo> Streams { get; set; } = new();

    [JsonIgnore]
    public Locator? EffectiveLocator
    {
        get
        {
            // Legacy projects may retain a baked MOV as their only representation.
            // A live composition must resolve its editable document, never that movie.
            if (Source.Kind == "mgt")
            {
                foreach (var role in new[] { "final", "original", "proxy", "preview" })
                {
                    var document = Representations.Find(r => r.Role == role && MgtLiveAsset.IsDocumentPath(r.Locator.Path));
                    if (document != null) return document.Locator;
                }
                if (MgtLiveAsset.IsDocumentPath(Source.Uri)) return new Locator(Source.Uri, Source.Relative);
            }
            var rep = Representations.Find(r => r.Role == "final")
                   ?? Representations.Find(r => r.Role == "original")
                   ?? Representations.Find(r => r.Role == "proxy")
                   ?? Representations.Find(r => r.Role == "preview");
            return rep?.Locator ?? (string.IsNullOrEmpty(Source.Uri) ? null : new Locator(Source.Uri, Source.Relative));
        }
    }
}
