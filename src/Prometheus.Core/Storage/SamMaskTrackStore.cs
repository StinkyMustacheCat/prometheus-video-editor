using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Prometheus.Core.Model;

namespace Prometheus.Core.Storage;

/// <summary>Indexed, immutable masks kept beside the project instead of bloating its JSON.</summary>
public static class SamMaskTrackStore
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PTHSAM25");
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, SamMaskKeyframe> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> CacheOrder = new();

    public static string PathFor(string projectPath, string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("El identificador de la máscara SAM no es válido.");
        return Path.Combine(ProjectStorage.GetSamDirectory(projectPath), id + ".sammask");
    }

    public static void Write(string projectPath, SamMaskTrack track,
        IReadOnlyList<SamMaskKeyframe> frames)
    {
        if (track.FrameCount < 1 || frames.Count != track.FrameCount ||
            track.Width < 1 || track.Height < 1 || (long)track.Width * track.Height > 8_000_000)
            throw new ArgumentException("El seguimiento SAM está incompleto.");
        var path = PathFor(projectPath, track.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 1 << 20))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(1);
                writer.Write(track.Width);
                writer.Write(track.Height);
                writer.Write(track.FrameCount);
                writer.Write(track.StartSourceSeconds);
                writer.Write(track.FramesPerSecond);
                var indexStart = stream.Position;
                for (var i = 0; i < frames.Count; i++) { writer.Write(0L); writer.Write(0); }
                var offsets = new (long Offset, int Length)[frames.Count];
                for (var i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    if (frame.Width != track.Width || frame.Height != track.Height ||
                        frame.AlphaDeflate.Length is < 1 or > 8_000_000)
                        throw new InvalidDataException("El seguimiento SAM contiene un fotograma inválido.");
                    offsets[i] = (stream.Position, frame.AlphaDeflate.Length);
                    writer.Write(frame.AlphaDeflate);
                }
                stream.Position = indexStart;
                foreach (var entry in offsets) { writer.Write(entry.Offset); writer.Write(entry.Length); }
                stream.Flush(true);
            }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Writes a new immutable track while streaming unchanged frames from the old one.</summary>
    public static void WritePatched(string projectPath, SamMaskTrack previous,
        SamMaskTrack replacement, IReadOnlyDictionary<int, SamMaskKeyframe> changed)
    {
        if (previous.FrameCount != replacement.FrameCount ||
            previous.Width != replacement.Width || previous.Height != replacement.Height ||
            previous.StartSourceSeconds != replacement.StartSourceSeconds ||
            previous.FramesPerSecond != replacement.FramesPerSecond)
            throw new ArgumentException("El seguimiento SAM cambió de formato durante la corrección.");
        Write(projectPath, replacement, new PatchedFrames(projectPath, previous, changed));
    }

    private sealed class PatchedFrames(string projectPath, SamMaskTrack previous,
        IReadOnlyDictionary<int, SamMaskKeyframe> changed) : IReadOnlyList<SamMaskKeyframe>
    {
        public int Count => previous.FrameCount;
        public SamMaskKeyframe this[int index] => changed.TryGetValue(index, out var patch)
            ? patch : Read(projectPath, previous, index);
        public IEnumerator<SamMaskKeyframe> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) yield return this[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static SamMaskKeyframe? At(Clip clip, double sourceSeconds, double fps,
        string? projectPath)
    {
        var manual = SamMaskKeyframe.At(clip, sourceSeconds, fps);
        if (manual != null || projectPath == null) return manual;
        foreach (var track in clip.SamMaskTracks.AsEnumerable().Reverse())
            if (track.FrameAt(sourceSeconds) is { } index)
                return Read(projectPath, track, index);
        return null;
    }

    public static SamMaskKeyframe Read(string projectPath, SamMaskTrack track, int index)
    {
        if (index < 0 || index >= track.FrameCount) throw new ArgumentOutOfRangeException(nameof(index));
        var path = PathFor(projectPath, track.Id);
        var key = path + ":" + index;
        lock (CacheLock) if (Cache.TryGetValue(key, out var cached)) return cached;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(8).SequenceEqual(Magic) || reader.ReadInt32() != 1 ||
            reader.ReadInt32() != track.Width || reader.ReadInt32() != track.Height ||
            reader.ReadInt32() != track.FrameCount || reader.ReadDouble() != track.StartSourceSeconds ||
            reader.ReadDouble() != track.FramesPerSecond)
            throw new InvalidDataException("El archivo de seguimiento SAM no coincide con el proyecto.");
        stream.Position = 40L + index * 12L;
        var offset = reader.ReadInt64();
        var length = reader.ReadInt32();
        if (length is < 1 or > 8_000_000 || offset < 40L + track.FrameCount * 12L ||
            offset + length > stream.Length)
            throw new InvalidDataException("El índice de máscaras SAM está dañado.");
        stream.Position = offset;
        var compressed = reader.ReadBytes(length);
        if (compressed.Length != length) throw new EndOfStreamException();
        var result = new SamMaskKeyframe { Id = track.Id + ":" + index,
            SourceSeconds = track.StartSourceSeconds + index / track.FramesPerSecond,
            Width = track.Width, Height = track.Height, AlphaDeflate = compressed };
        lock (CacheLock)
        {
            Cache[key] = result;
            CacheOrder.Enqueue(key);
            while (CacheOrder.Count > 128) Cache.Remove(CacheOrder.Dequeue());
        }
        return result;
    }

    public static void CopyReferenced(Project project, string? sourceProjectPath,
        string targetProjectPath)
    {
        if (sourceProjectPath == null || Path.GetFullPath(sourceProjectPath).Equals(
            Path.GetFullPath(targetProjectPath), StringComparison.OrdinalIgnoreCase)) return;
        foreach (var track in project.Sequences.SelectMany(sequence => AllClips(sequence.Clips))
            .SelectMany(clip => clip.SamMaskTracks.Concat(
                clip.MaskLayers.SelectMany(layer => layer.SamMaskTracks)))
            .DistinctBy(track => track.Id))
            CopyOne(sourceProjectPath, targetProjectPath, track.Id);
    }

    private static IEnumerable<Clip> AllClips(IEnumerable<Clip> clips)
    {
        foreach (var clip in clips)
        {
            yield return clip;
            if (clip.NestedContent == null) continue;
            foreach (var child in AllClips(clip.NestedContent.Clips)) yield return child;
        }
    }

    public static void CopyOne(string sourceProjectPath, string targetProjectPath, string trackId)
    {
        var source = PathFor(sourceProjectPath, trackId);
        if (!File.Exists(source)) throw new FileNotFoundException("Falta el seguimiento SAM.", source);
        var target = PathFor(targetProjectPath, trackId);
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target),
            StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        ProjectStorage.CopyResourceTransactional(source, target);
    }
}
