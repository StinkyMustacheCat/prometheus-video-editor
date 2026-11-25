using System;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

/// <summary>Reference to an immutable, indexed sequence of tracked SAM masks.</summary>
public sealed class SamMaskTrack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("start_source_seconds")]
    public double StartSourceSeconds { get; set; }

    [JsonPropertyName("frames_per_second")]
    public double FramesPerSecond { get; set; }

    [JsonPropertyName("frame_count")]
    public int FrameCount { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("source_file_length")]
    public long SourceFileLength { get; set; }

    [JsonPropertyName("source_file_write_utc_ticks")]
    public long SourceFileWriteUtcTicks { get; set; }

    public int? FrameAt(double sourceSeconds)
    {
        if (!double.IsFinite(sourceSeconds) || !double.IsFinite(StartSourceSeconds) ||
            !double.IsFinite(FramesPerSecond) || FramesPerSecond <= 0 || FrameCount <= 0)
            return null;
        var position = (sourceSeconds - StartSourceSeconds) * FramesPerSecond;
        if (position < -0.000001 || position >= FrameCount) return null;
        var frame = (int)Math.Floor(position + 0.000001);
        return frame < FrameCount ? frame : null;
    }
}
