using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Client.Services;

public static class LocalMediaService
{
    internal static int? AudioOrdinal(Asset asset, Clip clip, string path)
    {
        var stream = asset.Streams.FirstOrDefault(item => item.Id == clip.StreamId && item.Kind == "audio");
        if (stream == null) return null;
        var representation = asset.Representations.FirstOrDefault(item =>
            string.Equals(item.Locator.Path, path, StringComparison.OrdinalIgnoreCase));
        return representation?.StreamOrdinals.GetValueOrDefault(stream.Id, stream.Ordinal) ?? stream.Ordinal;
    }

    public static string Tool(string name)
    {
        var configured = Environment.GetEnvironmentVariable("PROMETHEUS_" + name.Replace("-", "").ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (name is "ffmpeg" or "ffprobe")
        {
            var bundled = Path.Combine(RuntimeDependencies.RuntimeRoot, "ffmpeg", name + ".exe");
            if (File.Exists(bundled)) return bundled;
        }
        var local = Path.Combine(AppContext.BaseDirectory, name + ".exe");
        if (File.Exists(local)) return local;
        var runtime = Path.Combine(RuntimeDependencies.RuntimeRoot, name + ".exe");
        return File.Exists(runtime) ? runtime : name + ".exe";
    }

    internal static Rational? ReadVideoRate(JsonElement stream)
    {
        foreach (var field in new[] { "avg_frame_rate", "r_frame_rate" })
        {
            if (!stream.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) continue;
            var parts = value.GetString()?.Split('/');
            if (parts?.Length == 2 && long.TryParse(parts[0], out var numerator) && numerator > 0 &&
                long.TryParse(parts[1], out var denominator) && denominator > 0)
                return new Rational(numerator, denominator);
        }
        return null;
    }

    public static async Task<Asset> ProbeAsync(string path, CancellationToken cancellation = default)
    {
        if (path.EndsWith(".mgt.json", StringComparison.OrdinalIgnoreCase))
            return MgtLiveAsset.Inspect(path);
        if (path.EndsWith(".mgtproj", StringComparison.OrdinalIgnoreCase))
        {
            var projectAsset = MgtLiveAsset.Inspect(path);
            await Task.Run(() => MgtProjectAudio.Resolve(projectAsset));
            return projectAsset;
        }
        using var process = new Process { StartInfo = new ProcessStartInfo(Tool("ffprobe")) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        }};
        foreach (var arg in new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", path })
            process.StartInfo.ArgumentList.Add(arg);
        var result = await ExternalProcess.RunAsync(process.StartInfo, TimeSpan.FromSeconds(30), cancellation);
        var output = result.Output;
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim());
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var file = new FileInfo(path);
        var asset = new Asset {
            Id = Guid.NewGuid().ToString("N"), Name = file.Name,
            Source = new MediaSource { Kind = "local", Uri = file.FullName, ImportedAt = DateTime.UtcNow.ToString("O") },
            ContainerFormat = root.TryGetProperty("format", out var format) && format.TryGetProperty("format_name", out var fmt) ? fmt.GetString() : null
        };
        asset.Representations.Add(new MediaRepresentation { Role = "original", Locator = new Locator(file.FullName), SizeBytes = file.Length, ModifiedUtc = file.LastWriteTimeUtc.ToString("O") });
        double duration = 0;
        if (root.TryGetProperty("format", out format) && format.TryGetProperty("duration", out var fd))
            double.TryParse(fd.GetString(), System.Globalization.CultureInfo.InvariantCulture, out duration);
        var formatDuration = duration;
        var ordinal = 0;
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var kind = stream.TryGetProperty("codec_type", out var k) ? k.GetString() : null;
            if (kind != "video" && kind != "audio") continue;
            var info = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Ordinal = stream.TryGetProperty("index", out var ix) ? ix.GetInt32() : ordinal, Kind = kind!,
                Codec = stream.TryGetProperty("codec_name", out var codec) ? codec.GetString() : null,
                Width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : null,
                Height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : null };
            var streamDuration = formatDuration;
            if (stream.TryGetProperty("duration", out var sd) &&
                double.TryParse(sd.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var measuredDuration) &&
                double.IsFinite(measuredDuration) && measuredDuration >= 0)
                streamDuration = measuredDuration;
            duration = Math.Max(duration, streamDuration);
            if (stream.TryGetProperty("sample_rate", out var sr) && int.TryParse(sr.GetString(), out var sampleRate)) info.SampleRate = sampleRate;
            if (stream.TryGetProperty("channels", out var ch)) info.Channels = ch.GetInt32();
            if (kind == "video" && ReadVideoRate(stream) is { } fps) info.NominalFps = fps;
            info.Duration = Rational.Time(Math.Max(0, streamDuration));
            asset.Streams.Add(info);
            ordinal++;
        }
        if (asset.Streams.Count == 0) throw new InvalidOperationException("El archivo no contiene vÃ­deo ni audio reconocible.");
        var still = new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".avif" }.Contains(file.Extension.ToLowerInvariant());
        if (still) foreach (var stream in asset.Streams.Where(s => s.Kind == "video")) stream.Kind = "image";
        asset.RecommendedDuration = Rational.Time(still ? 5 : Math.Max(duration, 1.0 / 60));
        return asset;
    }
}
