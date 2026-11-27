using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Primitives;
using Prometheus.Core.Undo;
using Prometheus.Client.Services;
using System.Text;

namespace Prometheus.UnitTests;

public class TranscriptionTests
{
    private const string Wordline = "UNIDAD: segundos (precisión de milisegundos)\n" +
        "FORMATO: inicio fin contenido\n\n" +
        "0.100 0.300 Hola\n0.320 0.600 mundo.\n" +
        "0.600 1.700 [PAUSA 1.100]\n1.800 2.000 Otra\n2.050 2.300 frase.\n";

    [Fact]
    public void SelectedAudioUsesTheOrdinalOfItsCurrentRepresentation()
    {
        var asset = new Asset
        {
            Streams = [new() { Id = "voice", Kind = "audio", Ordinal = 2 }],
            Representations =
            [
                new() { Locator = new Locator("proxy.mkv"), StreamOrdinals = new() { ["voice"] = 1 } }
            ]
        };
        var clip = new Clip { StreamId = "voice" };

        Assert.Equal(1, LocalMediaService.AudioOrdinal(asset, clip, "proxy.mkv"));
        Assert.Equal(2, LocalMediaService.AudioOrdinal(asset, clip, "original.mkv"));
        clip.StreamId = "";
        Assert.Null(LocalMediaService.AudioOrdinal(asset, clip, "proxy.mkv"));
    }

    [Fact]
    public void WordlineRoundTripAndPhraseOrWordProjection()
    {
        var doc = TranscriptOperations.Parse(Wordline, "Entrevista", 10);
        Assert.Equal(4, doc.Words.Count);
        Assert.Equal("word", doc.TimingPrecision);
        Assert.Equal(2, TranscriptOperations.Captions(doc, 10).Count);
        Assert.Equal("Hola mundo.", TranscriptOperations.Captions(doc, 10)[0].Text);
        doc.CaptionMode = "word";
        Assert.Equal(4, TranscriptOperations.Captions(doc, 10).Count);
        var saved = TranscriptOperations.ToWordline(doc);
        Assert.Equal(doc.Words.Select(word => word.Text),
            TranscriptOperations.Parse(saved, "Copia", 0).Words.Select(word => word.Text));
    }

