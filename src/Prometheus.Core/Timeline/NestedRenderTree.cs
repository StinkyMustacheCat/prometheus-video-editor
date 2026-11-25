using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public sealed record NestedRenderNode(Clip Clip, double LocalTimelineSeconds,
    NestedRenderLayer? Leaf, IReadOnlyList<NestedRenderNode> Children, VideoTransitionFrame? Transition = null,
    string? BlendModeOverride = null)
{
    public bool IsGroup => Leaf == null;
}

/// <summary>Reconstructs clip groups from the flattened, ordered render plan.</summary>
public static class NestedRenderTree
{
    public static IReadOnlyList<NestedRenderNode> Build(IReadOnlyList<NestedRenderLayer> layers, Sequence? sequence = null) =>
        BuildAtDepth(layers, 0, sequence?.Tracks);

    private static IReadOnlyList<NestedRenderNode> BuildAtDepth(IReadOnlyList<NestedRenderLayer> layers, int depth,
        IReadOnlyList<Track>? tracks)
    {
        if (tracks?.Any(HasTrackEffects) != true)
            return depth == 0 ? WrapTransitions(BuildClips(layers, depth), layers) : BuildClips(layers, depth);
        var result = new List<NestedRenderNode>();
        foreach (var lane in layers.GroupBy(layer => TrackIdAtDepth(layer, depth)))
        {
            var laneLayers = lane.ToArray();
            var children = BuildClips(laneLayers, depth);
            if (depth == 0) children = WrapTransitions(children, laneLayers);
            var track = tracks.FirstOrDefault(item => item.Id == lane.Key);
            if (track == null || !HasTrackEffects(track)) result.AddRange(children);
            else
            {
                var mode = "normal";
                if (children.Count == 1)
                {
                    var only = children[0];
                    var modes = only.Transition == null ? new[] { only.BlendModeOverride ?? only.Clip.Visual.BlendMode }
                        : only.Children.Select(c => c.BlendModeOverride ?? c.Clip.Visual.BlendMode).Distinct().ToArray();
                    if (modes.Length == 1) mode = modes[0];
                }
                // A lone clip's fusion follows its track effects, against the lower tracks.
                // Keep geometry/opacity inside the group and apply the blend only once.
                if (ClipBlendModes.GpuId(mode) != 0) children = new[] { children[0] with { BlendModeOverride = "normal" } };
                result.Add(new NestedRenderNode(new Clip { Id = "track-fx:" + track.Id, Effects = track.Effects,
                    Visual = new ClipVisualState { BlendMode = mode } }, children[0].LocalTimelineSeconds, null, children));
            }
        }
        return result;
    }

    private static IReadOnlyList<NestedRenderNode> WrapTransitions(IReadOnlyList<NestedRenderNode> nodes,
        IReadOnlyList<NestedRenderLayer> layers)
    {
        var frames = layers.Select(layer => layer.Transition).OfType<VideoTransitionFrame>().Distinct().ToArray();
        if (frames.Length == 0) return nodes;
        var result = new List<NestedRenderNode>();
        var added = new HashSet<string>();
        foreach (var node in nodes)
        {
            var frame = frames.FirstOrDefault(item => node.Clip.Id == item.Window.Left.Id || node.Clip.Id == item.Window.Right.Id);
            if (frame == null) { result.Add(node); continue; }
            if (!added.Add(frame.Window.Transition.Id)) continue;
            var children = nodes.Where(child => child.Clip.Id == frame.Window.Left.Id ||
                child.Clip.Id == frame.Window.Right.Id).ToArray();
            result.Add(new NestedRenderNode(new Clip { Id = "transition:" + frame.Window.Transition.Id },
                frame.Time, null, children, frame));
        }
        return result;
    }

    private static string TrackIdAtDepth(NestedRenderLayer layer, int depth) =>
        depth < layer.Containers.Count ? layer.Containers[depth].TrackId : layer.Clip.TrackId;

    public static bool HasTrackEffects(Track track) =>
        track.Kind == "video" && track.Effects.Any(effect => effect.Enabled &&
            AudioEffects.Find(effect.EffectId) == null && !DynamicMotionEffect.Supports(effect.EffectId));

    private static IReadOnlyList<NestedRenderNode> BuildClips(IReadOnlyList<NestedRenderLayer> layers, int depth)
    {
        var nodes = new List<NestedRenderNode>();
        foreach (var group in layers.GroupBy(layer => depth < layer.Containers.Count
                     ? layer.Containers[depth].Id : layer.Clip.Id))
        {
            var first = group.First();
            if (depth >= first.Containers.Count)
                nodes.Add(new NestedRenderNode(first.Clip, first.LocalTimelineSeconds, first, Array.Empty<NestedRenderNode>()));
            else
                nodes.Add(new NestedRenderNode(first.Containers[depth], first.ContainerTimelineSeconds[depth],
                    null, BuildAtDepth(group.ToArray(), depth + 1, first.Containers[depth].NestedContent?.Tracks)));
        }
        return nodes;
    }

    public static bool NeedsGroupComposition(IReadOnlyList<NestedRenderLayer> layers, Sequence? sequence = null) =>
        layers.Any(layer => ClipBlendModes.GpuId(layer.Clip.Visual.BlendMode) != 0 ||
            layer.Containers.Any(c => ClipBlendModes.GpuId(c.Visual.BlendMode) != 0)) ||
        layers.Any(layer => layer.Transition != null) ||
        (sequence?.Tracks.Any(track => HasTrackEffects(track) &&
            layers.Any(layer => TrackIdAtDepth(layer, 0) == track.Id)) ?? false) ||
        layers.Any(layer => layer.Containers.Select((container, index) => (container, index))
            .Any(item => item.container.NestedContent?.Tracks.Any(track => HasTrackEffects(track) &&
                TrackIdAtDepth(layer, item.index + 1) == track.Id) == true)) ||
        layers.SelectMany(layer => layer.Containers).DistinctBy(clip => clip.Id).Any(clip =>
            clip.Visual.Framing != "fit" || clip.Visual.PositionX != 0 || clip.Visual.PositionY != 0 ||
            clip.Visual.ScaleX != 1 || clip.Visual.ScaleY != 1 || clip.Visual.Opacity != 1 ||
            clip.Visual.RotationDegrees != 0 || clip.Visual.FlipX || clip.Visual.FlipY ||
            clip.Visual.AnchorX != 0.5 || clip.Visual.AnchorY != 0.5 ||
            clip.KeyframeTracks.Count > 0 ||
            ClipMaskStack.Read(clip).Any(mask => mask.Enabled && mask.Target != SamMaskTarget.Unassigned) ||
            clip.Effects.Any(effect => effect.Enabled));
}
