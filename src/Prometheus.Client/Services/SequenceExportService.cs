using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;
using SkiaSharp;

namespace Prometheus.Client.Services;

public enum ExportFormat { Mp4, Wav, Hevc, ProRes422, ProRes4444 }
public enum ExportEncoder { Automatic, Gpu, Cpu }

public sealed record GpuExportOptions(bool UseMarkedRange, int VideoBitrateMbps, int AudioBitrateKbps,
    ExportFormat Format = ExportFormat.Mp4, ExportEncoder Encoder = ExportEncoder.Automatic,
    bool AllowOffline = false, bool Overwrite = true, bool Recoverable = false, bool Resume = false,
    int? Width = null, int? Height = null);

public sealed record ExportInspection(double StartSeconds, double EndSeconds,
    IReadOnlyList<ExportMediaWarning> Warnings, IReadOnlyList<string> Issues)
{
    public bool Ready => Issues.Count == 0;
}

public sealed record ExportResult(ExportEncoder? Encoder, IReadOnlyList<ExportMediaWarning> Warnings);

/// <summary>Exports the composed montage from a snapshot independent of the monitor.</summary>
public static class SequenceExportService
{
    private static readonly SemaphoreSlim ExportSlot = new(1);
    private sealed class HardwareEncodingException(Exception error) : Exception(error.Message, error);

    public static ExportInspection Inspect(Project project, string? projectPath, string? outputPath, GpuExportOptions options)
    {
        var (inspection, _) = Prepare(project, projectPath, outputPath, options);
        return inspection;
    }

    /// <summary>Inspect an owned snapshot without blocking the UI. Do not mutate it until this task finishes.</summary>
    public static Task<ExportInspection> InspectAsync(Project snapshot, string? projectPath, string? outputPath,
        GpuExportOptions options, CancellationToken cancellation = default) => Task.Run(() =>
            Prepare(snapshot, projectPath, outputPath, options, cancellation).Inspection, cancellation);

    internal static (ExportInspection Inspection, ExportMediaPlan? Media) Prepare(Project project,
        string? projectPath, string? outputPath, GpuExportOptions options, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var sequence = project.ActiveSequence;
        var issues = new List<string>();
        var start = options.UseMarkedRange ? sequence?.MarkInSeconds ?? 0 : 0;
        var end = options.UseMarkedRange ? sequence?.MarkOutSeconds ?? 0 : sequence?.DurationSeconds ?? 0;
        if (sequence == null) issues.Add("No hay montaje activo.");
        else if (options.UseMarkedRange && (sequence.MarkInSeconds == null || sequence.MarkOutSeconds == null))
            issues.Add("Marca entrada y salida para exportar un tramo.");
        else if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start ||
            end > sequence.DurationSeconds + 0.001 || end - start > 12 * 3600)
            issues.Add("El rango de exportación está vacío o no es válido.");

        if ((options.Recoverable || options.Resume) && !SupportsRecovery(options.Format))
            issues.Add("La exportación recuperable está disponible para MP4 H.264.");

        if (options.Format != ExportFormat.Wav && SubtitleRenderPlan.Enabled(project))
        {
            try
            {
                var settings = sequence!.SubtitleAnimation!;
                _ = Prometheus.Core.Timeline.SubtitleAnimation.NormalizeTemplate(settings.TemplateJson);
                foreach (var (id, value) in settings.Parameters)
                    Prometheus.Core.Timeline.SubtitleAnimation.SetParameter(new() { TemplateJson = settings.TemplateJson }, id, value);
            }
            catch (Exception error) { issues.Add("Animación de subtítulos: " + error.Message); }
        }

