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

namespace Prometheus.Client.Mcp;

internal sealed class McpExportService(ProjectController controller)
{
    private readonly Dictionary<string, Job> _jobs = new();
    private Job? _active;

    private sealed class Job
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public required string OutputPath { get; init; }
        public required string ProjectId { get; init; }
        public required string SequenceId { get; init; }
        public int Revision { get; init; }
        public required ExportInspection Inspection { get; set; }
        public required GpuExportOptions Options { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public CancellationTokenSource Cancellation { get; } = new();
        public string State { get; set; } = "queued";
        public double Progress { get; set; }
        public string? Error { get; set; }
        public ExportEncoder? Encoder { get; set; }
        public DateTime CreatedUtc { get; } = DateTime.UtcNow;
        public DateTime? FinishedUtc { get; set; }
    }

    public async Task<object> ExecuteAsync(JsonElement args, CancellationToken cancellation = default) => (McpJson.String(args, "operation") ?? "preflight") switch
    {
        "preflight" => await PreflightAsync(args, cancellation),
        "start" => await StartWithNoticeAsync(args, resume: false, cancellation),
        "resume" => await StartWithNoticeAsync(args, resume: true, cancellation),
        "status" => Describe(Find(args)),
        "cancel" => Cancel(args),
        _ => throw new ArgumentException("operation debe ser preflight, start, resume, status o cancel")
    };

    private async Task<object> PreflightAsync(JsonElement args, CancellationToken cancellation)
    {
        var project = ProjectStore.Clone(controller.CurrentProject);
        var sourceProjectPath = controller.ResourceProjectPath;
        var sequence = project.ActiveSequence;
        var options = Options(args, sequence);
        var output = McpJson.String(args, "output_path");
        var recovery = output == null || options.Format != ExportFormat.Mp4 ? null :
            await Task.Run(() => RecoverableExport.Find(output), cancellation);
        if (recovery != null) options = recovery.Options;
        var inspection = recovery == null ? await SequenceExportService.InspectAsync(project, sourceProjectPath, output, options, cancellation) :
            new ExportInspection(recovery.StartSeconds, recovery.EndSeconds, [], []);
        var space = recovery?.Space ?? (output != null && options.Recoverable && inspection.Ready
            ? await Task.Run(() => RecoverableExport.Estimate(output, inspection.EndSeconds - inspection.StartSeconds,
                options.VideoBitrateMbps, options.AudioBitrateKbps), cancellation) : null);
        return new { ready = inspection.Ready && (space?.Fits ?? true), issues = inspection.Issues, warnings = Warnings(inspection),
            recovery = recovery == null ? null : new { directory = recovery.Directory, revision = recovery.Revision,
                completed_frames = recovery.CompletedFrames, total_frames = recovery.TotalFrames },
            required_bytes = space?.RequiredBytes, free_bytes = space?.FreeBytes,
            project_id = project.Id, revision = project.Revision, sequence_id = sequence?.Id,
            start_seconds = inspection.StartSeconds, end_seconds = inspection.EndSeconds,
            width = options.Format == ExportFormat.Wav ? (int?)null : options.Width ?? sequence?.Width,
            height = options.Format == ExportFormat.Wav ? (int?)null : options.Height ?? sequence?.Height, fps = sequence?.Fps.Seconds,
            suggested_name = sequence == null ? null : ExportPresentation.SuggestedName(project, sequence),
            format = options.Format.ToString().ToLowerInvariant(), allow_offline = options.AllowOffline };
    }

    private async Task<object> StartWithNoticeAsync(JsonElement args, bool resume, CancellationToken cancellation)
    {
        var projectId = controller.CurrentProject.Id;
        try { return await StartAsync(args, resume, cancellation); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            if (controller.CurrentProject.Id != projectId) throw;
            if (McpJson.String(args, "output_path") is { Length: > 0 } path && Path.IsPathFullyQualified(path))
                controller.ReportExportFailure(controller.CurrentProject.Id, path, error.Message);
            else
                controller.ReportNotice("mcp:export:setup", "Error de exportación MCP", error.Message);
            throw;
        }
    }

