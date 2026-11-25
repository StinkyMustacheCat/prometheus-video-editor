using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public static class EffectApplicationService
{
    public const string DragFormat = "Prometheus.EffectIds";

    private static bool IsStateEffect(string id) => id is MonochromeEffect.Id or
        ImageGeometryEffect.FlipHorizontalId or ImageGeometryEffect.FlipVerticalId;

    public static string[] ReadDragIds(Avalonia.Input.IDataObject data) =>
        data.Get(DragFormat) is string[] ids ? ids : Array.Empty<string>();

    public static bool CanApply(Project project, string? clipId, IEnumerable<string> effectIds)
    {
        var controller = ProjectController.Instance;
        var nestedRoot = clipId == controller?.SelectedClipId ? controller?.SelectedNestedRootId : null;
        return effectIds.Any(id => EffectCatalog.Available.Any(effect => effect.Id == id &&
            ResolveTarget(project, clipId, effect.Kind, nestedRoot) != null));
    }

    public static int Apply(ProjectController controller, string? clipId, IEnumerable<string> requestedIds)
    {
        var project = controller.CurrentProject;
        var nestedRoot = clipId == controller.SelectedClipId ? controller.SelectedNestedRootId : null;
        var requested = requestedIds.Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Entry: EffectCatalog.Available.FirstOrDefault(effect => effect.Id == id)))
            .Where(item => item.Entry != null)
            .Select(item => (item.Id, Target: ResolveTarget(project, clipId, item.Entry!.Kind,
                nestedRoot)))
            .Where(item => item.Target != null)
            .ToArray();
        var changed = requested.Where(item => IsStateEffect(item.Id) || item.Target!.Effects.All(effect =>
            effect.EffectId != item.Id || !effect.Enabled)).ToArray();
        if (changed.Length == 0) return 0;
        controller.UndoRedo.Commit(project =>
        {
            foreach (var group in changed.GroupBy(item => item.Target!.Id))
            {
                var clip = ProjectClipLookup.Find(project, group.Key, controller.Transport.PositionSeconds,
                    nestedRoot)?.Clip;
                if (clip != null) ApplyToClip(clip, group.Select(item => item.Id));
            }
        }, changed.Length == 1 ? "Aplicar efecto" : "Aplicar efectos");
        controller.ReportStatus(changed.Length == 1 ? "Efecto aplicado" : $"{changed.Length} efectos aplicados");
        return changed.Length;
    }

    public static int ApplyAt(ProjectController controller, string? clipId, string effectId,
        string? afterInstanceId)
    {
        var entry = EffectCatalog.Available.FirstOrDefault(effect => effect.Id == effectId);
        if (entry == null) return 0;
        var nestedRoot = clipId == controller.SelectedClipId ? controller.SelectedNestedRootId : null;
        var target = ResolveTarget(controller.CurrentProject, clipId, entry.Kind, nestedRoot);
        if (target == null || !IsStateEffect(effectId) &&
            target.Effects.Any(effect => effect.EffectId == effectId && effect.Enabled)) return 0;
        var targetId = target.Id;
        var changed = 0;
        controller.UndoRedo.Commit(project =>
        {
            var clip = ProjectClipLookup.Find(project, targetId, controller.Transport.PositionSeconds,
                nestedRoot)?.Clip;
            if (clip != null) changed = ApplyToClipAt(clip, effectId, afterInstanceId);
        }, "Añadir efecto");
        if (changed > 0) controller.ReportStatus("Efecto añadido");
        return changed;
    }

    public static int ApplyToClipAt(Clip clip, string effectId, string? afterInstanceId)
    {
        var previous = clip.Effects.Select(effect => effect.Id).ToHashSet(StringComparer.Ordinal);
        var changed = ApplyToClip(clip, [effectId]);
        var added = clip.Effects.FirstOrDefault(effect => effect.EffectId == effectId && !previous.Contains(effect.Id));
        if (added == null) return changed;
        clip.Effects.Remove(added);
        var anchor = afterInstanceId == null ? -1 : clip.Effects.FindIndex(effect => effect.Id == afterInstanceId);
        clip.Effects.Insert(anchor + 1, added);
        return changed;
    }

    public static bool ResetToDefaults(Clip clip, string instanceId)
    {
        var effect = clip.Effects.FirstOrDefault(item => item.Id == instanceId);
        if (effect == null) return false;
        var defaults = new Clip();
        if (ApplyToClip(defaults, [effect.EffectId]) == 0) return false;
        var initial = defaults.Effects.First(item => item.EffectId == effect.EffectId);
        effect.Parameters = initial.Parameters.ToDictionary(pair => pair.Key, pair => new EffectValue
        {
            Kind = pair.Value.Kind, Number = pair.Value.Number, Text = pair.Value.Text,
            Boolean = pair.Value.Boolean, ResourceId = pair.Value.ResourceId
        });
        effect.Enabled = initial.Enabled;
        KeyframeAnimation.RemoveForEffect(clip, instanceId);
        return true;
    }

    private static Clip? ResolveTarget(Project project, string? selectedClipId, string kind,
        string? nestedRootId)
    {
        var location = ProjectClipLookup.Find(project, selectedClipId,
            ProjectController.Instance?.Transport.PositionSeconds ?? 0, nestedRootId);
        if (location == null) return null;
        Clip? Valid(Clip clip) => location.Siblings.Any(item => item.Id == clip.Id) &&
            (clip.Id == location.Clip.Id ? location.Track :
                location.Tracks.FirstOrDefault(track => track.Id == clip.TrackId)) is
                { Kind: var clipKind, Locked: false } && clipKind == kind &&
            !PlaceholderService.IsPlaceholder(project.Assets.FirstOrDefault(asset => asset.Id == clip.AssetId))
                ? clip : null;
        if (Valid(location.Clip) != null) return location.Clip;
        if (kind != "audio" || location.Track.Kind != "video")
            return null;
        var link = location.LinkGroups.FirstOrDefault(group => group.ClipIds.Contains(location.Clip.Id));
        return link == null ? null : link.ClipIds.Select(id => location.Siblings.FirstOrDefault(clip => clip.Id == id))
            .Where(clip => clip != null).Select(clip => Valid(clip!)).FirstOrDefault(clip => clip != null);
    }

    public static int ApplyToClip(Clip clip, IEnumerable<string> requestedIds)
    {
        var applied = 0;
        foreach (var id in requestedIds.Distinct(StringComparer.Ordinal))
        {
            if (EffectCatalog.Available.All(effect => effect.Id != id)) continue;
            if (IsStateEffect(id))
            {
                var existing = clip.Effects.FirstOrDefault(effect => effect.EffectId == id);
                if (existing != null)
                {
                    existing.Enabled = !existing.Enabled;
                    applied++;
                    continue;
                }
            }
            if (clip.Effects.Any(effect => effect.EffectId == id && effect.Enabled)) continue;
            if (id == BasicColorEffect.Id) BasicColorEffect.Set(clip, true, BasicColorEffect.ReadStored(clip));
            else if (id == ExposureEffect.Id) ExposureEffect.Set(clip, true, ExposureEffect.ReadStored(clip));
            else if (id == LevelsEffect.Id) LevelsEffect.Set(clip, true, LevelsEffect.ReadStored(clip));
            else if (id == MonochromeEffect.Id) MonochromeEffect.Set(clip, true);
            else if (DynamicMotionEffect.Supports(id)) DynamicMotionEffect.Add(clip, id);
            else if (ColorFinishingEffect.Supports(id)) ColorFinishingEffect.Add(clip, id);
            else if (ImageGeometryEffect.Supports(id)) ImageGeometryEffect.Add(clip, id);
            else if (GpuFilterEffect.Supports(id)) GpuFilterEffect.Add(clip, id);
            else if (GpuDetailEffect.Supports(id)) GpuDetailEffect.Add(clip, id);
            else if (id == GpuLutEffect.Id) GpuLutEffect.Add(clip);
            else if (AudioEffects.Find(id) != null) AudioEffects.Add(clip, id);
            applied++;
        }
        return applied;
    }
}
