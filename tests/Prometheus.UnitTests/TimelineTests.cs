using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Xunit;

namespace Prometheus.UnitTests;

public class TimelineTests
{
    [Fact]
    public void SnappingEngine_SnapsWithinEightPixelThreshold()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var clip = new Clip
        {
            Id = "c1",
            Name = "Clip 1",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Time(2.0),
            Duration = Rational.Time(3.0) // End = 5.0s
        };
        seq.Clips.Add(clip);

        double zoom = 40.0; // 40 px/s -> 8 px = 8 / 40 = 0.2s threshold

        // 1. Probar punto cercano al inicio (2.0s): 2.10s (delta 0.1s <= 0.2s) -> Snap
        var snapNearStart = SnappingEngine.Snap(2.10, seq, playheadSeconds: 10.0, zoomPixelsPerSecond: zoom);
        Assert.True(snapNearStart.IsSnapped);
        Assert.Equal(2.0, snapNearStart.SnappedTime);

        // 2. Probar punto cercano al final (5.0s): 4.95s (delta 0.05s <= 0.2s) -> Snap
        var snapNearEnd = SnappingEngine.Snap(4.95, seq, playheadSeconds: 10.0, zoomPixelsPerSecond: zoom);
        Assert.True(snapNearEnd.IsSnapped);
        Assert.Equal(5.0, snapNearEnd.SnappedTime);