    private async Task<object> StartAsync(JsonElement args, bool resume, CancellationToken cancellation)
    {
        if (_active is { State: "queued" or "running" or "cancelling" })
            throw new InvalidOperationException("Ya hay una exportación MCP en curso.");
        var stateVersion = controller.StateVersion;
        var project = ProjectStore.Clone(controller.CurrentProject);
        var sourceProjectPath = controller.ResourceProjectPath;
        var path = McpJson.String(args, "output_path") ?? throw new ArgumentException("Falta output_path.");
        var saved = resume ? await Task.Run(() => RecoverableExport.Find(path), cancellation)
            ?? throw new InvalidOperationException("No hay una exportación guardada para ese destino.") : null;
        if (args.TryGetProperty("expected_revision", out var expected) &&
            (!expected.TryGetInt32(out var revision) || revision != (saved?.Revision ?? project.Revision)))
            throw new InvalidOperationException("La revisión cambió; consulta de nuevo el montaje.");
        var options = Options(args, project.ActiveSequence);
        if (resume && !SequenceExportService.SupportsRecovery(options.Format))
            throw new InvalidOperationException("La exportación recuperable está disponible para MP4 H.264.");
        if (resume) options = saved!.Options with { Recoverable = true, Resume = true, Overwrite = options.Overwrite };
        var inspection = saved == null ? await SequenceExportService.InspectAsync(project, sourceProjectPath, path, options, cancellation) :
            new ExportInspection(saved.StartSeconds, saved.EndSeconds, [], []);
        if (!inspection.Ready)
            throw new InvalidOperationException(string.Join(Environment.NewLine, inspection.Issues));
        cancellation.ThrowIfCancellationRequested();
        if (controller.StateVersion != stateVersion || controller.CurrentProject.Id != project.Id ||
            controller.CurrentProject.ActiveSequenceId != project.ActiveSequenceId)
            throw new InvalidOperationException("El proyecto o montaje cambió durante la comprobación. Consulta el estado y repite la exportación.");
        if (_active is { State: "queued" or "running" or "cancelling" })
            throw new InvalidOperationException("Ya hay una exportación MCP en curso.");
        var snapshot = project;
        var job = new Job { OutputPath = Path.GetFullPath(path), ProjectId = saved?.ProjectId ?? project.Id,
            SequenceId = saved?.SequenceId ?? project.ActiveSequenceId, Revision = saved?.Revision ?? project.Revision, Options = options,
            Inspection = inspection, Width = options.Format == ExportFormat.Wav ? null : options.Width ?? project.ActiveSequence?.Width,
            Height = options.Format == ExportFormat.Wav ? null : options.Height ?? project.ActiveSequence?.Height };
        _jobs[job.Id] = job;
        _active = job;
        if (_jobs.Count > 20)
        {
            var oldest = _jobs.Values.Where(x => x != job && x.FinishedUtc != null)
                .OrderBy(x => x.CreatedUtc).FirstOrDefault();
            if (oldest != null) _jobs.Remove(oldest.Id);
        }
        controller.Transport.Pause();
        var target = job.ProjectId == project.Id ? controller.CaptureHistoryTarget() with { Revision = job.Revision } : null;
        if (target != null)
            controller.RecordAction(target, resume ? "Reanudar exportación" : "Iniciar exportación", Path.GetFileName(job.OutputPath));
        _ = RunAsync(job, snapshot, sourceProjectPath, target);
        return Describe(job);
    }

    private async Task RunAsync(Job job, Project snapshot, string? projectPath, ProjectController.HistoryTarget? target)
    {
        job.State = "running";
        try
        {
            var progress = new Progress<double>(fraction => job.Progress = Math.Clamp(fraction, 0, 1));
            var result = await Task.Run(() => SequenceExportService.ExportGpuAsync(snapshot, job.OutputPath,
                projectPath, progress: progress, cancellation: job.Cancellation.Token, options: job.Options),
                job.Cancellation.Token);
            job.Progress = 1;
            job.Encoder = result.Encoder;
            job.Inspection = job.Inspection with { Warnings = result.Warnings };
            job.State = "completed";
            controller.ReportStatus("Exportación MCP terminada: " + Path.GetFileName(job.OutputPath));
            if (target != null)
                controller.RecordAction(target, "Exportar montaje", Path.GetFileName(job.OutputPath));
        }
        catch (OperationCanceledException)
        {
            job.State = "cancelled";
            if (target != null)
                controller.RecordAction(target, job.Options.Recoverable ? "Pausar exportación" : "Cancelar exportación",
                    Path.GetFileName(job.OutputPath));
        }
        catch (Exception error)
        {
            job.State = "failed";
            job.Error = error.Message;
            controller.ReportExportFailure(job.ProjectId, job.OutputPath, error.Message, target, job.Options.Format);
        }
        finally
        {
            job.FinishedUtc = DateTime.UtcNow;
            job.Cancellation.Dispose();
            if (_active == job) _active = null;
        }
    }

