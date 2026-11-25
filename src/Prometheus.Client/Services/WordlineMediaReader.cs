using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

/// <summary>Reads Wordline's existing timed-text metadata without changing the media file.</summary>
public static class WordlineMediaReader
{
    public static async Task<string?> ReadAsync(string path, CancellationToken cancellation = default)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".wav") return ReadWav(path);
        if (extension is ".mp4" or ".m4a" or ".mov")
        {
            var info = new ProcessStartInfo(LocalMediaService.Tool("ffprobe"));
            foreach (var arg in new[] { "-v", "error", "-show_entries",
                "format_tags=wordline_timed_text_v1", "-of", "json", path }) info.ArgumentList.Add(arg);
            using var json = JsonDocument.Parse(await RunAsync(info, cancellation));
            if (!json.RootElement.TryGetProperty("format", out var format) ||
                !format.TryGetProperty("tags", out var tags) ||
                !tags.TryGetProperty("wordline_timed_text_v1", out var value)) return null;
            return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value.GetString() ?? ""));
        }
        if (extension == ".mka")
        {
            var info = new ProcessStartInfo(LocalMediaService.Tool("ffprobe"));
            foreach (var arg in new[] { "-v", "error", "-select_streams", "t",
                "-show_entries", "stream=index:stream_tags=filename", "-of", "json", path })
                info.ArgumentList.Add(arg);
            using var json = JsonDocument.Parse(await RunAsync(info, cancellation));
            if (!json.RootElement.TryGetProperty("streams", out var streams)) return null;
            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("tags", out var tags) ||
                    !tags.TryGetProperty("filename", out var filename) ||
                    filename.GetString() != "wordline-timed-text-v1.txt") continue;
                var index = stream.GetProperty("index").GetInt32();
                var folder = Path.Combine(AppContext.BaseDirectory, "Cache", "Whisper");
                Directory.CreateDirectory(folder);
                var temporary = Path.Combine(folder, "wordline-" + Guid.NewGuid().ToString("N") + ".txt");
                try
                {
                    var ffmpeg = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg")) { WorkingDirectory = folder };
                    foreach (var arg in new[] { "-hide_banner", "-loglevel", "error",
                        "-dump_attachment:" + index, temporary, "-i", path, "-f", "null", "NUL" })
                        ffmpeg.ArgumentList.Add(arg);
                    await RunAsync(ffmpeg, cancellation);
                    return await File.ReadAllTextAsync(temporary, cancellation);
                }
                finally { try { File.Delete(temporary); } catch { } }
            }
        }
        return null;
    }

    private static string? ReadWav(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        if (file.Length < 12 || reader.ReadUInt32() != 0x46464952) return null;
        file.Position = 8;
        if (reader.ReadUInt32() != 0x45564157) return null;
        while (file.Position + 8 <= file.Length)
        {
            var kind = reader.ReadUInt32();
            var length = reader.ReadUInt32();
            var end = file.Position + length + length % 2;
            if (end > file.Length) break;
            if (kind == 0x5453494c && length >= 12 && reader.ReadUInt32() == 0x4f464e49)
            {
                while (file.Position + 8 <= end)
                {
                    var tag = reader.ReadUInt32();
                    var size = reader.ReadUInt32();
                    var next = file.Position + size + size % 2;
                    if (next > end) break;
                    if (tag == 0x544d4349 && size < 16 * 1024 * 1024)
                        return new UTF8Encoding(false, true).GetString(reader.ReadBytes((int)size)).TrimEnd('\0');
                    file.Position = next;
                }
            }
            file.Position = end;
        }
        return null;
    }

    private static async Task<string> RunAsync(ProcessStartInfo info, CancellationToken cancellation)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;
        using var process = new Process { StartInfo = info };
        process.Start();
        using var registration = cancellation.Register(() =>
        { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellation);
        if (process.ExitCode != 0) throw new InvalidOperationException((await error).Trim());
        return await output;
    }
}