        if (sequence != null && options.Format != ExportFormat.Wav)
        {
            try { sequence = ExportPresentation.OutputSequence(sequence, options); }
            catch (ArgumentException error) { issues.Add(error.Message); return (new(start, end, [], issues), null); }
            var proRes = IsProRes(options.Format);
            if (!double.IsFinite(sequence.Fps.Seconds) || sequence.Fps.Seconds <= 0 ||
                sequence.Width < 2 || sequence.Height < 2)
                issues.Add("El vídeo requiere dimensiones y FPS válidos.");
            else if (options.Format != ExportFormat.ProRes4444 &&
                ((sequence.Width & 1) != 0 || !proRes && (sequence.Height & 1) != 0))
                issues.Add(proRes ? "ProRes 422 requiere un ancho par." : "MP4 requiere dimensiones pares.");
            else if (proRes && options.Encoder == ExportEncoder.Gpu)
                issues.Add("ProRes compone en GPU y codifica en CPU. Usa Automático o CPU.");
            else if (options.Encoder == ExportEncoder.Gpu && (sequence.Width < 256 || sequence.Height < 256))
                issues.Add("NVIDIA requiere al menos 256 × 256 píxeles. Usa Automático o CPU.");
            if (!proRes && (options.VideoBitrateMbps is < 1 or > 100 || options.AudioBitrateKbps is < 96 or > 320))
                issues.Add("La calidad de exportación está fuera de rango.");
        }
        if (issues.Count != 0) return (new(start, end, [], issues), null);

