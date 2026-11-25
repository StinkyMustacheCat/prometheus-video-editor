using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Globalization;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public sealed record ExportMediaWarning(string ClipId, string ClipName, string Kind, string Substitution,
    double StartSeconds, double EndSeconds);

internal sealed class ExportMediaPlan
{
    private static readonly string[] Roles = ["final", "original", "proxy", "preview"];
    private readonly Project _project;
    private readonly string? _directory;
    private readonly Dictionary<string, (string? Path, MediaRepresentation? Representation)> _locations = new();
    private readonly Dictionary<string, int[]> _proxyAudio = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExportMediaWarning> _warnings = new();
    private readonly CancellationToken _cancellation;
    public IReadOnlyList<ExportMediaWarning> Warnings => _warnings;
    public AudioMixPlan Audio { get; }

    public ExportMediaPlan(Project project, string? projectPath, double start, double end, bool audioOnly,
        CancellationToken cancellation = default)
    {
        _cancellation = cancellation;
        cancellation.ThrowIfCancellationRequested();
        _project = project;
        _directory = projectPath == null ? null : Path.GetDirectoryName(Path.GetFullPath(projectPath));
        var sequence = project.ActiveSequence!;
        if (!audioOnly)
        {
            var transitionWindows = sequence.VideoTransitions.Select(t => VideoTransitionOperations.Window(sequence, t))
                .OfType<VideoTransitionWindow>().ToArray();
            InspectVideo(sequence.Tracks, sequence.Clips, 0, 1, start, end, 0, transitionWindows);
        }
        Audio = AudioMixPlan.Create(project, start, end, ResolveAudio);
    }

