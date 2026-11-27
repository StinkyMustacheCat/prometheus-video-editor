using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

public sealed record WhisperResult(TranscriptDocument Document, string Device);

public static class WhisperTranscriptionService
{
    private static readonly SemaphoreSlim WorkerSlot = new(1);

    private static string Root => RuntimeDependencies.WhisperRoot;
    private static string Python => RuntimeDependencies.WhisperPython;
    private static string Model => Path.Combine(Root, "model");
    private static string Script => Environment.GetEnvironmentVariable("PROMETHEUS_WHISPER_SCRIPT") ??
        Path.Combine(AppContext.BaseDirectory, "Runtime", "whisper_bridge.py");

    public static bool IsAvailable => File.Exists(Python) && File.Exists(Path.Combine(Model, "model.bin")) &&
        File.Exists(Path.Combine(Model, "config.json")) && File.Exists(Path.Combine(Model, "tokenizer.json")) &&
        File.Exists(Script);

    public static async Task<WhisperResult> TranscribeAsync(string mediaPath, Clip clip,
        string name, CancellationToken cancellation, IProgress<double>? progress = null, int? streamOrdinal = null)
    {
        if (!IsAvailable) throw new FileNotFoundException("No se encontró el motor local Whisper.");
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("No se encontró el audio seleccionado.", mediaPath);
        var speed = clip.Speed.Seconds;
        var start = clip.SourceIn.Seconds;
        var sourceDuration = clip.Duration.Seconds * speed;
        var clipId = clip.Id;
        var timelineStart = clip.TimelineStart.Seconds;
        if (!double.IsFinite(speed) || !double.IsFinite(start) || !double.IsFinite(sourceDuration) ||
            speed <= 0 || start < 0 || sourceDuration <= 0)
            throw new ArgumentException("El clip de audio tiene un rango o una velocidad inválidos.");
        var folder = Path.Combine(AppContext.BaseDirectory, "Cache", "Whisper",
            Guid.NewGuid().ToString("N"));
        var wav = Path.Combine(folder, "audio.wav");
        var json = Path.Combine(folder, "result.json");
        await WorkerSlot.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(folder);
            var ffmpeg = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"));
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-ss", Number(start), "-t", Number(sourceDuration), "-i", mediaPath,
                "-vn", "-map", streamOrdinal.HasValue ? $"0:{streamOrdinal.Value}" : "0:a:0", "-ac", "1", "-ar", "16000" })
                ffmpeg.ArgumentList.Add(arg);
            if (Math.Abs(speed - 1) > 0.0001)
            {
                ffmpeg.ArgumentList.Add("-af");
                ffmpeg.ArgumentList.Add(Tempo(speed));
            }
            ffmpeg.ArgumentList.Add("-c:a"); ffmpeg.ArgumentList.Add("pcm_s16le");
            ffmpeg.ArgumentList.Add(wav);
            await RunAsync(ffmpeg, cancellation).ConfigureAwait(false);
            progress?.Report(0.08);
            var python = new ProcessStartInfo(Python) { WorkingDirectory = Root };
            RuntimeDependencies.PreparePython(python);
            foreach (var arg in new[] { Script, "--input", wav, "--output", json,
                "--model", Model, "--language", "auto" }) python.ArgumentList.Add(arg);
            if (progress != null) python.ArgumentList.Add("--progress");
            var choices = ProcessingService.Candidates(ProcessingFunction.Whisper);
            string? fallback = null;
            for (var index = 0; index < choices.Length; index++)
            {
                var choice = choices[index];
                ProcessingService.Configure(python, choice);
                try
                {
                    await RunAsync(python, cancellation, progress, backgroundPriority: true).ConfigureAwait(false);
                    ProcessingService.Record(choice, choice.Backend == "cuda" ? "CUDA FP16" : "CPU INT8", fallback);
                    break;
                }
                catch (Exception error) when (error is not OperationCanceledException && !cancellation.IsCancellationRequested)
                {
                    ProcessingService.Record(choice, reason: error.Message, failed: true);
                    fallback = choice.Label + ": " + error.Message;
                    if (index == choices.Length - 1) throw;
                    File.Delete(json);
                }
            }
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(json, cancellation).ConfigureAwait(false));
            var root = result.RootElement;
            var document = new TranscriptDocument { Name = name,
                SourceClipId = clipId, TimelineStart = timelineStart,
                SourceTimeOrigin = start, SourceTimeRate = speed,
                Language = root.TryGetProperty("language", out var lang) ? lang.GetString() : null };
            foreach (var word in root.GetProperty("words").EnumerateArray())
                document.Words.Add(new TranscriptWord { Start = word.GetProperty("start").GetDouble(),
                    End = word.GetProperty("end").GetDouble(), Text = word.GetProperty("text").GetString() ?? "" });
            if (document.Words.Count == 0)
                throw new InvalidOperationException("No se detectó voz en el intervalo seleccionado.");
            document.Validate();
            return new WhisperResult(document,
                root.TryGetProperty("device", out var device) ? device.GetString() ?? "cpu" : "cpu");
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            finally { WorkerSlot.Release(); }
        }
    }

    private static async Task RunAsync(ProcessStartInfo info, CancellationToken cancellation, IProgress<double>? progress = null,
        bool backgroundPriority = false)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromHours(2));
        using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar la transcripción.");
        using var registration = deadline.Token.Register(() => ExternalProcess.Kill(process));
        if (backgroundPriority)
        {
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        var error = ExternalProcess.ReadDiagnosticsAsync(process.StandardError);
        var events = ReportProgressAsync(process.StandardOutput, progress);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await events.WaitAsync(deadline.Token).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Transcripción: " + (await error.WaitAsync(deadline.Token).ConfigureAwait(false)).Trim());
        }
        catch
        {
            ExternalProcess.Kill(process);
            await process.WaitForExitAsync().ConfigureAwait(false);
            try { await Task.WhenAll(error, events).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            cancellation.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("Whisper superó el tiempo de espera.");
            throw;
        }
    }


    private static async Task ReportProgressAsync(StreamReader reader, IProgress<double>? progress)
    {
        const string marker = "PROMETHEUS_PROGRESS:";
        var reported = 0d;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            if (line.StartsWith(marker, StringComparison.Ordinal) &&
                double.TryParse(line[marker.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                var next = 0.08 + Math.Clamp(value, 0, 1) * 0.9;
                if (double.IsFinite(next) && next > reported)
                {
                    reported = next;
                    progress?.Report(next);
                }
            }
    }

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string Tempo(double speed)
    {
        var parts = new List<string>();
        while (speed > 2) { parts.Add("atempo=2"); speed /= 2; }
        while (speed < 0.5) { parts.Add("atempo=0.5"); speed *= 2; }
        parts.Add("atempo=" + Number(speed));
        return string.Join(",", parts);
    }
}
