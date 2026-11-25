using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public class ProjectRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Recovery363-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectRecoveryStore _store;
    public ProjectRecoveryTests()
    {
        Directory.CreateDirectory(_root);
        _store = new ProjectRecoveryStore(Path.Combine(_root, "Recovery"));
    }

    [Fact]
    public void NamedAndUnnamedSessionsAreSeparateAndLiveProcessesAreExcluded()
    {
        var project = ProjectStore.CreateNew();
        var original = Path.Combine(_root, "Original.pthproj");
        ProjectStore.SaveTransactional(project, original);
        _store.Save(ProjectStore.Clone(project), original, "session-one");
        _store.Save(ProjectStore.Clone(project), null, "session-two");
        Assert.Empty(_store.List());
        var all = _store.List(includeActive: true);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, item => item.OriginalPath == original);
        Assert.Contains(all, item => item.OriginalPath == null);
        Assert.Null(ProjectStore.LoadTransactional(original, out _).Recovery);
    }

    [Fact]
    public void InterruptedSessionKeepsItsRevisionAndCorruptPrimaryOffersValidBackup()
    {
        var project = ProjectStore.CreateNew();
        var original = Path.Combine(_root, "Original.pthproj");
        ProjectStore.SaveTransactional(project, original);
        project.Name = "Saved second";
        ProjectStore.SaveTransactional(project, original);
        project.Revision = 7;
        _store.Save(project, original, "interrupted");
        var recoveryPath = _store.FileFor(project.Id, "interrupted");
        var snapshot = ProjectStore.LoadTransactional(recoveryPath, out _);
        snapshot.Recovery = snapshot.Recovery! with { ProcessId = int.MaxValue };
        ProjectStore.SaveTransactional(snapshot, recoveryPath);
        File.WriteAllText(original, "{broken");
        var candidates = _store.List(original);
        Assert.Contains(candidates, entry => !entry.IsBackup && entry.Revision == 7);
        Assert.Contains(candidates, entry => entry.IsBackup && entry.OriginalPath == original);
        Assert.Null(_store.Read(original));
    }

    [Fact]
    public void CleanupOnlyRemovesGeneratedRecoveryFilesAndResources()
    {
        var project = ProjectStore.CreateNew();
        var original = Path.Combine(_root, "Original.pthproj");
        ProjectStore.SaveTransactional(project, original);
        _store.Save(project, original, "cleanup");
        var recovery = _store.FileFor(project.Id, "cleanup");
        var assetPath = Path.Combine(ProjectStorage.GetDataDirectory(recovery), "marker.txt");
        File.WriteAllText(assetPath, "resource");
        _store.Remove(original);
        Assert.True(File.Exists(original));
        _store.Remove(recovery);
        Assert.False(File.Exists(recovery));
        Assert.False(Directory.Exists(ProjectStorage.GetDataDirectory(recovery)));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void RelativeMediaKeepsItsOriginalLocationWhenSavingToAnotherFolder()
    {
        var original = Path.Combine(_root, "Original.pthproj");
        var project = ProjectStore.CreateNew();
        project.Assets.Add(new Asset { Id = "relative", Source = new MediaSource { Uri = "clip.mp4", Relative = true },
            Representations = [new MediaRepresentation { Locator = new Locator("clip.mp4", true) }] });
        ProjectRecoveryStore.ResolveRelativeMedia(project, original);
        Assert.Equal(Path.Combine(_root, "clip.mp4"), project.Assets[0].Source.Uri);
        Assert.Equal(project.Assets[0].Source.Uri, project.Assets[0].EffectiveLocator!.Path);
        Assert.False(project.Assets[0].Source.Relative);
        Assert.False(project.Assets[0].EffectiveLocator!.Relative);
    }

    [Fact]
    public void ReplacementRequiresEveryUsedStreamAndItsVisibleSourceRange()
    {
        var project = ReplacementProject();
        var original = project.Assets[0];
        var videoOnly = Candidate(12, null);
        Assert.Throws<InvalidOperationException>(() => ProjectMediaRecovery.ValidateReplacement(project, original, videoOnly));
        var shortAudio = Candidate(12, 6);
        Assert.Throws<InvalidOperationException>(() => ProjectMediaRecovery.ValidateReplacement(project, original, shortAudio));
        var valid = Candidate(12, 10);
        Assert.Equal(2, ProjectMediaRecovery.ValidateReplacement(project, original, valid).Count);
    }

    [Fact]
    public void ReplacementAlsoChecksNestedChildren()
    {
        var project = ReplacementProject();
        var audio = project.ActiveSequence!.Clips.First(c => c.StreamId == "old-audio");
        project.ActiveSequence.Clips.Remove(audio);
        var container = project.ActiveSequence.Clips[0];
        container.NestedContent = new NestedContent { Clips = [audio] };
        Assert.Throws<InvalidOperationException>(() =>
            ProjectMediaRecovery.ValidateReplacement(project, project.Assets[0], Candidate(12, 6)));
    }

    [Fact]
    public void RelinkPreservesClipIdsTimingsAndStreamIdsWhileMappingNewOrdinals()
    {
        var project = ReplacementProject();
        var sequence = project.ActiveSequence!;
        var original = project.Assets[0];
        var ids = sequence.Clips.Select(c => c.Id).ToArray();
        var path = Path.Combine(_root, "new.mp4");
        File.WriteAllBytes(path, [1, 2, 3]);
        var candidate = Candidate(12, 10);
        candidate.Streams.Single(s => s.Kind == "audio").Ordinal = 3;
        ProjectMediaRecovery.Relink(project, original, path, candidate);
        Assert.Equal(ids, sequence.Clips.Select(c => c.Id));
        var audio = sequence.Clips.Single(c => c.StreamId == "old-audio");
        Assert.Equal(2, audio.SourceIn.Seconds);
        Assert.Equal(3, audio.Duration.Seconds);
        Assert.Equal(2, audio.Speed.Seconds);
        Assert.Equal(3, original.Streams.Single(s => s.Id == audio.StreamId).Ordinal);
        Assert.Equal(3, original.Representations[0].StreamOrdinals[audio.StreamId]);
        Assert.Equal(10, audio.AvailableSourceDurationSeconds);
    }

    [Fact]
    public void RejectedReplacementLeavesTheWholeProjectUntouched()
    {
        var project = ReplacementProject();
        var before = ProjectStore.Serialize(project);
        Assert.Throws<InvalidOperationException>(() =>
            ProjectMediaRecovery.Relink(project, project.Assets[0], Path.Combine(_root, "bad.mp4"), Candidate(12, 6)));
        Assert.Equal(before, ProjectStore.Serialize(project));
    }

    [Fact]
    public void ReplacementDisappearingBeforeCommitDoesNotChangeTheProject()
    {
        var project = ReplacementProject();
        var before = ProjectStore.Serialize(project);
        Assert.Throws<FileNotFoundException>(() =>
            ProjectMediaRecovery.Relink(project, project.Assets[0], Path.Combine(_root, "gone.mp4"), Candidate(12, 10)));
        Assert.Equal(before, ProjectStore.Serialize(project));
    }

    [Fact]
    public void UnusedMissingStreamsAreReplacedWithActualAvailableStreams()
    {
        var project = ReplacementProject();
        project.ActiveSequence!.Clips.RemoveAll(clip => clip.StreamId == "old-audio");
        var path = Path.Combine(_root, "silent.mp4");
        File.WriteAllBytes(path, [1]);
        ProjectMediaRecovery.Relink(project, project.Assets[0], path, Candidate(12, null));
        Assert.Equal("old-video", Assert.Single(project.Assets[0].Streams).Id);
    }

    [Fact]
    public void AFailedResourceCopyKeepsThePreviousRecoveryResourceIntact()
    {
        var destination = Path.Combine(_root, "Previous.cube");
        File.WriteAllText(destination, "previous");
        Assert.Throws<FileNotFoundException>(() =>
            ProjectStorage.CopyResourceTransactional(Path.Combine(_root, "missing.cube"), destination));
        Assert.Equal("previous", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp-*"));
    }

    [Fact]
    public async Task RealProbeRejectsAudioThatEndsBeforeTheVideoAndEditedRange()
    {
        var path = Path.Combine(_root, "ShortAudio.mp4");
        var start = new System.Diagnostics.ProcessStartInfo(Prometheus.Client.Services.LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-v", "error", "-f", "lavfi", "-i", "color=s=32x32:r=1:d=12",
            "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=6", "-c:v", "libx264",
            "-preset", "ultrafast", "-c:a", "aac", "-y", path })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await errors);
        var project = ReplacementProject();
        var found = await Prometheus.Client.Services.LocalMediaService.ProbeAsync(path);
        Assert.Equal(6, found.Streams.Single(stream => stream.Kind == "audio").Duration.Seconds, 2);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Prometheus.Client.Services.MediaRelinkService.CheckAsync(project, project.Assets[0], path));
        Assert.Contains("termina antes", error.Message);
    }

    [Fact]
    public void MalformedRecoveriesAreSkippedWithoutHidingValidSessions()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Recovery"));
        var invalid = ProjectStore.CreateNew();
        invalid.Format = "wrong-format";
        File.WriteAllText(Path.Combine(_root, "Recovery", "invalid.pthproj"), ProjectStore.Serialize(invalid));
        File.WriteAllText(Path.Combine(_root, "Recovery", "null.pthproj"), "null");
        var valid = ProjectStore.CreateNew();
        _store.Save(valid, null, "valid");
        Assert.Single(_store.List(includeActive: true));
    }

    [Theory]
    [InlineData("dip-black", false)]
    [InlineData("dip-white", false)]
    [InlineData("dissolve", true)]
    public void ReplacementTailMatchesTheActualTransitionSourceRequirement(string kind, bool needsTail)
    {
        var project = ReplacementProject();
        var seq = project.ActiveSequence!;
        var left = seq.Clips.First(c => c.StreamId == "old-video");
        left.Duration = Rational.Time(2);
        var right = new Clip { AssetId = left.AssetId, StreamId = left.StreamId, TrackId = left.TrackId,
            TimelineStart = Rational.Time(2), Duration = Rational.Time(1), SourceIn = Rational.Time(.5) };
        seq.Clips.Add(right);
        seq.VideoTransitions.Add(new VideoTransition { Kind = kind, LeftClipId = left.Id,
            RightClipId = right.Id, DurationFrames = 24 });
        var candidate = Candidate(2, 10);
        if (needsTail)
            Assert.Throws<InvalidOperationException>(() => ProjectMediaRecovery.ValidateReplacement(project, project.Assets[0], candidate));
        else
            Assert.Equal(2, ProjectMediaRecovery.ValidateReplacement(project, project.Assets[0], candidate).Count);
    }

    [Fact]
    public async Task RecoveryFindsValidatedMediaInOriginalAssetsWithoutChangingResourceRoot()
    {
        var original = Path.Combine(_root, "Original.pthproj");
        var recovery = Path.Combine(_root, "Recovery", "session.pthproj");
        var media = Path.Combine(ProjectStorage.GetDataDirectory(original), "clip.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        using (var writer = new BinaryWriter(File.Create(media)))
        {
            const int bytes = 48000 * 2 * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
            writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        var asset = await Prometheus.Client.Services.LocalMediaService.ProbeAsync(media);
        var project = ProjectStore.CreateNew();
        project.Assets.Add(asset);
        asset.Source.Uri = Path.Combine(_root, "gone", "clip.wav");
        foreach (var rep in asset.Representations) rep.Locator.Path = asset.Source.Uri;
        var recovered = await Prometheus.Client.Services.MediaRelinkService.RecoverAsync(project, recovery, original);
        Assert.Equal(asset.Id, Assert.Single(recovered));
        Assert.Equal(media, asset.EffectiveLocator!.Path);
        Assert.Equal(1, asset.Streams.Single().Duration.Seconds, 3);
        Assert.Equal(recovery, new ProjectRecoveryEntry(recovery, original, "", DateTime.UtcNow, 0, false).ResourcePath);
    }

    private static Project ReplacementProject()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        project.Assets.Add(new Asset { Id = "media", Name = "Media", Streams = [
            new StreamInfo { Id = "old-video", Kind = "video", Ordinal = 0, Duration = Rational.Time(12) },
            new StreamInfo { Id = "old-audio", Kind = "audio", Ordinal = 1, Duration = Rational.Time(10) }
        ] });
        sequence.Clips.Add(new Clip { AssetId = "media", StreamId = "old-video",
            TrackId = sequence.Tracks.First(t => t.Kind == "video").Id, Duration = Rational.Time(3) });
        sequence.Clips.Add(new Clip { AssetId = "media", StreamId = "old-audio", SourceIn = Rational.Time(2),
            Speed = new Rational(2, 1), Duration = Rational.Time(3), TrackId = sequence.Tracks.First(t => t.Kind == "audio").Id });
        return project;
    }

    private static Asset Candidate(double videoDuration, double? audioDuration)
    {
        var result = new Asset { Id = "candidate", Streams = [
            new StreamInfo { Id = "new-video", Kind = "video", Ordinal = 0, Duration = Rational.Time(videoDuration) }
        ] };
        if (audioDuration is { } seconds)
            result.Streams.Add(new StreamInfo { Id = "new-audio", Kind = "audio", Ordinal = 1, Duration = Rational.Time(seconds) });
        return result;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
