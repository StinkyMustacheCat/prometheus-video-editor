using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class TimelineLinkedEditingTests
{
    [Fact]
    public void OverwritingAnOutgoingVideoEdgeRemovesItsTransition()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var track = sequence.Tracks.First(t => t.Kind == "video").Id;
        var left = new Clip { TrackId = track, Duration = Rational.Time(3) };
        var right = new Clip { TrackId = track, TimelineStart = Rational.Time(3), Duration = Rational.Time(2) };
        var incoming = new Clip { TrackId = track, TimelineStart = Rational.Time(2), Duration = Rational.One };
        sequence.Clips.AddRange([left, right, incoming]);
        sequence.VideoTransitions.Add(new VideoTransition {
            LeftClipId = left.Id, RightClipId = right.Id, Kind = VideoTransitionOperations.DipBlack, DurationFrames = 12 });
        CollisionEngine.Overwrite(sequence, [incoming.Id]);
        Assert.Empty(sequence.VideoTransitions);
        Assert.Equal(2, left.EndSeconds);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(120)]
    public void SplitAllowsExactlyOneSequenceFrameAtTheEdge(int fps)
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        sequence.Fps = new Rational(fps, 1);
        var clip = new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "video").Id, Duration = Rational.One };
        sequence.Clips.Add(clip);
        var right = CollisionEngine.SplitClip(sequence, clip.Id, 1d / fps).Right;
        Assert.NotNull(right);
        Assert.InRange(Math.Abs(clip.Duration.Seconds - 1d / fps), 0, 1d / 48000);
    }

    [Fact]
    public void RippleDeleteRejectsCrossTrackCollisionBeforeChangingAnything()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var videoTrack = sequence.Tracks.First(t => t.Kind == "video").Id;
        var audioTrack = sequence.Tracks.First(t => t.Kind == "audio").Id;
        var selected = new Clip { TrackId = videoTrack, TimelineStart = Rational.Time(2), Duration = Rational.One };
        sequence.Clips.AddRange([
            selected,
            new Clip { TrackId = audioTrack, Duration = Rational.Time(4) },
            new Clip { TrackId = audioTrack, TimelineStart = Rational.Time(4), Duration = Rational.Time(2) }
        ]);
        var before = ProjectStore.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => CollisionEngine.RippleDeleteClip(sequence, selected.Id));
        Assert.Equal(before, ProjectStore.Serialize(project));
        Assert.Throws<InvalidOperationException>(() => CollisionEngine.RippleDeleteRange(sequence, 2, 1));
        Assert.Equal(before, ProjectStore.Serialize(project));
    }

    [Fact]
    public void RippleTrimMovesLaterLinkedMediaAcrossTracksAndRejectsLockedPartners()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var videoTrack = sequence.Tracks.First(t => t.Kind == "video").Id;
        var audioTrack = sequence.Tracks.First(t => t.Kind == "audio");
        var anchor = new Clip { TrackId = videoTrack, Duration = Rational.Time(4) };
        var nextVideo = new Clip { TrackId = videoTrack, TimelineStart = Rational.Time(4), Duration = Rational.One };
        var nextAudio = new Clip { TrackId = audioTrack.Id, TimelineStart = Rational.Time(4), Duration = Rational.One };
        sequence.Clips.AddRange([anchor, nextVideo, nextAudio]);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = [nextVideo.Id, nextAudio.Id] });
        audioTrack.Locked = true;
        var before = ProjectStore.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => CollisionEngine.RippleTrimClip(sequence, anchor.Id, false, 3));
        Assert.Equal(before, ProjectStore.Serialize(project));
        audioTrack.Locked = false;
        Assert.True(CollisionEngine.RippleTrimClip(sequence, anchor.Id, false, 3));
        Assert.Equal(3, nextVideo.TimelineStart.Seconds);
        Assert.Equal(nextVideo.TimelineStart, nextAudio.TimelineStart);
        Assert.Equal(3, anchor.EndSeconds);
    }

    [Fact]
    public void RollEditRejectsSeparatedClipsWithoutMovingThem()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var track = sequence.Tracks.First(t => t.Kind == "video").Id;
        var left = new Clip { TrackId = track, Duration = Rational.Time(2) };
        var right = new Clip { TrackId = track, TimelineStart = Rational.Time(4), Duration = Rational.Time(2) };
        sequence.Clips.AddRange([left, right]);
        var before = ProjectStore.Serialize(project);
        Assert.False(CollisionEngine.RollEdit(sequence, left.Id, right.Id, 2.5));
        Assert.Equal(before, ProjectStore.Serialize(project));
    }

    [Fact]
    public void RemovingAnAudioEdgeKeepsOnlyTheSurvivingEdgesFade()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var clip = new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "audio").Id, Duration = Rational.Time(5) };
        sequence.Clips.Add(clip);
        sequence.AudioTransitions.AddRange([
            new AudioTransition { Kind = AudioTransitionOperations.FadeIn, ClipId = clip.Id, DurationSeconds = .2 },
            new AudioTransition { Kind = AudioTransitionOperations.FadeOut, ClipId = clip.Id, DurationSeconds = .3 }
        ]);
        Assert.True(CollisionEngine.RangeTrim(sequence, 0, 1));
        Assert.Equal(AudioTransitionOperations.FadeOut, Assert.Single(sequence.AudioTransitions).Kind);
        Assert.True(CollisionEngine.RangeTrim(sequence, 4, 5));
        Assert.Empty(sequence.AudioTransitions);
    }

    [Fact]
    public void ToggleGap_SecondClickClearsItWithoutChangingClipSelectionRules()
    {
        var selection = new TimelineSelection();
        selection.SelectClips(new[] { "first", "last" });
        selection.ToggleGap("track", 2, 5);
        Assert.True(selection.HasGapSelection);
        selection.ToggleGap("track", 2, 5);
        Assert.False(selection.HasSelection);
        selection.SelectClips(new[] { "first" });
        selection.SelectClips(new[] { "last" }, addToSelection: true);
        Assert.Equal(2, selection.SelectedClipIds.Count);
    }

    [Fact]
    public void RippleTrimClip_UsesPlayheadAndPreservesLinkedPair()
    {
        var seq = ProjectStore.CreateNew().ActiveSequence!;
        var video = seq.Tracks.First(t => t.Kind == "video");
        var audio = seq.Tracks.First(t => t.Kind == "audio");
        seq.Clips.AddRange(new[] {
            new Clip { Id = "v", TrackId = video.Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(4) },
            new Clip { Id = "a", TrackId = audio.Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(4) },
            new Clip { Id = "next", TrackId = video.Id, TimelineStart = Rational.Time(5), Duration = Rational.Time(2) }
        });
        seq.LinkGroups.Add(new LinkGroup { ClipIds = { "v", "a" } });

        Assert.True(CollisionEngine.RippleTrimClip(seq, "v", true, 2));
        Assert.Equal(2, seq.Clips.First(c => c.Id == "v").TimelineStart.Seconds);
        Assert.Equal(1, seq.Clips.First(c => c.Id == "a").SourceIn.Seconds);
        Assert.Equal(5, seq.Clips.First(c => c.Id == "next").TimelineStart.Seconds);

        Assert.True(CollisionEngine.RippleTrimClip(seq, "v", false, 4));
        Assert.Equal(4, seq.Clips.First(c => c.Id == "v").EndSeconds);
        Assert.Equal(4, seq.Clips.First(c => c.Id == "a").EndSeconds);
        Assert.Equal(4, seq.Clips.First(c => c.Id == "next").TimelineStart.Seconds);
        Assert.Contains(seq.LinkGroups, g => g.ClipIds.Contains("v") && g.ClipIds.Contains("a"));
    }

    [Fact]
    public void DirectionalSelection_VerticalSelectsEntireTracks()
    {
        var seq = ProjectStore.CreateNew().ActiveSequence!;
        var tracks = seq.Tracks.Take(3).Select(t => t.Id).ToArray();
        seq.Clips.AddRange(new[] {
            new Clip { Id = "top", TrackId = tracks[0], TimelineStart = Rational.Time(0), Duration = Rational.Time(1) },
            new Clip { Id = "middle", TrackId = tracks[1], TimelineStart = Rational.Time(20), Duration = Rational.Time(1) },
            new Clip { Id = "bottom", TrackId = tracks[2], TimelineStart = Rational.Time(40), Duration = Rational.Time(1) }
        });

        Assert.Equal(new[] { "top", "middle" }, DirectionalSelectionEngine.Select(seq, "up", 10, tracks, tracks[1]));
        Assert.Equal(new[] { "middle", "bottom" }, DirectionalSelectionEngine.Select(seq, "down", 10, tracks, tracks[1]));
    }

    [Fact]
    public void CreateInGap_CoversExactIntervalAndLeavesNoGap()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var trackId = seq.Tracks.First(t => t.Kind == "audio").Id;
        seq.Clips.Add(new Clip { Id = "before", TrackId = trackId, Duration = Rational.Time(2) });
        seq.Clips.Add(new Clip { Id = "after", TrackId = trackId, TimelineStart = Rational.Time(7), Duration = Rational.Time(2) });

        var reserve = PlaceholderService.CreateInGap(project, trackId, 2, 7);

        Assert.Equal(2, reserve.TimelineStart.Seconds);
        Assert.Equal(7, reserve.EndSeconds);
        Assert.Empty(CollisionEngine.DetectGaps(seq, trackId));
        Assert.Throws<InvalidOperationException>(() => PlaceholderService.CreateInGap(project, trackId, 2, 7));
    }

    [Fact]
    public void SplitLinkedClips_CutsVideoAndAudioAndPreservesEffects()
    {
        var seq = ProjectStore.CreateNew().ActiveSequence!;
        var video = new Clip { Id = "v", TrackId = seq.Tracks.First(t => t.Kind == "video").Id,
            TimelineStart = Rational.Time(1), Duration = Rational.Time(4),
            Effects = { new EffectInstance { Id = "fx-v", EffectId = "color" } } };
        var audio = new Clip { Id = "a", TrackId = seq.Tracks.First(t => t.Kind == "audio").Id,
            TimelineStart = Rational.Time(1), Duration = Rational.Time(4),
            Effects = { new EffectInstance { Id = "fx-a", EffectId = "gain" } } };
        seq.Clips.AddRange(new[] { video, audio });
        seq.LinkGroups.Add(new LinkGroup { ClipIds = { video.Id, audio.Id } });

        var rights = CollisionEngine.SplitLinkedClips(seq, video.Id, 3);

        Assert.Equal(2, rights.Count);
        Assert.All(rights, c => Assert.Equal(3, c.TimelineStart.Seconds));
        Assert.Equal(2, seq.LinkGroups.Count);
        Assert.Contains(seq.LinkGroups, g => g.ClipIds.Contains("v") && g.ClipIds.Contains("a"));
        Assert.Contains(seq.LinkGroups, g => rights.All(c => g.ClipIds.Contains(c.Id)));
        Assert.Equal("color", rights.Single(c => c.TrackId == video.TrackId).Effects.Single().EffectId);
        Assert.NotEqual("fx-v", rights.Single(c => c.TrackId == video.TrackId).Effects.Single().Id);
    }

    [Fact]
    public void DuplicateClips_PreservesLinkAndIndependentEffectIds()
    {
        var seq = ProjectStore.CreateNew().ActiveSequence!;
        var video = new Clip { Id = "v", TrackId = seq.Tracks.First(t => t.Kind == "video").Id,
            Duration = Rational.Time(2), Effects = { new EffectInstance { Id = "fx", EffectId = "color" } } };
        var audio = new Clip { Id = "a", TrackId = seq.Tracks.First(t => t.Kind == "audio").Id, Duration = Rational.Time(2) };
        seq.Clips.AddRange(new[] { video, audio });
        seq.LinkGroups.Add(new LinkGroup { ClipIds = { "v", "a" } });

        var copies = CollisionEngine.DuplicateClips(seq, new[] { "v", "a" }, 3);

        Assert.Equal(2, copies.Count);
        Assert.Contains(seq.LinkGroups, g => copies.All(c => g.ClipIds.Contains(c.Id)));
        Assert.Equal("color", copies.Single(c => c.TrackId == video.TrackId).Effects.Single().EffectId);
        Assert.NotEqual("fx", copies.Single(c => c.TrackId == video.TrackId).Effects.Single().Id);
    }

    [Fact]
    public void RippleDeleteClip_DeletesLinkedPairAndShiftsLaterClips()
    {
        var seq = ProjectStore.CreateNew().ActiveSequence!;
        var videoTrack = seq.Tracks.First(t => t.Kind == "video");
        var audioTrack = seq.Tracks.First(t => t.Kind == "audio");
        seq.Clips.AddRange(new[] {
            new Clip { Id = "v", TrackId = videoTrack.Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(2) },
            new Clip { Id = "a", TrackId = audioTrack.Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(2) },
            new Clip { Id = "later", TrackId = videoTrack.Id, TimelineStart = Rational.Time(5), Duration = Rational.Time(1) }
        });
        seq.LinkGroups.Add(new LinkGroup { ClipIds = { "v", "a" } });

        CollisionEngine.RippleDeleteClip(seq, "v");

        Assert.Single(seq.Clips);
        Assert.Equal(3, seq.Clips.Single().TimelineStart.Seconds);
        Assert.Empty(seq.LinkGroups);
    }

    [Fact]
    public void RippleDeleteClip_RejectsLockedTrackWithoutMutation()
    {
        var seq = ProjectStore.CreateNew().ActiveSequence!;
        var track = seq.Tracks.First(t => t.Kind == "video");
        seq.Clips.Add(new Clip { Id = "v", TrackId = track.Id, Duration = Rational.Time(2) });
        track.Locked = true;

        Assert.Throws<InvalidOperationException>(() => CollisionEngine.RippleDeleteClip(seq, "v"));
        Assert.Single(seq.Clips);
    }
}
