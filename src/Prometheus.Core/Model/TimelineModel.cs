using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Model;

public class EffectValue
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "number"; // "number", "text", "boolean", "resource"

    [JsonPropertyName("number")]
    public double? Number { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("boolean")]
    public bool? Boolean { get; set; }

    [JsonPropertyName("resource_id")]
    public string? ResourceId { get; set; }
}

public class EffectInstance
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("effect_id")]
    public string EffectId { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("parameters")]
    public Dictionary<string, EffectValue> Parameters { get; set; } = new();
}

public class Track
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "V1";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "video"; // "video", "audio"

    [JsonPropertyName("role")]
    public string Role { get; set; } = "normal"; // "normal", "subtitle"

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("presentation_height")]
    public int PresentationHeight { get; set; } = 44;

    [JsonPropertyName("compact")]
    public bool Compact { get; set; }

    [JsonPropertyName("show_names")]
    public bool ShowNames { get; set; } = true;

    [JsonPropertyName("solo")]
    public bool Solo { get; set; }

    [JsonPropertyName("gain_db")]
    public double GainDb { get; set; } = 0.0;

    [JsonPropertyName("balance")]
    public double Balance { get; set; } = 0.0;

    [JsonPropertyName("effects")]
    public List<EffectInstance> Effects { get; set; } = new();

}

public class Clip
{
    internal Clip ShallowCopy() => (Clip)MemberwiseClone();

