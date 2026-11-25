using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

public readonly record struct WaveformPeak(float LeftMinimum, float LeftMaximum,
    float RightMinimum, float RightMaximum)
{
    public static WaveformPeak Merge(WaveformPeak a, WaveformPeak b) => new(
        Math.Min(a.LeftMinimum, b.LeftMinimum), Math.Max(a.LeftMaximum, b.LeftMaximum),
        Math.Min(a.RightMinimum, b.RightMinimum), Math.Max(a.RightMaximum, b.RightMaximum));
}

public sealed class WaveformPeakData
{
    private readonly WaveformPeak[][] _levels;

    public int Count => _levels[0].Length;
    public long MemoryBytes { get; }

    public WaveformPeakData(WaveformPeak[] basePeaks)
    {
        if (basePeaks.Length == 0) throw new ArgumentException("La onda no contiene muestras.", nameof(basePeaks));
        var levels = new List<WaveformPeak[]> { basePeaks };
        while (levels[^1].Length > 1)
        {
            var previous = levels[^1];
            var next = new WaveformPeak[(previous.Length + 1) / 2];
            for (var i = 0; i < next.Length; i++)
                next[i] = i * 2 + 1 < previous.Length
                    ? WaveformPeak.Merge(previous[i * 2], previous[i * 2 + 1])
                    : previous[i * 2];
            levels.Add(next);
        }
        _levels = levels.ToArray();
        MemoryBytes = _levels.Sum(level => (long)level.Length * 16);
    }

    // Every source sample in the displayed time interval contributes to the column.
    public bool TryGetRange(double startSeconds, double endSeconds, out WaveformPeak peak)
    {
        peak = default;
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) ||
            endSeconds <= 0 || startSeconds >= Count / 1000.0 || endSeconds <= startSeconds)
            return false;
        var first = Math.Clamp((int)Math.Floor(startSeconds * 1000), 0, Count - 1);
        var last = Math.Clamp((int)Math.Ceiling(endSeconds * 1000), first + 1, Count);
        var found = false;
        while (first < last)
        {
            var alignment = first == 0 ? _levels.Length - 1 : BitOperations.TrailingZeroCount((uint)first);
            var size = BitOperations.Log2((uint)(last - first));
            var level = Math.Min(Math.Min(alignment, size), _levels.Length - 1);
            var current = _levels[level][first >> level];
            peak = found ? WaveformPeak.Merge(peak, current) : current;
            found = true;
            first += 1 << level;
        }
        return found;
    }
}