        // 3. Probar punto lejano: 3.5s (delta > 0.2s) -> No Snap
        var noSnap = SnappingEngine.Snap(3.50, seq, playheadSeconds: 10.0, zoomPixelsPerSecond: zoom);
        Assert.False(noSnap.IsSnapped);
        Assert.Equal(3.50, noSnap.SnappedTime);
    }

    [Fact]
    public void CollisionEngine_SplitClip_DividesAccuratelyAndPreservesLinkGroups()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var clip = new Clip
        {
            Id = "original_clip",
            Name = "Video Clip",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Time(1.0),
            Duration = Rational.Time(4.0), // 1.0s a 5.0s
            SourceIn = Rational.Time(0.0)
        };
        seq.Clips.Add(clip);

        seq.LinkGroups.Add(new LinkGroup { Id = "lg1", ClipIds = { "original_clip", "audio_partner" } });

        // Cortar en 3.0s (mitad del clip)
        var (left, right) = CollisionEngine.SplitClip(seq, "original_clip", 3.0);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.Equal(1.0, left.TimelineStart.Seconds);
        Assert.Equal(2.0, left.Duration.Seconds);

        Assert.Equal(3.0, right.TimelineStart.Seconds);
        Assert.Equal(2.0, right.Duration.Seconds);
        Assert.Equal(2.0, right.SourceIn.Seconds);

        // Comprobar link_group actualizado
        Assert.Contains(right.Id, seq.LinkGroups[0].ClipIds);
    }

    [Fact]
    public void CollisionEngine_Overwrite_NonDestructivelySlicesUnderlyingClip()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        // Clip de fondo de 0 a 10s
        var baseClip = new Clip
        {
            Id = "base",
            Name = "Base Clip",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Zero,
            Duration = Rational.Time(10.0)
        };
        seq.Clips.Add(baseClip);

        // Clip entrante de 3s a 6s
        var incoming = new Clip
        {
            Id = "incoming",
            Name = "Incoming Clip",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Time(3.0),
            Duration = Rational.Time(3.0) // 3.0 a 6.0s
        };
        seq.Clips.Add(incoming);

        CollisionEngine.Overwrite(seq, new[] { "incoming" });

        var trackClips = seq.Clips.Where(c => c.TrackId == seq.Tracks[0].Id).OrderBy(c => c.TimelineStart.Seconds).ToList();

        // Debe haber 3 clips: trozo izquierdo (0 a 3s), incoming (3 a 6s), trozo derecho (6 a 10s)
        Assert.Equal(3, trackClips.Count);

        Assert.Equal(0.0, trackClips[0].TimelineStart.Seconds);
        Assert.Equal(3.0, trackClips[0].Duration.Seconds);

        Assert.Equal("incoming", trackClips[1].Id);
        Assert.Equal(3.0, trackClips[1].TimelineStart.Seconds);
        Assert.Equal(3.0, trackClips[1].Duration.Seconds);

        Assert.Equal(6.0, trackClips[2].TimelineStart.Seconds);
        Assert.Equal(4.0, trackClips[2].Duration.Seconds);
    }

    [Fact]
    public void CollisionEngine_DetectMicroGaps_IdentifiesGapsUnderThreeFrames()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var clip1 = new Clip
        {
            Id = "c1",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Zero,
            Duration = Rational.Time(2.0) // Termina en 2.0s
        };
        var clip2 = new Clip
        {
            Id = "c2",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Time(2.03), // Hueco de 0.03s <= 0.05s (3 frames)
            Duration = Rational.Time(2.0)
        };
        seq.Clips.Add(clip1);
        seq.Clips.Add(clip2);

        var gaps = CollisionEngine.DetectMicroGaps(seq);

        Assert.Single(gaps);
        Assert.Equal(2.0, Math.Round(gaps[0].StartSeconds, 4));
        Assert.Equal(2.03, Math.Round(gaps[0].EndSeconds, 4));
        Assert.Equal(0.03, Math.Round(gaps[0].DurationSeconds, 4));
    }

    [Fact]
    public void CollisionEngine_RippleDeleteRange_ClosesEmptyGaps()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var clipA = new Clip
        {
            Id = "a",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Zero,
            Duration = Rational.Time(2.0)
        };
        var clipB = new Clip
        {
            Id = "b",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Time(3.0), // Hueco de 1s (2s a 3s)
            Duration = Rational.Time(2.0)
        };
        seq.Clips.Add(clipA);
        seq.Clips.Add(clipB);

        CollisionEngine.RippleDeleteRange(seq, 2.0, 1.0);

        Assert.Equal(0.0, clipA.TimelineStart.Seconds);
        Assert.Equal(2.0, clipB.TimelineStart.Seconds); // Desplazado 1.0s a la izquierda
    }

    [Fact]
    public void CollisionEngine_CheckShortTail_DetectsExcessDuration()
    {
        var clip = new Clip
        {
            Id = "tail_clip",
            SourceIn = Rational.Time(2.0),
            Duration = Rational.Time(5.0), // Requiere 7.0s de medio fuente
            Speed = Rational.One
        };

        // Si el archivo fuente solo dura 4.0s:
        var info = CollisionEngine.CheckShortTail(clip, streamDurationSeconds: 4.0);

        Assert.True(info.HasShortTail);
        Assert.Equal(2.0, info.AvailableMediaSeconds); // Solo hay 4 - 2 = 2 segundos disponibles
        Assert.Equal(3.0, info.MissingDurationSeconds); // Faltan 5 - 2 = 3 segundos
    }

    [Fact]
    public void CollisionEngine_SetClipSpeed_AdjustsDurationClamped()
    {
        var clip = new Clip
        {
            Id = "speed_clip",
            Duration = Rational.Time(4.0),
            Speed = Rational.One
        };

        // Aumentar velocidad al doble (2.0 = 200%) -> duración debe reducirse a la mitad (2.0s)
        CollisionEngine.SetClipSpeed(clip, 2.0);

        Assert.Equal(2.0, clip.Speed.Seconds);
        Assert.Equal(2.0, clip.Duration.Seconds);

        // Probar clamp al máximo (400%)
        CollisionEngine.SetClipSpeed(clip, 10.0);
        Assert.Equal(4.0, clip.Speed.Seconds);
    }

    [Fact]
    public void LinkedSpeedStopsAtEarliestNeighborWithoutDesynchronizingAudio()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var videoTrack = sequence.Tracks.First(t => t.Kind == "video");
        var audioTrack = sequence.Tracks.First(t => t.Kind == "audio");
        var video = new Clip { TrackId = videoTrack.Id, Duration = Rational.Time(4) };
        var audio = new Clip { TrackId = audioTrack.Id, Duration = Rational.Time(4) };
        sequence.Clips.AddRange(new[] { video, audio,
            new Clip { TrackId = audioTrack.Id, TimelineStart = Rational.Time(6), Duration = Rational.Time(2) } });
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = new List<string> { video.Id, audio.Id } });

        var limited = CollisionEngine.SetLinkedClipSpeed(sequence, video.Id, 0.5);

        Assert.True(limited);
        Assert.Equal(6, video.Duration.Seconds);
        Assert.Equal(video.Duration.Seconds, audio.Duration.Seconds);
        Assert.Equal(0.5, audio.Speed.Seconds);
    }

    [Fact]
    public void TimelineSelection_HandlesClipsAndGapsProperly()
    {
        var selection = new TimelineSelection();
        Assert.False(selection.HasSelection);
        Assert.False(selection.HasGapSelection);

        selection.SelectClip("clip1");
        Assert.True(selection.HasSelection);
        Assert.True(selection.IsSelected("clip1"));
        Assert.Single(selection.SelectedClipIds);

        selection.SelectClips(new[] { "clip2", "clip3" }, addToSelection: true);
        Assert.Equal(3, selection.SelectedClipIds.Count);

        selection.SelectGap("track1", 2.0, 2.05);
        Assert.True(selection.HasGapSelection);
        Assert.Empty(selection.SelectedClipIds);
        Assert.NotNull(selection.CurrentGap);
        Assert.Equal("track1", selection.CurrentGap.TrackId);
        Assert.Equal(2.0, selection.CurrentGap.StartSeconds);
        Assert.Equal(2.05, selection.CurrentGap.EndSeconds);
        Assert.Equal(0.05, Math.Round(selection.CurrentGap.DurationSeconds, 4));

        selection.Clear();
        Assert.False(selection.HasSelection);
        Assert.Null(selection.CurrentGap);
    }

    [Fact]
    public void ProjectStore_PopulateSampleClips_CreatesDemoClipsWithMicroGapAndLinks()
    {
        var project = ProjectStore.CreateNew("Demo");
        ProjectStore.PopulateSampleClips(project);

        var seq = project.ActiveSequence!;
        Assert.NotNull(seq);
        Assert.NotEmpty(seq.Clips);
        Assert.Equal(6, seq.Clips.Count);

        // Verifica LinkGroups para sincronización A/V
        Assert.Equal(2, seq.LinkGroups.Count);
        Assert.All(seq.LinkGroups, lg => Assert.Equal(2, lg.ClipIds.Count));

        // Verifica detección de micro-hueco en la secuencia de muestra
        var microGaps = CollisionEngine.DetectMicroGaps(seq);
        Assert.NotEmpty(microGaps);
        Assert.Contains(microGaps, g => Math.Abs(g.DurationSeconds - 0.05) < 0.001);

        // Verifica clip con Short Tail
        var shortTailClip = seq.Clips.Find(c => c.Name == "Entrevista_02.mov");
        Assert.NotNull(shortTailClip);
        Assert.True(shortTailClip.AvailableSourceDurationSeconds.HasValue);
        var tailInfo = CollisionEngine.CheckShortTail(shortTailClip, shortTailClip.AvailableSourceDurationSeconds!.Value);
        Assert.True(tailInfo.HasShortTail);
    }

    [Fact]
    public void CollisionEngine_RangeTrim_RemovesAndSlicesAreaProperly()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        // Clip 1: 0.0s a 4.0s
        var c1 = new Clip { Id = "c1", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Zero, Duration = Rational.Time(4.0) };
        // Clip 2: 5.0s a 10.0s
        var c2 = new Clip { Id = "c2", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Time(5.0), Duration = Rational.Time(5.0) };
        seq.Clips.Add(c1);
        seq.Clips.Add(c2);

        // Recortar rango de 2.0s a 7.0s
        bool trimmed = CollisionEngine.RangeTrim(seq, 2.0, 7.0);
        Assert.True(trimmed);

        // c1 debe terminar en 2.0s
        Assert.Equal(2.0, c1.Duration.Seconds);

        // c2 debe haber sido recortado de 5.0-7.0s, empezando ahora en 7.0s y durando 3.0s
        var remainingC2 = seq.Clips.Find(c => c.Id == "c2");
        Assert.NotNull(remainingC2);
        Assert.Equal(7.0, remainingC2.TimelineStart.Seconds);
        Assert.Equal(3.0, remainingC2.Duration.Seconds);
    }

    [Fact]
    public void CollisionEngine_RollEdit_AdjustsContiguousClipsSimultaneously()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var left = new Clip { Id = "left", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Zero, Duration = Rational.Time(5.0) };
        var right = new Clip { Id = "right", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Time(5.0), Duration = Rational.Time(5.0) };
        seq.Clips.Add(left);
        seq.Clips.Add(right);

        // Mover el punto de corte de 5.0s a 6.0s (+1.0s)
        bool rolled = CollisionEngine.RollEdit(seq, "left", "right", 6.0);
        Assert.True(rolled);

        Assert.Equal(6.0, left.Duration.Seconds);
        Assert.Equal(6.0, right.TimelineStart.Seconds);
        Assert.Equal(4.0, right.Duration.Seconds);
        Assert.Equal(1.0, right.SourceIn.Seconds);
    }

    [Fact]
    public void CollisionEngine_DuplicateClips_PreservesOriginalAndCleansNames()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var orig = new Clip { Id = "orig", Name = "Clip_01.mp4", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Time(2.0), Duration = Rational.Time(4.0) };
        seq.Clips.Add(orig);

        var copies = CollisionEngine.DuplicateClips(seq, new[] { "orig" }, 5.0);
        Assert.Single(copies);

        var copy1 = copies[0];
        Assert.Equal("Clip_01.mp4 (Copia)", copy1.Name);
        Assert.Equal(7.0, copy1.TimelineStart.Seconds);
        Assert.Equal(2.0, orig.TimelineStart.Seconds); // Original permanece intacto en 2.0s

        // Duplicar la copia para probar incremento numérico (Copia 2)
        var secondCopies = CollisionEngine.DuplicateClips(seq, new[] { copy1.Id }, 6.0);
        Assert.Single(secondCopies);
        Assert.Equal("Clip_01.mp4 (Copia 2)", secondCopies[0].Name);
    }

    [Fact]
    public void SnappingEngine_SnapClip_SnapsForwardAndBackward()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        // Clip 1 fijo de 0.0s a 4.0s
        seq.Clips.Add(new Clip { Id = "c1", Name = "Clip1", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Zero, Duration = Rational.Time(4.0) });
        // Clip 2 fijo de 10.0s a 14.0s
        seq.Clips.Add(new Clip { Id = "c2", Name = "Clip2", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Time(10.0), Duration = Rational.Time(4.0) });

        double zoom = 40.0; // 8px / 40 = 0.2s tolerancia

        // 1. Snapping hacia atrás (Cabeza del clip acercándose al final de c1 en 4.0s)
        // Clip que se arrastra con duración 3.0s propuesto en 4.10s (dentro de los 0.2s de tolerancia)
        var snapBack = SnappingEngine.SnapClip(
            proposedStartSeconds: 4.10,
            durationSeconds: 3.0,
            sequence: seq,
            playheadSeconds: 20.0,
            zoomPixelsPerSecond: zoom,
            excludedClipIds: new[] { "moving" });

        Assert.True(snapBack.IsSnapped);
        Assert.Equal(4.0, snapBack.SnappedTime); // Cabeza se imanta al fin de c1

        // 2. Snapping hacia adelante (Cola del clip acercándose al inicio de c2 en 10.0s)
        // Clip con duración 3.0s propuesto en 6.90s -> su cola estaría en 9.90s (delta de 0.1s respecto a 10.0s <= 0.2s)
        var snapForward = SnappingEngine.SnapClip(
            proposedStartSeconds: 6.90,
            durationSeconds: 3.0,
            sequence: seq,
            playheadSeconds: 20.0,
            zoomPixelsPerSecond: zoom,
            excludedClipIds: new[] { "moving" });

        Assert.True(snapForward.IsSnapped);
        Assert.Equal(7.0, snapForward.SnappedTime); // 10.0s - 3.0s = 7.0s inicio imantado
    }

    [Fact]
    public void CollisionEngine_RollEdit_RollsBackwardAccurately()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var left = new Clip { Id = "left", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Zero, Duration = Rational.Time(6.0) };
        var right = new Clip { Id = "right", TrackId = seq.Tracks[0].Id, TimelineStart = Rational.Time(6.0), Duration = Rational.Time(4.0), SourceIn = Rational.Time(2.0) };
        seq.Clips.Add(left);
        seq.Clips.Add(right);

        // Rodar el corte hacia la izquierda (de 6.0s a 4.0s: delta = -2.0s)
        bool rolled = CollisionEngine.RollEdit(seq, "left", "right", 4.0);
        Assert.True(rolled);

        Assert.Equal(4.0, left.Duration.Seconds);
        Assert.Equal(4.0, right.TimelineStart.Seconds);
        Assert.Equal(6.0, right.Duration.Seconds);
        Assert.Equal(0.0, right.SourceIn.Seconds); // SourceIn rodó 2.0s hacia atrás
    }

    [Fact]
    public void CollisionEngine_AddTrack_CreatesVideoAndAudioTracks()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        int initialVideoCount = seq.Tracks.Count(t => t.Kind == "video");
        int initialAudioCount = seq.Tracks.Count(t => t.Kind == "audio");

        var newVTrack = CollisionEngine.AddTrack(seq, "video");
        Assert.Equal("video", newVTrack.Kind);
        Assert.Equal($"V{initialVideoCount + 1}", newVTrack.Name);
        Assert.True(newVTrack.Enabled);
        Assert.False(newVTrack.Locked);

        var newATrack = CollisionEngine.AddTrack(seq, "audio");
        Assert.Equal("audio", newATrack.Kind);
        Assert.Equal($"A{initialAudioCount + 1}", newATrack.Name);
    }

    [Fact]
    public void CollisionEngine_DetectGaps_And_RippleDeleteGap_ClosesSpace()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;
        var trackId = seq.Tracks[0].Id;

        // Clip 1 de 0.0s a 3.0s
        seq.Clips.Add(new Clip { Id = "c1", TrackId = trackId, TimelineStart = Rational.Zero, Duration = Rational.Time(3.0) });
        // Hueco de 3.0s a 6.0s (duración 3.0s)
        // Clip 2 de 6.0s a 10.0s
        seq.Clips.Add(new Clip { Id = "c2", TrackId = trackId, TimelineStart = Rational.Time(6.0), Duration = Rational.Time(4.0) });

        var gaps = CollisionEngine.DetectGaps(seq, trackId);
        Assert.Single(gaps);
        Assert.Equal(3.0, gaps[0].StartSeconds);
        Assert.Equal(6.0, gaps[0].EndSeconds);
        Assert.Equal(3.0, gaps[0].DurationSeconds);

        // Cerrar el hueco mediante Ripple Delete
        bool deleted = CollisionEngine.RippleDeleteGap(seq, trackId, 3.0, 6.0);
        Assert.True(deleted);

        // c2 debe haberse desplazado 3.0s a la izquierda hasta 3.0s
        var c2 = seq.Clips.Find(c => c.Id == "c2");
        Assert.NotNull(c2);
        Assert.Equal(3.0, c2.TimelineStart.Seconds);

        // Ahora no deben quedar gaps
        var remainingGaps = CollisionEngine.DetectGaps(seq, trackId);
        Assert.Empty(remainingGaps);
    }

    [Fact]
    public void ClassicGapCloseMovesUnlinkedLaterMediaAndRespectsTrackLocks()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        sequence.CloseGapAllTracks = true;
        var video = sequence.Tracks.First(track => track.Kind == "video");
        var audio = sequence.Tracks.First(track => track.Kind == "audio");
        var laterAudio = new Clip { TrackId = audio.Id, TimelineStart = Rational.Time(8), Duration = Rational.Time(2) };
        var laterVideo = new Clip { TrackId = video.Id, TimelineStart = Rational.Time(6), Duration = Rational.Time(2) };
        sequence.Clips.AddRange([new Clip { TrackId = video.Id, Duration = Rational.Time(3) }, laterVideo, laterAudio]);
        audio.Locked = true;
        Assert.Throws<InvalidOperationException>(() => CollisionEngine.RippleDeleteGap(sequence, video.Id, 3, 6));
        Assert.Equal(6, laterVideo.TimelineStart.Seconds);
        Assert.Equal(8, laterAudio.TimelineStart.Seconds);
        audio.Locked = false;
        Assert.True(CollisionEngine.RippleDeleteGap(sequence, video.Id, 3, 6));
        Assert.Equal(3, laterVideo.TimelineStart.Seconds);
        Assert.Equal(5, laterAudio.TimelineStart.Seconds);
    }

    [Fact]
    public void ClosingGapDoesNotMoveUnrelatedTracksAcrossExistingMedia()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var video = sequence.Tracks.First(t => t.Kind == "video").Id;
        var audio = sequence.Tracks.First(t => t.Kind == "audio").Id;
        sequence.Clips.AddRange([
            new Clip { Id = "v-before", TrackId = video, Duration = Rational.Time(3) },
            new Clip { Id = "v-after", TrackId = video, TimelineStart = Rational.Time(6), Duration = Rational.Time(2) },
            new Clip { Id = "a-before", TrackId = audio, Duration = Rational.Time(7) },
            new Clip { Id = "a-after", TrackId = audio, TimelineStart = Rational.Time(7), Duration = Rational.Time(2) }
        ]);

        Assert.True(CollisionEngine.RippleDeleteGap(sequence, video, 3, 6));
        Assert.Equal(3, sequence.Clips.First(c => c.Id == "v-after").TimelineStart.Seconds);
        Assert.Equal(7, sequence.Clips.First(c => c.Id == "a-after").TimelineStart.Seconds);
        Assert.Empty(CollisionEngine.DetectGaps(sequence, video));
    }

    [Fact]
    public void ClosingGapRejectsLinkedCollisionWithoutChangingEitherTrack()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var video = sequence.Tracks.First(t => t.Kind == "video").Id;
        var audio = sequence.Tracks.First(t => t.Kind == "audio").Id;
        sequence.Clips.AddRange([
            new Clip { Id = "v-before", TrackId = video, Duration = Rational.Time(3) },
            new Clip { Id = "v-after", TrackId = video, TimelineStart = Rational.Time(6), Duration = Rational.Time(2) },
            new Clip { Id = "a-before", TrackId = audio, Duration = Rational.Time(5) },
            new Clip { Id = "a-after", TrackId = audio, TimelineStart = Rational.Time(6), Duration = Rational.Time(2) }
        ]);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = { "v-after", "a-after" } });

        Assert.Throws<InvalidOperationException>(() => CollisionEngine.RippleDeleteGap(sequence, video, 3, 6));
        Assert.Equal(6, sequence.Clips.First(c => c.Id == "v-after").TimelineStart.Seconds);
        Assert.Equal(6, sequence.Clips.First(c => c.Id == "a-after").TimelineStart.Seconds);
    }

    [Fact]
    public void ClosingGapMovesLinkedCompanionWhenBothLanesHaveRoom()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        var video = sequence.Tracks.First(t => t.Kind == "video").Id;
        var audio = sequence.Tracks.First(t => t.Kind == "audio").Id;
        sequence.Clips.AddRange([
            new Clip { Id = "v-before", TrackId = video, Duration = Rational.Time(3) },
            new Clip { Id = "v-after", TrackId = video, TimelineStart = Rational.Time(6), Duration = Rational.Time(2) },
            new Clip { Id = "a-before", TrackId = audio, Duration = Rational.Time(2) },
            new Clip { Id = "a-after", TrackId = audio, TimelineStart = Rational.Time(6), Duration = Rational.Time(2) }
        ]);
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = { "v-after", "a-after" } });

        Assert.True(CollisionEngine.RippleDeleteGap(sequence, video, 3, 6));
        Assert.Equal(3, sequence.Clips.First(c => c.Id == "v-after").TimelineStart.Seconds);
        Assert.Equal(3, sequence.Clips.First(c => c.Id == "a-after").TimelineStart.Seconds);
        Assert.Contains(sequence.LinkGroups, g => g.ClipIds.Contains("v-after") && g.ClipIds.Contains("a-after"));
    }
}
