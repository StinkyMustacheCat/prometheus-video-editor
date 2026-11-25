using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

/// <summary>One independently editable source mask on a clip.</summary>
public sealed class ClipMaskLayer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Máscara";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "shape"; // shape | sam

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("blend")]
    public string Blend { get; set; } = "add"; // add | subtract | intersect

    [JsonPropertyName("target")]
    public string Target { get; set; } = SamMaskTarget.Opacity;

    [JsonPropertyName("feather_pixels")]
    public double FeatherPixels { get; set; }

    [JsonPropertyName("expand_pixels")]
    public double ExpandPixels { get; set; }

    [JsonPropertyName("inverted")]
    public bool Inverted { get; set; }

    [JsonPropertyName("shape")]
    public ShapeMask? Shape { get; set; }

    [JsonPropertyName("shape_keyframes")]
    public List<ShapeMaskKeyframe> ShapeKeyframes { get; set; } = new();

    [JsonPropertyName("sam_masks")]
    public List<SamMaskKeyframe> SamMasks { get; set; } = new();

    [JsonPropertyName("sam_mask_tracks")]
    public List<SamMaskTrack> SamMaskTracks { get; set; } = new();
}

public static class ClipMaskStack
{
    /// <summary>Existing 2.5 projects remain readable until an edit materializes the list.</summary>
    public static IReadOnlyList<ClipMaskLayer> Read(Clip clip)
    {
        if (clip.MaskLayersInitialized) return clip.MaskLayers;
        var legacy = new List<ClipMaskLayer>();
        if (clip.ShapeMask is { } shape)
            legacy.Add(new ClipMaskLayer { Id = shape.Id, Name = "Forma 1",
                Shape = shape, Target = clip.SamMaskTarget,
                FeatherPixels = clip.SamMaskFeatherPixels,
                ExpandPixels = clip.SamMaskExpandPixels,
                Inverted = clip.SamMaskInverted });
        if (clip.SamMasks.Count > 0 || clip.SamMaskTracks.Count > 0)
            legacy.Add(new ClipMaskLayer {
                Id = clip.SamMaskTracks.LastOrDefault()?.Id ??
                    clip.SamMasks.LastOrDefault()?.Id ?? Guid.NewGuid().ToString("N"),
                Name = "SAM 1", Kind = "sam", Target = clip.SamMaskTarget,
                FeatherPixels = clip.SamMaskFeatherPixels,
                ExpandPixels = clip.SamMaskExpandPixels, Inverted = clip.SamMaskInverted,
                SamMasks = clip.SamMasks.ToList(), SamMaskTracks = clip.SamMaskTracks.ToList()
            });
        return legacy;
    }

    public static List<ClipMaskLayer> Edit(Clip clip)
    {
        if (!clip.MaskLayersInitialized) clip.MaskLayers = Read(clip).ToList();
        clip.MaskLayersInitialized = true;
        return clip.MaskLayers;
    }
}
