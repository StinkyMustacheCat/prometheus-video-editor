using System.Linq;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public class EffectApplicationTests
{
    [Fact]
    public void AppliesMultipleEffectsOnceAndRejectsUnknownIds()
    {
        var clip = new Clip();
        var ids = new[] { BasicColorEffect.Id, ExposureEffect.Id, BasicColorEffect.Id, "unknown" };
        Assert.Equal(2, EffectApplicationService.ApplyToClip(clip, ids));
        Assert.Equal(2, clip.Effects.Count);
        Assert.All(clip.Effects, effect => Assert.True(effect.Enabled));
        Assert.Equal(0, EffectApplicationService.ApplyToClip(clip, ids));
    }

    [Fact]
    public void ReactivatesExistingEffectAndKeepsItsValues()
    {
        var clip = new Clip();
        BasicColorEffect.Set(clip, false, new BasicColorSettings(0.25, 1.5, 0.7));
        // Set(false) on an absent effect intentionally does nothing.
        BasicColorEffect.Set(clip, true, new BasicColorSettings(0.25, 1.5, 0.7));
        BasicColorEffect.Set(clip, false, BasicColorEffect.ReadStored(clip));
        Assert.Equal(1, EffectApplicationService.ApplyToClip(clip, new[] { BasicColorEffect.Id }));
        Assert.Single(clip.Effects);
        Assert.True(BasicColorEffect.Find(clip)!.Enabled);
        Assert.Equal(0.25, BasicColorEffect.ReadStored(clip).Brightness);
    }

    [Fact]
    public void ReapplyingStateEffectTogglesWithoutDuplicating()
    {
        var clip = new Clip();
        Assert.Equal(1, EffectApplicationService.ApplyToClip(clip, [ImageGeometryEffect.FlipHorizontalId]));
        Assert.True(clip.Effects.Single().Enabled);
        Assert.Equal(1, EffectApplicationService.ApplyToClip(clip, [ImageGeometryEffect.FlipHorizontalId]));
        Assert.False(clip.Effects.Single().Enabled);
        Assert.Equal(1, EffectApplicationService.ApplyToClip(clip, [ImageGeometryEffect.FlipHorizontalId]));
        Assert.True(clip.Effects.Single().Enabled);
    }

    [Fact]
    public void QuickAddInsertsAfterChosenEffect()
    {
        var clip = new Clip();
        EffectApplicationService.ApplyToClip(clip, [ExposureEffect.Id, LevelsEffect.Id]);
        var anchor = clip.Effects[0].Id;
        Assert.Equal(1, EffectApplicationService.ApplyToClipAt(clip, BasicColorEffect.Id, anchor));
        Assert.Equal(new[] { ExposureEffect.Id, BasicColorEffect.Id, LevelsEffect.Id },
            clip.Effects.Select(effect => effect.EffectId));
    }

    [Fact]
    public void ResetWholeEffectRemovesOnlyItsAnimation()
    {
        var clip = new Clip();
        EffectApplicationService.ApplyToClip(clip, [BasicColorEffect.Id, ExposureEffect.Id]);
        var basic = clip.Effects.First(effect => effect.EffectId == BasicColorEffect.Id);
        var exposure = clip.Effects.First(effect => effect.EffectId == ExposureEffect.Id);
        ImageGeometryEffect.Number(basic, "brightness", 0.7);
        KeyframeAnimation.Set(clip, KeyframeAnimation.EffectTarget(basic, "brightness"), 0, 0.7);
        KeyframeAnimation.Set(clip, KeyframeAnimation.EffectTarget(exposure, "stops"), 0, 1);
        Assert.True(EffectApplicationService.ResetToDefaults(clip, basic.Id));
        Assert.Equal(0, BasicColorEffect.ReadStored(clip).Brightness);
        Assert.Null(KeyframeAnimation.Find(clip, KeyframeAnimation.EffectTarget(basic, "brightness")));
        Assert.NotNull(KeyframeAnimation.Find(clip, KeyframeAnimation.EffectTarget(exposure, "stops")));
    }

    [Fact]
    public void BlurAndChromaAreAvailableFromTheEffectLibrary()
    {
        Assert.Contains(EffectCatalog.Available, effect => effect.Id == GpuFilterEffect.BlurId);
        Assert.Contains(EffectCatalog.Available, effect => effect.Id == GpuFilterEffect.ChromaId);
        var clip = new Clip();
        Assert.Equal(2, EffectApplicationService.ApplyToClip(clip,
            [GpuFilterEffect.BlurId, GpuFilterEffect.ChromaId]));
        Assert.True(GpuFilterEffect.Read(clip).ChromaEnabled);
        Assert.Equal(6, GpuFilterEffect.Read(clip).BlurRadius);
    }

    [Fact]
    public void AudioEffectCanTargetTheAudioLinkedToSelectedVideo()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var videoTrack = sequence.Tracks.First(t => t.Kind == "video");
        var audioTrack = sequence.Tracks.First(t => t.Kind == "audio");
        var video = new Clip { TrackId = videoTrack.Id };
        var audio = new Clip { TrackId = audioTrack.Id };
        sequence.Clips.Add(video);
        sequence.Clips.Add(audio);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = [video.Id, audio.Id] });
        Assert.True(EffectApplicationService.CanApply(project, video.Id, [AudioEffects.HighPassId]));
        audioTrack.Locked = true;
        Assert.False(EffectApplicationService.CanApply(project, video.Id, [AudioEffects.HighPassId]));
    }

    [Fact]
    public void CompleteLegacyAudioCatalogIsAvailable()
    {
        Assert.Equal(12, AudioEffects.All.Count);
        var libraryIds = EffectCatalog.Available.Where(effect => effect.Kind == "audio")
            .Select(effect => effect.Id).ToHashSet();
        Assert.True(AudioEffects.All.All(definition => libraryIds.Contains(definition.Id)));
    }
}