public static class WaveformPeakCache
{
    private const int BinsPerSecond = 1000;
    private const int CacheMagic = 0x33465750; // PWF3: signed float peaks at 1 ms.
    private const long MemoryLimit = 128L * 1024 * 1024;
    private const long DiskLimit = 2L * 1024 * 1024 * 1024;
    private static readonly string Folder = Environment.GetEnvironmentVariable("PROMETHEUS_WAVEFORM_CACHE") ??
        Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory)!, "Prometheus", "WaveformCacheV3");
    private static readonly string LegacyFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "WaveformCacheV3");
    private static readonly SemaphoreSlim DecoderSlot = new(1);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (WaveformPeakData Data, LinkedListNode<string> Node)> Memory = new();
    private static readonly LinkedList<string> Recent = new();
    private static readonly Dictionary<string, Task<WaveformPeakData?>> Pending = new();
    private static readonly Dictionary<string, WaveformFailure> Failed = new();
    private static long _memoryBytes;

    public static Task<WaveformPeakData?> Request(string path, int streamOrdinal, int sampleRate)
    {
        var key = SourceKey(path, streamOrdinal, sampleRate);
        if (key == null) return Task.FromResult<WaveformPeakData?>(null);
        sampleRate = NormalizeRate(sampleRate);
        lock (Gate)
        {
            if (Memory.TryGetValue(key, out var cached))
            {
                Recent.Remove(cached.Node);
                Recent.AddFirst(cached.Node);
                return Task.FromResult<WaveformPeakData?>(cached.Data);
            }
            if (Pending.TryGetValue(key, out var task)) return task;
            if (Failed.TryGetValue(key, out var failed) && failed.Remaining(Environment.TickCount64) > 0)
                return Task.FromResult<WaveformPeakData?>(null);
            var completion = new TaskCompletionSource<WaveformPeakData?>(TaskCreationOptions.RunContinuationsAsynchronously);
            task = completion.Task;
            Pending[key] = task;
            _ = Task.Run(async () =>
            {
                WaveformPeakData? data = null;
                try { data = await LoadOrBuildAsync(Path.GetFullPath(path), streamOrdinal, sampleRate, key); }
                catch { /* An unavailable waveform must never interrupt timeline editing. */ }
                lock (Gate)
                {
                    if (data is null)
                    {
                        Failed[key] = WaveformFailure.Next(Failed.TryGetValue(key, out var prior) ? prior : null, Environment.TickCount64);
                        if (Failed.Count > 1024) Failed.Remove(Failed.MinBy(pair => pair.Value.RetryAt).Key);
                    }
                    else { Failed.Remove(key); StoreInMemory(key, data); }
                    Pending.Remove(key);
                }
                completion.SetResult(data);
            });
            return task;
        }
    }

    private static int NormalizeRate(int rate) => rate is >= 8000 and <= 384000 ? rate : 48000;

    internal static TimeSpan RetryDelay(string source)
    {
        lock (Gate) return TimeSpan.FromMilliseconds(Failed.TryGetValue(source, out var failure)
            ? failure.Remaining(Environment.TickCount64) : 0);
    }

    internal static string? SourceKey(string path, int streamOrdinal, int sampleRate)
    {
        try
        {
            var file = new FileInfo(path); if (!file.Exists) return null;
            sampleRate = NormalizeRate(sampleRate);
            var identity = $"PWF3|{file.FullName.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{streamOrdinal}|{sampleRate}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }
        catch { return null; }
    }

    private static void StoreInMemory(string key, WaveformPeakData data)
    {
        var node = Recent.AddFirst(key);
        Memory[key] = (data, node);
        _memoryBytes += data.MemoryBytes;
        while (_memoryBytes > MemoryLimit && Recent.Count > 1)
        {
            var last = Recent.Last!;
            _memoryBytes -= Memory[last.Value].Data.MemoryBytes;
            Memory.Remove(last.Value);
            Recent.RemoveLast();
        }
    }

    private static async Task<WaveformPeakData> LoadOrBuildAsync(string path, int ordinal, int rate, string key)
    {
        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, key + ".pwf");
        if (File.Exists(target))
        {
            try
            {
                var result = Load(target);
                File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
                return result;
            }
            catch { try { File.Delete(target); } catch { } }
        }
        var legacy = Path.Combine(LegacyFolder, key + ".pwf");
        if (!string.Equals(legacy, target, StringComparison.OrdinalIgnoreCase) && File.Exists(legacy))
            try { return Load(legacy); } catch { }
        await DecoderSlot.WaitAsync();
        try
        {
            // Recheck after waiting: another completed request may have written the same file.
            if (File.Exists(target)) return Load(target);
            var peaks = await DecodeAsync(path, ordinal, rate);
            var data = new WaveformPeakData(peaks);
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new BinaryWriter(new FileStream(temp, FileMode.CreateNew, FileAccess.Write)))
                {
                    writer.Write(CacheMagic);
                    writer.Write(BinsPerSecond);
                    writer.Write(peaks.Length);
                    foreach (var peak in peaks)
                    {
                        writer.Write(peak.LeftMinimum); writer.Write(peak.LeftMaximum);
                        writer.Write(peak.RightMinimum); writer.Write(peak.RightMaximum);
                    }
                }
                File.Move(temp, target, true);
            }
            finally { try { File.Delete(temp); } catch { } }
            TrimDiskCache(target);
            return data;
        }
        finally { DecoderSlot.Release(); }
    }

    private static WaveformPeakData Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadInt32() != CacheMagic || reader.ReadInt32() != BinsPerSecond)
            throw new InvalidDataException("Caché de onda incompatible.");
        var count = reader.ReadInt32();
        if (count < 1 || count > 100_000_000 || reader.BaseStream.Length != 12L + count * 16L)
            throw new InvalidDataException("Caché de onda dañada.");
        var peaks = new WaveformPeak[count];
        for (var i = 0; i < count; i++)
            peaks[i] = new WaveformPeak(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return new WaveformPeakData(peaks);
    }

    private static async Task<WaveformPeak[]> DecodeAsync(string path, int ordinal, int rate)
    {
        var start = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", path,
                     "-map", "0:" + ordinal, "-vn", "-ac", "2", "-ar", rate.ToString(),
                     "-acodec", "pcm_f32le", "-f", "f32le", "pipe:1" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo analizar audio.");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var errorTask = process.StandardError.ReadToEndAsync();
        var buffer = new byte[65544];
        var peaks = new List<WaveformPeak>();
        var pendingBytes = 0;
        long frame = 0;
        long currentBin = -1;
        float leftMin = 0, leftMax = 0, rightMin = 0, rightMax = 0;
        while (true)
        {
            var read = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(pendingBytes, buffer.Length - pendingBytes));
            if (read == 0) break;
            var available = pendingBytes + read;
            var consumed = available - available % 8;
            for (var i = 0; i < consumed; i += 8)
            {
                var bin = frame * BinsPerSecond / rate;
                if (bin != currentBin)
                {
                    if (currentBin >= 0) peaks.Add(new WaveformPeak(leftMin, leftMax, rightMin, rightMax));
                    currentBin = bin;
                    leftMin = leftMax = rightMin = rightMax = 0;
                }
                var left = BitConverter.ToSingle(buffer, i);
                var right = BitConverter.ToSingle(buffer, i + 4);
                if (float.IsFinite(left)) { leftMin = Math.Min(leftMin, left); leftMax = Math.Max(leftMax, left); }
                if (float.IsFinite(right)) { rightMin = Math.Min(rightMin, right); rightMax = Math.Max(rightMax, right); }
                frame++;
            }
            pendingBytes = available - consumed;
            if (pendingBytes != 0) Buffer.BlockCopy(buffer, consumed, buffer, 0, pendingBytes);
        }
        if (currentBin >= 0) peaks.Add(new WaveformPeak(leftMin, leftMax, rightMin, rightMax));
        await process.WaitForExitAsync();
        var error = await errorTask;
        if (process.ExitCode != 0 || pendingBytes != 0 || peaks.Count == 0)
            throw new InvalidDataException("No se pudo construir la onda: " + error.Trim());
        return peaks.ToArray();
    }

    private static void TrimDiskCache(string keep)
    {
        try
        {
            var files = new DirectoryInfo(Folder).EnumerateFiles("*.pwf").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            long size = 0;
            foreach (var file in files)
            {
                size += file.Length;
                if (size <= DiskLimit || file.FullName.Equals(keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { file.Delete(); size -= file.Length; } catch { }
            }
        }
        catch { }
    }
}