    private object Cancel(JsonElement args)
    {
        var job = Find(args);
        if (job.State is "queued" or "running")
        {
            job.State = "cancelling";
            job.Cancellation.Cancel();
        }
        return Describe(job);
    }

    private Job Find(JsonElement args)
    {
        var id = McpJson.String(args, "job_id") ?? throw new ArgumentException("Falta job_id.");
        return _jobs.TryGetValue(id, out var job) ? job : throw new ArgumentException("Exportación no encontrada.");
    }

    private static GpuExportOptions Options(JsonElement args, Sequence? sequence)
    {
        var marked = McpJson.String(args, "range") switch
        {
            null => sequence?.MarkInSeconds != null && sequence.MarkOutSeconds != null,
            "all" => false, "marked" => true, _ => throw new ArgumentException("range debe ser all o marked.")
        };
        var format = McpJson.String(args, "format") switch
        {
            null or "mp4" => ExportFormat.Mp4, "wav" => ExportFormat.Wav,
            "hevc" => ExportFormat.Hevc, "prores422" => ExportFormat.ProRes422, "prores4444" => ExportFormat.ProRes4444,
            _ => throw new ArgumentException("format debe ser mp4, hevc, prores422, prores4444 o wav.")
        };
        var encoder = McpJson.String(args, "encoder") switch
        {
            null or "auto" => ExportEncoder.Automatic, "gpu" => ExportEncoder.Gpu, "cpu" => ExportEncoder.Cpu,
            _ => throw new ArgumentException("encoder debe ser auto, gpu o cpu.")
        };
        int Bitrate(string key, int fallback) => !args.TryGetProperty(key, out var value) ? fallback :
            value.TryGetInt32(out var number) ? number : throw new ArgumentException($"{key} debe ser entero.");
        return new(marked, Bitrate("video_bitrate_mbps", 12), Bitrate("audio_bitrate_kbps", 192),
            format, encoder, McpJson.Bool(args, "allow_offline"), McpJson.Bool(args, "overwrite"),
            McpJson.Bool(args, "recoverable"), Width: OptionalDimension("width"), Height: OptionalDimension("height"));

        int? OptionalDimension(string key) => !args.TryGetProperty(key, out var value) ? null :
            value.TryGetInt32(out var number) ? number : throw new ArgumentException($"{key} debe ser entero.");
    }

    private static object Warnings(ExportInspection inspection) => inspection.Warnings.Select(warning => new
    {
        clip_id = warning.ClipId, clip_name = warning.ClipName, media_kind = warning.Kind,
        substitution = warning.Substitution, start_seconds = warning.StartSeconds, end_seconds = warning.EndSeconds
    }).ToArray();

    private static object Describe(Job job) => new { job_id = job.Id, state = job.State,
        progress = job.Progress, output_path = job.OutputPath, project_id = job.ProjectId,
        sequence_id = job.SequenceId, revision = job.Revision, error = job.Error,
        width = job.Width, height = job.Height,
        encoder = job.Encoder?.ToString().ToLowerInvariant(), warnings = Warnings(job.Inspection),
        recovery_directory = job.Options.Recoverable && job.State != "completed" ? RecoverableExport.Folder(job.OutputPath) : null,
        start_seconds = job.Inspection.StartSeconds, end_seconds = job.Inspection.EndSeconds,
        created_utc = job.CreatedUtc, finished_utc = job.FinishedUtc };
}
