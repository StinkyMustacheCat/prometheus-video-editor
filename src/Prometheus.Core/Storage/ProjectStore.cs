using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Core.Storage;

public static class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonOptions) { WriteIndented = false };

    internal static string SerializeSnapshot(Project project) => JsonSerializer.Serialize(project, SnapshotOptions);

    internal static byte[] SerializeSnapshotBytes(Project project) => JsonSerializer.SerializeToUtf8Bytes(project, SnapshotOptions);

    internal static Project DeserializeSnapshot(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<Project>(json, JsonOptions)
        ?? throw new InvalidDataException("La instantánea del proyecto no contiene una estructura válida.");

    public static Project CreateNew(string name = "Sin título")
    {
        var project = new Project
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Revision = 0,
            Format = "prometheus-project",
            SchemaVersion = "2.0.0"
        };

        var sequence = new Sequence
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Montaje 1",
            Width = 1920,
            Height = 1080,
            Fps = new Rational(60, 1),
            MixSampleRate = 48000,
            MixChannels = 2,
            ColorSpace = "rec709-sdr"
        };

        sequence.Tracks.Add(new Track { Id = Guid.NewGuid().ToString("N"), Name = "V2", Kind = "video", Order = 1 });
        sequence.Tracks.Add(new Track { Id = Guid.NewGuid().ToString("N"), Name = "V1", Kind = "video", Order = 0 });
        sequence.Tracks.Add(new Track { Id = Guid.NewGuid().ToString("N"), Name = "A1", Kind = "audio", Order = 0 });
        sequence.Tracks.Add(new Track { Id = Guid.NewGuid().ToString("N"), Name = "A2", Kind = "audio", Order = 1 });

        project.Sequences.Add(sequence);
        project.ActiveSequenceId = sequence.Id;

        return project;
    }

    /// <summary>
    /// Puebla clips de muestra y grupos vinculados para la secuencia maestra.
    /// </summary>
    public static void PopulateSampleClips(Project project)
    {
        var seq = project.ActiveSequence;
        if (seq == null) return;

        var v2 = seq.Tracks.Find(t => t.Name == "V2");
        var v1 = seq.Tracks.Find(t => t.Name == "V1");
        var a1 = seq.Tracks.Find(t => t.Name == "A1");
        var a2 = seq.Tracks.Find(t => t.Name == "A2");

        if (v1 == null || a1 == null) return;

        // Clip 1: CamA_001.mp4 [0.5s - 6.5s]
        var clipV1_1 = new Clip
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "CamA_001.mp4",
            TrackId = v1.Id,
            TimelineStart = Rational.Time(0.5),
            Duration = Rational.Time(6.0),
            SourceIn = Rational.Zero,
            Speed = Rational.One
        };

        // Clip 2: Entrevista_02.mov [6.55s - 11.55s] (Micro-gap exacto de 0.05s / 3 frames en 6.50s - 6.55s)
        var clipV1_2 = new Clip
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Entrevista_02.mov",
            TrackId = v1.Id,
            TimelineStart = Rational.Time(6.55),
            Duration = Rational.Time(5.0),
            SourceIn = Rational.Zero,
            Speed = Rational.One,
            AvailableSourceDurationSeconds = 4.2 // Demostración de Short Tail
        };

        // Clip V2: Grafismo_Inferior.png [2.0s - 6.0s]
        if (v2 != null)
        {
            var clipV2 = new Clip
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Grafismo_Inferior.png",
                TrackId = v2.Id,
                TimelineStart = Rational.Time(2.0),
                Duration = Rational.Time(4.0),
                SourceIn = Rational.Zero,
                Speed = Rational.One
            };
            seq.Clips.Add(clipV2);
        }

        // Clip A1_1: CamA_001.wav [0.5s - 6.5s]
        var clipA1_1 = new Clip
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "CamA_001.wav",
            TrackId = a1.Id,
            TimelineStart = Rational.Time(0.5),
            Duration = Rational.Time(6.0),
            SourceIn = Rational.Zero,
            Speed = Rational.One
        };

        // Clip A1_2: Entrevista_02.wav [6.55s - 11.55s]
        var clipA1_2 = new Clip
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Entrevista_02.wav",
            TrackId = a1.Id,
            TimelineStart = Rational.Time(6.55),
            Duration = Rational.Time(5.0),
            SourceIn = Rational.Zero,
            Speed = Rational.One
        };

        // Clip A2: Musica_Fondo.wav [0.0s - 14.0s]
        if (a2 != null)
        {
            var clipA2 = new Clip
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Musica_Fondo.wav",
                TrackId = a2.Id,
                TimelineStart = Rational.Zero,
                Duration = Rational.Time(14.0),
                SourceIn = Rational.Zero,
                Speed = Rational.One
            };
            seq.Clips.Add(clipA2);
        }

        seq.Clips.Add(clipV1_1);
        seq.Clips.Add(clipV1_2);
        seq.Clips.Add(clipA1_1);
        seq.Clips.Add(clipA1_2);

        // Vinculación A/V
        seq.LinkGroups.Add(new LinkGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            ClipIds = new() { clipV1_1.Id, clipA1_1.Id }
        });

        seq.LinkGroups.Add(new LinkGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            ClipIds = new() { clipV1_2.Id, clipA1_2.Id }
        });
    }

    public static string Serialize(Project project)
    {
        return JsonSerializer.Serialize(project, JsonOptions);
    }

    public static Project Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<Project>(json, JsonOptions);
        if (project == null)
            throw new InvalidDataException("El JSON del proyecto no contiene una estructura válida.");

        return project;
    }

    public static Project Clone(Project project)
    {
        return DeserializeSnapshot(SerializeSnapshotBytes(project));
    }

    /// <summary>
    /// Guarda el proyecto de forma atómica y transaccional (.tmp -> .bak -> .pthproj).
    /// </summary>
    public static PreflightReport SaveTransactional(Project project, string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

        ProjectStorage.EnsureDirectories(fullPath);

        // 1. Auditoría de preflight previa a la escritura
        var report = PreflightAuditor.Audit(project, directory, autoPurgeOrphans: true);
        if (!report.IsValid)
        {
            throw new InvalidOperationException($"No se puede guardar el proyecto debido a fallos de integridad: {string.Join("; ", report.Errors)}");
        }

        string tmpPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string bakPath = fullPath + ".bak";

        try
        {
            // 2. Escribir copia en archivo temporal
            string json = Serialize(project);
            File.WriteAllText(tmpPath, json, new UTF8Encoding(false));

            // 3. Verificar que el archivo temporal se puede leer íntegramente
            var verified = Deserialize(File.ReadAllText(tmpPath));
            var verifyReport = PreflightAuditor.Audit(verified, directory, autoPurgeOrphans: false);
            if (!verifyReport.IsValid)
            {
                throw new InvalidOperationException("Falló la verificación del volcado temporal de guardado.");
            }

            // 4. Reemplazo atómico transaccional
            if (File.Exists(fullPath))
            {
                File.Replace(tmpPath, fullPath, bakPath);
            }
            else
            {
                File.Move(tmpPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { /* Ignore */ }
            }
        }

        return report;
    }

    /// <summary>
    /// Carga el proyecto desde disco ejecutando el preflight de integridad estructural y detección de medios desconectados.
    /// </summary>
    public static Project LoadTransactional(string filePath, out PreflightReport report)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Archivo de proyecto no encontrado: '{fullPath}'.", fullPath);
        }

        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string json = File.ReadAllText(fullPath, Encoding.UTF8);

        var project = Deserialize(json);
        foreach (var sequence in project.Sequences.Where(s => s.Name == "Secuencia Maestra"))
            sequence.Name = "Montaje 1";

        foreach (var sequence in project.Sequences)
        {
            if (SubtitleTrack.Find(sequence) is not { } subtitle) continue;
            subtitle.Name = SubtitleTrack.Name;
            subtitle.Locked = false;
        }

        // Preflight estructural con purga de huérfanos y auditoría de medios
        report = PreflightAuditor.Audit(project, directory, autoPurgeOrphans: true);

        if (!report.IsValid)
        {
            throw new InvalidDataException($"Error de integridad al abrir proyecto: {string.Join("; ", report.Errors)}");
        }

        return project;
    }
}
