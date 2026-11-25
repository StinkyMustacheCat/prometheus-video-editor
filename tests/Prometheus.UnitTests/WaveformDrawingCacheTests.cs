using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class WaveformDrawingCacheTests
{
    private static WaveformPeakData Source() => new(Enumerable.Range(0, 5000)
        .Select(i => new WaveformPeak(-i % 17 / 10f, i % 23 / 10f,
            -i % 29 / 10f, i % 31 / 10f)).ToArray());

    private static WaveformDrawingKey Key(string source = "source") => new(source,
        1.025, 2.333, .123, 1.25, .9, 173.3, 0, 550);

    [Theory]
    [InlineData(1.25)]
    [InlineData(-.75)]
    public void VisibleColumns_MatchDirectPeakQueriesAtTrimSpeedAndViewport(double speed)
    {
        var source = Source();
        var key = Key() with { Speed = speed, SourceIn = speed < 0 ? 4 : .123 };
        var drawing = WaveformColumns.Build(source, key);
        for (var i = 0; i < drawing.Columns.Length; i++)
        {
            var start = Math.Max(key.Start, key.Offset + (key.From + i) / key.Zoom);
            var end = Math.Min(key.Start + key.Duration, key.Offset + (key.From + i + 1) / key.Zoom);
            var a = key.SourceIn + (start - key.Start) * speed;
            var b = key.SourceIn + (end - key.Start) * speed;
            var found = end > start && source.TryGetRange(Math.Min(a, b), Math.Max(a, b), out _);
            Assert.Equal(found, drawing.Columns[i].HasSamples);
            if (!found) continue;
            source.TryGetRange(Math.Min(a, b), Math.Max(a, b), out var peak);
            Assert.Equal(Math.Min(peak.LeftMinimum, peak.RightMinimum), drawing.Columns[i].Minimum);
            Assert.Equal(Math.Max(peak.LeftMaximum, peak.RightMaximum), drawing.Columns[i].Maximum);
        }
    }

    [Fact]
    public void OneMillisecondTransient_PreservesSignedOverrangeAcrossChannels()
    {
        var peaks = new WaveformPeak[100];
        peaks[11] = new(-.2f, 1.4f, -1.7f, .3f);
        var source = new WaveformPeakData(peaks);
        var key = new WaveformDrawingKey("impulse", 0, .1, 0, 1, 0, 1000, 0, 100);
        var columns = WaveformColumns.Build(source, key).Columns;
        Assert.Equal(0, columns[10].Maximum);
        Assert.Equal(1.4f, columns[11].Maximum);
        Assert.Equal(-1.7f, columns[11].Minimum);
        var overview = WaveformColumns.Build(source, key with { Zoom = 10, To = 1 });
        Assert.Equal(1.4f, overview.Columns[0].Maximum);
        Assert.Equal(-1.7f, overview.Columns[0].Minimum);
    }

    [Fact]
    public async Task PendingRequests_CoalesceAndCompletedDrawingSurvivesSourceEviction()
    {
        var cache = new WaveformDrawingCache();
        var pending = new TaskCompletionSource<WaveformPeakData?>();
        var calls = 0;
        var first = cache.Request(Key(), () => { calls++; return pending.Task; });
        Assert.Same(first, cache.Request(Key(), () => throw new Exception("Duplicate load")));
        pending.SetResult(Source());
        var columns = await first;
        Assert.NotNull(columns);
        Assert.Same(first, cache.Request(Key(), () => throw new Exception("Evicted source requested again")));
        Assert.Equal(1, calls);
        Assert.Same(columns, await first);
    }

    [Fact]
    public async Task DrawingLru_RetainsRecentlyViewedEnvelopeAndBoundsMemory()
    {
        var source = Source();
        var key = Key();
        var size = 256 + 12 * (key.To - key.From);
        var cache = new WaveformDrawingCache(size * 2);
        Task<WaveformPeakData?> Load() => Task.FromResult<WaveformPeakData?>(source);
        var a = cache.Request(key, Load);
        await a;
        var b = cache.Request(key with { Source = "b" }, Load);
        await b;
        Assert.Same(a, cache.Request(key, Load));
        await cache.Request(key with { Source = "c" }, Load);
        Assert.InRange(cache.MemoryBytes, 1, size * 2);
        Assert.Same(a, cache.Request(key, Load));
        Assert.NotSame(b, cache.Request(key with { Source = "b" }, Load));
    }

    [Fact]
    public async Task UnavailableSource_DoesNotStartEndlessReloads()
    {
        var cache = new WaveformDrawingCache();
        var task = cache.Request(Key(), () => Task.FromException<WaveformPeakData?>(new IOException("Offline")));
        Assert.Null(await task);
        Assert.Same(task, cache.Request(Key(), () => throw new Exception("Retried")));
    }

    [Fact]
    public async Task PendingLimit_DefersNewWorkWithoutLosingAlreadyRequestedDrawing()
    {
        var cache = new WaveformDrawingCache();
        var pending = new TaskCompletionSource<WaveformPeakData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = Enumerable.Range(0, 32).Select(i => cache.Request(Key(i.ToString()), () => pending.Task)).ToArray();
        Assert.Null(await cache.Request(Key("deferred"), () => throw new Exception("Limit ignored")));
        Assert.Same(jobs[0], cache.Request(Key("0"), () => throw new Exception("Duplicated")));
        pending.SetResult(Source());
        await Task.WhenAll(jobs);
        Assert.NotNull(await cache.Request(Key("deferred"), () => Task.FromResult<WaveformPeakData?>(Source())));
    }

    [Fact]
    public void SourceIdentity_SeparatesChangesToFileStreamAndRate()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            File.WriteAllBytes(path, [1]);
            var key = WaveformPeakCache.SourceKey(path, 0, 48000);
            Assert.NotNull(key);
            Assert.Equal(key, WaveformPeakCache.SourceKey(path, 0, 0));
            Assert.NotEqual(key, WaveformPeakCache.SourceKey(path, 1, 48000));
            Assert.NotEqual(key, WaveformPeakCache.SourceKey(path, 0, 44100));
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
            Assert.NotEqual(key, WaveformPeakCache.SourceKey(path, 0, 48000));
            File.WriteAllBytes(path, [1, 2]);
            Assert.NotEqual(key, WaveformPeakCache.SourceKey(path, 0, 48000));
        }
        finally { File.Delete(path); }
        Assert.Null(WaveformPeakCache.SourceKey(path, 0, 48000));
    }
}
