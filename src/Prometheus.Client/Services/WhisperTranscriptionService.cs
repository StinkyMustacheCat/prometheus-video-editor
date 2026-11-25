using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

public sealed record WhisperResult(TranscriptDocument Document, string Device);

public static class WhisperTranscriptionService
{
    private static string Root => RuntimeDependencies.WhisperRoot;
    private static string Python => RuntimeDependencies.WhisperPython;
    private static string Model => Path.Combine(Root, "model");
    private static string Script => Environment.GetEnvironmentVariable("PROMETHEUS_WHISPER_SCRIPT") ??
        Path.Combine(AppContext.BaseDirectory, "Runtime", "whisper_bridge.py");

    public static bool IsAvailable => File.Exists(Python) && File.Exists(Path.Combine(Model, "model.bin")) &&
        File.Exists(Script);

    public static async Task<WhisperResult> TranscribeAsync(string mediaPath, Clip clip,
        string name, CancellationToken cancellation, IProgress<double>? progress = null, int? streamOrdinal = null)
    {
        if (!IsAvailable) throw new FileNotFoundException("No se encontró el motor local Whisper.");
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("No se encontró el audio seleccionado.", mediaPath);
        if (clip.Speed.Seconds <= 0 || clip.Duration.Seconds <= 0)
            throw new ArgumentException("El clip de audio tiene una duración o velocidad inválida.");
        var folder = Path.Combine(AppContext.BaseDirectory, "Cache", "Whisper",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var wav = Path.Combine(folder, "audio.wav");
        var json = Path.Combine(folder, "result.json");
        try
        {
            var sourceDuration = clip.Duration.Seconds * clip.Speed.Seconds;
            var ffmpeg = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"));
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-ss", Number(clip.SourceIn.Seconds), "-t", Number(sourceDuration), "-i", mediaPath,
                "-vn", "-map", streamOrdinal.HasValue ? $"0:{streamOrdinal.Value}" : "0:a:0", "-ac", "1", "-ar", "16000" })
                ffmpeg.ArgumentList.Add(arg);
            if (Math.Abs(clip.Speed.Seconds - 1) > 0.0001)
            {
                ffmpeg.ArgumentList.Add("-af");
                ffmpeg.ArgumentList.Add(Tempo(clip.Speed.Seconds));
            }
            ffmpeg.ArgumentList.Add("-c:a"); ffmpeg.ArgumentList.Add("pcm_s16le");
            ffmpeg.ArgumentList.Add(wav);
            await RunAsync(ffmpeg, cancellation);
            progress?.Report(0.08);
            var python = new ProcessStartInfo(Python) { WorkingDirectory = Root };
            RuntimeDependencies.PreparePython(python);
            foreach (var arg in new[] { Script, "--input", wav, "--output", json,
                "--model", Model, "--language", "auto" }) python.ArgumentList.Add(arg);
            if (progress != null) python.ArgumentList.Add("--progress");
            await RunAsync(python, cancellation, progress);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(json, cancellation));
            var root = result.RootElement;
            var document = new TranscriptDocument { Name = name,
                SourceClipId = clip.Id, TimelineStart = clip.TimelineStart.Seconds,
                SourceTimeOrigin = clip.SourceIn.Seconds, SourceTimeRate = clip.Speed.Seconds,
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
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    private static async Task RunAsync(ProcessStartInfo info, CancellationToken cancellation, IProgress<double>? progress = null)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromHours(2));
        using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar la transcripción.");
        using var registration = deadline.Token.Register(() => ExternalProcess.Kill(process));
        var error = ExternalProcess.ReadDiagnosticsAsync(process.StandardError);
        var events = ReportProgressAsync(process.StandardOutput, progress);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            await events;
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Transcripción: " + (await error).Trim());
        }
        catch
        {
            ExternalProcess.Kill(process);
            await process.WaitForExitAsync();
            try { await Task.WhenAll(error, events); } catch { }
            cancellation.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("Whisper superó el tiempo de espera.");
            throw;
        }
    }


    private static async Task ReportProgressAsync(StreamReader reader, IProgress<double>? progress)
    {
        const string marker = "PROMETHEUS_PROGRESS:";
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            if (line.StartsWith(marker, StringComparison.Ordinal) &&
                double.TryParse(line[marker.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                progress?.Report(0.08 + Math.Clamp(value, 0, 1) * 0.9);
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
