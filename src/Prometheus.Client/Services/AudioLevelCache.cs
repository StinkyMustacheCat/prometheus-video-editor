using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

public readonly record struct StereoLevel(double Left, double Right);

/// <summary>Small source-level peak windows used by the live mixer meters.</summary>
public static class AudioLevelCache
{
    private const int SampleRate = 8000;
    private const int WindowFrames = 800;
    private const int SegmentSeconds = 10;
    public const double PeakWindowSeconds = (double)WindowFrames / SampleRate;
    public const int SegmentDurationSeconds = SegmentSeconds;
    internal const int MaximumSegments = 256;
    private const int MaximumPendingDecodes = 8;
    private sealed record CachedSegment(Task<StereoLevel[]> Data, DateTime Created)
    {
        public long LastAccess { get; set; }
    }
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, CachedSegment> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Task<StereoLevel[]> Empty = Task.FromResult(Array.Empty<StereoLevel>());
    private static long _accessSequence;
    internal static int CachedSegmentsCount { get { lock (CacheGate) return Cache.Count; } }
    private static readonly SemaphoreSlim DecoderSlots = new(2);

    public static void Warm(Project project)
    {
        var sequence = project.ActiveSequence;
        if (sequence == null) return;
        void WarmClip(Clip clip)
        {
            if (clip.NestedContent != null)
            {
                foreach (var child in clip.NestedContent.Clips) WarmClip(child);
                return;
            }
            var asset = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
            var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId && s.Kind == "audio");
            var path = MgtProjectAudio.Resolve(asset) ?? asset?.EffectiveLocator?.Path;
            if (stream != null && !string.IsNullOrEmpty(path) && File.Exists(path))
                Load(path, stream.Ordinal, Math.Max(0, (int)(clip.SourceIn.Seconds / SegmentSeconds)));
        }
        foreach (var clip in sequence.Clips) WarmClip(clip);
    }

    public static StereoLevel TrackLevel(Project project, Track track, double timelineTime) =>
        track.Enabled && track.Kind == "audio" && TrackLevels(project, timelineTime).TryGetValue(track.Id, out var level)
            ? level : default;

    public static IReadOnlyDictionary<string, StereoLevel> TrackLevels(Project project, double timelineTime)
    {
        var levels = new Dictionary<string, StereoLevel>();
        if (project.ActiveSequence == null) return levels;
        // Only the current meter window is needed. Future clips must not trigger
        // media lookups or allocate a complete remaining-timeline mix on every tick.
        var plan = AudioMixPlan.Create(project, timelineTime, timelineTime + PeakWindowSeconds, null);
        var groups = plan.Groups.ToDictionary(group => group.Id);
        foreach (var input in plan.Inputs.Where(input => input.TimelineDelay < .0001))
        {
            var sourceTime = input.SourceStart;
            var segment = Math.Max(0, (int)(sourceTime / SegmentSeconds));
            var task = Load(input.Path, input.StreamOrdinal, segment);
            if (sourceTime - segment * SegmentSeconds >= SegmentSeconds - 1.5)
                Load(input.Path, input.StreamOrdinal, segment + 1);
            if (!task.IsCompletedSuccessfully) continue;
            var peaks = task.Result;
            var index = (int)((sourceTime - segment * SegmentSeconds) * 10);
            if (index < 0 || index >= peaks.Length) continue;
            var gain = Math.Pow(10, input.GainDb / 20) * input.Envelope.GainAt(timelineTime);
            for (var id = input.ParentGroupId; id != null && groups.TryGetValue(id, out var group); id = group.ParentId)
                gain *= group.Envelope.GainAt(timelineTime);
            var current = levels.GetValueOrDefault(input.RootTrackId);
            var left = current.Left + peaks[index].Left * gain * (input.Balance > 0 ? 1 - input.Balance : 1);
            var right = current.Right + peaks[index].Right * gain * (input.Balance < 0 ? 1 + input.Balance : 1);
            levels[input.RootTrackId] = new StereoLevel(left, right);
        }
        return levels;
    }

    private static Task<StereoLevel[]> Load(string path, int ordinal, int segment)
    {
        string key;
        try { key = path + "|" + ordinal + "|" + segment + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Empty; }
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var entry))
            {
                if (entry.Data.IsCompletedSuccessfully && entry.Data.Result.Length == 0 &&
                    DateTime.UtcNow - entry.Created > TimeSpan.FromSeconds(5))
                    Cache.Remove(key);
                else
                {
                    entry.LastAccess = ++_accessSequence;
                    return entry.Data;
                }
            }
            if (Cache.Values.Count(item => !item.Data.IsCompleted) >= MaximumPendingDecodes) return Empty;
            if (Cache.Count >= MaximumSegments)
            {
                var oldest = Cache.Where(pair => pair.Value.Data.IsCompleted)
                    .MinBy(pair => pair.Value.LastAccess);
                if (oldest.Value == null) return Empty;
                Cache.Remove(oldest.Key);
            }
            var task = Task.Run(() => DecodeAsync(path, ordinal, segment));
            Cache[key] = new CachedSegment(task, DateTime.UtcNow) { LastAccess = ++_accessSequence };
            return task;
        }
    }

    public static Task<StereoLevel[]> RequestSegment(string path, int ordinal, int segment) =>
        Load(path, ordinal, Math.Max(0, segment));

    private static async Task<StereoLevel[]> DecodeAsync(string path, int ordinal, int segment)
    {
        await DecoderSlots.WaitAsync();
        try
        {
            var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-ss", (segment * SegmentSeconds).ToString(),
                         "-i", path, "-t", SegmentSeconds.ToString(), "-map", "0:" + ordinal,
                         "-vn", "-f", "s16le", "-acodec", "pcm_s16le", "-ac", "2", "-ar", SampleRate.ToString(), "pipe:1" })
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo analizar audio");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var stop = deadline.Token.Register(() => ExternalProcess.Kill(process));
            var errors = ExternalProcess.ReadDiagnosticsAsync(process.StandardError);
            try
            {
                var peaks = new List<StereoLevel>();
                var buffer = new byte[WindowFrames * 4];
                while (true)
                {
                    var filled = 0;
                    while (filled < buffer.Length)
                    {
                        var read = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), deadline.Token);
                        if (read == 0) break;
                        filled += read;
                    }
                    if (filled == 0) break;
                    double left = 0, right = 0;
                    for (var i = 0; i + 3 < filled; i += 4)
                    {
                        left = Math.Max(left, Math.Abs(BitConverter.ToInt16(buffer, i) / 32768.0));
                        right = Math.Max(right, Math.Abs(BitConverter.ToInt16(buffer, i + 2) / 32768.0));
                    }
                    peaks.Add(new StereoLevel(left, right));
                    if (filled < buffer.Length) break;
                }
                await process.WaitForExitAsync(deadline.Token);
                await errors;
                return process.ExitCode == 0 ? peaks.ToArray() : Array.Empty<StereoLevel>();
            }
            finally { ExternalProcess.Kill(process); }
        }
        catch { return Array.Empty<StereoLevel>(); }
        finally { DecoderSlots.Release(); }
    }
}