    [Fact]
    public void EditingTranscriptAndReapplyingOnlyReplacesItsOwnCaptions()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var document = TranscriptOperations.Parse(Wordline, "Entrevista", 10);
        sequence.Transcriptions.Add(document);
        var manual = SubtitleTrack.Add(project, "Manual", 0, 2);
        var first = TranscriptOperations.Apply(project, document.Id);
        Assert.Equal(2, first.Count);
        Assert.Equal(3, sequence.Clips.Count(clip => clip.TrackId == SubtitleTrack.Find(sequence)!.Id));
        Assert.Equal("Hola mundo.", project.Assets.Single(asset => asset.Id == sequence.Clips
            .Single(clip => clip.Id == first[0]).AssetId).TextOverlay!.Content);
        document.Words[0].Text = "Buenas";
        var second = TranscriptOperations.Apply(project, document.Id);
        Assert.Equal(2, second.Count);
        Assert.Contains(sequence.Clips, clip => clip.Id == manual.Id);
        Assert.DoesNotContain(sequence.Clips, clip => clip.Id == first[0]);
        Assert.Equal("Buenas mundo.", project.Assets.Single(asset => asset.Id == sequence.Clips
            .Single(clip => clip.Id == second[0]).AssetId).TextOverlay!.Content);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        Assert.Equal("Buenas", restored.ActiveSequence!.Transcriptions.Single().Words[0].Text);
        Assert.Equal(2, restored.ActiveSequence.Clips.Count(clip => clip.TranscriptId == document.Id));
    }

    [Fact]
    public void ManualOverlapPreventsReapplyWithoutDeletingOldCaptions()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var document = TranscriptOperations.Parse(Wordline, "Entrevista", 10);
        sequence.Transcriptions.Add(document);
        var first = TranscriptOperations.Apply(project, document.Id);
        SubtitleTrack.Add(project, "Manual", 20, 1);
        document.TimelineStart = 20;
        Assert.Throws<InvalidOperationException>(() => TranscriptOperations.Apply(project, document.Id));
        Assert.All(first, id => Assert.Contains(sequence.Clips, clip => clip.Id == id));
    }

    [Fact]
    public void SrtKeepsPhraseTimingWithoutInventingWordTimestamps()
    {
        var srt = "1\n00:00:01,000 --> 00:00:03,000\nHola mundo\n\n" +
            "2\n00:00:04,000 --> 00:00:05,000\nSiguiente frase";
        var doc = TranscriptOperations.Parse(srt, "SRT", 0);
        Assert.Equal("cue", doc.TimingPrecision);
        Assert.Equal(2, TranscriptOperations.Captions(doc, 0).Count);
        Assert.Throws<InvalidOperationException>(() => TranscriptOperations.ToWordline(doc));
    }

    [Fact]
    public async Task WordlineMetadataCanBeReadFromWavWithoutChangingAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-wordline-" + Guid.NewGuid().ToString("N") + ".wav");
        var payload = Encoding.UTF8.GetBytes("WORDLINE_TIMED_TEXT_VERSION=1\n" + Wordline);
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write((uint)(4 + 8 + 4 + 8 + payload.Length + payload.Length % 2));
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("LIST"));
                writer.Write((uint)(4 + 8 + payload.Length + payload.Length % 2));
                writer.Write(Encoding.ASCII.GetBytes("INFO"));
                writer.Write(Encoding.ASCII.GetBytes("ICMT"));
                writer.Write((uint)payload.Length);
                writer.Write(payload);
                if (payload.Length % 2 == 1) writer.Write((byte)0);
            }
            var value = await WordlineMediaReader.ReadAsync(path);
            Assert.NotNull(value);
            Assert.StartsWith("WORDLINE_TIMED_TEXT_VERSION=1\n", value);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WordlineFromTrimmedFasterClipMapsToTimelineSeconds()
    {
        var source = TranscriptOperations.Parse(Wordline, "Audio", 0);
        var clip = new Clip { TimelineStart = Rational.Time(8), SourceIn = Rational.Time(0.25),
            Duration = Rational.Time(1), Speed = Rational.Time(2) };
        var mapped = TranscriptOperations.AlignToClip(source, clip);
        Assert.Equal(clip.Id, mapped.SourceClipId);
        Assert.Equal(8, mapped.TimelineStart);
        Assert.Equal(0, mapped.Words[0].Start, 3);
        Assert.Equal(0.175, mapped.Words[1].End, 3);
        Assert.Equal(8, TranscriptOperations.Captions(mapped, mapped.TimelineStart)[0].Start, 3);
    }

    [Fact]
    public void MovingAudioPreservesEditedCapsulesAndDeletingItKeepsTheTranscript()
    {
        var (manager, audioId, transcriptId, cues) = LinkedCaptions();
        manager.Commit(project =>
        {
            var first = project.ActiveSequence!.Clips.Single(clip => clip.Id == cues[0]);
            first.TimelineStart = Rational.Time(10.2);
            project.Assets.Single(asset => asset.Id == first.AssetId).TextOverlay!.Content = "Corrección en cápsula";
            project.ActiveSequence.Clips.Single(clip => clip.Id == cues[1]).TranscriptId = null;
        }, "Editar y desvincular cápsulas");

        manager.Commit(project => project.ActiveSequence!.Clips.Single(clip => clip.Id == audioId)
            .TimelineStart = Rational.Time(15), "Mover audio");
        var moved = manager.CurrentProject.ActiveSequence!;
        Assert.Equal(15.2, moved.Clips.Single(clip => clip.Id == cues[0]).TimelineStart.Seconds, 4);
        Assert.Equal(11.8, moved.Clips.Single(clip => clip.Id == cues[1]).TimelineStart.Seconds, 4);
        var assetId = moved.Clips.Single(clip => clip.Id == cues[0]).AssetId;
        Assert.Equal("Corrección en cápsula", manager.CurrentProject.Assets.Single(asset => asset.Id == assetId)
            .TextOverlay!.Content);

        manager.Commit(project => project.ActiveSequence!.Clips.RemoveAll(clip => clip.Id == audioId), "Eliminar audio");
        var after = manager.CurrentProject.ActiveSequence!;
        Assert.DoesNotContain(after.Clips, clip => clip.Id == cues[0]);
        Assert.DoesNotContain(manager.CurrentProject.Assets, asset => asset.Id == assetId);
        Assert.Contains(after.Clips, clip => clip.Id == cues[1]);
        var doc = Assert.Single(after.Transcriptions);
        Assert.Equal(transcriptId, doc.Id);
        Assert.Null(doc.SourceClipId);
        Assert.Equal(15, doc.TimelineStart);
        Assert.True(manager.Undo());
        Assert.True(manager.Undo());
        Assert.Equal(10.2, manager.CurrentProject.ActiveSequence!.Clips.Single(clip => clip.Id == cues[0])
            .TimelineStart.Seconds, 4);
        Assert.True(manager.Redo());
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(manager.CurrentProject)).ActiveSequence!;
        Assert.Equal(audioId, restored.Transcriptions.Single().SourceClipId);
        Assert.Equal(15.2, restored.Clips.Single(clip => clip.Id == cues[0]).TimelineStart.Seconds, 4);
        Assert.Null(restored.Clips.Single(clip => clip.Id == cues[1]).TranscriptId);
    }

    [Fact]
    public void AudioMoveCannotOverwriteAnotherCaptionOrMoveACapsuleOnALockedTrack()
    {
        var (manager, audioId, _, cues) = LinkedCaptions();
        void MoveAudio(Project project) => project.ActiveSequence!.Clips.Single(clip => clip.Id == audioId)
            .TimelineStart = Rational.Time(20);

        manager.Commit(project => SubtitleTrack.Add(project, "Manual", 20, 1), "Subtítulo manual");
        Assert.Throws<InvalidOperationException>(() => manager.Commit(MoveAudio, "Mover audio"));
        Assert.Equal(10, manager.CurrentProject.ActiveSequence!.Clips.Single(clip => clip.Id == audioId)
            .TimelineStart.Seconds);
        Assert.True(manager.Undo());
        manager.Commit(project => SubtitleTrack.Find(project.ActiveSequence!)!.Locked = true, "Bloquear pista");
        Assert.Throws<InvalidOperationException>(() => manager.Commit(MoveAudio, "Mover audio"));
        Assert.Throws<InvalidOperationException>(() => manager.Commit(project => project.ActiveSequence!.Clips
            .RemoveAll(clip => clip.Id == audioId), "Eliminar audio"));
        Assert.Equal(10.1, manager.CurrentProject.ActiveSequence!.Clips.Single(clip => clip.Id == cues[0])
            .TimelineStart.Seconds, 4);
    }

    [Fact]
    public void SplitThenMoveAndRetimeTheRightAudioPieceKeepsOneTranscript()
    {
        var (manager, audioId, transcriptId, cues) = LinkedCaptions();
        manager.Commit(p => CollisionEngine.SplitClip(p.ActiveSequence!, audioId, 11), "Dividir audio");
        var sequence = manager.CurrentProject.ActiveSequence!;
        var right = sequence.Clips.Single(c => c.AudioSplitAncestors?.Contains(audioId) == true);
        Assert.Equal(right.Id, TranscriptOperations.AudioSourceId(sequence, sequence.Clips.Single(c => c.Id == cues[1])));
        manager.Commit(p =>
        {
            var part = p.ActiveSequence!.Clips.Single(c => c.Id == right.Id);
            part.TimelineStart = Rational.Time(14);
            part.Speed = new Rational(2, 1);
            part.Duration = Rational.Time(1);
        }, "Mover y acelerar el fragmento");
        sequence = manager.CurrentProject.ActiveSequence!;
        var document = Assert.Single(sequence.Transcriptions);
        Assert.Equal(transcriptId, document.Id);
        Assert.Equal(4, document.Words.Count);
        Assert.Equal(10.1, sequence.Clips.Single(c => c.Id == cues[0]).TimelineStart.Seconds, 4);
        Assert.Equal(14.4, sequence.Clips.Single(c => c.Id == cues[1]).TimelineStart.Seconds, 4);
        Assert.Equal(14.4, TranscriptOperations.TimelinePositionForWord(sequence, document, document.Words[2])!.Value, 4);

        manager.Commit(p => p.ActiveSequence!.Clips.RemoveAll(c => c.Id == audioId), "Eliminar el fragmento inicial");
        sequence = manager.CurrentProject.ActiveSequence!;
        Assert.Equal(audioId, Assert.Single(sequence.Transcriptions).SourceClipId);
        Assert.DoesNotContain(sequence.Clips, c => c.Id == cues[0]);
        Assert.Contains(sequence.Clips, c => c.Id == cues[1]);
        manager.Commit(p => TranscriptOperations.Apply(p, transcriptId), "Actualizar subtítulos");
        var updated = Assert.Single(manager.CurrentProject.ActiveSequence!.Clips.Where(c => c.TranscriptId == transcriptId));
        Assert.Equal(14.4, updated.TimelineStart.Seconds, 4);
    }

    [Theory]
    [InlineData(false, 18)]
    [InlineData(true, 17.25)]
    public void SelectedSilencesCutLinkedMediaAndCaptionsInOneUndo(bool classic, double otherStart)
    {
        var (manager, audioId, transcriptId, cues) = LinkedCaptions();
        string videoId = "", otherId = "";
        manager.Commit(p =>
        {
            var sequence = p.ActiveSequence!;
            sequence.CloseGapAllTracks = classic;
            var videoTracks = sequence.Tracks.Where(t => t.Kind == "video" && t.Role != "subtitle").ToArray();
            var linked = AddVideo(p, videoTracks[0], 10, 3);
            videoId = linked.Id;
            var later = AddVideo(p, videoTracks[1], 18, 1);
            otherId = later.Id;
            sequence.LinkGroups.Add(new LinkGroup { ClipIds = [audioId, videoId] });
        }, "Preparar toma vinculada");
        manager.Commit(p => SilenceCutOperations.Apply(p.ActiveSequence!, audioId,
            [new SilenceCut(10.75, 11), new SilenceCut(11.25, 11.75)]), "Eliminar silencios");

        var sequence = manager.CurrentProject.ActiveSequence!;
        Assert.Single(sequence.Transcriptions);
        Assert.Equal(otherStart, sequence.Clips.Single(c => c.Id == otherId).TimelineStart.Seconds, 4);
        Assert.Equal(11.05, sequence.Clips.Single(c => c.Id == cues[1]).TimelineStart.Seconds, 4);
        var audioClips = sequence.Clips.Where(c => c.Id == audioId || c.AudioSplitAncestors?.Contains(audioId) == true).ToArray();
        Assert.Equal(3, audioClips.Length);
        Assert.Equal(2.25, audioClips.Sum(c => c.Duration.Seconds), 4);
        Assert.All(audioClips, audio =>
        {
            var group = Assert.Single(sequence.LinkGroups.Where(g => g.ClipIds.Contains(audio.Id)));
            var video = sequence.Clips.Single(c => c.Id == group.ClipIds.Single(id => id != audio.Id));
            Assert.Equal(audio.TimelineStart, video.TimelineStart);
            Assert.Equal(audio.Duration, video.Duration);
        });
        Assert.True(manager.Undo());
        sequence = manager.CurrentProject.ActiveSequence!;
        Assert.Equal(3, sequence.Clips.Single(c => c.Id == audioId).Duration.Seconds, 4);
        Assert.Equal(3, sequence.Clips.Single(c => c.Id == videoId).Duration.Seconds, 4);
        Assert.Equal(11.8, sequence.Clips.Single(c => c.Id == cues[1]).TimelineStart.Seconds, 4);
        Assert.Equal(transcriptId, Assert.Single(sequence.Transcriptions).Id);
    }

    [Fact]
    public void LeavingSilenceGapsKeepsLaterMediaAndCaptionsAtTheOriginalTime()
    {
        var (manager, audioId, _, cues) = LinkedCaptions();
        manager.Commit(p => SilenceCutOperations.Apply(p.ActiveSequence!, audioId,
            [new SilenceCut(10.75, 11.5)], closeGaps: false), "Dejar huecos");
        var sequence = manager.CurrentProject.ActiveSequence!;
        var tail = Assert.Single(sequence.Clips.Where(c => c.AudioSplitAncestors?.Contains(audioId) == true));
        Assert.Equal(11.5, tail.TimelineStart.Seconds, 4);
        Assert.Equal(11.8, sequence.Clips.Single(c => c.Id == cues[1]).TimelineStart.Seconds, 4);
        Assert.True(manager.Undo());
        Assert.Equal(3, manager.CurrentProject.ActiveSequence!.Clips.Single(c => c.Id == audioId).Duration.Seconds, 4);
    }

    [Fact]
    public void CaptureInsertionPreservesAudioAncestryCaptionsAndOutgoingFade()
    {
        var (manager, audioId, documentId, cues) = LinkedCaptions();
        manager.Commit(p => p.ActiveSequence!.AudioTransitions.Add(new AudioTransition {
            Kind = AudioTransitionOperations.FadeOut, ClipId = audioId, DurationSeconds = .2 }), "Fade");
        manager.Commit(p => CapturedFramePlacement.Insert(p, new Asset {
            Id = "capture-asset", Name = "Capture", RecommendedDuration = Rational.Time(5),
            Streams = [new StreamInfo { Id = "capture-stream", Kind = "image" }] }, 11), "Insert capture");
        var sequence = manager.CurrentProject.ActiveSequence!;
        var tail = sequence.Clips.Single(c => c.AudioSplitAncestors?.Contains(audioId) == true);
        var caption = sequence.Clips.Single(c => c.Id == cues[1]);
        Assert.Equal(16.8, caption.TimelineStart.Seconds, 4);
        Assert.Equal(tail.Id, caption.SubtitleAudioClipId);
        Assert.Equal(tail.Id, Assert.Single(sequence.AudioTransitions).ClipId);
        Assert.Equal(documentId, Assert.Single(sequence.Transcriptions).Id);
        Assert.True(manager.Undo());
        Assert.Equal(11.8, manager.CurrentProject.ActiveSequence!.Clips.Single(c => c.Id == cues[1]).TimelineStart.Seconds, 4);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(60)]
    [InlineData(120)]
    public void CuttingAtCaptionEdgeKeepsOnlyDisplayableFragments(int fps)
    {
        var (manager, audioId, documentId, _) = LinkedCaptions();
        manager.Commit(p => p.ActiveSequence!.Fps = new Rational(fps, 1), "FPS");
        manager.Commit(p => CollisionEngine.SplitClip(p.ActiveSequence!, audioId, 10.105), "Cut near caption");
        var sequence = manager.CurrentProject.ActiveSequence!;
        Assert.All(sequence.Clips.Where(c => c.TranscriptId == documentId),
            cue => Assert.True(cue.Duration.Seconds + .0001 >= 1d / fps));
        Assert.Equal(4, Assert.Single(sequence.Transcriptions).Words.Count);
    }

    private static Clip AddVideo(Project project, Track track, double start, double duration)
    {
        var asset = new Asset { Id = Guid.NewGuid().ToString("N"), Name = "Video de prueba",
            Streams = [new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "video" }] };
        project.Assets.Add(asset);
        var clip = new Clip { AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            TrackId = track.Id, TimelineStart = Rational.Time(start), Duration = Rational.Time(duration) };
        project.ActiveSequence!.Clips.Add(clip);
        return clip;
    }

    private static (UndoRedoManager Manager, string AudioId, string TranscriptId, IReadOnlyList<string> Cues) LinkedCaptions()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var asset = new Asset { Id = "audio-prueba", Name = "Audio de prueba",
            Streams = [new StreamInfo { Id = "audio-prueba-stream", Kind = "audio" }] };
        project.Assets.Add(asset);
        var audio = new Clip { AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            TrackId = sequence.Tracks.First(track => track.Kind == "audio").Id,
            TimelineStart = Rational.Time(10), Duration = Rational.Time(3) };
        sequence.Clips.Add(audio);
        var document = TranscriptOperations.AlignToClip(TranscriptOperations.Parse(Wordline, "Audio", 0), audio);
        sequence.Transcriptions.Add(document);
        var cues = TranscriptOperations.Apply(project, document.Id);
        return (new UndoRedoManager(project), audio.Id, document.Id, cues);
    }
}
