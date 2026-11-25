using System.Linq;
using System.Collections.Generic;
using System;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

/// <summary>The evaluated GPU effects for one clip frame.</summary>
public sealed record GpuEffectProfile(
    BasicColorSettings Color, ExposureSettings Exposure, LevelsSettings Levels,
    bool Monochrome, ColorFinishingSettings Finishing, ImageGeometrySettings Geometry,
    GpuFilterSettings Filter, GpuDetailSettings Detail, string? LutPath, bool LutTetrahedral)
{
    public static GpuEffectProfile Read(Project project, Clip clip, string? projectPath) => new(
        BasicColorEffect.Read(clip), ExposureEffect.Read(clip), LevelsEffect.Read(clip),
        MonochromeEffect.IsEnabled(clip), ColorFinishingEffect.Read(clip),
        ImageGeometryEffect.Read(clip), GpuFilterEffect.Read(clip),
        GpuDetailEffect.Read(clip), GpuLutEffect.ResolvePath(project, clip, projectPath),
        GpuLutEffect.IsTetrahedral(clip));

    public void ApplyTo(GpuVideoSurface gpu)
    {
        gpu.ColorSettings = Color;
        gpu.ExposureSettings = Exposure;
        gpu.LevelsSettings = Levels;
        gpu.MonochromeEnabled = Monochrome;
        gpu.FinishingSettings = Finishing;
        gpu.GeometrySettings = Geometry;
        gpu.FilterSettings = Filter;
        gpu.DetailSettings = Detail;
        gpu.LutPath = LutPath;
        gpu.LutTetrahedral = LutTetrahedral;
    }

    public static void Configure(GpuVideoSurface gpu, Project project, Clip clip,
        double timelineSeconds, double sourceSeconds, string? projectPath, string? proxyPath = null)
    {
        var evaluated = KeyframeAnimation.EvaluateEffects(clip, timelineSeconds);
        Read(project, evaluated, projectPath).ApplyTo(gpu);
        gpu.SamMaskRefinement = new SamMaskRefinement(clip.SamMaskFeatherPixels,
            clip.SamMaskExpandPixels, clip.SamMaskInverted).Clamped();
        gpu.MaskBaseProfile = null;
        gpu.MaskGroups = null;
        gpu.LegacyShapeMask = null;
        if (clip.MaskLayersInitialized)
        {
            ConfigureLayers(gpu, project, clip, evaluated, timelineSeconds,
                sourceSeconds, projectPath, proxyPath);
            return;
        }
        gpu.MaskStack = null;
        var fps = project.ActiveSequence?.Fps.Seconds ?? 60;
        var sourcePath = proxyPath ?? project.Assets.FirstOrDefault(asset => asset.Id == clip.AssetId)
            ?.EffectiveLocator?.Path;
        var track = clip.SamMaskTracks.LastOrDefault();
        var still = StillImageRules.IsStill(project, clip);
        var outdated = !still && track != null &&
            SamMaskTrackValidity.Problem(clip, track, sourcePath, checkSourceIdentity: proxyPath == null) != null;
        gpu.SamMask = clip.SamMaskTarget == SamMaskTarget.Unassigned ? null :
            clip.ShapeMask is { } shape ? ShapeMaskRaster.ForSurface(shape, gpu.Width, gpu.Height) : still
            ? clip.SamMasks.LastOrDefault() ?? (track == null ? null :
                SamMaskTrackStore.At(clip, track.StartSourceSeconds, fps, projectPath))
            : outdated ? SamMaskKeyframe.At(clip, sourceSeconds, fps)
            : SamMaskTrackStore.At(clip, sourceSeconds, fps, projectPath);
        gpu.LegacyShapeMask = gpu.SamMask == null ? null : clip.ShapeMask;
        var target = SamMaskTarget.SelectedEffect(clip);
        if (target == null) return;
        var withoutTarget = new Clip { Effects = evaluated.Effects
            .Where(effect => effect.Id != target.Id).ToList() };
        gpu.MaskBaseProfile = Read(project, withoutTarget, projectPath);
    }

    private static void ConfigureLayers(GpuVideoSurface gpu, Project project, Clip clip,
        Clip evaluated, double timelineSeconds, double sourceSeconds, string? projectPath, string? proxyPath = null)
    {
        gpu.SamMask = null;
        gpu.MaskStack = null;
        gpu.SamMaskRefinement = SamMaskRefinement.Neutral;
        gpu.MaskGroups = ResolveMaskGroups(project, clip, evaluated, timelineSeconds,
            sourceSeconds, projectPath, proxyPath);
    }

    internal static IReadOnlyDictionary<int, IReadOnlyList<GpuMaskLayerFrame>> ResolveMaskGroups(
        Project project, Clip clip, Clip evaluated, double timelineSeconds, double sourceSeconds,
        string? projectPath, string? proxyPath = null)
    {
        var active = ClipMaskStack.Read(clip).Where(layer => layer.Enabled &&
            layer.Target != SamMaskTarget.Unassigned).ToArray();
        var fps = project.ActiveSequence?.Fps.Seconds ?? 60;
        var still = StillImageRules.IsStill(project, clip);
        var sourcePath = proxyPath ?? project.Assets.FirstOrDefault(asset => asset.Id == clip.AssetId)
            ?.EffectiveLocator?.Path;
        var groups = new Dictionary<int, List<GpuMaskLayerFrame>>();
        foreach (var layer in active)
        {
            var stage = layer.Target == SamMaskTarget.Opacity ? GpuMaskStages.Opacity : -1;
            if (stage < 0)
            {
                var target = new Clip { SamMaskTarget = layer.Target,
                    Effects = evaluated.Effects };
                var effect = SamMaskTarget.SelectedEffect(target);
                if (effect != null) stage = GpuMaskStages.ForEffect(effect.EffectId);
            }
            if (stage < 0) continue;
            SamMaskKeyframe? mask = null;
            ShapeMask? shape = null;
            if (layer.Kind == "shape")
                shape = ShapeMaskAnimation.Evaluate(layer, still
                    ? clip.SourceIn.Seconds + Math.Max(0,
                        timelineSeconds - clip.TimelineStart.Seconds) * clip.Speed.Seconds
                    : sourceSeconds, fps);
            else
            {
                var proxy = new Clip { SamMasks = layer.SamMasks,
                    SamMaskTracks = layer.SamMaskTracks };
                var track = layer.SamMaskTracks.LastOrDefault();
                var outdated = !still && track != null &&
                    SamMaskTrackValidity.Problem(clip, track, sourcePath, checkSourceIdentity: proxyPath == null) != null;
                mask = still ? layer.SamMasks.LastOrDefault() ??
                    (track == null ? null : SamMaskTrackStore.At(proxy,
                        track.StartSourceSeconds, fps, projectPath))
                    : outdated ? SamMaskKeyframe.At(proxy, sourceSeconds, fps)
                    : SamMaskTrackStore.At(proxy, sourceSeconds, fps, projectPath);
            }
            if (mask != null || shape != null)
            {
                if (!groups.TryGetValue(stage, out var frames))
                    groups[stage] = frames = new List<GpuMaskLayerFrame>();
                frames.Add(new GpuMaskLayerFrame(layer.Id, mask,
                    new SamMaskRefinement(layer.FeatherPixels, layer.ExpandPixels,
                        layer.Inverted).Clamped(), layer.Blend, shape));
            }
        }
        return groups.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<GpuMaskLayerFrame>)pair.Value);
    }
}
