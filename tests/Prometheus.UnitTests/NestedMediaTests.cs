using System;
using System.IO;
using System.Linq;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Preflight;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;
using Xunit;

namespace Prometheus.UnitTests;

public class NestedMediaTests
{
    private static Project ProjectWithMedia(string? audioPath = null)
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var video = new Asset { Id = "video-asset", Name = "Vídeo", Source = new MediaSource { Kind = "local", Uri = "video.mp4" } };
        video.Streams.Add(new StreamInfo { Id = "video-stream", Kind = "video", Duration = Rational.Time(8) });
        var audio = new Asset { Id = "audio-asset", Name = "Audio", Source = new MediaSource { Kind = "local", Uri = audioPath ?? "audio.wav" } };
        audio.Streams.Add(new StreamInfo { Id = "audio-stream", Kind = "audio", Duration = Rational.Time(8) });
        project.Assets.Add(video);
        project.Assets.Add(audio);
        seq.Clips.Add(new Clip { Id = "video", Name = "Vídeo", AssetId = video.Id, StreamId = "video-stream",
            TrackId = seq.Tracks.First(t => t.Kind == "video").Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(4),
            Effects = { new EffectInstance { EffectId = "child-color" } } });
        seq.Clips.Add(new Clip { Id = "audio", Name = "Audio", AssetId = audio.Id, StreamId = "audio-stream",
            TrackId = seq.Tracks.First(t => t.Kind == "audio").Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(4) });
        seq.LinkGroups.Add(new LinkGroup { ClipIds = { "video", "audio" } });
        return project;
    }

    [Fact]
    public void UnnestingAClipWhoseOriginalEdgesWereTrimmedDropsThoseFades()
    {
        var manager = new UndoRedoManager(ProjectWithMedia());
        string outerId = "";
        manager.Commit(p => {
            p.ActiveSequence!.AudioTransitions.AddRange([
                new AudioTransition { Kind = AudioTransitionOperations.FadeIn, ClipId = "audio", DurationSeconds = .2 },
                new AudioTransition { Kind = AudioTransitionOperations.FadeOut, ClipId = "audio", DurationSeconds = .2 }
            ]);
            outerId = NestedMediaOperations.Create(p, ["video"]);
        }, "Nest audio fades");
        manager.Commit(p => {
            var outer = p.ActiveSequence!.Clips.Single(c => c.Id == outerId);
            var audio = NestedMediaOperations.Companion(p.ActiveSequence, outer)!;
            outer.SourceIn = audio.SourceIn = Rational.One;
            outer.Duration = audio.Duration = Rational.Time(2);
        }, "Trim container");
        manager.Commit(p => NestedMediaOperations.Unnest(p, outerId), "Unnest");
        Assert.Empty(manager.CurrentProject.ActiveSequence!.AudioTransitions);
    }

    [Fact]
    public void UnnestingBothSplitHalvesRemapsEffectsAndKeepsAnimatedSourceTime()
    {
        var manager = new UndoRedoManager(ProjectWithMedia());
        string outerId = "";
        manager.Commit(p => {
            outerId = NestedMediaOperations.Create(p, ["video"]);
            var child = p.ActiveSequence!.Clips.Single(c => c.Id == outerId).NestedContent!.Clips.Single(c => c.Id == "video");
            child.MaskLayersInitialized = true;
            child.MaskLayers = [new ClipMaskLayer { Target = "effect:" + child.Effects[0].Id }];
            KeyframeAnimation.Set(child, "visual.opacity", 0, 0);
            KeyframeAnimation.Set(child, "visual.opacity", 4, 1);
        }, "Nest");
        manager.Commit(p => CollisionEngine.SplitLinkedClips(p.ActiveSequence!, outerId, 3), "Split");
        var rightId = manager.CurrentProject.ActiveSequence!.Clips.Single(c => c.NestedRole == "video" && c.Id != outerId).Id;
        manager.Commit(p => NestedMediaOperations.Unnest(p, outerId), "Unnest left");
        manager.Commit(p => NestedMediaOperations.Unnest(p, rightId), "Unnest right");
        var videos = manager.CurrentProject.ActiveSequence!.Clips.Where(c => c.Name == "Vídeo").OrderBy(c => c.TimelineStart).ToArray();
        Assert.Equal(2, videos.Length);
        Assert.Equal(2, videos.SelectMany(c => c.Effects).Select(e => e.Id).Distinct().Count());
        Assert.All(videos, c => Assert.Equal("effect:" + c.Effects[0].Id, c.MaskLayers[0].Target));
        Assert.Equal(.625, KeyframeAnimation.Evaluate(videos[1], "visual.opacity", 3.5, 0), 6);
        Assert.True(manager.Undo());
        Assert.Contains(manager.CurrentProject.ActiveSequence!.Clips, c => c.Id == rightId && c.NestedContent != null);
        Assert.True(manager.Redo());
        Assert.True(PreflightAuditor.Audit(manager.CurrentProject).IsValid);
    }

    [Fact]
    public void UnnestingTrimmedSpeededContainerPreservesAnimation()
    {
        var project = ProjectWithMedia();
        var outerId = NestedMediaOperations.Create(project, ["video"]);
        var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
        var child = outer.NestedContent!.Clips.Single(c => c.Id == "video");
        KeyframeAnimation.Set(child, "visual.opacity", 0, 0);
        KeyframeAnimation.Set(child, "visual.opacity", 4, 1);
        var audio = NestedMediaOperations.Companion(project.ActiveSequence!, outer)!;
        outer.SourceIn = audio.SourceIn = Rational.One;
        outer.Speed = audio.Speed = new Rational(2, 1);
        outer.Duration = audio.Duration = Rational.One;
        NestedMediaOperations.Unnest(project, outerId);
        var video = project.ActiveSequence.Clips.Single(c => c.Name == "Vídeo");
        Assert.Equal(2, video.Speed.Seconds);
        Assert.Equal(.375, KeyframeAnimation.Evaluate(video, "visual.opacity", 1.25, 0), 6);
    }

    [Fact]
    public void NestAndUnnest_KeepChildEffectsAndDiscardContainerEffects()
    {
        var undo = new UndoRedoManager(ProjectWithMedia());
        string? outerId = null;
        undo.Commit(p => outerId = NestedMediaOperations.Create(p, new[] { "video" }), "Anidar");
        var project = undo.CurrentProject;
        var seq = project.ActiveSequence!;
        var outer = seq.Clips.Single(c => c.Id == outerId);
        Assert.NotNull(NestedMediaOperations.Companion(seq, outer));
        Assert.Equal(2, outer.NestedContent!.Clips.Count);
        outer.Effects.Add(new EffectInstance { EffectId = "container-blur" });
        var revealed = NestedMediaOperations.Unnest(project, outer.Id);
        Assert.Equal(2, revealed.Count);
        var child = seq.Clips.Single(c => c.Name == "Vídeo");
        Assert.Contains(child.Effects, e => e.EffectId == "child-color");
        Assert.DoesNotContain(seq.Clips.SelectMany(c => c.Effects), e => e.EffectId == "container-blur");
        Assert.True(undo.Undo());
        Assert.Equal(2, undo.CurrentProject.ActiveSequence!.Clips.Count);
    }

    [Fact]
    public void Nest_UsesLowestAvailableVideoAndHighestAvailableAudioWithoutReplacingOutsideClips()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var selectedVideo = sequence.Clips.Single(c => c.Id == "video");
        var selectedAudio = sequence.Clips.Single(c => c.Id == "audio");
        var upperVideo = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 1);
        selectedVideo.TrackId = upperVideo.Id;
        var excludedVideo = new Clip { Id = "excluded", AssetId = "video-asset", StreamId = "video-stream",
            TrackId = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 0).Id,
            TimelineStart = Rational.Time(1), Duration = Rational.Time(4) };
        sequence.Clips.Add(excludedVideo);
        var nestedId = NestedMediaOperations.Create(project, new[] { selectedVideo.Id, selectedAudio.Id });
        var nested = sequence.Clips.Single(c => c.Id == nestedId);
        Assert.Equal(upperVideo.Id, nested.TrackId);
        Assert.Contains(sequence.Clips, c => c.Id == excludedVideo.Id && c.TrackId == excludedVideo.TrackId);
        Assert.Equal(0, sequence.Tracks.Single(t => t.Id == NestedMediaOperations.Companion(sequence, nested)!.TrackId).Order);
    }

    [Fact]
    public void MovingLastChildrenBack_TrimsContainerButReservedSpaceKeepsItsTail()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var nestedId = NestedMediaOperations.Create(project, new[] { "video" });
        var nested = sequence.Clips.Single(c => c.Id == nestedId);
        var video = nested.NestedContent!.Clips.Single(c => c.Id == "video");
        var audio = nested.NestedContent.Clips.Single(c => c.Id == "audio");
        NestedMediaOperations.UpdateChild(project, nestedId, video.Id, 0, 2, video.TrackId);
        NestedMediaOperations.UpdateChild(project, nestedId, audio.Id, 0, 2, audio.TrackId);
        Assert.Equal(2, nested.Duration.Seconds, 4);
        Assert.Equal(2, NestedMediaOperations.Companion(sequence, nested)!.Duration.Seconds, 4);
    }

    [Fact]
    public void ReservedChild_PreservesIntentionalTrailingSpace()
    {
        var project = ProjectWithMedia();
        var nestedId = NestedMediaOperations.Create(project, new[] { "video" });
        var nested = project.ActiveSequence!.Clips.Single(c => c.Id == nestedId);
        var visual = nested.NestedContent!.Clips.Single(c => c.Id == "video");
        var audio = nested.NestedContent.Clips.Single(c => c.Id == "audio");
        var reserve = PlaceholderService.CreateAsset(false, 2);
        project.Assets.Add(reserve);
        nested.NestedContent.Clips.Add(new Clip { AssetId = reserve.Id, StreamId = reserve.Streams[0].Id,
            TrackId = visual.TrackId, TimelineStart = Rational.Time(2), Duration = Rational.Time(2) });
        NestedMediaOperations.UpdateChild(project, nestedId, visual.Id, 0, 2, visual.TrackId);
        NestedMediaOperations.UpdateChild(project, nestedId, audio.Id, 0, 2, audio.TrackId);
        Assert.Equal(4, nested.Duration.Seconds, 4);
    }

    [Fact]
    public void MovingFirstChildrenRight_TrimsLeadingBlankWithoutMovingTheirVisibleTime()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var nestedId = NestedMediaOperations.Create(project, new[] { "video" });
        var nested = sequence.Clips.Single(c => c.Id == nestedId);
        var audioOuter = NestedMediaOperations.Companion(sequence, nested)!;
        var videoChild = nested.NestedContent!.Clips.Single(c => c.Id == "video");
        var audioChild = nested.NestedContent.Clips.Single(c => c.Id == "audio");
        NestedMediaOperations.UpdateChild(project, nestedId, videoChild.Id, 2, 2, videoChild.TrackId);
        NestedMediaOperations.UpdateChild(project, nestedId, audioChild.Id, 2, 2, audioChild.TrackId);
        Assert.Equal(3, nested.TimelineStart.Seconds, 4);
        Assert.Equal(2, nested.SourceIn.Seconds, 4);
        Assert.Equal(2, nested.Duration.Seconds, 4);
        Assert.Equal(3, audioOuter.TimelineStart.Seconds, 4);
        Assert.Equal(2, audioOuter.Duration.Seconds, 4);
    }

    [Fact]
    public void UnlinkedAudio_UnnestsOnlyAudio_ThenVideoUnnestsOnlyVideo()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var videoId = NestedMediaOperations.Create(project, new[] { "video" });
        var video = sequence.Clips.Single(c => c.Id == videoId);
        var audio = NestedMediaOperations.Companion(sequence, video)!;
        sequence.LinkGroups.RemoveAll(g => g.ClipIds.Contains(video.Id));
        var revealedAudio = NestedMediaOperations.Unnest(project, audio.Id);
        Assert.Single(revealedAudio);
        Assert.Contains(sequence.Clips, c => c.Id == video.Id);
        Assert.DoesNotContain(sequence.Clips, c => c.Id == audio.Id);
        var revealedVideo = NestedMediaOperations.Unnest(project, video.Id);
        Assert.Single(revealedVideo);
        Assert.DoesNotContain(sequence.Clips, c => c.Id == video.Id);
    }

    [Fact]
    public void UnlinkedAudio_EditDoesNotChangeVideoContainer()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var videoId = NestedMediaOperations.Create(project, new[] { "video" });
        var video = sequence.Clips.Single(c => c.Id == videoId);
        var audio = NestedMediaOperations.Companion(sequence, video)!;
        sequence.LinkGroups.RemoveAll(g => g.ClipIds.Contains(video.Id));
        var audioChild = audio.NestedContent!.Clips.Single(c => c.Id == "audio");
        NestedMediaOperations.UpdateChild(project, audio.Id, audioChild.Id, 0, 2, audioChild.TrackId);
        Assert.Equal(2, audio.Duration.Seconds, 4);
        Assert.Equal(4, video.Duration.Seconds, 4);
        Assert.Equal(4, video.NestedContent!.Clips.Single(c => c.Id == "audio").Duration.Seconds, 4);
    }

    [Fact]
    public void NestedAudio_PreservesSourceTimingAndContainerGain()
    {
        var path = Path.GetTempFileName();
        try
        {
            var project = ProjectWithMedia(path);
            var outerId = NestedMediaOperations.Create(project, new[] { "video" });
            var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
            var companion = NestedMediaOperations.Companion(project.ActiveSequence, outer)!;
            companion.GainDb = -4;
            var plan = AudioMixPlan.Create(project, 2);
            var input = Assert.Single(plan.Inputs);
            Assert.Equal(1, input.SourceStart, 4);
            Assert.Equal(3, input.SourceDuration, 4);
            Assert.Equal(-4, input.GainDb, 4);
            Assert.Equal(companion.TrackId, input.RootTrackId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NestedWithinNested_ResolvesOriginalLeafAndPersists()
    {
        var project = ProjectWithMedia();
        var innerId = NestedMediaOperations.Create(project, new[] { "video" }, "Interior");
        var outerId = NestedMediaOperations.Create(project, new[] { innerId }, "Exterior");
        var serialized = ProjectStore.Serialize(project);
        var restored = ProjectStore.Deserialize(serialized);
        var outer = restored.ActiveSequence!.Clips.Single(c => c.Id == outerId);
        NestedMediaOperations.Validate(restored, outer);
        var layer = Assert.Single(NestedRenderPlan.At(restored, 2, "video"));
        Assert.Equal("video", layer.Clip.Id);
        Assert.Equal(2, layer.Containers.Count);
    }

    [Fact]
    public void MiniTrackEdit_RejectsCollisionAndKeepsLinkedAudioSynchronized()
    {
        var project = ProjectWithMedia();
        var seq = project.ActiveSequence!;
        var asset = project.Assets.First(a => a.Id == "video-asset");
        seq.Clips.Add(new Clip { Id = "second", Name = "Segundo", AssetId = asset.Id, StreamId = "video-stream",
            TrackId = seq.Clips.First(c => c.Id == "video").TrackId, TimelineStart = Rational.Time(5), Duration = Rational.Time(2) });
        var outerId = NestedMediaOperations.Create(project, new[] { "video", "second" });
        var outer = seq.Clips.Single(c => c.Id == outerId);
        var track = outer.NestedContent!.Clips.First(c => c.Id == "video").TrackId;
        Assert.Throws<InvalidOperationException>(() => NestedMediaOperations.UpdateChild(project, outerId, "video", 0, 5, track));
        NestedMediaOperations.UpdateChild(project, outerId, "video", 0, 3, track);
        var companion = NestedMediaOperations.Companion(seq, outer)!;
        Assert.Equal(3, companion.NestedContent!.Clips.First(c => c.Id == "video").Duration.Seconds, 4);
    }

    [Fact]
    public void DeepEdit_UpdatesOnlyChildAndExpandsBothContainerLevels()
    {
        var project = ProjectWithMedia();
        var innerId = NestedMediaOperations.Create(project, new[] { "video" }, "Interior");
        var outerId = NestedMediaOperations.Create(project, new[] { innerId }, "Exterior");
        var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
        var inner = outer.NestedContent!.Clips.Single(c => c.Id == innerId);
        var childTrack = inner.NestedContent!.Clips.Single(c => c.Id == "video").TrackId;
        NestedMediaOperations.UpdateChild(project, outerId, "video", 0, 6, childTrack);
        Assert.Equal(6, outer.Duration.Seconds, 4);
        Assert.Equal(6, outer.NestedContent.Clips.Single(c => c.Id == innerId).Duration.Seconds, 4);
        Assert.Equal(6, NestedMediaOperations.FindChild(project, outerId, "video")!.Value.Child.Duration.Seconds, 4);
    }

    [Fact]
    public void UnnestTrimmedContainer_RevealsOnlyVisibleChildRange()
    {
        var project = ProjectWithMedia();
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
        var audio = NestedMediaOperations.Companion(project.ActiveSequence, outer)!;
        outer.SourceIn = audio.SourceIn = Rational.Time(1);
        outer.Duration = audio.Duration = Rational.Time(2);
        NestedMediaOperations.Unnest(project, outerId);
        var video = project.ActiveSequence.Clips.Single(c => c.Name == "Vídeo");
        Assert.Equal(1, video.TimelineStart.Seconds, 4);
        Assert.Equal(2, video.Duration.Seconds, 4);
        Assert.Equal(1, video.SourceIn.Seconds, 4);
    }

    [Fact]
    public void ChildProperties_DoNotAlterContainerOrSibling()
    {
        var project = ProjectWithMedia();
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
        outer.Effects.Add(new EffectInstance { EffectId = "container-effect" });
        NestedMediaOperations.SetChildProperties(project, outerId, "audio", "Voz", true, -8, 0.5);
        Assert.Equal(-8, NestedMediaOperations.FindChild(project, outerId, "audio")!.Value.Child.GainDb);
        Assert.Equal(0, NestedMediaOperations.FindChild(project, outerId, "video")!.Value.Child.GainDb);
        Assert.Single(outer.Effects);
        Assert.DoesNotContain(outer.NestedContent!.Clips.SelectMany(c => c.Effects), e => e.EffectId == "container-effect");
    }

    [Fact]
    public void TwoLevels_SeekAndSpeedMapToTheSameVideoAndAudioSourceTime()
    {
        var path = Path.GetTempFileName();
        try
        {
            var project = ProjectWithMedia(path);
            var innerId = NestedMediaOperations.Create(project, new[] { "video" }, "Interior");
            var outerId = NestedMediaOperations.Create(project, new[] { innerId }, "Exterior");
            var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
            var audio = NestedMediaOperations.Companion(project.ActiveSequence, outer)!;
            outer.SourceIn = audio.SourceIn = Rational.Time(1);
            outer.Speed = audio.Speed = Rational.Time(2);
            outer.Duration = audio.Duration = Rational.Time(1.5);
            var frame = Assert.Single(NestedRenderPlan.At(project, 1.25, "video"));
            Assert.Equal(1.5, frame.SourceSeconds, 4);
            var input = Assert.Single(AudioMixPlan.Create(project, 1).Inputs);
            Assert.Equal(1, input.SourceStart, 4);
            Assert.Equal(3, input.SourceDuration, 4);
            Assert.Equal(2, input.Speed, 4);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RemovingLastAudioChild_RemovesTheAudioCompanionOnly()
    {
        var project = ProjectWithMedia();
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        NestedMediaOperations.RemoveChild(project, outerId, "audio");
        var sequence = project.ActiveSequence!;
        var visual = sequence.Clips.Single(c => c.Id == outerId);
        Assert.Null(NestedMediaOperations.Companion(sequence, visual));
        Assert.Single(visual.NestedContent!.Clips);
        Assert.Throws<InvalidOperationException>(() => NestedMediaOperations.RemoveChild(project, outerId, "video"));
    }

    [Fact]
    public void UnnestWithOccupiedTrack_PreservesVideoLayerOrderWithoutOverlap()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var upperTrack = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 1);
        var lowerTrack = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 0);
        sequence.Clips.Single(c => c.Id == "video").TrackId = lowerTrack.Id;
        sequence.Clips.Add(new Clip { Id = "upper", Name = "Superior", AssetId = "video-asset", StreamId = "video-stream",
            TrackId = upperTrack.Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(4) });
        var outerId = NestedMediaOperations.Create(project, new[] { "video", "upper" });
        sequence.Clips.Add(new Clip { Id = "occupant", Name = "Ocupante", AssetId = "video-asset", StreamId = "video-stream",
            TrackId = lowerTrack.Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(4) });
        NestedMediaOperations.Unnest(project, outerId);
        var lower = sequence.Clips.Single(c => c.Name == "Vídeo");
        var upper = sequence.Clips.Single(c => c.Name == "Superior");
        Assert.NotEqual(lower.TrackId, sequence.Clips.Single(c => c.Id == "occupant").TrackId);
        Assert.True(sequence.Tracks.Single(t => t.Id == lower.TrackId).Order < sequence.Tracks.Single(t => t.Id == upper.TrackId).Order);
    }

    [Fact]
    public void RenderPlan_LeavesContainerEffectsOnTheCompositionBoundary()
    {
        var project = ProjectWithMedia();
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
        outer.Effects.Add(new EffectInstance { EffectId = "composition-effect" });
        var layer = Assert.Single(NestedRenderPlan.At(project, 2, "video"));
        Assert.Contains(layer.Clip.Effects, e => e.EffectId == "child-color");
        Assert.DoesNotContain(layer.Clip.Effects, e => e.EffectId == "composition-effect");
        Assert.Contains(layer.Containers.Single().Effects, e => e.EffectId == "composition-effect");
    }

    [Fact]
    public void Unnest_ReusesNeutralTracksAndRemovesUnreferencedContainerAsset()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var originalTracks = sequence.Tracks.Count;
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        var generatedId = sequence.Clips.Single(c => c.Id == outerId).AssetId;
        NestedMediaOperations.Unnest(project, outerId);
        Assert.Equal(originalTracks, sequence.Tracks.Count);
        Assert.DoesNotContain(project.Assets, a => a.Id == generatedId);
        Assert.Equal(2, sequence.Clips.Count);
    }

    [Fact]
    public void Unnest_DoesNotPlaceAChildFromAnEffectTrackOnANeutralTrack()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var source = sequence.Tracks.Single(t => t.Id == sequence.Clips.Single(c => c.Id == "video").TrackId);
        source.Effects.Add(new EffectInstance { EffectId = "track-effect" });
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        NestedMediaOperations.Unnest(project, outerId);
        var child = sequence.Clips.Single(c => c.Name == "Vídeo");
        Assert.NotEqual(source.Id, child.TrackId);
        Assert.Contains(sequence.Tracks.Single(t => t.Id == child.TrackId).Effects, e => e.EffectId == "track-effect");
    }

    [Fact]
    public void NestingOneMemberOfAnEditGroup_KeepsTheWholeGroupInside()
    {
        var project = ProjectWithMedia();
        var sequence = project.ActiveSequence!;
        var upperTrack = sequence.Tracks.Single(t => t.Kind == "video" && t.Order == 1);
        sequence.Clips.Add(new Clip { Id = "second-video", Name = "Segundo", AssetId = "video-asset", StreamId = "video-stream",
            TrackId = upperTrack.Id, TimelineStart = Rational.Time(5), Duration = Rational.Time(2) });
        EditGroupOperations.Group(sequence, new[] { "video", "second-video" });
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        var outer = sequence.Clips.Single(c => c.Id == outerId);
        Assert.Contains(outer.NestedContent!.Clips, c => c.Id == "second-video");
        Assert.Single(outer.NestedContent.EditGroups);
        NestedMediaOperations.Unnest(project, outerId);
        Assert.Single(sequence.EditGroups);
        Assert.Equal(2, sequence.EditGroups.Single().ClipIds.Count);
    }

    [Fact]
    public void PreflightRejectsAContainerThatReferencesItself()
    {
        var project = ProjectWithMedia();
        var outerId = NestedMediaOperations.Create(project, new[] { "video" });
        var outer = project.ActiveSequence!.Clips.Single(c => c.Id == outerId);
        outer.NestedContent!.Clips.First(c => c.Id == "video").AssetId = outer.AssetId;
        var report = PreflightAuditor.Audit(project);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("anidado"));
    }
}
