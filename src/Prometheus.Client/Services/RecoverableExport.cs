using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

internal static class RecoverableExport
{
    internal const string WorkerFlag = "--export-worker";
    private const string Suffix = ".prometheus-export";
    private const int SegmentSeconds = 30;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal sealed record Checkpoint(string ProjectId, string SequenceId, string ProjectName, int Revision,
        double StartSeconds, double EndSeconds, int CompletedFrames, int TotalFrames, string Directory, Space Space,
        GpuExportOptions Options);
    internal sealed record Space(long RequiredBytes, long? FreeBytes)
    {
        public bool Fits => FreeBytes == null || FreeBytes >= RequiredBytes;
        public string Description => $"Espacio temporal estimado: {RequiredBytes / 1048576d:0} MB" +
            (FreeBytes is { } free ? $" · libre: {free / 1048576d:0} MB" : "");
    }

    private sealed record Job(string Destination, string? ProjectPath, string ProjectId, string SequenceId,
        string ProjectName, int Revision, double StartSeconds, double EndSeconds,
        GpuExportOptions Options, int TotalFrames, int SegmentFrames, string MediaSignature,
        ExportEncoder? Encoder = null, int Segments = 0, string? TimeBase = null)
    {
        public int CompletedFrames => (int)Math.Min(TotalFrames, (long)Segments * SegmentFrames);
    }

    private sealed record WorkerMessage(double? Progress = null, ExportResult? Result = null,
        string? Error = null, bool Cancelled = false);

    internal static string Folder(string path) => Path.GetFullPath(path) + Suffix;

    internal static Checkpoint? Find(string path)
    {
        var folder = Folder(path);
        if (!File.Exists(Manifest(folder))) return null;
        var job = Read(folder);
        return new(job.ProjectId, job.SequenceId, job.ProjectName, job.Revision, job.StartSeconds,
            job.EndSeconds, job.CompletedFrames, job.TotalFrames, folder,
            Estimate(path, job.EndSeconds - job.StartSeconds, job.Options.VideoBitrateMbps,
                job.Options.AudioBitrateKbps, job.CompletedFrames / (double)job.TotalFrames), job.Options);
    }

