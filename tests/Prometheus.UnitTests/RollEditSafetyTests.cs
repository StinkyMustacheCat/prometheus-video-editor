using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class RollEditSafetyTests
{
    [Theory]
    [InlineData("dissolve", 24, 1, 1)]
    [InlineData("dip-black", 25, 1, 1)]
    [InlineData("dip-white", 25, .5, 2)]
    [InlineData("dissolve", 25, 2, .5)]
    public void RollRetainsFullVideoTransitionAcrossSourceAndDurationLimits(string kind, int frames, double leftSpeed, double rightSpeed)
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        seq.Fps = new Rational(30000, 1001);
        var track = seq.Tracks.First(t => t.Kind == "video");
        var left = Add(project, track, 0, 2, 3, 12, leftSpeed);
        var right = Add(project, track, 3, 2, 3, 12, rightSpeed);
        var transition = VideoTransitionOperations.Apply(project, left.Id, right.Id, kind, frames);
        var baseline = ProjectStore.Serialize(project);
        foreach (var wanted in new[] { -10d, .1, 1, 2.75, 3d, 3.25, 4, 5.99, 50 })
        {
            var copy = ProjectStore.Deserialize(baseline);
            var s = copy.ActiveSequence!;
            var before = ProjectStore.Serialize(copy);
            var plan = RollEditOperations.Prepare(copy, s, left.Id, right.Id, wanted);
            Assert.NotNull(plan);
            Assert.Equal(before, ProjectStore.Serialize(copy));
            Assert.True(RollEditOperations.Apply(copy, s, left.Id, right.Id, wanted));
            var l = s.Clips.Single(c => c.Id == left.Id);
            var r = s.Clips.Single(c => c.Id == right.Id);
            Assert.InRange(Math.Abs(l.EndSeconds - plan!.CutSeconds), 0, .00011);
            Assert.Equal(l.EndSeconds, r.TimelineStart.Seconds, 6);
            Assert.InRange(Math.Abs(r.EndSeconds - 6), 0, 1d / 48000);
            Assert.True(VideoTransitionOperations.MaximumFrames(copy, s, left.Id, right.Id, kind) >= frames);
            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(copy));
            var saved = Assert.Single(restored.ActiveSequence!.VideoTransitions);
            Assert.Equal(transition.Id, saved.Id);
            Assert.Equal(frames, saved.DurationFrames);
        }
    }

    [Fact]
    public void RollReservesDissolveHandlesAndUndoRestoresSourceClock()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        seq.Fps = new Rational(24, 1);
        var track = seq.Tracks.First(t => t.Kind == "video");
        var left = Add(project, track, 0, 1, 2, 8);
        var right = Add(project, track, 2, 1, 2, 8);
        VideoTransitionOperations.Apply(project, left.Id, right.Id, VideoTransitionOperations.Dissolve, 24);
        var undo = new UndoRedoManager(project);
        undo.Commit(p => Assert.True(RollEditOperations.Apply(p, p.ActiveSequence!, left.Id, right.Id, -20)), "Roll");
        var changed = undo.CurrentProject.ActiveSequence!;
        Assert.Equal(1.5, changed.Clips.Single(c => c.Id == left.Id).EndSeconds, 5);
        Assert.Equal(.5, changed.Clips.Single(c => c.Id == right.Id).SourceIn.Seconds, 5);
        Assert.Equal(24, Assert.Single(changed.VideoTransitions).DurationFrames);
        Assert.True(undo.Undo());
        Assert.Equal(2, undo.CurrentProject.ActiveSequence!.Clips.Single(c => c.Id == left.Id).EndSeconds);
        Assert.True(undo.Redo());
        Assert.Equal(24, Assert.Single(undo.CurrentProject.ActiveSequence!.VideoTransitions).DurationFrames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollProtectsNeighboringTransitionsAndAudioFades(bool audio)
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        seq.Fps = new Rational(24, 1);
        var track = seq.Tracks.First(t => t.Kind == (audio ? "audio" : "video"));
        var clips = Enumerable.Range(0, 4).Select(i => Add(project, track, i * 3, 2, 3, 12)).ToArray();
        if (audio)
        {
            for (var i = 0; i < 3; i++) AudioTransitionOperations.Apply(project, AudioTransitionOperations.Crossfade, clips[i].Id, clips[i + 1].Id, 1);
            AudioTransitionOperations.Apply(project, AudioTransitionOperations.FadeIn, clips[0].Id, duration: .8);
            AudioTransitionOperations.Apply(project, AudioTransitionOperations.FadeOut, clips[3].Id, duration: .8);
        }
        else for (var i = 0; i < 3; i++) VideoTransitionOperations.Apply(project, clips[i].Id, clips[i + 1].Id, VideoTransitionOperations.Dissolve, 24);
        var baseline = ProjectStore.Serialize(project);
        for (var i = 0; i <= 36; i++)
        {
            var candidate = ProjectStore.Deserialize(baseline);
            var undo = new UndoRedoManager(candidate);
            undo.Commit(p => Assert.True(RollEditOperations.Apply(p, p.ActiveSequence!, clips[1].Id, clips[2].Id, 1 + i / 3d)), "Roll");
            var changed = undo.CurrentProject.ActiveSequence!;
            if (audio)
            {
                Assert.Equal(5, changed.AudioTransitions.Count);
                Assert.All(changed.AudioTransitions.Where(t => t.Kind == AudioTransitionOperations.Crossfade), t => Assert.Equal(1, t.DurationSeconds, 7));
            }
            else
            {
                Assert.Equal(3, changed.VideoTransitions.Count);
                Assert.All(changed.VideoTransitions, t => Assert.Equal(24, t.DurationFrames));
            }
        }
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(50)]
    public void RollProtectsStandaloneMovingAndFixedFades(double at)
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var track = seq.Tracks.First(t => t.Kind == "audio");
        var left = Add(project, track, 0, 1, 3, 12);
        var right = Add(project, track, 3, 1, 3, 12);
        foreach (var clip in new[] { left, right })
        {
            AudioTransitionOperations.Apply(project, AudioTransitionOperations.FadeIn, clip.Id, duration: .8);
            AudioTransitionOperations.Apply(project, AudioTransitionOperations.FadeOut, clip.Id, duration: .8);
        }
        var undo = new UndoRedoManager(project);
        undo.Commit(p => RollEditOperations.Apply(p, p.ActiveSequence!, left.Id, right.Id, at), "Roll");
        var transitions = undo.CurrentProject.ActiveSequence!.AudioTransitions;
        Assert.Equal(4, transitions.Count);
        Assert.All(transitions, t => Assert.Equal(.8, t.DurationSeconds, 7));
    }

    [Fact]
    public void MgtTransitionAndStillImagesKeepTheirFullWindow()
    {
        var template = MgtGpuTransition.Parse(File.ReadAllText(Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "Runtime", "MgtTransitions"), "*.mgt.json")[0]));
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        seq.Fps = new Rational(24, 1);
        var track = seq.Tracks.First(t => t.Kind == "video");
        var left = Add(project, track, 0, 0, 3, 3);
        var right = Add(project, track, 3, 0, 3, 3);
        foreach (var asset in project.Assets) asset.Streams[0].Kind = "image";
        var transition = VideoTransitionOperations.Apply(project, left.Id, right.Id, template.Id, 24, template);
        Assert.True(RollEditOperations.Apply(project, seq, left.Id, right.Id, 50));
        Assert.Equal(6 - (transition.DurationFrames / 2d) / 24, left.EndSeconds, 5);
        Assert.Equal(0, right.SourceIn.Seconds);
        var saved = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        Assert.Equal(transition.DurationFrames, Assert.Single(saved.ActiveSequence!.VideoTransitions).DurationFrames);
    }

    [Fact]
    public void SplitRetainsTheCutAndCaptureInsertionSeparatesItWithUndo()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var track = seq.Tracks.First(t => t.Kind == "video");
        var left = Add(project, track, 0, 1, 3, 12);
        var right = Add(project, track, 3, 1, 3, 12);
        VideoTransitionOperations.Apply(project, left.Id, right.Id, VideoTransitionOperations.Dissolve, 24);
        var undo = new UndoRedoManager(project);
        undo.Commit(p => CollisionEngine.SplitClip(p.ActiveSequence!, left.Id, 1), "Dividir");
        var split = Assert.Single(undo.CurrentProject.ActiveSequence!.VideoTransitions);
        Assert.NotEqual(left.Id, split.LeftClipId);
        Assert.Equal(right.Id, split.RightClipId);
        Assert.Equal(24, split.DurationFrames);
        undo.Commit(p => CapturedFramePlacement.Insert(p, new Asset { Id = "capture", RecommendedDuration = Rational.Time(1),
            Streams = [new StreamInfo { Id = "image", Kind = "image" }] }, 3), "Insertar captura");
        Assert.Empty(undo.CurrentProject.ActiveSequence!.VideoTransitions);
        Assert.True(undo.Undo());
        Assert.Equal(split.Id, Assert.Single(undo.CurrentProject.ActiveSequence!.VideoTransitions).Id);
        Assert.True(undo.Undo());
        Assert.Equal(left.Id, Assert.Single(undo.CurrentProject.ActiveSequence!.VideoTransitions).LeftClipId);
    }

    [Fact]
    public void NoTransitionRollUsesSourceDurationEvenWithoutAClipOverride()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var track = seq.Tracks.First(t => t.Kind == "video");
        var left = Add(project, track, 0, 0, 2, 2.5);
        var right = Add(project, track, 2, 1, 3, 12);
        Assert.Null(left.AvailableSourceDurationSeconds);
        Assert.True(RollEditOperations.Apply(project, seq, left.Id, right.Id, 4));
        Assert.Equal(2.5, left.EndSeconds, 6);
        Assert.Equal(5, right.EndSeconds, 6);
    }

    [Fact]
    public void RollRejectsLockedOrSeparatedClipsWithoutMutation()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var track = seq.Tracks.First(t => t.Kind == "video");
        var left = Add(project, track, 0, 1, 2, 8);
        var right = Add(project, track, 2, 1, 2, 8);
        track.Locked = true;
        var baseline = ProjectStore.Serialize(project);
        Assert.False(RollEditOperations.Apply(project, seq, left.Id, right.Id, 3));
        Assert.Equal(baseline, ProjectStore.Serialize(project));
        track.Locked = false; right.TimelineStart = Rational.Time(3);
        Assert.Null(RollEditOperations.Prepare(project, seq, left.Id, right.Id, 3));
        Assert.Null(RollEditOperations.Prepare(project, seq, left.Id, right.Id, double.NaN));
    }

    internal static Clip Add(Project project, Track track, double at, double sourceIn, double duration, double sourceDuration, double speed = 1)
    {
        var stream = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = track.Kind, Duration = Rational.Time(sourceDuration) };
        var asset = new Asset { Id = Guid.NewGuid().ToString("N"), Streams = [stream] };
        project.Assets.Add(asset);
        var clip = new Clip { AssetId = asset.Id, StreamId = stream.Id, TrackId = track.Id, TimelineStart = Rational.Time(at),
            SourceIn = Rational.Time(sourceIn), Duration = Rational.Time(duration), Speed = Rational.Time(speed) };
        project.ActiveSequence!.Clips.Add(clip);
        return clip;
    }
}