    [JsonPropertyName("audio_split_ancestors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AudioSplitAncestors { get; set; }

    [JsonPropertyName("subtitle_audio_clip_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubtitleAudioClipId { get; set; }

    [JsonPropertyName("mgt_overrides")]
    public Dictionary<string, JsonElement> MgtOverrides { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("transcript_id")]
    public string? TranscriptId { get; set; }
    [JsonPropertyName("caption_words")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CaptionWord>? CaptionWords { get; set; }
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label_id")]
    public string? LabelId { get; set; }

    [JsonPropertyName("track_id")]
    public string TrackId { get; set; } = string.Empty;

    [JsonPropertyName("asset_id")]
    public string AssetId { get; set; } = string.Empty;

    [JsonPropertyName("stream_id")]
    public string StreamId { get; set; } = string.Empty;

    [JsonPropertyName("timeline_start")]
    public Rational TimelineStart { get; set; } = Rational.Zero;

    [JsonPropertyName("duration")]
    public Rational Duration { get; set; } = Rational.FromFrames(60, 60);

    [JsonPropertyName("source_in")]
    public Rational SourceIn { get; set; } = Rational.Zero;

    [JsonPropertyName("speed")]
    public Rational Speed { get; set; } = Rational.One;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("gain_db")]
    public double GainDb { get; set; } = 0.0;

    [JsonPropertyName("balance")]
    public double Balance { get; set; } = 0.0;

    [JsonPropertyName("visual")]
    public ClipVisualState Visual { get; set; } = new();

    [JsonPropertyName("effects")]
    public List<EffectInstance> Effects { get; set; } = new();

    [JsonPropertyName("keyframe_tracks")]
    public List<KeyframeTrack> KeyframeTracks { get; set; } = new();

    [JsonPropertyName("sam_masks")]
    public List<SamMaskKeyframe> SamMasks { get; set; } = new();

    [JsonPropertyName("shape_mask")]
    public ShapeMask? ShapeMask { get; set; }

    [JsonPropertyName("mask_layers")]
    public List<ClipMaskLayer> MaskLayers { get; set; } = new();

    [JsonPropertyName("mask_layers_initialized")]
    public bool MaskLayersInitialized { get; set; }

    [JsonPropertyName("sam_mask_tracks")]
    public List<SamMaskTrack> SamMaskTracks { get; set; } = new();

    [JsonPropertyName("sam_mask_target")]
    public string SamMaskTarget { get; set; } = "opacity";

    [JsonPropertyName("sam_mask_feather_pixels")]
    public double SamMaskFeatherPixels { get; set; }

    [JsonPropertyName("sam_mask_expand_pixels")]
    public double SamMaskExpandPixels { get; set; }

    [JsonPropertyName("sam_mask_inverted")]
    public bool SamMaskInverted { get; set; }

    [JsonPropertyName("nested_content")]
    public NestedContent? NestedContent { get; set; }

    [JsonPropertyName("nested_role")]
    public string? NestedRole { get; set; }

    [JsonPropertyName("available_source_duration_seconds")]
    public double? AvailableSourceDurationSeconds { get; set; }

    [JsonPropertyName("web_auto_duration_seconds")]
    public double? WebAutoDurationSeconds { get; set; }

    [JsonPropertyName("web_requested_out_seconds")]
    public double? WebRequestedOutSeconds { get; set; }

    [JsonIgnore]
    public double EndSeconds => TimelineStart.Seconds + Duration.Seconds;
}

public class ClipVisualState
{
    [JsonPropertyName("blend_mode")]
    public string BlendMode { get; set; } = "normal";
    [JsonPropertyName("framing")]
    public string Framing { get; set; } = "fit";
    [JsonPropertyName("position_x")]
    public double PositionX { get; set; }
    [JsonPropertyName("position_y")]
    public double PositionY { get; set; }
    [JsonPropertyName("scale_x")]
    public double ScaleX { get; set; } = 1;
    [JsonPropertyName("scale_y")]
    public double ScaleY { get; set; } = 1;
    [JsonPropertyName("scale_linked")]
    public bool ScaleLinked { get; set; } = true;
    [JsonPropertyName("flip_x")]
    public bool FlipX { get; set; }
    [JsonPropertyName("flip_y")]
    public bool FlipY { get; set; }
    [JsonPropertyName("anchor_x")]
    public double AnchorX { get; set; } = 0.5;
    [JsonPropertyName("anchor_y")]
    public double AnchorY { get; set; } = 0.5;
    [JsonPropertyName("rotation_degrees")]
    public double RotationDegrees { get; set; }
    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;
}

public class NestedContent
{
    [JsonPropertyName("audio_transitions")]
    public List<AudioTransition> AudioTransitions { get; set; } = new();

    [JsonPropertyName("tracks")]
    public List<Track> Tracks { get; set; } = new();

    [JsonPropertyName("clips")]
    public List<Clip> Clips { get; set; } = new();

    [JsonPropertyName("link_groups")]
    public List<LinkGroup> LinkGroups { get; set; } = new();

    [JsonPropertyName("edit_groups")]
    public List<EditGroup> EditGroups { get; set; } = new();
}

public class LinkGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("clip_ids")]
    public List<string> ClipIds { get; set; } = new();
}

public class EditGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("number")]
    public int Number { get; set; } = 1;

    [JsonPropertyName("clip_ids")]
    public List<string> ClipIds { get; set; } = new();
}

public class SequenceMarker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Marcador";

    [JsonPropertyName("time")]
    public Rational Time { get; set; } = Rational.Zero;

    [JsonPropertyName("color_argb")]
    public uint ColorArgb { get; set; } = 0xFFFFB95F;
}

