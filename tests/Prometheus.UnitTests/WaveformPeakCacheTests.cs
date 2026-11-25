using System;
using System.IO;
using System.Threading.Tasks;
using Prometheus.Client.Services;
using Xunit;

namespace Prometheus.UnitTests;

public class WaveformPeakCacheTests
{
    [Fact]
    public async Task FloatWaveform_PreservesOverrangePeaksAndMillisecondPosition()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-waveform-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteFloatWave(path);
            var waveform = await WaveformPeakCache.Request(path, 0, 48000);
            Assert.NotNull(waveform);
            Assert.True(waveform.Count >= 120);

            Assert.True(waveform.TryGetRange(0.010, 0.011, out var before));
            Assert.Equal(0, before.LeftMaximum);

            Assert.True(waveform.TryGetRange(0.011, 0.012, out var positive));
            Assert.InRange(positive.LeftMaximum, 1.39f, 1.41f);

            Assert.True(waveform.TryGetRange(0.012, 0.013, out var negative));
            Assert.InRange(negative.RightMinimum, -0.71f, -0.69f);

            Assert.True(waveform.TryGetRange(0.010, 0.014, out var combined));
            Assert.InRange(combined.LeftMaximum, 1.39f, 1.41f);
            Assert.InRange(combined.RightMinimum, -0.71f, -0.69f);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private static void WriteFloatWave(string path)
    {
        const int rate = 48000;
        const int frames = 5760;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + frames * 8);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)3); // IEEE float
        writer.Write((short)2);
        writer.Write(rate);
        writer.Write(rate * 8);
        writer.Write((short)8);
        writer.Write((short)32);
        writer.Write("data"u8.ToArray());
        writer.Write(frames * 8);
        for (var i = 0; i < frames; i++)
        {
            writer.Write(i == 528 ? 1.4f : 0f);
            writer.Write(i == 576 ? -0.7f : 0f);
        }
    }
}
