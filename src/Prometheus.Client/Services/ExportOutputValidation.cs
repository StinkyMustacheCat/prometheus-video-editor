using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

internal static class ExportOutputValidation
{
    public static async Task VerifyAsync(string path, ExportFormat format, Sequence sequence,
        double duration, int frames, CancellationToken cancellation)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new IOException("No se generó un archivo de salida.");
        var start = new ProcessStartInfo(LocalMediaService.Tool("ffprobe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo comprobar la exportación.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            cancellation.ThrowIfCancellationRequested();
            throw new IOException("No se pudo comprobar la exportación a tiempo.");
        }
        if (process.ExitCode != 0) throw new IOException("La exportación no se puede leer: " + (await errors).Trim());
        using var data = JsonDocument.Parse(await output);
        Validate(data.RootElement, format, sequence, duration, frames);
    }

    internal static void Validate(JsonElement root, ExportFormat format, Sequence sequence, double duration, int frames)
    {
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var audio = streams.FirstOrDefault(stream => Text(stream, "codec_type") == "audio");
        if (audio.ValueKind != JsonValueKind.Object || Text(audio, "sample_rate") != "48000" ||
            !audio.TryGetProperty("channels", out var channels) || channels.GetInt32() != 2 ||
            Text(audio, "codec_name") != (format is ExportFormat.Mp4 or ExportFormat.Hevc ? "aac" : "pcm_s16le"))
            throw new IOException("La exportación no contiene el audio estéreo esperado.");
        var audioDuration = Number(audio, "duration");
        if (audioDuration is { } ad && Math.Abs(ad - duration) > .15)
            throw new IOException("La duración del audio exportado no coincide con el intervalo.");
        if (format != ExportFormat.Wav)
        {
            var video = streams.FirstOrDefault(stream => Text(stream, "codec_type") == "video");
            var codec = format == ExportFormat.Mp4 ? "h264" : format == ExportFormat.Hevc ? "hevc" : "prores";
            if (video.ValueKind != JsonValueKind.Object || Text(video, "codec_name") != codec ||
                video.GetProperty("width").GetInt32() != sequence.Width ||
                video.GetProperty("height").GetInt32() != sequence.Height ||
                !int.TryParse(Text(video, "nb_frames"), out var count) || count != frames)
                throw new IOException("El vídeo exportado no contiene las dimensiones o los fotogramas esperados.");
            if (!RateEquals(Text(video, "avg_frame_rate"), sequence))
                throw new IOException("La frecuencia de fotogramas exportada no coincide con el montaje.");
            var videoDuration = Number(video, "duration");
            if (videoDuration is { } vd && Math.Abs(vd - frames / sequence.Fps.Seconds) > .01 + 1 / sequence.Fps.Seconds)
                throw new IOException("La duración del vídeo exportado no coincide con sus fotogramas.");
        }
        var formatDuration = root.TryGetProperty("format", out var container) ? Number(container, "duration") : null;
        if (formatDuration == null || Math.Abs(formatDuration.Value - duration) > .15 + 1 / sequence.Fps.Seconds)
            throw new IOException("La duración del archivo exportado no coincide con el intervalo.");
    }

    internal static bool RateEquals(string? text, Sequence sequence)
    {
        var parts = text?.Split('/');
        return parts is { Length: 2 } && long.TryParse(parts[0], out var n) && long.TryParse(parts[1], out var d)
            && n > 0 && d > 0 && (Int128)n * sequence.Fps.D == (Int128)d * sequence.Fps.N;
    }

    private static string? Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) ? value.GetString() : null;
    private static double? Number(JsonElement element, string key) =>
        double.TryParse(Text(element, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) ? value : null;
}
