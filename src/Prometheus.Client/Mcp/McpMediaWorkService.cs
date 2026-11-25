using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Mcp;

internal sealed class McpMediaWorkService(ProjectController controller)
{
    private readonly Dictionary<string, Job> _jobs = new();

    private sealed class Job(string kind, string projectId, string sequenceId, Clip clip, string path)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Kind { get; } = kind;
        public string ProjectId { get; } = projectId;
        public string SequenceId { get; } = sequenceId;
        public Clip Clip { get; } = clip;
        public string Path { get; } = path;
        public DateTime LastWrite { get; } = File.GetLastWriteTimeUtc(path);
        public CancellationTokenSource Cancellation { get; } = new();
        public string State { get; set; } = "running";
        public string Stage { get; set; } = "preparing";
        public double Progress { get; set; }
        public string? Error { get; set; }
        public object? Result { get; set; }
    }

    public object Execute(JsonElement args) => McpJson.String(args, "operation") switch
    {
        "start" => Start(args),
        "status" => Describe(Find(args)),
        "cancel" => Cancel(Find(args)),
        _ => throw new ArgumentException("operation debe ser start, status o cancel.")
    };

    public void CancelAll()
    {
        foreach (var job in _jobs.Values.Where(item => item.State == "running").ToArray()) Cancel(job);
    }

    private Job Find(JsonElement args)
    {
        var id = McpJson.String(args, "job_id") ?? throw new ArgumentException("Falta job_id.");
        return _jobs.TryGetValue(id, out var job) ? job : throw new ArgumentException("No existe ese trabajo.");
    }

    private static object Cancel(Job job)
    {
        if (job.State == "running")
        {
            job.State = "cancelling";
            job.Cancellation.Cancel();
        }
        return Describe(job);
    }

    private object Start(JsonElement args)
    {
        var kind = McpJson.String(args, "kind") ?? throw new ArgumentException("Falta kind.");
        var project = controller.CurrentProject;
        if (args.TryGetProperty("expected_revision", out _) && McpJson.Int(args, "expected_revision", -1) != project.Revision)
            throw new InvalidOperationException("El proyecto cambió. Vuelve a consultar su revisión.");
        var sequence = project.Sequences.FirstOrDefault(item =>
            item.Id == (McpJson.String(args, "sequence_id") ?? project.ActiveSequenceId))
            ?? throw new ArgumentException("Montaje no encontrado.");
        var clipId = McpJson.String(args, "clip_id") ?? controller.SelectedClipId;
        var clip = sequence.Clips.FirstOrDefault(item => item.Id == clipId)
            ?? throw new ArgumentException("Indica un clip del montaje.");
        var audioJob = kind is "whisper" or "silence";
        if (audioJob && !IsAudio(sequence, clip))
        {
            var group = sequence.LinkGroups.FirstOrDefault(item => item.ClipIds.Contains(clip.Id));
            clip = sequence.Clips.FirstOrDefault(item => group?.ClipIds.Contains(item.Id) == true && IsAudio(sequence, item))
                ?? throw new ArgumentException("Se necesita un clip de audio o un vídeo con audio vinculado.");
        }
        if (sequence.Tracks.Single(item => item.Id == clip.TrackId).Locked)
            throw new InvalidOperationException("Desbloquea la pista para editar este clip.");
        var (asset, path) = Media(project, clip);
        if (_jobs.Values.Any(item => item.ProjectId == project.Id && item.Clip.Id == clip.Id &&
            item.Kind == kind && item.State is "running" or "cancelling"))
            throw new InvalidOperationException("Ya hay un trabajo de ese tipo para este clip.");

        var job = new Job(kind, project.Id, sequence.Id, clip, path);
        Func<Task<object>> run = kind switch
        {
            "whisper" => PrepareWhisper(job, asset, args),
            "silence" => PrepareSilence(job, asset, sequence.Fps.Seconds, args),
            "sam_seed" => PrepareSeed(job, project, args),
            "sam_track" => PrepareTracking(job, project, args),
            _ => throw new ArgumentException("kind debe ser whisper, silence, sam_seed o sam_track.")
        };
        if (_jobs.Count >= 40)
        {
            var old = _jobs.Values.FirstOrDefault(item => item.State is "completed" or "cancelled" or "failed");
            if (old != null) _jobs.Remove(old.Id);
        }
        _jobs.Add(job.Id, job);
        _ = RunAsync(job, run);
        return Describe(job);
    }

    private async Task RunAsync(Job job, Func<Task<object>> action)
    {
        try
        {
            job.Result = await action();
            job.Progress = 1;
            job.State = "completed";
            job.Stage = "complete";
        }
        catch (OperationCanceledException)
        {
            job.State = "cancelled";
        }
        catch (Exception error)
        {
            job.State = "failed";
            job.Error = error.Message;
            if (controller.CurrentProject.Id == job.ProjectId)
                controller.ReportNotice("mcp-analysis:" + job.Id, "Error de análisis MCP", job.Kind + " · " + error.Message);
        }
        finally { job.Cancellation.Dispose(); }
    }

    private Func<Task<object>> PrepareWhisper(Job job, Asset asset, JsonElement args)
    {
        if (!WhisperTranscriptionService.IsAvailable) throw new InvalidOperationException("El motor Whisper local no está disponible.");
        var mode = McpJson.String(args, "caption_mode") ?? "phrase";
        if (mode is not ("phrase" or "word")) throw new ArgumentException("caption_mode debe ser phrase o word.");
        var apply = McpJson.Bool(args, "apply_captions");
        var stream = AudioOrdinal(asset, job.Clip, job.Path);
        return async () =>
        {
            var progress = new Progress<double>(value =>
            {
                if (job.State != "running") return;
                job.Stage = "transcribing";
                job.Progress = Math.Max(job.Progress, value);
            });
            var result = await WhisperTranscriptionService.TranscribeAsync(job.Path, job.Clip,
                "Whisper · " + asset.Name, job.Cancellation.Token, progress, stream);
            result.Document.CaptionMode = mode;
            job.Stage = "applying";
            IReadOnlyList<string> captions = [];
            Commit(job, (project, sequence, current) =>
            {
                result.Document.TimelineStart = current.TimelineStart.Seconds;
                sequence.Transcriptions.Add(result.Document);
                if (apply && result.Document.Words.Count != 0)
                    captions = TranscriptOperations.Apply(project, result.Document.Id);
            }, "Transcribir clip con Whisper");
            return new { document_id = result.Document.Id, words_count = result.Document.Words.Count,
                caption_ids = captions, mode, device = result.Device };
        };
    }

    private Func<Task<object>> PrepareSilence(Job job, Asset asset, double fps, JsonElement args)
    {
        var apply = McpJson.Bool(args, "apply");
        var ripple = McpJson.Bool(args, "ripple");
        var stream = AudioOrdinal(asset, job.Clip, job.Path);
        var settings = McpJson.String(args, "preset") switch
        {
            "soft" => new SilenceSettings(-42, 0.80, 0.20, 0.25),
            null or "balanced" => new SilenceSettings(-36, 0.50, 0.12, 0.18),
            "aggressive" => new SilenceSettings(-30, 0.30, 0.07, 0.10),
            _ => throw new ArgumentException("preset debe ser soft, balanced o aggressive.")
        };
        return async () =>
        {
            job.Stage = "detecting_silence";
            var cuts = await SilenceAnalysisService.AnalyzeAsync(job.Path, stream, job.Clip, fps,
                settings, job.Cancellation.Token);
            if (apply && cuts.Count != 0)
                Commit(job, (_, sequence, current) =>
                {
                    if (current.TimelineStart != job.Clip.TimelineStart) throw ChangedClip();
                    SilenceCutOperations.Apply(sequence, current.Id, cuts, closeGaps: ripple);
                }, "Eliminar silencios");
            return new { cuts, applied = apply && cuts.Count != 0 };
        };
    }

    private Func<Task<object>> PrepareSeed(Job job, Project project, JsonElement args)
    {
        if (!Sam2Bridge.IsAvailable) throw new InvalidOperationException("El motor SAM 2.1 local no está disponible.");
        var points = McpJson.Array(args, "points").EnumerateArray().Select(point =>
            (X: McpJson.Number(point, "x"), Y: McpJson.Number(point, "y"), Positive: McpJson.Bool(point, "positive", true))).ToArray();
        if (points.Length is < 1 or > 32 || !points.Any(point => point.Positive) ||
            points.Any(point => !(point.X is >= 0 and <= 1) || !(point.Y is >= 0 and <= 1)))
            throw new ArgumentException("Usa entre 1 y 32 puntos normalizados en el medio y al menos un punto positivo.");
        var still = StillImageRules.IsStill(project, job.Clip);
        var sourceTime = SourceTime(job.Clip, args);
        var name = McpJson.String(args, "name") ?? "SAM";
        return async () =>
        {
            var token = job.Cancellation.Token;
            var frame = await Task.Run(() =>
            {
                using var gpu = new GpuVideoSurface(job.Path, still: still, initialSeconds: sourceTime, exactDecode: !still);
                if (!gpu.RenderOffscreen(sourceTime, 1.0 / 120, token))
                    throw new InvalidOperationException("No se pudo leer el fotograma de referencia.");
                return gpu.CaptureAnalysisFrame();
            }, token);
            job.Stage = "segmenting";
            job.Progress = 0.15;
            await using var worker = new Sam2Bridge();
            await worker.PrepareAsync(frame.Width, frame.Height, frame.Bgra, token);
            var mask = await worker.PredictAsync(points.Select(point => (
                point.X * (frame.Width - 1), point.Y * (frame.Height - 1), point.Positive)).ToArray(), token);
            var seed = SamMaskKeyframe.Create(sourceTime, mask.Width, mask.Height, mask.Alpha);
            var layer = new ClipMaskLayer { Name = name, Kind = "sam", SamMasks = [seed] };
            Commit(job, (_, _, clip) => ClipMaskStack.Edit(clip).Add(layer), "Aplicar máscara SAM");
            return new { mask_id = layer.Id, seed_id = seed.Id, source_seconds = sourceTime, score = mask.Score };
        };
    }

    private Func<Task<object>> PrepareTracking(Job job, Project project, JsonElement args)
    {
        if (!Sam2VideoBridge.IsAvailable) throw new InvalidOperationException("El motor de seguimiento SAM 2.1 no está disponible.");
        if (StillImageRules.IsStill(project, job.Clip)) throw new ArgumentException("Una imagen fija no necesita seguimiento.");
        var projectPath = controller.CurrentProjectPath
            ?? throw new InvalidOperationException("Guarda el proyecto antes de iniciar el seguimiento.");
        projectPath = controller.ResourceProjectPath ?? projectPath;
        var requestedMask = McpJson.String(args, "mask_id");
        var mask = ClipMaskStack.Read(job.Clip).LastOrDefault(item => item.Kind == "sam" &&
            (requestedMask == null || item.Id == requestedMask))
            ?? throw new ArgumentException("Se necesita una capa SAM con una máscara inicial.");
        var seed = args.TryGetProperty("at_seconds", out _)
            ? SamMaskKeyframe.At(new Clip { SamMasks = mask.SamMasks }, SourceTime(job.Clip, args), project.Sequences.Single(s => s.Id == job.SequenceId).Fps.Seconds)
            : mask.SamMasks.LastOrDefault();
        if (seed == null) throw new ArgumentException("No hay una máscara inicial en ese fotograma.");
        var snapshot = ProjectStore.Clone(project);
        snapshot.ActiveSequenceId = job.SequenceId;
        var snapshotClip = snapshot.ActiveSequence!.Clips.Single(item => item.Id == job.Clip.Id);
        snapshotClip.SamMasks = mask.SamMasks;
        snapshotClip.SamMaskTracks = mask.SamMaskTracks;
        snapshotClip.ShapeMask = null;
        return async () =>
        {
            job.Stage = "tracking";
            var progress = new Progress<(int Done, int Total)>(value =>
            {
                if (job.State == "running" && value.Total > 0) job.Progress = (double)value.Done / value.Total;
            });
            var track = await Task.Run(() => SamTrackingService.Track(snapshot, job.Clip.Id, job.Path,
                projectPath, seed.SourceSeconds, seed, progress, job.Cancellation.Token), job.Cancellation.Token);
            Commit(job, (_, _, clip) =>
            {
                var layer = ClipMaskStack.Edit(clip).FirstOrDefault(item => item.Id == mask.Id && item.Kind == "sam" &&
                    item.SamMasks.Any(current => current.Id == seed.Id))
                    ?? throw new InvalidOperationException("La máscara inicial cambió durante el seguimiento.");
                var currentPath = controller.ResourceProjectPath ?? throw ChangedClip();
                SamMaskTrackStore.CopyOne(projectPath, currentPath, track.Id);
                layer.SamMaskTracks.Clear();
                layer.SamMaskTracks.Add(track);
            }, "Seguir máscara SAM");
            return new { mask_id = mask.Id, track_id = track.Id, frames_count = track.FrameCount };
        };
    }

    private void Commit(Job job, Action<Project, Sequence, Clip> mutation, string description)
    {
        job.Cancellation.Token.ThrowIfCancellationRequested();
        if (controller.CurrentProject.Id != job.ProjectId) throw ChangedClip();
        controller.UndoRedo.Commit(project =>
        {
            var sequence = project.Sequences.FirstOrDefault(item => item.Id == job.SequenceId) ?? throw ChangedClip();
            var clip = sequence.Clips.FirstOrDefault(item => item.Id == job.Clip.Id);
            if (clip == null || clip.AssetId != job.Clip.AssetId || clip.StreamId != job.Clip.StreamId ||
                clip.SourceIn != job.Clip.SourceIn || clip.Duration != job.Clip.Duration || clip.Speed != job.Clip.Speed ||
                sequence.Tracks.FirstOrDefault(item => item.Id == clip.TrackId) is not { Locked: false } ||
                !string.Equals(Media(project, clip).Path, job.Path, StringComparison.OrdinalIgnoreCase) ||
                File.GetLastWriteTimeUtc(job.Path) != job.LastWrite)
                throw ChangedClip();
            var previous = project.ActiveSequenceId;
            project.ActiveSequenceId = sequence.Id;
            try { mutation(project, sequence, clip); }
            finally { project.ActiveSequenceId = previous; }
        }, description);
    }

    private static (Asset Asset, string Path) Media(Project project, Clip clip)
    {
        var asset = project.Assets.FirstOrDefault(item => item.Id == clip.AssetId);
        var path = asset?.EffectiveLocator?.Path;
        if (asset == null || path == null || !File.Exists(path))
            throw new FileNotFoundException("El medio del clip no está disponible.");
        return (asset, Path.GetFullPath(path));
    }

    private static int AudioOrdinal(Asset asset, Clip clip, string path)
    {
        var stream = asset.Streams.FirstOrDefault(item => item.Id == clip.StreamId && item.Kind == "audio")
            ?? throw new ArgumentException("El medio no contiene la pista de audio seleccionada.");
        var representation = asset.Representations.FirstOrDefault(item =>
            string.Equals(item.Locator.Path, path, StringComparison.OrdinalIgnoreCase));
        return representation?.StreamOrdinals.GetValueOrDefault(stream.Id, stream.Ordinal) ?? stream.Ordinal;
    }

    private static bool IsAudio(Sequence sequence, Clip clip) =>
        sequence.Tracks.Any(item => item.Id == clip.TrackId && item.Kind == "audio");

    private double SourceTime(Clip clip, JsonElement args)
    {
        var timeline = args.TryGetProperty("at_seconds", out _) ? McpJson.Number(args, "at_seconds") : controller.Transport.PositionSeconds;
        if (timeline < clip.TimelineStart.Seconds || timeline >= clip.EndSeconds || !double.IsFinite(timeline))
            throw new ArgumentOutOfRangeException("at_seconds", "El fotograma debe estar dentro del clip.");
        return clip.SourceIn.Seconds + (timeline - clip.TimelineStart.Seconds) * clip.Speed.Seconds;
    }

    private static InvalidOperationException ChangedClip() =>
        new("El clip, su pista o su archivo cambiaron durante el análisis. Inicia el trabajo de nuevo.");

    private static object Describe(Job job) => new { job_id = job.Id, kind = job.Kind, state = job.State,
        stage = job.Stage, progress = job.Progress, project_id = job.ProjectId, sequence_id = job.SequenceId,
        clip_id = job.Clip.Id, error = job.Error, result = job.Result };
}
