using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;
using Xunit;

namespace Prometheus.UnitTests;

public class AudioMixPlanTests
{
    [Fact]
    public void PreviewSpeed_TwoTimesHalvesOutputDurationAndTempo()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var track = seq.Tracks.First(t => t.Kind == "video");
        seq.Clips.Add(new Clip { TrackId = track.Id, Duration = Rational.Time(4) });
        var plan = AudioMixPlan.Create(project, 0);
        var args = plan.BuildProcess(2).ArgumentList;
        Assert.Contains("atempo=2", args.First(a => a.Contains("amix=")));
        var durationIndex = args.IndexOf("-t");
        Assert.Equal("2", args[durationIndex + 1]);
    }

    [Fact]
    public void LevelCache_ReportsSourcePeakAndTrackGain()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-audio-level-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(path);
            var project = ProjectStore.CreateNew();
            var seq = project.ActiveSequence!;
            var track = seq.Tracks.First(t => t.Kind == "audio");
            var asset = new Asset { Id = "tone", Source = new MediaSource { Kind = "local", Uri = path } };
            asset.Streams.Add(new StreamInfo { Id = "audio-stream", Kind = "audio", Ordinal = 0 });
            project.Assets.Add(asset);
            seq.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = track.Id,
                Duration = Rational.Time(1) });
            AudioLevelCache.Warm(project);
            StereoLevel level = default;
            for (var i = 0; i < 100; i++)
            {
                level = AudioLevelCache.TrackLevel(project, track, 0.5);
                if (level.Left > 0) break;
                Thread.Sleep(20);
            }
            Assert.InRange(level.Left, 0.02, 0.04);
            track.GainDb = -6;
            var attenuated = AudioLevelCache.TrackLevel(project, track, 0.5);
            Assert.InRange(attenuated.Left, 0.01, 0.02);
            track.Enabled = false;
            Assert.Equal(0, AudioLevelCache.TrackLevel(project, track, 0.5).Left);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Create_RespectsMuteSeekSpeedGainAndBalance()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-audio-plan-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(path);
            var project = ProjectStore.CreateNew();
            var seq = project.ActiveSequence!;
            var tracks = seq.Tracks.Where(t => t.Kind == "audio").ToArray();
            var asset = new Asset { Id = "tone", Source = new MediaSource { Kind = "local", Uri = path } };
            asset.Streams.Add(new StreamInfo { Id = "audio-stream", Kind = "audio", Ordinal = 0 });
            project.Assets.Add(asset);
            seq.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = tracks[0].Id,
                TimelineStart = Rational.Time(1), Duration = Rational.Time(3), SourceIn = Rational.Time(0.5),
                Speed = Rational.Time(0.5), GainDb = -3, Balance = 0.25 });
            seq.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = tracks[1].Id,
                TimelineStart = Rational.Time(1), Duration = Rational.Time(3) });
            tracks[0].GainDb = -6;
            tracks[1].Enabled = false;

            var plan = AudioMixPlan.Create(project, 2);

            var input = Assert.Single(plan.Inputs);
            Assert.Equal(1, input.SourceStart);
            Assert.Equal(1, input.SourceDuration);
            Assert.Equal(0.5, input.Speed);
            Assert.Equal(-9, input.GainDb);
            Assert.Equal(0.25, input.Balance);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Create_SoloExcludesOtherTracksAndAppliesMasterGain()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-audio-solo-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(path);
            var project = ProjectStore.CreateNew();
            var seq = project.ActiveSequence!;
            var tracks = seq.Tracks.Where(t => t.Kind == "audio").ToArray();
            var asset = new Asset { Id = "tone", Source = new MediaSource { Kind = "local", Uri = path } };
            asset.Streams.Add(new StreamInfo { Id = "audio-stream", Kind = "audio", Ordinal = 0 });
            project.Assets.Add(asset);
            foreach (var track in tracks)
                seq.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = track.Id,
                    Duration = Rational.Time(1) });
            tracks[0].Solo = true;
            seq.MasterGainDb = -6;

            var plan = AudioMixPlan.Create(project, 0);

            Assert.Single(plan.Inputs);
            Assert.Equal(-6, plan.MasterGainDb);
            Assert.Contains("volume=-6dB[mix]", plan.BuildProcess().ArgumentList.First(a => a.Contains("volume=")));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void TwoAudioTracks_AreMixedWithTimelineDelay()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-audio-test-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(path);
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            var asset = new Asset { Id = "tone", Source = new MediaSource { Kind = "local", Uri = path } };
            asset.Streams.Add(new StreamInfo { Id = "audio-stream", Kind = "audio", Ordinal = 0 });
            project.Assets.Add(asset);
            var tracks = sequence.Tracks.Where(t => t.Kind == "audio").ToArray();
            sequence.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = tracks[0].Id,
                Duration = Rational.Time(1) });
            sequence.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = tracks[1].Id,
                TimelineStart = Rational.Time(0.25), Duration = Rational.Time(1) });

            var plan = AudioMixPlan.Create(project, 0);
            Assert.Equal(2, plan.Inputs.Count);
            using var process = Process.Start(plan.BuildProcess())!;
            using var pcm = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(pcm);
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
            var bytes = pcm.ToArray();
            short Sample(double seconds) => BitConverter.ToInt16(bytes, (int)(seconds * 48000) * 4);
            Assert.InRange(Sample(0.1), 900, 1100);
            Assert.InRange(Sample(0.4), 1800, 2200);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void ClipAndTrackFiltersReachThePreviewAndAffectPcm()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-audio-filter-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(path);
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            var track = sequence.Tracks.First(t => t.Kind == "audio");
            var asset = new Asset { Id = "tone", Source = new MediaSource { Kind = "local", Uri = path } };
            asset.Streams.Add(new StreamInfo { Id = "audio-stream", Kind = "audio", Ordinal = 0 });
            project.Assets.Add(asset);
            var clip = new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = track.Id,
                Duration = Rational.Time(1) };
            sequence.Clips.Add(clip);
            AudioEffects.Add(clip, AudioEffects.HighPassId);
            AudioEffects.Add(track, AudioEffects.LowPassId);
            var plan = AudioMixPlan.Create(project, 0);
            var input = Assert.Single(plan.Inputs);
            Assert.Contains("highpass", input.EffectChain);
            Assert.DoesNotContain("lowpass", input.EffectChain);
            Assert.Contains("lowpass", Assert.Single(plan.Groups).EffectChain);
            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
            Assert.Equal(input.EffectChain, Assert.Single(AudioMixPlan.Create(restored, 0).Inputs).EffectChain);
            using var process = Process.Start(plan.BuildProcess())!;
            using var pcm = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(pcm);
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
            var sample = BitConverter.ToInt16(pcm.ToArray(), 48000 / 2 * 4);
            Assert.InRange(Math.Abs((int)sample), 0, 50);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void NestedParentProcessesTheMixedChildren()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-nested-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(path);
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            var outerTrack = sequence.Tracks.First(t => t.Kind == "audio");
            var asset = new Asset { Id = "tone", Source = new MediaSource { Kind = "local", Uri = path } };
            asset.Streams.Add(new StreamInfo { Id = "audio-stream", Kind = "audio", Ordinal = 0 });
            project.Assets.Add(asset);
            var innerA = new Track { Kind = "audio", Order = 0 };
            var innerB = new Track { Kind = "audio", Order = 1 };
            var parent = new Clip { TrackId = outerTrack.Id, Duration = Rational.Time(1),
                NestedRole = "audio", NestedContent = new NestedContent
                {
                    Tracks = [innerA, innerB],
                    Clips =
                    [
                        new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = innerA.Id,
                            Duration = Rational.Time(1) },
                        new Clip { AssetId = asset.Id, StreamId = "audio-stream", TrackId = innerB.Id,
                            Duration = Rational.Time(1) }
                    ]
                }
            };
            AudioEffects.Add(parent, AudioEffects.GateId);
            AudioEffects.Set(parent.Effects.Single(), "threshold_db", -30);
            AudioEffects.Set(parent.Effects.Single(), "ratio", 20);
            sequence.Clips.Add(parent);

            var plan = AudioMixPlan.Create(project, 0);
            Assert.Equal(2, plan.Inputs.Count);
            Assert.DoesNotContain(plan.Inputs, input => input.EffectChain.Contains("agate"));
            Assert.Contains("agate", Assert.Single(plan.Groups).EffectChain);
            using var process = Process.Start(plan.BuildProcess())!;
            using var pcm = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(pcm);
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
            var sample = BitConverter.ToInt16(pcm.ToArray(), 48000 / 2 * 4);
            Assert.InRange(sample, 1500, 2400);

            var wrapperTrack = new Track { Kind = "audio", Order = 0 };
            parent.TrackId = wrapperTrack.Id;
            var wrapper = new Clip { TrackId = outerTrack.Id, Duration = Rational.Time(1),
                NestedRole = "audio", NestedContent = new NestedContent
                { Tracks = [wrapperTrack], Clips = [parent] } };
            AudioEffects.Add(wrapper, AudioEffects.EchoId);
            sequence.Clips.Clear();
            sequence.Clips.Add(wrapper);
            var twoLevels = AudioMixPlan.Create(project, 0);
            Assert.Equal(2, twoLevels.Groups.Count);
            using var nestedProcess = Process.Start(twoLevels.BuildProcess())!;
            using var nestedPcm = new MemoryStream();
            nestedProcess.StandardOutput.BaseStream.CopyTo(nestedPcm);
            var nestedError = nestedProcess.StandardError.ReadToEnd();
            nestedProcess.WaitForExit();
            Assert.True(nestedProcess.ExitCode == 0, nestedError);
            Assert.True(nestedPcm.Length > 0);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void EdgeFadesContinueFromTheCorrectGainWhenStartingInsideThem()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-edge-fades-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(path);
            var project = ProjectStore.CreateNew();
            var track = project.ActiveSequence!.Tracks.First(t => t.Kind == "audio");
            var clip = PlaceTone(project, track, path, 0, 0, 1, 1);
            AudioTransitionOperations.Apply(project, AudioTransitionOperations.FadeIn, clip.Id, duration: .4);
            AudioTransitionOperations.Apply(project, AudioTransitionOperations.FadeOut, clip.Id, duration: .4);

            var pcm = Mix(project, 0);
            Assert.InRange(Sample(pcm, .2), 495, 505);
            Assert.InRange(Sample(pcm, .5), 995, 1005);
            Assert.InRange(Sample(pcm, .8), 495, 505);
            Assert.InRange(Sample(pcm, .999), 0, 5);
            Assert.InRange(Sample(Mix(project, .2), 0), 495, 505);
            Assert.InRange(Sample(Mix(project, .8), 0), 495, 505);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CrossfadeUsesBothSourceHandlesAndProducesTheSameSamplesAfterSeek()
    {
        var a = Path.Combine(Path.GetTempPath(), "prometheus-cross-a-" + Guid.NewGuid().ToString("N") + ".wav");
        var b = Path.Combine(Path.GetTempPath(), "prometheus-cross-b-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteConstantWave(a, 96000, 1000);
            WriteConstantWave(b, 96000, 3000);
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            var track = sequence.Tracks.First(t => t.Kind == "audio");
            var left = PlaceTone(project, track, a, 0, .2, 1, 2);
            var right = PlaceTone(project, track, b, 1, .3, 1, 2);
            Assert.Equal(.6, AudioTransitionOperations.MaximumSeconds(project, sequence,
                AudioTransitionOperations.Crossfade, left.Id, right.Id), 6);
            AudioTransitionOperations.Apply(project, AudioTransitionOperations.Crossfade, left.Id, right.Id, .4);

            var inside = AudioMixPlan.Create(project, 1.05);
            Assert.Equal(2, inside.Inputs.Count);
            Assert.Equal(1.25, inside.Inputs[0].SourceStart, 6);
            Assert.Equal(.35, inside.Inputs[1].SourceStart, 6);
            var pcm = Mix(project, 0);
            Assert.InRange(Sample(pcm, .7), 995, 1005);
            Assert.InRange(Sample(pcm, .9), 1495, 1505);
            Assert.InRange(Sample(pcm, 1), 1995, 2005);
            Assert.InRange(Sample(pcm, 1.1), 2495, 2505);
            Assert.InRange(Sample(pcm, 1.3), 2995, 3005);
            var seek = Mix(project, 1.05);
            Assert.InRange(Math.Abs(Sample(pcm, 1.05) - Sample(seek, 0)), 0, 2);

            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
            var undo = new UndoRedoManager(restored);
            undo.Commit(p => CollisionEngine.SplitClip(p.ActiveSequence!, left.Id, .5), "Dividir audio");
            var updated = undo.CurrentProject.ActiveSequence!;
            var cross = Assert.Single(updated.AudioTransitions);
            Assert.NotEqual(left.Id, cross.ClipId);
            Assert.Equal(.4, cross.DurationSeconds, 6);
            Assert.True(undo.Undo());
            Assert.Equal(left.Id, Assert.Single(undo.CurrentProject.ActiveSequence!.AudioTransitions).ClipId);
        }
        finally { File.Delete(a); File.Delete(b); }
    }

    private static Clip PlaceTone(Project project, Track track, string path, double start, double sourceIn,
        double duration, double sourceDuration)
    {
        var stream = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "audio", Duration = Rational.Time(sourceDuration) };
        var asset = new Asset { Id = Guid.NewGuid().ToString("N"), Streams = [stream],
            Source = new MediaSource { Kind = "local", Uri = path } };
        project.Assets.Add(asset);
        var clip = new Clip { TrackId = track.Id, AssetId = asset.Id, StreamId = stream.Id,
            TimelineStart = Rational.Time(start), SourceIn = Rational.Time(sourceIn), Duration = Rational.Time(duration) };
        project.ActiveSequence!.Clips.Add(clip);
        return clip;
    }

    private static byte[] Mix(Project project, double start)
    {
        using var process = Process.Start(AudioMixPlan.Create(project, start).BuildProcess())!;
        using var pcm = new MemoryStream();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(pcm);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr.GetAwaiter().GetResult());
        return pcm.ToArray();
    }

    private static short Sample(byte[] pcm, double time) => BitConverter.ToInt16(pcm, (int)Math.Round(time * 48000) * 4);

    private static void WriteConstantWave(string path, int frames = 48000, short value = 1000)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + frames * 4);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(48000);
        writer.Write(192000);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(frames * 4);
        for (var i = 0; i < frames; i++) { writer.Write(value); writer.Write(value); }
    }
}