    internal static Space Estimate(string destination, double seconds, int videoMbps, int audioKbps,
        double completed = 0)
    {
        // Keep the video chunks until the verified MP4 is ready. Audio is mixed once.
        var videoBytes = Math.Max(0, seconds) * videoMbps * 125000;
        var audioBytes = Math.Max(0, seconds) * (48000 * 4 + audioKbps * 125);
        var required = (long)Math.Ceiling((videoBytes * (2 - Math.Clamp(completed, 0, 1)) + audioBytes) * 1.25 + 16 * 1048576);
        long? free = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!);
            if (drive.IsReady) free = drive.AvailableFreeSpace;
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { }
        return new(required, free);
    }

    internal static async Task<ExportResult> RunAsync(Project project, string destination, string? projectPath,
        GpuExportOptions options, IProgress<double>? progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        if (options.Format != ExportFormat.Mp4 || !destination.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La exportación recuperable está disponible para MP4.");
        var folder = Folder(destination);
        if (options.Resume)
        {
            if (Find(destination) == null) throw new InvalidOperationException("No hay una exportación guardada para esa ruta.");
        }
        else
        {
            var (inspection, media) = SequenceExportService.Prepare(project, projectPath, destination, options, cancellation);
            if (!inspection.Ready) throw new InvalidOperationException(string.Join(Environment.NewLine, inspection.Issues));
            var output = ExportPresentation.OutputSequence(project.ActiveSequence!, options);
            options = options with { Width = output.Width, Height = output.Height };
            var seconds = inspection.EndSeconds - inspection.StartSeconds;
            CheckSpace(Estimate(destination, seconds, options.VideoBitrateMbps, options.AudioBitrateKbps));
            Directory.CreateDirectory(folder);
            using var protect = Lock(folder);
            if (File.Exists(Manifest(folder)))
                throw new InvalidOperationException("Ya hay una exportación guardada en ese destino. Elige Reanudar u otro nombre.");
            if (Directory.EnumerateFileSystemEntries(folder).Any(path => Path.GetFileName(path) is not ("job.lock" or "snapshot.json" or "snapshot.json.tmp" or "job.json.tmp")))
                throw new IOException("La carpeta de recuperación ya contiene otros archivos. Elige otro nombre de salida.");
            var fps = project.ActiveSequence!.Fps.Seconds;
            var total = Math.Max(1, checked((int)Math.Ceiling(seconds * fps - 0.000001)));
            WriteAtomic(Snapshot(folder), ProjectStore.Serialize(project));
            Write(folder, new(destination, projectPath, project.Id, project.ActiveSequenceId, project.Name, project.Revision,
                inspection.StartSeconds, inspection.EndSeconds, options with { Resume = false }, total, Math.Max(1, checked((int)Math.Round(fps * SegmentSeconds))), media!.SourceSignature()));
        }

        return await StartWorkerAsync(folder, options.Overwrite, progress, cancellation);
    }

    private static async Task<ExportResult> StartWorkerAsync(string folder, bool overwrite,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Prometheus.Client.exe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(WorkerFlag);
        start.ArgumentList.Add(folder);
        start.ArgumentList.Add(overwrite ? "overwrite" : "keep");
        using var worker = Process.Start(start) ?? throw new IOException("No se inició el proceso de exportación.");
        var errors = worker.StandardError.ReadToEndAsync();
        using var registration = cancellation.Register(() =>
        {
            try { worker.StandardInput.WriteLine("cancel"); worker.StandardInput.Flush(); }
            catch (Exception error) when (error is IOException or InvalidOperationException) { }
        });
        WorkerMessage? final = null;
        var reading = ReadWorkerAsync();
        try { await reading.WaitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            try { await reading.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (TimeoutException)
            {
                if (!worker.HasExited) worker.Kill(entireProcessTree: true);
                await reading;
            }
        }
        await worker.WaitForExitAsync();
        if (final?.Result != null && worker.ExitCode == 0) return final.Result;
        if (cancellation.IsCancellationRequested || final?.Cancelled == true)
            throw new OperationCanceledException(cancellation);
        var detail = final?.Error ?? (await errors).Trim();
        throw new InvalidOperationException("Exportación interrumpida. Reanuda con el mismo destino. " + detail);

        async Task ReadWorkerAsync()
        {
            while (await worker.StandardOutput.ReadLineAsync() is { } line)
            {
                WorkerMessage? message;
                try { message = JsonSerializer.Deserialize<WorkerMessage>(line, Json); }
                catch (JsonException) { continue; }
                if (message?.Progress is { } value) progress?.Report(value);
                if (message is { Result: not null } or { Error: not null } or { Cancelled: true }) final = message;
            }
        }
    }

    internal static int WorkerEntry(string folder, bool overwrite)
    {
        using var cancellation = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            try { Console.In.ReadLine(); cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        });
        try
        {
            var result = RenderAsync(folder, overwrite, value => Send(new(Progress: value)), cancellation.Token)
                .GetAwaiter().GetResult();
            Send(new(Result: result));
            return 0;
        }
        catch (OperationCanceledException) { Send(new(Cancelled: true)); return 2; }
        catch (Exception error) { Send(new(Error: error.Message)); return 1; }
    }

    private static async Task<ExportResult> RenderAsync(string folder, bool overwrite,
        Action<double> report, CancellationToken cancellation)
    {
        folder = Path.GetFullPath(folder);
        if (!folder.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Carpeta de recuperación inválida.");
        using var protect = Lock(folder);
        var job = Read(folder);
        var project = ProjectStore.Deserialize(File.ReadAllText(Snapshot(folder)));
        var options = job.Options with { Overwrite = overwrite };
        var (inspection, media) = SequenceExportService.Prepare(project, job.ProjectPath, job.Destination, options, cancellation);
        if (!inspection.Ready) throw new InvalidOperationException(string.Join(Environment.NewLine, inspection.Issues));
        if (media!.SourceSignature() != job.MediaSignature)
            throw new InvalidOperationException("Los medios cambiaron desde el inicio. Restáuralos para reanudar o elige un destino nuevo.");
        var sequence = ExportPresentation.OutputSequence(project.ActiveSequence!, options);
        var fps = sequence.Fps.Seconds;
        var duration = inspection.EndSeconds - inspection.StartSeconds;
        var chunks = ((job.TotalFrames - 1) / job.SegmentFrames) + 1;
        var keep = 0;
        while (keep < job.Segments)
        {
            cancellation.ThrowIfCancellationRequested();
            var probe = await ProbeAsync(Chunk(folder, keep), FrameCount(keep), sequence, false, cancellation);
            if (probe == null || probe != job.TimeBase) break;
            keep++;
        }
        if (keep != job.Segments)
        {
            job = job with { Segments = keep, Encoder = keep == 0 ? null : job.Encoder, TimeBase = keep == 0 ? null : job.TimeBase };
            Write(folder, job);
        }
        var partial = Path.Combine(folder, "segment.part.mp4");
        var audio = Path.Combine(folder, "audio.pcm");
        var final = Path.Combine(folder, "final.part.mp4");
        var concat = Path.Combine(folder, "segments.ffconcat");
        File.Delete(partial);
        File.Delete(audio);
        File.Delete(final);
        CheckSpace(Estimate(job.Destination, duration, options.VideoBitrateMbps,
            options.AudioBitrateKbps, job.CompletedFrames / (double)job.TotalFrames));
        report(0.95 * job.CompletedFrames / job.TotalFrames);

        if (job.Segments < chunks)
        {
            using var renderer = new GpuExportFrameRenderer(project, job.ProjectPath, media, sequence.Width, sequence.Height);
            var meter = Stopwatch.StartNew();
            for (var part = job.Segments; part < chunks; part++)
            {
                cancellation.ThrowIfCancellationRequested();
                var first = part * job.SegmentFrames;
                var count = FrameCount(part);
                var encoder = await SequenceExportService.RenderVideoAsync(renderer, sequence,
                    job.Encoder ?? options.Encoder, options.VideoBitrateMbps * 1000000, partial,
                    inspection.StartSeconds, first, count, fraction =>
                    {
                        if (meter.ElapsedMilliseconds < 100) return;
                        report(0.95 * (first + count * fraction) / job.TotalFrames);
                        meter.Restart();
                    }, null, cancellation, independentSegment: true);
                var timeBase = await ProbeAsync(partial, count, sequence, false, cancellation)
                    ?? throw new IOException($"El tramo {part + 1} no pasó la comprobación de vídeo.");
                if (job.TimeBase != null && job.TimeBase != timeBase)
                    throw new IOException("El nuevo tramo usa una base de tiempo distinta.");
                File.Move(partial, Chunk(folder, part), overwrite: true);
                job = job with { Segments = part + 1, Encoder = encoder, TimeBase = timeBase };
                Write(folder, job);
                report(0.95 * job.CompletedFrames / job.TotalFrames);
            }
        }

        await SequenceExportService.RenderAudioAsync(media.Audio, audio, cancellation);
        var script = new StringBuilder("ffconcat version 1.0\n");
        for (var part = 0; part < chunks; part++)
        {
            script.Append("file ").Append(Path.GetFileName(Chunk(folder, part))).Append('\n');
            script.Append("duration ").Append((FrameCount(part) / fps).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        }
        File.WriteAllText(concat, script.ToString(), new UTF8Encoding(false));
        var mux = SequenceExportService.GpuMuxProcess(concat, audio, duration, final,
            options.AudioBitrateKbps, concatenate: true);
        await SequenceExportService.RunProcessAsync(mux, "No se pudieron unir los tramos terminados", cancellation);
        if (await ProbeAsync(final, job.TotalFrames, sequence, true, cancellation) == null)
            throw new IOException("El MP4 final no pasó la comprobación de imagen y audio.");
        await ExportOutputValidation.VerifyAsync(final, ExportFormat.Mp4, sequence,
            duration, job.TotalFrames, cancellation);
        cancellation.ThrowIfCancellationRequested();
        File.Move(final, job.Destination, overwrite);
        report(1);

        // Clean only files belonging to this export. The MP4 is already published.
        for (var part = 0; part < chunks; part++) TryDelete(Chunk(folder, part));
        foreach (var path in new[] { audio, concat, Snapshot(folder), Manifest(folder) }) TryDelete(path);
        protect.Dispose();
        TryDelete(Path.Combine(folder, "job.lock"));
        try { Directory.Delete(folder); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new(job.Encoder, inspection.Warnings);

        int FrameCount(int part) => Math.Min(job.SegmentFrames, job.TotalFrames - part * job.SegmentFrames);
    }

    private static async Task<string?> ProbeAsync(string path, int frames, Sequence sequence,
        bool expectAudio, CancellationToken cancellation)
    {
        if (!File.Exists(path)) return null;
        var start = new ProcessStartInfo(LocalMediaService.Tool("ffprobe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-v", "error", "-show_entries",
            "stream=codec_type,codec_name,width,height,nb_frames,time_base,avg_frame_rate,duration", "-of", "json", path })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("No se inició ffprobe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(await errors)) return null;
        using var data = JsonDocument.Parse(await output);
        var streams = data.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(item => item.GetProperty("codec_type").GetString() == "video");
        if (video.ValueKind != JsonValueKind.Object ||
            video.GetProperty("codec_name").GetString() != "h264" ||
            video.GetProperty("width").GetInt32() != sequence.Width || video.GetProperty("height").GetInt32() != sequence.Height ||
            !video.TryGetProperty("nb_frames", out var frameValue) || !int.TryParse(frameValue.GetString(), out var actual) || actual != frames)
            return null;
        if (!ExportOutputValidation.RateEquals(video.GetProperty("avg_frame_rate").GetString(), sequence)) return null;
        if (expectAudio && !streams.Any(item => item.GetProperty("codec_type").GetString() == "audio" &&
            item.GetProperty("codec_name").GetString() == "aac")) return null;
        return video.GetProperty("time_base").GetString();
    }

    private static FileStream Lock(string folder)
    {
        try { return new FileStream(Path.Combine(folder, "job.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new IOException("Este destino de exportación está ocupado.", error); }
    }

    private static string Manifest(string folder) => Path.Combine(folder, "job.json");
    private static string Snapshot(string folder) => Path.Combine(folder, "snapshot.json");
    private static string Chunk(string folder, int index) => Path.Combine(folder, $"segment-{index:D6}.mp4");

    private static Job Read(string folder)
    {
        var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(Manifest(folder)), Json);
        if (job == null || !string.Equals(Folder(job.Destination), folder, StringComparison.OrdinalIgnoreCase) ||
            job.TotalFrames < 1 || job.SegmentFrames < 1 || job.Segments < 0 ||
            job.Segments > (job.TotalFrames - 1) / job.SegmentFrames + 1 ||
            job.Segments > 0 && (job.Encoder is null or ExportEncoder.Automatic || string.IsNullOrWhiteSpace(job.TimeBase)))
            throw new InvalidDataException("La exportación guardada no es válida.");
        return job;
    }

    private static void Write(string folder, Job job) => WriteAtomic(Manifest(folder), JsonSerializer.Serialize(job, Json));
    private static void WriteAtomic(string path, string text)
    {
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        {
            stream.Write(Encoding.UTF8.GetBytes(text));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static void CheckSpace(Space space)
    {
        if (!space.Fits) throw new IOException("Espacio insuficiente para guardar el MP4 y sus tramos. " + space.Description);
    }

    private static void Send(WorkerMessage message) => Console.WriteLine(JsonSerializer.Serialize(message, Json));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
