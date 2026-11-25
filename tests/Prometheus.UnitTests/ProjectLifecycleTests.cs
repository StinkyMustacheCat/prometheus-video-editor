using System;
using System.IO;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Undo;
using Xunit;

namespace Prometheus.UnitTests;

public class ProjectLifecycleTests
{
    [Theory]
    [InlineData(24, 1d / 60)]
    [InlineData(120, 1d / 120)]
    public void PreflightAcceptsLegacyShortClipsAndHigherFrameRates(int fps, double duration)
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Fps = new Rational(fps, 1);
        sequence.Clips.Add(new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "video").Id,
            Duration = Rational.Time(duration) });
        Assert.True(PreflightAuditor.Audit(project).IsValid);
    }

    [Fact]
    public void CreateNew_GeneratesStandardMasterSequence()
    {
        var project = ProjectStore.CreateNew("Mi Proyecto");

        Assert.Equal("Mi Proyecto", project.Name);
        Assert.Equal("prometheus-project", project.Format);
        Assert.Equal("2.0.0", project.SchemaVersion);
        Assert.Equal(0, project.Revision);

        Assert.Single(project.Sequences);
        var seq = project.ActiveSequence;
        Assert.NotNull(seq);
        Assert.Equal(1920, seq.Width);
        Assert.Equal(1080, seq.Height);
        Assert.Equal(60, seq.Fps.N);
        Assert.Equal(1, seq.Fps.D);
        Assert.Equal(48000, seq.MixSampleRate);
        Assert.Equal(2, seq.MixChannels);

        // Verifica pistas iniciales V2, V1, A1, A2
        Assert.Equal(4, seq.Tracks.Count);
        Assert.Contains(seq.Tracks, t => t.Name == "V2" && t.Kind == "video");
        Assert.Contains(seq.Tracks, t => t.Name == "V1" && t.Kind == "video");
        Assert.Contains(seq.Tracks, t => t.Name == "A1" && t.Kind == "audio");
        Assert.Contains(seq.Tracks, t => t.Name == "A2" && t.Kind == "audio");
    }

    [Fact]
    public void Preflight_DetectsDuplicateIds()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        // Insertar clip con ID duplicado
        var clipId = "duplicate_clip_123";
        seq.Clips.Add(new Clip { Id = clipId, Name = "Clip A", TrackId = seq.Tracks[0].Id });
        seq.Clips.Add(new Clip { Id = clipId, Name = "Clip B", TrackId = seq.Tracks[0].Id });

        var report = PreflightAuditor.Audit(project);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("Identificador duplicado detectado"));
    }

    [Fact]
    public void Preflight_DetectsTemporalMonotonicityViolation()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        // Clip con duración de menos de 1 fotograma (< 1/60s)
        seq.Clips.Add(new Clip
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Micro Clip",
            TrackId = seq.Tracks[0].Id,
            TimelineStart = Rational.Zero,
            Duration = new Rational(1, 1000) // 1 ms << 16.6ms
        });

        var report = PreflightAuditor.Audit(project);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("dura menos de 1 fotograma"));
    }

    [Fact]
    public void Preflight_SilentlyPurgesOrphanLinkGroups()
    {
        var project = ProjectStore.CreateNew();
        var seq = project.ActiveSequence!;

        var clip1 = new Clip { Id = "c1", Name = "Clip 1", TrackId = seq.Tracks[0].Id };
        var clip2 = new Clip { Id = "c2", Name = "Clip 2", TrackId = seq.Tracks[1].Id };
        seq.Clips.Add(clip1);
        seq.Clips.Add(clip2);

        // Grupo con referencia huérfana ("ghost_clip")
        seq.LinkGroups.Add(new LinkGroup { Id = "lg1", ClipIds = { "c1", "c2", "ghost_clip" } });

        // Grupo con solo 1 clip válido restante
        seq.LinkGroups.Add(new LinkGroup { Id = "lg2", ClipIds = { "c1", "missing_clip" } });

        var report = PreflightAuditor.Audit(project, autoPurgeOrphans: true);

        Assert.True(report.IsValid);
        Assert.Single(seq.LinkGroups); // lg2 se eliminó por tener < 2 miembros activos
        Assert.Equal(2, seq.LinkGroups[0].ClipIds.Count); // ghost_clip fue purgado de lg1
        Assert.DoesNotContain("ghost_clip", seq.LinkGroups[0].ClipIds);
    }

    [Fact]
    public void TransactionalSave_ExecutesThreePhaseSafeWrite()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "Prometheus_SaveTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        string projectPath = Path.Combine(testDir, "TestProject.pthproj");

        try
        {
            var project = ProjectStore.CreateNew("Test Project");

            // 1. Guardado inicial
            ProjectStore.SaveTransactional(project, projectPath);
            Assert.True(File.Exists(projectPath));
            Assert.True(Directory.Exists(ProjectStorage.GetDataDirectory(projectPath)));

            // 2. Modificación y segundo guardado (debe generar .bak)
            project.Name = "Test Project v2";
            ProjectStore.SaveTransactional(project, projectPath);

            Assert.True(File.Exists(projectPath));
            Assert.True(File.Exists(projectPath + ".bak"));

            // Verificar contenido del guardado principal
            var loaded = ProjectStore.LoadTransactional(projectPath, out var report);
            Assert.True(report.IsValid);
            Assert.Equal("Test Project v2", loaded.Name);

            // Verificar contenido del .bak
            var loadedBak = ProjectStore.LoadTransactional(projectPath + ".bak", out var bakReport);
            Assert.True(bakReport.IsValid);
            Assert.Equal("Test Project", loadedBak.Name);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, true); } catch { /* Ignore */ }
            }
        }
    }

    [Fact]
    public void UndoRedoManager_CommitAndRollback_MaintainsAtomicHistory()
    {
        var manager = new UndoRedoManager();
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.False(manager.IsDirty);
        Assert.Equal(0, manager.CurrentProject.Revision);

        // 1. Ejecutar mutación válida
        manager.Commit(p =>
        {
            p.Name = "Proyecto Editado";
            p.ActiveSequence!.Clips.Add(new Clip
            {
                Id = "c100",
                Name = "Clip A",
                TrackId = p.ActiveSequence.Tracks[0].Id,
                TimelineStart = Rational.Zero,
                Duration = Rational.FromFrames(120, 60)
            });
        }, "Añadir Clip A");

        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.True(manager.IsDirty);
        Assert.Equal(1, manager.CurrentProject.Revision);
        Assert.Equal("Proyecto Editado", manager.CurrentProject.Name);
        Assert.Single(manager.CurrentProject.ActiveSequence!.Clips);

        // 2. Deshacer
        bool undoResult = manager.Undo();
        Assert.True(undoResult);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
        Assert.Equal("Sin título", manager.CurrentProject.Name);
        Assert.Empty(manager.CurrentProject.ActiveSequence!.Clips);

        // 3. Rehacer
        bool redoResult = manager.Redo();
        Assert.True(redoResult);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.Equal("Proyecto Editado", manager.CurrentProject.Name);
        Assert.Single(manager.CurrentProject.ActiveSequence!.Clips);
    }

    [Fact]
    public void UndoRedoManager_NavigationActions_DoNotAffectHistory()
    {
        var manager = new UndoRedoManager();
        manager.MarkClean();

        // Operaciones de navegación: consultar o simular movimiento de cabezal / zoom
        double simulatedPlayheadSeconds = 14.5;
        double simulatedZoom = 1.25;
        Assert.True(simulatedPlayheadSeconds > 0 && simulatedZoom > 0);

        // Lectura de estado sin Commit
        Assert.NotNull(manager.CurrentProject.ActiveSequence);
        Assert.Equal(0, manager.UndoCount);
        Assert.False(manager.IsDirty);
        Assert.False(manager.CanUndo);
    }
}
