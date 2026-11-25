using System.Diagnostics;
using System.Text.Json;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public class AudioExportStability38Tests
{
    [Fact]
    public void FractionalFrameClockDoesNotAccumulateDrift()
    {
        var fps = new Rational(30000, 1001);
        Assert.Equal(3_603_600_000_000L, GpuVideoWriter.FrameTimestamp(10_800_000, fps));
        Assert.Equal(333667, GpuVideoWriter.FrameTimestamp(1, fps));
        Assert.Equal(667333, GpuVideoWriter.FrameTimestamp(2, fps));
    }

    [Fact]
    public void MasterMeterPreservesPeaksAboveFullScaleAndStereoAlignment()
    {
        StereoLevel level = default;
        var provider = new AudioPreviewService.PcmPipeProvider(value => level = value);
        var samples = new[] { 1.5f, -.75f, -.2f, 2f };
        var bytes = samples.SelectMany(BitConverter.GetBytes).ToArray();
        provider.Add(bytes, bytes.Length);
        var read = new byte[bytes.Length + 8];
        Assert.Equal(bytes.Length, provider.Read(read, 8, bytes.Length));
        Assert.Equal(1.5, level.Left, 5);
        Assert.Equal(2, level.Right, 5);
        Assert.Equal(1.5f, BitConverter.ToSingle(read, 8));
    }

    [Fact]
    public void OutputValidationRejectsMissingAudioWrongFramesAndWrongRate()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        sequence.Width = 256; sequence.Height = 256; sequence.Fps = new Rational(30000, 1001);
        string Document(string rate, int frames, bool audio) => JsonSerializer.Serialize(new {
            streams = new object[] { new { codec_type = "video", codec_name = "h264",
                width = 256, height = 256, nb_frames = frames.ToString(), avg_frame_rate = rate, duration = "1.001" } }
                .Concat(audio ? new object[] { new { codec_type = "audio", codec_name = "aac",
                    sample_rate = "48000", channels = 2, duration = "1.001" } } : []).ToArray(),
            format = new { duration = "1.001" } });
        using var good = JsonDocument.Parse(Document("30000/1001", 30, true));
        ExportOutputValidation.Validate(good.RootElement, ExportFormat.Mp4, sequence, 1.001, 30);
        foreach (var value in new[] { Document("2997/100", 30, true),
            Document("30000/1001", 29, true), Document("30000/1001", 30, false) })
        {
            using var bad = JsonDocument.Parse(value);
            Assert.Throws<IOException>(() => ExportOutputValidation.Validate(bad.RootElement,
                ExportFormat.Mp4, sequence, 1.001, 30));
        }
    }

    [Fact]
    public async Task TrackGateProcessesTheCombinedSignalAndClipEchoStopsAtItsEdge()
    {
        var path = Path.Combine(Path.GetTempPath(), "audio-bus-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("RIFF".ToCharArray()); writer.Write(36 + 192000);
                writer.Write("WAVEfmt ".ToCharArray()); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
                writer.Write((short)4); writer.Write((short)16); writer.Write("data".ToCharArray()); writer.Write(192000);
                for (var i = 0; i < 96000; i++) writer.Write((short)1000);
            }
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            var track = sequence.Tracks.First(t => t.Kind == "audio");
            var stream = new StreamInfo { Kind = "audio" };
            var asset = new Asset { Source = new MediaSource { Kind = "local", Uri = path }, Streams = [stream] };
            project.Assets.Add(asset);
            for (var i = 0; i < 2; i++)
                sequence.Clips.Add(new Clip { AssetId = asset.Id, StreamId = stream.Id, TrackId = track.Id, Duration = Rational.Time(1) });
            AudioEffects.Add(track, AudioEffects.GateId);
            AudioEffects.Set(track.Effects.Single(), "threshold_db", -30);
            AudioEffects.Set(track.Effects.Single(), "ratio", 20);
            var mix = await Decode(project);
            Assert.InRange(BitConverter.ToInt16(mix, 48000 / 2 * 4), 1500, 2400);
            track.Effects.Clear();
            sequence.Clips.RemoveAt(1);
            AudioEffects.Add(sequence.Clips[0], AudioEffects.EchoId);
            sequence.Clips.Add(new Clip { TrackId = sequence.Tracks.First(t => t.Kind == "video").Id,
                Duration = Rational.Time(2) });
            mix = await Decode(project);
            Assert.Equal(0, BitConverter.ToInt16(mix, (int)(1.2 * 48000) * 4));
        }
        finally { File.Delete(path); }
    }

    private static async Task<byte[]> Decode(Project project)
    {
        using var process = Process.Start(AudioMixPlan.Create(project, 0).BuildProcess())!;
        var errors = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(output);
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await errors);
        return output.ToArray();
    }
}
