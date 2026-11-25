using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

internal readonly record struct WaveformDrawingKey(string Source, double Start, double Duration,
    double SourceIn, double Speed, double Offset, double Zoom, int From, int To);

internal readonly record struct WaveformColumn(float Minimum, float Maximum, bool HasSamples);

internal sealed record WaveformColumns(int FirstPixel, WaveformColumn[] Columns)
{
    public static WaveformColumns Build(WaveformPeakData waveform, WaveformDrawingKey key)
    {
        var columns = new WaveformColumn[Math.Max(0, key.To - key.From)];
        for (var i = 0; i < columns.Length; i++)
        {
            var px = key.From + i;
            var start = Math.Max(key.Start, key.Offset + px / key.Zoom);
            var end = Math.Min(key.Start + key.Duration, key.Offset + (px + 1) / key.Zoom);
            if (end <= start) continue;
            var sourceStart = key.SourceIn + (start - key.Start) * key.Speed;
            var sourceEnd = key.SourceIn + (end - key.Start) * key.Speed;
            if (!waveform.TryGetRange(Math.Min(sourceStart, sourceEnd), Math.Max(sourceStart, sourceEnd), out var peak)) continue;
            columns[i] = new(Math.Min(peak.LeftMinimum, peak.RightMinimum),
                Math.Max(peak.LeftMaximum, peak.RightMaximum), true);
        }
        return new(key.From, columns);
    }
}

// Keep the tiny visible envelope independently of the full-source peak LRU.
// Evicting a long source must not blank an already drawn waveform on the next paint.
internal sealed class WaveformDrawingCache
{
    private sealed class Entry(Task<WaveformColumns?> task, LinkedListNode<WaveformDrawingKey> node)
    {
        public Task<WaveformColumns?> Task { get; set; } = task;
        public LinkedListNode<WaveformDrawingKey> Node { get; } = node;
        public WaveformFailure? Failure { get; set; }
        public long Bytes { get; set; }
    }
    private readonly object _gate = new();
    private readonly Dictionary<WaveformDrawingKey, Entry> _entries = new();
    private readonly LinkedList<WaveformDrawingKey> _recent = new();
    private readonly long _limit;
    private readonly Func<long> _clock;
    private int _pending;
    private static readonly Task<WaveformColumns?> Deferred = Task.FromResult<WaveformColumns?>(null);
    public long MemoryBytes { get; private set; }

    public WaveformDrawingCache(long memoryLimit = 16 * 1024 * 1024) : this(memoryLimit, () => Environment.TickCount64) { }
    internal WaveformDrawingCache(long memoryLimit, Func<long> clock) { _limit = memoryLimit; _clock = clock; }

    public TimeSpan RetryDelay(WaveformDrawingKey key)
    {
        lock (_gate) return TimeSpan.FromMilliseconds(_pending >= 32 ? 250 :
            _entries.TryGetValue(key, out var entry) && entry.Failure is { } failure ? failure.Remaining(_clock()) : 250);
    }

    public Task<WaveformColumns?> Request(WaveformDrawingKey key, Func<Task<WaveformPeakData?>> load)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _recent.Remove(existing.Node); _recent.AddFirst(existing.Node);
                if (!existing.Task.IsCompleted || existing.Failure is not { } failure || failure.Remaining(_clock()) > 0)
                    return existing.Task;
            }
            if (_pending >= 32) return existing?.Task ?? Deferred;
            var completion = new TaskCompletionSource<WaveformColumns?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = existing;
            if (entry == null)
            {
                entry = new Entry(completion.Task, _recent.AddFirst(key)) { Bytes = 256 };
                _entries.Add(key, entry); MemoryBytes += entry.Bytes;
            }
            else entry.Task = completion.Task;
            _pending++;
            _ = BuildAsync();
            return completion.Task;

            async Task BuildAsync()
            {
                WaveformColumns? columns = null;
                try
                {
                    var data = await load().ConfigureAwait(false);
                    if (data != null) columns = WaveformColumns.Build(data, key);
                }
                catch { /* Waveform display must not interrupt editing. */ }
                lock (_gate)
                {
                    _pending--;
                    entry.Failure = columns == null ? WaveformFailure.Next(entry.Failure, _clock()) : null;
                    var bytes = 256 + (columns?.Columns.LongLength ?? 0) * 12;
                    MemoryBytes += bytes - entry.Bytes; entry.Bytes = bytes;
                    // Complete before eviction so only ready entries can leave the cache.
                    completion.SetResult(columns);
                    var node = _recent.Last;
                    while (MemoryBytes > _limit && _entries.Count > 1 && node != null)
                    {
                        var previous = node.Previous;
                        var candidate = _entries[node.Value];
                        if (candidate.Task.IsCompleted)
                        {
                            MemoryBytes -= candidate.Bytes;
                            _entries.Remove(node.Value); _recent.Remove(node);
                        }
                        node = previous;
                    }
                }
            }
        }
    }
}