public class Sequence
{
    [JsonPropertyName("subtitle_animation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubtitleAnimationSettings? SubtitleAnimation { get; set; }
    [JsonPropertyName("transcriptions")]
    public List<TranscriptDocument> Transcriptions { get; set; } = new();
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("master_gain_db")]
    public double MasterGainDb { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Montaje 1";

    [JsonPropertyName("video_area_height")]
    public double? VideoAreaHeight { get; set; }

    [JsonPropertyName("subtitle_lane_expanded")]
    public bool SubtitleLaneExpanded { get; set; }

    [JsonPropertyName("close_gap_all_tracks")]
    public bool CloseGapAllTracks { get; set; }

    [JsonPropertyName("view_playhead_seconds")]
    public double ViewPlayheadSeconds { get; set; }

    [JsonPropertyName("view_offset_seconds")]
    public double ViewOffsetSeconds { get; set; }

    [JsonPropertyName("view_zoom_pixels_per_second")]
    public double ViewZoomPixelsPerSecond { get; set; } = 40;

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1920;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1080;

    [JsonPropertyName("fps")]
    public Rational Fps { get; set; } = new(60, 1);

    [JsonPropertyName("color_space")]
    public string ColorSpace { get; set; } = "rec709-sdr";

    [JsonPropertyName("mix_sample_rate")]
    public int MixSampleRate { get; set; } = 48000;

    [JsonPropertyName("mix_channels")]
    public int MixChannels { get; set; } = 2;

    [JsonPropertyName("tracks")]
    public List<Track> Tracks { get; set; } = new();

    [JsonPropertyName("clips")]
    public List<Clip> Clips { get; set; } = new();

    [JsonPropertyName("link_groups")]
    public List<LinkGroup> LinkGroups { get; set; } = new();

    [JsonPropertyName("edit_groups")]
    public List<EditGroup> EditGroups { get; set; } = new();

    [JsonPropertyName("audio_transitions")]
    public List<AudioTransition> AudioTransitions { get; set; } = new();

    [JsonPropertyName("video_transitions")]
    public List<VideoTransition> VideoTransitions { get; set; } = new();

    [JsonPropertyName("markers")]
    public List<SequenceMarker> Markers { get; set; } = new();

    [JsonPropertyName("mark_in_seconds")]
    public double? MarkInSeconds { get; set; }

    [JsonPropertyName("mark_out_seconds")]
    public double? MarkOutSeconds { get; set; }

    [JsonIgnore]
    public double DurationSeconds => Clips.Count == 0 ? 0 : Clips.Max(c => c.EndSeconds);
}

public class EffectResource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "lut3d";

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("relative")]
    public bool Relative { get; set; }
}

public class Project
{
    [JsonPropertyName("recovery")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectRecoveryInfo? Recovery { get; set; }

    [JsonPropertyName("mgt_transition_library")]
    public List<MgtGpuTransition> MgtTransitionLibrary { get; set; } = new();

    [JsonPropertyName("link_shelf")]
    public List<LinkShelfItem> LinkShelf { get; set; } = new();
    [JsonPropertyName("media_folders")]
    public List<MediaFolder> MediaFolders { get; set; } = new();
    [JsonPropertyName("media_folder_tab_order")]
    public List<string> MediaFolderTabOrder { get; set; } = new();
    [JsonPropertyName("format")]
    public string Format { get; set; } = "prometheus-project";

    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "2.0.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Sin título";

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("active_sequence_id")]
    public string ActiveSequenceId { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<Asset> Assets { get; set; } = new();

    [JsonPropertyName("effect_resources")]
    public List<EffectResource> EffectResources { get; set; } = new();

    [JsonPropertyName("sequences")]
    public List<Sequence> Sequences { get; set; } = new();

    [JsonIgnore]
    public Sequence? ActiveSequence => Sequences.Find(s => s.Id == ActiveSequenceId) ?? Sequences.FirstOrDefault();
}

public class LinkShelfItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "web";

    [JsonPropertyName("added_at")]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("placed_at")]
    public DateTime? PlacedAt { get; set; }

    [JsonPropertyName("in_seconds")]
    public double? InSeconds { get; set; }

    [JsonPropertyName("out_seconds")]
    public double? OutSeconds { get; set; }

    [JsonPropertyName("web_metadata")]
    public WebMediaMetadata? WebMetadata { get; set; }

    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("media_kind")]
    public string? MediaKind { get; set; }

    [JsonPropertyName("thumbnail_path")]
    public string? ThumbnailPath { get; set; }

    [JsonPropertyName("metadata_status")]
    public string? MetadataStatus { get; set; }

    [JsonPropertyName("metadata_error")]
    public string? MetadataError { get; set; }
}
