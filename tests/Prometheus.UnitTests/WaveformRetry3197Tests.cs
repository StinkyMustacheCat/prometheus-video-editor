using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class WaveformRetry3197Tests
{
    private static WaveformDrawingKey Key(string source="source")=>new(source,0,.1,0,1,0,1000,0,100);
    private static WaveformPeakData Peaks()=>new(Enumerable.Repeat(new WaveformPeak(-.3f,1.4f,-.7f,.2f),100).ToArray());

    [Fact]
    public void FailuresUseMonotonicBoundedBackoff()
    {
        WaveformFailure? failure=null;long now=100;
        foreach(var delay in new[]{2000,4000,8000,16000,32000,60000,60000,60000})
        {
            failure=WaveformFailure.Next(failure,now);
            Assert.Equal(delay,failure.Value.Remaining(now));
            Assert.Equal(1,failure.Value.Remaining(now+delay-1));
            Assert.Equal(0,failure.Value.Remaining(now+delay));
            now+=delay;
        }
    }

    [Fact]
    public async Task DrawingFailureRetriesAfterCooldownAndCoalescesConcurrentRecovery()
    {
        long clock=0;var cache=new WaveformDrawingCache(1024*1024,()=>clock);var calls=0;var key=Key();
        var failed=cache.Request(key,()=>{calls++;return Task.FromResult<WaveformPeakData?>(null);});
        Assert.Null(await failed);
        clock=1999;
        Assert.Same(failed,cache.Request(key,()=>throw new Exception("Early retry")));
        Assert.Equal(1,calls);
        clock=2000;var pending=new TaskCompletionSource<WaveformPeakData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery=cache.Request(key,()=>{calls++;return pending.Task;});
        Assert.NotSame(failed,recovery);
        for(var i=0;i<100;i++)Assert.Same(recovery,cache.Request(key,()=>throw new Exception("Duplicate work")));
        pending.SetResult(Peaks());Assert.NotNull(await recovery);
        Assert.Equal(2,calls);clock=1_000_000;
        Assert.Same(recovery,cache.Request(key,()=>throw new Exception("Rebuilt successful waveform")));
    }

    [Fact]
    public async Task RepeatedDrawingFailuresDoNotSpinAndKeepMemoryAccountingStable()
    {
        long clock=0;var cache=new WaveformDrawingCache(1024*1024,()=>clock);var calls=0;var key=Key();
        Task<WaveformPeakData?> Fail(){calls++;throw new IOException("Temporarily unavailable");}
        var first=cache.Request(key,Fail);Assert.Null(await first);var bytes=cache.MemoryBytes;
        for(var i=0;i<1000;i++)Assert.Same(first,cache.Request(key,Fail));
        Assert.Equal(1,calls);clock=2000;
        var second=cache.Request(key,Fail);Assert.Null(await second);Assert.Equal(2,calls);
        Assert.Equal(bytes,cache.MemoryBytes);Assert.Equal(4000,cache.RetryDelay(key).TotalMilliseconds);
        clock=5999;Assert.Same(second,cache.Request(key,Fail));Assert.Equal(2,calls);
        clock=6000;Assert.NotNull(await cache.Request(key,()=>Task.FromResult<WaveformPeakData?>(Peaks())));
        Assert.True(cache.MemoryBytes>bytes);
    }

    [Fact]
    public async Task RetryingOneSourceNeverInvalidatesAlreadyDrawnNeighbors()
    {
        long clock=0;var cache=new WaveformDrawingCache(1024*1024,()=>clock);var ready=Key("ready");var unavailable=Key("unavailable");
        var good=cache.Request(ready,()=>Task.FromResult<WaveformPeakData?>(Peaks()));var columns=await good;
        Assert.Null(await cache.Request(unavailable,()=>Task.FromResult<WaveformPeakData?>(null)));
        clock=2000;
        Assert.NotNull(await cache.Request(unavailable,()=>Task.FromResult<WaveformPeakData?>(Peaks())));
        Assert.Same(good,cache.Request(ready,()=>throw new Exception("Discarded ready neighbor")));
        Assert.Same(columns,await good);
        Assert.Equal(1.4f,columns!.Columns[11].Maximum);
    }

    [Fact]
    public async Task SourceRecoversWithoutChangingItsIdentityOrRestartingTheSession()
    {
        var path=Path.Combine(Path.GetTempPath(),"waveform-retry-"+Guid.NewGuid()+".wav");
        try
        {
            using(var writer=new BinaryWriter(File.Create(path)))
            {writer.Write("RIFF"u8);writer.Write(36+4800*8);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)3);writer.Write((short)2);
             writer.Write(48000);writer.Write(48000*8);writer.Write((short)8);writer.Write((short)32);writer.Write("data"u8);writer.Write(4800*8);
             for(var i=0;i<4800;i++){writer.Write(i==528?1.4f:0f);writer.Write(i==576?-.7f:0f);}}
            var valid=File.ReadAllBytes(path);var broken=valid.ToArray();Array.Fill(broken,(byte)0,0,12);
            File.WriteAllBytes(path,broken);var stamp=File.GetLastWriteTimeUtc(path);var key=WaveformPeakCache.SourceKey(path,0,48000)!;
            Assert.Null(await WaveformPeakCache.Request(path,0,48000));
            File.WriteAllBytes(path,valid);File.SetLastWriteTimeUtc(path,stamp);
            Assert.Equal(key,WaveformPeakCache.SourceKey(path,0,48000));
            Assert.Null(await WaveformPeakCache.Request(path,0,48000));
            await Task.Delay(WaveformPeakCache.RetryDelay(key)+TimeSpan.FromMilliseconds(75));
            var tasks=Enumerable.Range(0,20).Select(_=>WaveformPeakCache.Request(path,0,48000)).ToArray();var values=await Task.WhenAll(tasks);
            var data=values[0];Assert.NotNull(data);Assert.All(values,value=>Assert.Same(data,value));
            Assert.True(data!.TryGetRange(.011,.012,out var peak));Assert.InRange(peak.LeftMaximum,1.39f,1.41f);
            Assert.Equal(TimeSpan.Zero,WaveformPeakCache.RetryDelay(key));Assert.Same(data,await WaveformPeakCache.Request(path,0,48000));
        }
        finally {File.Delete(path);}
    }
}
