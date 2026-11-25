using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

public sealed class TranscriptWord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("start")]
    public double Start { get; set; }
    [JsonPropertyName("end")]
    public double End { get; set; }
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>Editable source transcript, independent of rendered subtitle clips.</summary>
public sealed class TranscriptDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Transcripción";
    [JsonPropertyName("language")]
    public string? Language { get; set; }
    [JsonPropertyName("source_clip_id")]
    public string? SourceClipId { get; set; }
    [JsonPropertyName("source_time_origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SourceTimeOrigin { get; set; }

    [JsonPropertyName("source_time_rate")]
    public double SourceTimeRate { get; set; } = 1;

    [JsonPropertyName("timeline_start")]
    public double TimelineStart { get; set; }
    [JsonPropertyName("caption_mode")]
    public string CaptionMode { get; set; } = "phrase"; // phrase | word
    [JsonPropertyName("timing_precision")]
    public string TimingPrecision { get; set; } = "word"; // word | cue
    [JsonPropertyName("words")]
    public List<TranscriptWord> Words { get; set; } = new();

    public void Validate()
    {
        if (Words.Count is < 1 or > 20000 || CaptionMode is not ("phrase" or "word") ||
            TimingPrecision is not ("word" or "cue") ||
            !double.IsFinite(TimelineStart) || TimelineStart < 0 ||
            !double.IsFinite(SourceTimeRate) || SourceTimeRate <= 0)
            throw new ArgumentException("La transcripción no tiene tiempos válidos.");
        var previous = -1d;
        foreach (var word in Words)
        {
            if (!double.IsFinite(word.Start) || !double.IsFinite(word.End) ||
                word.Start < 0 || word.End <= word.Start || word.Start < previous ||
                word.End > 24 * 3600 || string.IsNullOrWhiteSpace(word.Text) || word.Text.Length > 500)
                throw new ArgumentException("Una palabra de la transcripción no es válida.");
            previous = word.Start;
        }
    }
}