    internal string SourceSignature()
    {
        var sources = _locations.OrderBy(location => location.Key, StringComparer.Ordinal)
            .Select(location => new { Id = location.Key, Role = location.Value.Representation?.Role, File = Stamp(location.Value.Path) });
        var audio = Audio.Inputs.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(Stamp);
        var data = JsonSerializer.Serialize(new { Sources = sources, Audio = audio });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));

        static object? Stamp(string? path)
        {
            if (path == null) return null;
            var file = new FileInfo(path);
            return new { Path = file.FullName, Length = file.Exists ? file.Length : -1,
                Updated = file.LastWriteTimeUtc.Ticks };
        }
    }

    public bool UsesProxy(Asset? asset) => asset?.Source.Kind != "mgt" && Locate(asset).Representation?.Role is "proxy" or "preview";

    public string? VideoPath(Asset? asset, int? outputWidth = null, int? outputHeight = null)
    {
        if (asset?.TextOverlay != null) return TextMediaService.Resolve(_project, asset, outputWidth, outputHeight);
        return Locate(asset).Path;
    }

    public string ResolveDestination(string path, string extension)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"La salida debe terminar en {extension}.");
        if (_project.Assets.Any(asset => asset.Representations.Select(item => item.Locator)
            .Append(asset.EffectiveLocator).Select(ResolveLocal)
            .Any(source => string.Equals(source, full, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("La salida coincide con un medio del proyecto.");
        return full;
    }

    private void InspectVideo(IReadOnlyList<Track> tracks, IReadOnlyList<Clip> clips, double bias,
        double rate, double lower, double upper, int depth, IReadOnlyList<VideoTransitionWindow>? transitions)
    {
        if (depth >= 16) return;
        var videoTracks = tracks.Where(track => track.Kind == "video" && track.Enabled)
            .Select(track => track.Id).ToHashSet();
        foreach (var clip in clips.Where(clip => clip.Enabled && clip.Speed.Seconds > 0 && videoTracks.Contains(clip.TrackId)))
        {
            _cancellation.ThrowIfCancellationRequested();
            var localStart = clip.TimelineStart.Seconds;
            var localEnd = clip.EndSeconds;
            if (transitions != null)
                foreach (var window in transitions)
                    if (window.Left.Id == clip.Id || window.Right.Id == clip.Id)
                    {
                        localStart = Math.Min(localStart, window.Start);
                        localEnd = Math.Max(localEnd, window.End);
                    }

            var from = Math.Max(lower, (localStart - bias) / rate);
            var to = Math.Min(upper, (localEnd - bias) / rate);
            if (to <= from) continue;
            if (clip.NestedContent is { } content)
            {
                if (clip.NestedRole == "video")
                    InspectVideo(content.Tracks, content.Clips,
                        clip.SourceIn.Seconds + (bias - clip.TimelineStart.Seconds) * clip.Speed.Seconds,
                        rate * clip.Speed.Seconds, from, to, depth + 1, null);
                continue;
            }

            var asset = _project.Assets.FirstOrDefault(item => item.Id == clip.AssetId);
            if (asset?.TextOverlay != null) continue;
            var source = Locate(asset);
            AddWarning(clip, asset, "video", source, from, to);
        }
    }

    private (string? Path, int Ordinal) ResolveAudio(Asset? asset, StreamInfo? stream,
        Clip clip, double from, double to)
    {
        _cancellation.ThrowIfCancellationRequested();
        var source = stream == null ? default : Locate(asset);
        if (source.Path != null && asset?.Source.Kind == "mgt" && asset.ContainerFormat == "mgtproj")
        {
            try { source.Path = MgtProjectAudio.Resolve(asset, source.Path); }
            catch (Exception error)
            {
                throw new InvalidOperationException($"«{clip.Name}» · {Timecode(from)} · {error.Message}", error);
            }
        }
        var ordinal = stream == null ? 0 : source.Representation?.StreamOrdinals.GetValueOrDefault(stream.Id, stream.Ordinal)
            ?? stream.Ordinal;
        if (source.Path != null && source.Representation?.Role is "proxy" or "preview" &&
            !HasCurrentAudioMapping(source.Path, source.Representation, stream!))
        {
            var audioStreams = ProbeProxyAudio(source.Path);
            if (!audioStreams.Contains(ordinal))
            {
                if (audioStreams.Length == 1 && asset?.Streams.Count(item => item.Kind == "audio") == 1)
                    ordinal = audioStreams[0];
                else source = default;
            }
        }
        AddWarning(clip, asset, "audio", source, from, to);
        return (source.Path, ordinal);
    }

    private static bool HasCurrentAudioMapping(string path, MediaRepresentation representation, StreamInfo stream)
    {
        if (!representation.StreamOrdinals.TryGetValue(stream.Id, out var ordinal) || ordinal < 0) return false;
        var file = new FileInfo(path);
        if (representation.SizeBytes is { } size && file.Length != size) return false;
        if (representation.ModifiedUtc is { } modified && (!DateTime.TryParse(modified, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var stamp) || stamp.ToUniversalTime() != file.LastWriteTimeUtc)) return false;
        return true;
    }


    private int[] ProbeProxyAudio(string path)
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_proxyAudio.TryGetValue(path, out var result)) return result;
        var info = new ProcessStartInfo(LocalMediaService.Tool("ffprobe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-v", "error", "-select_streams", "a", "-show_entries",
            "stream=index", "-of", "csv=p=0", path }) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info);
            if (process != null)
            {
                using var stopped = _cancellation.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                });
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (process.WaitForExit(2500))
                {
                    _cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode == 0)
                        return _proxyAudio[path] = output.GetAwaiter().GetResult().Split(
                            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(line => int.TryParse(line, out _)).Select(int.Parse).ToArray();
                }
                else process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { }
        _cancellation.ThrowIfCancellationRequested();
        return _proxyAudio[path] = [];
    }

    private void AddWarning(Clip clip, Asset? asset, string kind, (string? Path, MediaRepresentation? Representation) source,
        double from, double to)
    {
        if (source.Path == null)
            _warnings.Add(new(clip.Id, clip.Name, kind, kind == "audio" ? "silence" : "transparent", from, to));
        else if (asset?.Source.Kind != "mgt" && source.Representation?.Role is "proxy" or "preview")
        {
            var selectedLight = asset?.Source.Kind == "web" && source.Representation.Role == "preview" &&
                asset.Representations.All(rep => rep.Role != "final");
            _warnings.Add(new(clip.Id, clip.Name, kind, selectedLight ? "web_preview" : "proxy", from, to));
        }
    }

    private (string? Path, MediaRepresentation? Representation) Locate(Asset? asset)
    {
        if (asset == null) return default;
        if (_locations.TryGetValue(asset.Id, out var location)) return location;
        if (asset.Source.Kind == "mgt")
        {
            var document = asset.EffectiveLocator;
            var path = MgtLiveAsset.IsDocumentPath(document?.Path) ? ResolveLocal(document) : null;
            return _locations[asset.Id] = path != null && File.Exists(path)
                ? (path, asset.Representations.FirstOrDefault(rep => ReferenceEquals(rep.Locator, document)))
                : default;
        }
        foreach (var role in Roles)
            foreach (var representation in asset.Representations.Where(item => item.Role == role))
                if (ResolveLocal(representation.Locator) is { } path && File.Exists(path))
                    return _locations[asset.Id] = (path, representation);
        var source = ResolveLocal(new Locator(asset.Source.Uri ?? "", asset.Source.Relative));
        return _locations[asset.Id] = source != null && File.Exists(source) ? (source, null) : default;
    }

    private string? ResolveLocal(Locator? locator)
    {
        if (locator == null || string.IsNullOrWhiteSpace(locator.Path)) return null;
        var path = locator.Path;
        if (Path.IsPathFullyQualified(path)) return Path.GetFullPath(path);
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri)) return uri.IsFile ? uri.LocalPath : null;
        return _directory == null ? null : Path.GetFullPath(path, _directory);
    }

    public static string Timecode(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff");
}
