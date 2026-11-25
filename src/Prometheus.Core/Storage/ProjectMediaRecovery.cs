using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Prometheus.Core.Model;

namespace Prometheus.Core.Storage;

public static class ProjectMediaRecovery
{
    public static IReadOnlyList<string> RecoverFromProjectAssets(Project project, string projectFile)
    {
        var root = ProjectStorage.GetDataDirectory(projectFile);
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var files = EnumerateFiles(root);
        var recovered = new List<string>();
        foreach (var asset in project.Assets.Where(asset => NeedsRecovery(asset, projectFile)))
        {
            var match = FindUnique(asset, files, root);
            if (match == null) continue;
            Relink(asset, match);
            recovered.Add(asset.Id);
        }
        return recovered;
    }

    public static string[] EnumerateFiles(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true
        }).Where(path => !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static bool NeedsRecovery(Asset asset, string projectFile)
    {
        if (asset.Source.Kind is not ("local" or "web" or "mgt")) return false;
        var locator = asset.EffectiveLocator;
        if (locator == null || string.IsNullOrWhiteSpace(locator.Path)) return false;
        if (asset.Source.Kind == "web" && Uri.TryCreate(locator.Path, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https") return false;
        var path = locator.Relative || !Path.IsPathRooted(locator.Path)
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, locator.Path))
            : locator.Path;
        return !File.Exists(path);
    }

    public static string? FindUnique(Asset asset, IReadOnlyList<string> candidates, string root)
    {
        var locator = asset.EffectiveLocator;
        if (locator == null) return null;
        var expectedName = Path.GetFileName(locator.Path);
        var representation = asset.Representations.FirstOrDefault(rep => ReferenceEquals(rep.Locator, locator));
        var possible = candidates.Where(path =>
            string.Equals(Path.GetFileName(path), expectedName, StringComparison.OrdinalIgnoreCase) ||
            asset.Source.Kind == "web" && Path.GetFileName(path).StartsWith(asset.Id + "-", StringComparison.OrdinalIgnoreCase))
            .Where(path => representation?.SizeBytes is not > 0 || new FileInfo(path).Length == representation.SizeBytes)
            .ToArray();
        if (representation?.Sha256 is { Length: > 0 } expectedHash)
            possible = possible.Where(path =>
            {
                using var input = File.OpenRead(path);
                return string.Equals(Convert.ToHexString(SHA256.HashData(input)), expectedHash,
                    StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        if (possible.Length == 1) return possible[0];
        if (possible.Length == 0) return null;
        var oldPath = locator.Path.Replace('\\', '/');
        var marker = oldPath.IndexOf(".assets/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;
        var suffix = oldPath[(marker + ".assets/".Length)..];
        var exact = possible.Where(path => string.Equals(Path.GetRelativePath(root, path).Replace('\\', '/'),
            suffix, StringComparison.OrdinalIgnoreCase)).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }

    public static IReadOnlyDictionary<string, StreamInfo> ValidateReplacement(Project project, Asset asset, Asset candidate)
    {
        if ((asset.Source.Kind == "mgt") != (candidate.Source.Kind == "mgt"))
            throw new InvalidOperationException("El archivo sustituto debe ser una composición MGT compatible.");
        var used = project.Sequences.SelectMany(sequence => AllClips(sequence.Clips))
            .Where(clip => clip.AssetId == asset.Id).ToArray();
        foreach (var key in used.SelectMany(clip => clip.MgtOverrides.Keys).Distinct())
            if (asset.MgtParameters.FirstOrDefault(parameter => parameter.Id == key) is { } originalParameter &&
                !candidate.MgtParameters.Any(parameter => parameter.Id == key && parameter.Type == originalParameter.Type))
                throw new InvalidOperationException("El MGT sustituto no conserva el parámetro utilizado: " + key);
        var required = asset.Streams.Where(stream => used.Length == 0 ||
            used.Any(clip => clip.StreamId == stream.Id)).ToArray();
        var matches = new Dictionary<string, StreamInfo>();
        foreach (var original in required)
        {
            var choices = candidate.Streams.Where(stream => stream.Kind == original.Kind).ToArray();
            var found = choices.FirstOrDefault(stream => stream.Ordinal == original.Ordinal)
                ?? (choices.Length == 1 ? choices[0] : null);
            if (found == null || matches.Values.Contains(found))
                throw new InvalidOperationException($"No se pudo identificar la pista {original.Kind} #{original.Ordinal} utilizada por el montaje.");
            var end = used.Where(clip => clip.StreamId == original.Id)
                .Select(clip => RequiredSourceEnd(project, clip)).DefaultIfEmpty(0).Max();
            if (original.Kind != "image" && (!double.IsFinite(found.Duration.Seconds) ||
                found.Duration.Seconds + 1d / 48000 < end))
                throw new InvalidOperationException($"La pista {original.Kind} #{original.Ordinal} termina antes del tramo editado ({end:0.###} s).");
            matches[original.Id] = found;
        }
        return matches;
    }

    private static double RequiredSourceEnd(Project project, Clip clip)
    {
        var end = clip.EndSeconds;
        foreach (var sequence in project.Sequences)
        {
            foreach (var transition in sequence.VideoTransitions.Where(item => item.LeftClipId == clip.Id &&
                (item.Kind == Prometheus.Core.Timeline.VideoTransitionOperations.Dissolve || item.Mgt != null)))
                if (Prometheus.Core.Timeline.VideoTransitionOperations.Window(sequence, transition) is { } window)
                    end = Math.Max(end, window.End);
            foreach (var transition in sequence.AudioTransitions.Where(item => item.ClipId == clip.Id && item.RightClipId != null))
                if (Prometheus.Core.Timeline.AudioTransitionOperations.Span(sequence.Clips, transition) is { } span)
                    end = Math.Max(end, span.End);
        }
        return clip.SourceIn.Seconds + (end - clip.TimelineStart.Seconds) * clip.Speed.Seconds;
    }

    private static IEnumerable<Clip> AllClips(IEnumerable<Clip> clips)
    {
        foreach (var clip in clips)
        {
            yield return clip;
            if (clip.NestedContent != null)
                foreach (var child in AllClips(clip.NestedContent.Clips)) yield return child;
        }
    }

    public static void Relink(Project project, Asset asset, string path, Asset candidate)
    {
        var matches = ValidateReplacement(project, asset, candidate);
        Relink(asset, path);
        foreach (var representation in asset.Representations)
        foreach (var stream in asset.Streams)
            representation.StreamOrdinals.TryAdd(stream.Id, stream.Ordinal);
        var current = asset.Representations.First(item => ReferenceEquals(item.Locator, asset.EffectiveLocator));
        current.Sha256 = null;
        foreach (var stream in asset.Streams)
        {
            if (!matches.TryGetValue(stream.Id, out var replacement)) continue;
            current.StreamOrdinals[stream.Id] = replacement.Ordinal;
            stream.Ordinal = replacement.Ordinal;
            stream.Duration = replacement.Duration;
            stream.Codec = replacement.Codec;
            stream.Width = replacement.Width;
            stream.Height = replacement.Height;
            stream.SampleRate = replacement.SampleRate;
            stream.Channels = replacement.Channels;
            stream.NominalFps = replacement.NominalFps;
            stream.TimeBase = replacement.TimeBase;
        }
        foreach (var clip in project.Sequences.SelectMany(sequence => AllClips(sequence.Clips))
            .Where(clip => clip.AssetId == asset.Id))
            if (matches.TryGetValue(clip.StreamId, out var stream) && stream.Kind != "image")
                clip.AvailableSourceDurationSeconds = stream.Duration.Seconds;
        var retained = asset.Streams.Where(stream => matches.ContainsKey(stream.Id)).ToList();
        var additional = candidate.Streams.Where(stream => !matches.Values.Contains(stream)).ToArray();
        asset.Streams = retained.Concat(additional).ToList();
        foreach (var stream in additional) current.StreamOrdinals[stream.Id] = stream.Ordinal;
        asset.RecommendedDuration = candidate.RecommendedDuration;
        asset.ContainerFormat = candidate.ContainerFormat;
        asset.MgtParameters = candidate.MgtParameters;
    }

    public static void Relink(Asset asset, string path)
    {
        path = Path.GetFullPath(path);
        var file = new FileInfo(path);
        var length = file.Length;
        var modified = file.LastWriteTimeUtc.ToString("O");
        var locator = asset.EffectiveLocator;
        var representation = asset.Representations.FirstOrDefault(rep => ReferenceEquals(rep.Locator, locator));
        if (representation == null)
        {
            representation = new MediaRepresentation { Role = asset.Source.Kind == "web" ? "final" : "original" };
            asset.Representations.Add(representation);
        }
        representation.Locator.Path = path;
        representation.Locator.Relative = false;
        representation.SizeBytes = length;
        representation.ModifiedUtc = modified;
        if (asset.Source.Kind != "web")
        {
            asset.Source.Uri = path;
            asset.Source.Relative = false;
        }
    }
}
