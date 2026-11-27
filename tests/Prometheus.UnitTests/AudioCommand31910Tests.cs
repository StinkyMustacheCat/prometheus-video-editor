using System.Diagnostics;
using System.Text;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public sealed class AudioCommand31910Tests
{
    [Fact]
    public void LongGraphIsExternalizedUnchangedAndDeletedOnDispose()
    {
        using var folder = new TemporaryFolder();
        var graph = "[0:a]" + string.Concat(Enumerable.Repeat("anull,", 7000)) + "volume=0.5[mix]";
        var start = Start(graph);
        Assert.True(AudioMixCommand.CommandLengthUpperBound(start) > 32767);
        string path;
        using (var command = new AudioMixCommand(start, folder.Path))
        {
            var index = start.ArgumentList.IndexOf("-/filter_complex");
            Assert.True(index >= 0);
            path = start.ArgumentList[index + 1];
            Assert.Equal(graph, File.ReadAllText(path));
            Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()));
            Assert.True(AudioMixCommand.CommandLengthUpperBound(start) < 32767);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ShortGraphDoesNotCreateATemporaryFile()
    {
        using var folder = new TemporaryFolder();
        var start = Start("[0:a]volume=0.5[mix]");
        using var command = new AudioMixCommand(start, folder.Path);
        Assert.Contains("-filter_complex", start.ArgumentList);
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public void ResidualOversizeRoutesFailClearlyAndCleanUpTheGraph()
    {
        using var folder = new TemporaryFolder();
        var start = Start("[0:a]anull[mix]");
        start.ArgumentList.Add(new string('x', 40000));
        var error = Assert.Throws<InvalidOperationException>(() => new AudioMixCommand(start, folder.Path));
        Assert.Contains("rutas de audio", error.Message);
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public void FailedProcessStartStillDeletesTheGraph()
    {
        using var folder = new TemporaryFolder();
        var start = Start("[0:a]anull[mix]" + new string(' ', 40000));
        start.FileName = System.IO.Path.Combine(folder.Path, "missing.exe");
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
        {
            using var command = new AudioMixCommand(start, folder.Path);
            using var process = Process.Start(command.StartInfo);
        });
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public async Task OversizeFilterStartsAndPreservesActualPcmOutput()
    {
        using var folder = new TemporaryFolder();
        var simple = "[0:a]volume=0.5[mix]";
        var large = simple + new string(' ', 40000);
        Assert.True(AudioMixCommand.CommandLengthUpperBound(Start(large)) > 32767);
        var expected = await Decode(Start(simple), folder.Path);
        var actual = await Decode(Start(large), folder.Path);
        Assert.NotEmpty(actual);
        Assert.Equal(expected, actual);
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public async Task CancelledDecoderDeletesTheGraphAfterExit()
    {
        using var folder = new TemporaryFolder();
        using (var command = new AudioMixCommand(Start("[0:a]anull[mix]" + new string(' ', 40000)), folder.Path))
        {
            using var process = Process.Start(command.StartInfo)!;
            process.Kill(true);
            await process.WaitForExitAsync();
        }
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public async Task ManyCutsRenderBothRawExportAndWavWithoutChangingTheMix()
    {
        using var folder = new TemporaryFolder();
        var source = System.IO.Path.Combine(folder.Path, "source.wav");
        using (var writer = new BinaryWriter(File.Create(source)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 4800 * 4);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(4800 * 4);
            for (var i = 0; i < 4800; i++) { writer.Write((short)12000); writer.Write((short)-6000); }
        }
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Clips.Clear();
        var track = sequence.Tracks.First(t => t.Kind == "audio");
        var asset = new Asset { Id = "many-cuts", Source = new MediaSource { Kind = "local", Uri = source } };
        asset.Streams.Add(new StreamInfo { Id = "stream", Kind = "audio", Ordinal = 0 });
        project.Assets.Add(asset);
        for (var i = 0; i < 220; i++)
            sequence.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "stream", TrackId = track.Id,
                TimelineStart = Rational.Time(i / 10.0), Duration = Rational.Time(.1) });
        var plan = AudioMixPlan.Create(project, 0);
        Assert.Equal(220, plan.Inputs.Count);
        Assert.True(AudioMixCommand.CommandLengthUpperBound(plan.BuildProcess()) > 32767);
        var rawPath = System.IO.Path.Combine(folder.Path, "export.pcm");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await SequenceExportService.RenderAudioAsync(plan, rawPath, timeout.Token);
        var raw = File.ReadAllBytes(rawPath);
        Assert.Equal(220 * 4800 * 4, raw.Length);
        var leftPeak = 0;
        var rightPeak = 0;
        for (var i = 0; i < raw.Length; i += 4)
        {
            leftPeak = Math.Max(leftPeak, Math.Abs((int)BitConverter.ToInt16(raw, i)));
            rightPeak = Math.Max(rightPeak, Math.Abs((int)BitConverter.ToInt16(raw, i + 2)));
        }
        Assert.InRange(leftPeak, 11990, 12010);
        Assert.InRange(rightPeak, 5990, 6010);
        var wavePath = System.IO.Path.Combine(folder.Path, "export.wav");
        using (var command = plan.PrepareProcess(waveOutput: wavePath))
            await SequenceExportService.RunProcessAsync(command.StartInfo, "WAV", timeout.Token);
        var wave = File.ReadAllBytes(wavePath);
        Assert.True(wave.Length > raw.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        // FFmpeg can add metadata before data; compare the actual PCM chunk.
        var offset = 12;
        while (Encoding.ASCII.GetString(wave, offset, 4) != "data")
            offset += 8 + (int)((BitConverter.ToUInt32(wave, offset + 4) + 1) & ~1u);
        Assert.Equal(raw, wave.AsSpan(offset + 8, raw.Length).ToArray());
    }

    private static ProcessStartInfo Start(string graph)
    {
        var start = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var value in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
            "sine=frequency=440:sample_rate=48000:duration=0.05", "-filter_complex", graph, "-map", "[mix]",
            "-f", "s16le", "-acodec", "pcm_s16le", "-ac", "2", "-ar", "48000", "pipe:1" })
            start.ArgumentList.Add(value);
        return start;
    }

    private static async Task<byte[]> Decode(ProcessStartInfo start, string folder)
    {
        using var command = new AudioMixCommand(start, folder);
        using var process = Process.Start(command.StartInfo)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var stop = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var errors = process.StandardError.ReadToEndAsync();
        using var bytes = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(bytes, deadline.Token);
        await process.WaitForExitAsync(deadline.Token);
        Assert.True(process.ExitCode == 0, await errors);
        return bytes.ToArray();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audio-31910-" + Guid.NewGuid().ToString("N"));
        public TemporaryFolder() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