        var media = new ExportMediaPlan(project, projectPath, start, end, options.Format == ExportFormat.Wav, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (!options.AllowOffline && media.Warnings.Any(w => w.Substitution != "web_preview"))
            issues.Add("Hay medios desconectados o vistas ligeras en el rango. Activa «Permitir medios sin conexión» para continuar.");
        if (outputPath != null)
        {
            try
            {
                var destination = media.ResolveDestination(outputPath, Extension(options.Format));
                if (File.Exists(destination) && !options.Overwrite)
                    issues.Add("El archivo de salida ya existe.");
            }
            catch (Exception error) { issues.Add(error.Message); }
        }
        return (new(start, end, media.Warnings, issues), media);
    }

    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Wav => ".wav",
        ExportFormat.ProRes422 or ExportFormat.ProRes4444 => ".mov",
        _ => ".mp4"
    };

    internal static bool SupportsRecovery(ExportFormat format) => format == ExportFormat.Mp4;
    internal static bool IsProRes(ExportFormat format) => format is ExportFormat.ProRes422 or ExportFormat.ProRes4444;

    public static async Task<ExportResult> ExportGpuAsync(Project project, string outputPath, string? projectPath,
        Action? validateProject = null, IProgress<double>? progress = null,
        CancellationToken cancellation = default, GpuExportOptions? options = null)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        options ??= new(sequence.MarkInSeconds.HasValue && sequence.MarkOutSeconds.HasValue,
            12, 192, Encoder: ExportEncoder.Gpu);
        if (options.Recoverable || options.Resume)
        {
            if (!SupportsRecovery(options.Format))
                throw new InvalidOperationException("La exportación recuperable está disponible para MP4 H.264.");
            if (!await ExportSlot.WaitAsync(0, cancellation))
                throw new InvalidOperationException("Ya hay una exportación en curso.");
            try { return await RecoverableExport.RunAsync(project, outputPath, projectPath, options, progress, cancellation); }
            finally { ExportSlot.Release(); }
        }
        var (inspection, media) = Prepare(project, projectPath, outputPath, options, cancellation);
        if (!inspection.Ready) throw new InvalidOperationException(string.Join(Environment.NewLine, inspection.Issues));
        if (options.Format != ExportFormat.Wav) sequence = ExportPresentation.OutputSequence(sequence, options);
        var destination = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var suffix = Guid.NewGuid().ToString("N");
        var tempOutput = destination + ".part-" + suffix + Extension(options.Format);
        var tempVideo = destination + ".video-" + suffix + Extension(options.Format);
        var tempAudio = destination + ".audio-" + suffix + ".pcm";

        if (!await ExportSlot.WaitAsync(0, cancellation))
            throw new InvalidOperationException("Ya hay una exportación en curso.");
        try
        {
            if (options.Format == ExportFormat.Wav)
            {
                var info = media!.Audio.BuildProcess(waveOutput: tempOutput);
                await RunProcessAsync(info, "No se pudo exportar el audio", cancellation);
                await CommitOutputAsync(0);
                return new(null, inspection.Warnings);
            }

            await RenderAudioAsync(media!.Audio, tempAudio, cancellation);
            var fps = sequence.Fps.Seconds;
            var duration = inspection.EndSeconds - inspection.StartSeconds;
            var frames = checked((int)Math.Ceiling(duration * fps - 0.000001));
            var videoRate = checked(options.VideoBitrateMbps * 1_000_000);
            using var renderer = new GpuExportFrameRenderer(project, projectPath, media, sequence.Width, sequence.Height);
            var encoder = await RenderVideoAsync(renderer, sequence, options.Encoder, videoRate, tempVideo,
                inspection.StartSeconds, 0, frames, fraction => progress?.Report(fraction * 0.95),
                validateProject, cancellation, format: options.Format);
            cancellation.ThrowIfCancellationRequested();
            await RunProcessAsync(GpuMuxProcess(tempVideo, tempAudio, duration, tempOutput,
                options.AudioBitrateKbps, format: options.Format), "No se pudo unir audio y vídeo", cancellation);
            await CommitOutputAsync(frames);
            return new(encoder, inspection.Warnings);

            async Task CommitOutputAsync(int expectedFrames)
            {
                cancellation.ThrowIfCancellationRequested();
                await ExportOutputValidation.VerifyAsync(tempOutput, options.Format, sequence,
                    inspection.EndSeconds - inspection.StartSeconds, expectedFrames, cancellation);
                cancellation.ThrowIfCancellationRequested();
                File.Move(tempOutput, destination, options.Overwrite);
                progress?.Report(1);
            }
        }
        finally
        {
            try
            {
                File.Delete(tempVideo);
                File.Delete(tempAudio);
                File.Delete(tempOutput);
            }
            finally { ExportSlot.Release(); }
        }
    }

    internal static async Task<ExportEncoder> RenderVideoAsync(GpuExportFrameRenderer renderer,
        Sequence sequence, ExportEncoder requested, int videoRate, string tempVideo,
        double timelineStart, int firstFrame, int frames, Action<double>? report,
        Action? validateProject, CancellationToken cancellation, bool independentSegment = false,
        ExportFormat format = ExportFormat.Mp4)
    {
        var fps = sequence.Fps.Seconds;
        var hevc = format == ExportFormat.Hevc;
        if (IsProRes(format))
        {
            await RenderPipeAsync("prores_ks", hardware: false);
            return ExportEncoder.Cpu;
        }

        var encoder = requested == ExportEncoder.Cpu || requested == ExportEncoder.Automatic &&
            (sequence.Width < 256 || sequence.Height < 256) ? ExportEncoder.Cpu : ExportEncoder.Gpu;
        if (encoder == ExportEncoder.Gpu)
        {
            try
            {
                try { await RenderGpuAsync(); }
                catch (HardwareEncodingException) when (hevc)
                {
                    File.Delete(tempVideo);
                    report?.Invoke(0);
                    await RenderPipeAsync("hevc_nvenc", hardware: true);
                }
            }
            catch (HardwareEncodingException error)
            {
                if (requested == ExportEncoder.Gpu)
                    throw new InvalidOperationException("Falló el codificador NVIDIA. " + error.Message, error);
                File.Delete(tempVideo);
                encoder = ExportEncoder.Cpu;
                report?.Invoke(0);
            }
        }
        if (encoder == ExportEncoder.Cpu) await RenderPipeAsync(hevc ? "libx265" : "libx264", hardware: false);
        return encoder;

        async Task RenderGpuAsync()
        {
            GpuVideoWriter? writer = null;
            try
            {
                for (var i = 0; i < frames; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    validateProject?.Invoke();
                    await renderer.RenderAsync(timelineStart + (firstFrame + i) / fps, cancellation);
                    try
                    {
                        renderer.WithOutputTexture((device, context, texture) =>
                        {
                            writer ??= new GpuVideoWriter(device, context, tempVideo,
                                sequence.Width, sequence.Height, sequence.Fps, videoRate, hevc);
                            writer.Write(texture, i);
                        });
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { throw new HardwareEncodingException(error); }
                    report?.Invoke((i + 1d) / frames);
                }
                try
                {
                    writer?.Finish();
                    writer?.Dispose();
                    writer = null;
                    // Media Foundation timestamps use 100 ns. Remux their track into
                    // exact FPS ticks before validation or concatenation of segments.
                    var normalized = tempVideo + ".rate.mp4";
                    try
                    {
                        var normalize = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg")) {
                            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                            "-i", tempVideo, "-map", "0:v:0", "-an", "-c:v", "copy", "-bsf:v",
                            $"setts=pts=round(PTS/{sequence.Fps.D})*{sequence.Fps.D}:dts=round(DTS/{sequence.Fps.D})*{sequence.Fps.D}:duration={sequence.Fps.D}:time_base=1/{sequence.Fps.N}:prescale=1",
                            "-video_track_timescale",
                            sequence.Fps.N.ToString(CultureInfo.InvariantCulture), normalized })
                            normalize.ArgumentList.Add(arg);
                        await RunProcessAsync(normalize, "No se pudo fijar la base de tiempo del vídeo", cancellation);
                        File.Move(normalized, tempVideo, overwrite: true);
                    }
                    finally { File.Delete(normalized); }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { throw new HardwareEncodingException(error); }
            }
            finally { writer?.Dispose(); }
        }

        async Task RenderPipeAsync(string codec, bool hardware)
        {
            var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true
            };
            var rate = sequence.Fps;
            void Add(params string[] values) { foreach (var value in values) info.ArgumentList.Add(value); }
            Add("-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo",
                "-pixel_format", "bgra", "-video_size", $"{sequence.Width}x{sequence.Height}",
                "-framerate", $"{rate.N}/{rate.D}", "-i", "pipe:0", "-an");
            var proRes = IsProRes(format);
            var alpha = format == ExportFormat.ProRes4444;
            var pixelFormat = alpha ? "yuva444p10le" : proRes ? "yuv422p10le" : "yuv420p";
            Add("-vf", $"scale=out_color_matrix=bt709:out_range=tv,format={pixelFormat}", "-c:v", codec);
            if (alpha) Add("-profile:v", "4", "-alpha_bits", "16");
            else if (proRes) Add("-profile:v", "2");
            else Add("-preset", hardware ? "p5" : "veryfast", "-b:v", videoRate.ToString(CultureInfo.InvariantCulture));
            if (independentSegment) Add("-bf", "0");
            Add("-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv", tempVideo);
            using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar FFmpeg.");
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await using (var input = process.StandardInput.BaseStream)
                {
                    for (var i = 0; i < frames; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        validateProject?.Invoke();
                        await renderer.RenderAsync(timelineStart + (firstFrame + i) / fps, cancellation);
                        using var bitmap = renderer.Readback();
                        var bytes = bitmap.Bytes;
                        if (alpha) UnpremultiplyBgra(bytes);
                        var rowSize = sequence.Width * 4;
                        if (rowSize == bitmap.RowBytes)
                            await input.WriteAsync(bytes, cancellation);
                        else
                            for (var row = 0; row < sequence.Height; row++)
                                await input.WriteAsync(bytes.AsMemory(row * bitmap.RowBytes, rowSize), cancellation);
                        report?.Invoke((i + 1d) / frames);
                    }
                }
                await process.WaitForExitAsync(cancellation);
                if (process.ExitCode != 0)
                    throw new IOException("Falló el codificador " + codec + ": " + (await errorTask).Trim());
            }
            catch (Exception error)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                if (hardware && error is IOException && !cancellation.IsCancellationRequested)
                    throw new HardwareEncodingException(error);
                throw;
            }
        }

    }

    // The compositor produces premultiplied BGRA. MOV stores straight color with a separate alpha channel.
    private static void UnpremultiplyBgra(Span<byte> pixels)
    {
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            if (alpha is 0 or 255) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                var index = offset + channel;
                pixels[index] = (byte)Math.Min(255, (pixels[index] * 255 + alpha / 2) / alpha);
            }
        }
    }

    internal static async Task RunProcessAsync(ProcessStartInfo info, string failure, CancellationToken cancellation)
    {
        using var process = Process.Start(info) ?? throw new IOException(failure);
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        if (process.ExitCode != 0) throw new InvalidOperationException(failure + ": " + (await errors).Trim());
    }

    internal static ProcessStartInfo GpuMuxProcess(string video, string audio, double duration, string output,
        int audioBitrateKbps, bool concatenate = false, ExportFormat format = ExportFormat.Mp4)
    {
        var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true
        };
        void Add(params string[] values) { foreach (var value in values) info.ArgumentList.Add(value); }
        Add("-nostdin", "-hide_banner", "-loglevel", "error", "-y");
        if (concatenate) Add("-f", "concat", "-safe", "1");
        Add("-i", video, "-f", "s16le", "-ar", "48000", "-ac", "2", "-i", audio,
            "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy");
        if (IsProRes(format)) Add("-c:a", "pcm_s16le");
        else Add("-c:a", "aac", "-b:a", audioBitrateKbps.ToString(CultureInfo.InvariantCulture) + "k");
        if (format == ExportFormat.Hevc) Add("-tag:v", "hvc1");
        Add("-color_primaries", "bt709", "-color_trc", "bt709",
            "-colorspace", "bt709", "-color_range", "tv",
            "-t", duration.ToString("0.########", CultureInfo.InvariantCulture),
            "-movflags", "+faststart", output);
        return info;
    }

    public static async Task ExportAsync(Project project, string outputPath,
        Func<double, CancellationToken, Task<SKBitmap>> renderFrame,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var fps = sequence.Fps.Seconds;
        if (fps <= 0 || sequence.Width <= 0 || sequence.Height <= 0)
            throw new InvalidOperationException("La resolución o los FPS del montaje no son válidos.");
        var start = sequence.MarkInSeconds ?? 0;
        var end = sequence.MarkOutSeconds ?? sequence.DurationSeconds;
        if (end <= start || end - start > 12 * 60 * 60)
            throw new InvalidOperationException("El rango de exportación está vacío o no es válido.");
        var frameCount = checked((int)Math.Ceiling((end - start) * fps - 0.000001));
        if (frameCount < 1) throw new InvalidOperationException("No hay fotogramas para exportar.");

        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var token = Guid.NewGuid().ToString("N");
        var tempVideo = destination + ".part-" + token + ".mp4";
        var tempAudio = Path.Combine(Path.GetTempPath(), "prometheus-audio-" + token + ".pcm");
        try
        {
            await RenderAudioAsync(AudioMixPlan.Create(project, start), tempAudio, cancellation);
            using var process = new Process { StartInfo = VideoProcess(sequence.Width, sequence.Height,
                fps, frameCount, end - start, tempAudio, tempVideo) };
            process.Start();
            var errorTask = process.StandardError.ReadToEndAsync(cancellation);
            try
            {
                await using var input = process.StandardInput.BaseStream;
                for (var i = 0; i < frameCount; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var at = start + i / fps;
                    using var bitmap = await renderFrame(at, cancellation);
                    if (bitmap.Width != sequence.Width || bitmap.Height != sequence.Height ||
                        bitmap.ColorType != SKColorType.Bgra8888)
                        throw new InvalidOperationException("El monitor entregó un fotograma de tamaño o formato inesperado.");
                    var bytes = bitmap.Bytes;
                    var rowSize = sequence.Width * 4;
                    for (var y = 0; y < sequence.Height; y++)
                        await input.WriteAsync(bytes.AsMemory(y * bitmap.RowBytes, rowSize), cancellation);
                    progress?.Report((i + 1d) / frameCount);
                }
                await input.FlushAsync(cancellation);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            try { await process.WaitForExitAsync(cancellation); }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            var error = await errorTask;
            if (process.ExitCode != 0 || !File.Exists(tempVideo) || new FileInfo(tempVideo).Length == 0)
                throw new InvalidOperationException("FFmpeg no pudo terminar el vídeo: " + error.Trim());
            File.Move(tempVideo, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempVideo)) File.Delete(tempVideo);
            if (File.Exists(tempAudio)) File.Delete(tempAudio);
        }
    }

    internal static async Task RenderAudioAsync(AudioMixPlan plan, string path, CancellationToken cancellation)
    {
        using var process = new Process { StartInfo = plan.BuildProcess() };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellation);
        try
        {
            await using var output = File.Create(path);
            await process.StandardOutput.BaseStream.CopyToAsync(output, cancellation);
            await process.WaitForExitAsync(cancellation);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException("No se pudo mezclar el audio: " + (await errorTask).Trim());
    }

    private static ProcessStartInfo VideoProcess(int width, int height, double fps, int frames,
        double duration, string audioPath, string outputPath)
    {
        var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardError = true
        };
        void Add(params string[] values) { foreach (var value in values) info.ArgumentList.Add(value); }
        var n = CultureInfo.InvariantCulture;
        Add("-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{width}x{height}",
            "-framerate", fps.ToString("0.########", n), "-i", "pipe:0",
            "-f", "s16le", "-ar", "48000", "-ac", "2", "-i", audioPath,
            "-map", "0:v:0", "-map", "1:a:0", "-frames:v", frames.ToString(n),
            "-t", duration.ToString("0.########", n),
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", outputPath);
        return info;
    }
}
