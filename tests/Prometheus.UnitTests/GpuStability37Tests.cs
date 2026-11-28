using System.Diagnostics;
using Prometheus.Client.Services;
using Prometheus.Graphics;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.UnitTests;

public class GpuStability37Tests
{
    [Fact]
    public void MissingPtsStillProducesOneEntryPerFrame()
    {
        Assert.True(FfmpegVideoReader.TryReadFrameTimestamp("[Parsed_showinfo_2 @ abc] n: 0 pts: NOPTS pts_time:NOPTS", out var missing));
        Assert.True(double.IsNaN(missing));
        Assert.True(FfmpegVideoReader.TryReadFrameTimestamp("[Parsed_showinfo_2 @ abc] n: 1 pts: 1200000 pts_time:1.2", out var seconds));
        Assert.Equal(1.2, seconds);
        Assert.False(FfmpegVideoReader.TryReadFrameTimestamp("[Parsed_showinfo_2 @ abc] config in time_base: 1/1000000", out _));
    }

    [Fact]
    public void PackedPremultiplicationPreservesAlphaInWholeVectorsAndTheTail()
    {
        byte[] input = [0, 80, 255, 0, 16, 96, 240, 64, 215, 155, 45, 128,
            255, 128, 64, 255, 33, 121, 219, 175];
        byte[] expected = new byte[input.Length];
        for (var offset = 0; offset < input.Length; offset += 4)
        {
            var alpha = input[offset + 3];
            for (var channel = 0; channel < 3; channel++)
                expected[offset + channel] = (byte)((input[offset + channel] * alpha + 128) >> 8);
            expected[offset + 3] = alpha;
        }
        BgraAlpha.Premultiply(input);
        Assert.Equal(expected, input);
    }

    [Theory]
    [InlineData("qtrle", "argb")]
    [InlineData("prores_ks", "yuva444p10le")]
    public async Task MovAlphaUsesSourceTimestampsAndPreservesTransparencyAfterReuse(string encoder, string format)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "gpu-mov-alpha-" + Guid.NewGuid().ToString("N") + ".mov");
        try
        {
            var arguments = new List<string> { "-f", "lavfi", "-i",
                "nullsrc=s=96x64:r=10:d=2.8,format=gbrap,geq=r='40+5*N':g=100:b=55:a='if(lt(X,24),0,if(lt(X,72),N*8+20,255))'",
                "-c:v", encoder, "-pix_fmt", format };
            if (encoder == "prores_ks") arguments.AddRange(["-profile:v", "4", "-alpha_bits", "16"]);
            else arguments.AddRange(["-g", "10"]);
            Run([.. arguments, path]);

            using var surface = new GpuVideoSurface(path);
            Assert.True(surface.HasReusableMovAlpha);
            foreach (var (seconds, frame) in new[] { (2.055, 20), (.45, 4), (.53, 5), (1.75, 17) })
            {
                Assert.True(await surface.PresentAsync(seconds, false, updateVisual: false));
                using var bitmap = surface.ReadbackBitmap();
                Assert.Equal(0, bitmap.GetPixel(10, 32).Alpha);
                Assert.InRange((int)bitmap.GetPixel(48, 32).Alpha, 8 * frame + 18, 8 * frame + 22);
                Assert.Equal(255, bitmap.GetPixel(84, 32).Alpha);
                Assert.True(surface.PrepareForReuse());
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextureTimeoutDoesNotGrantOwnership()
    {
        if (!OperatingSystem.IsWindows()) return;
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0], out var device, out _, out var context).CheckError();
        using (device) using (context)
        using (var texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            8, 8, 1, 1, BindFlags.ShaderResource, miscFlags: ResourceOptionFlags.SharedKeyedMutex)))
        using (var mutex = texture.QueryInterface<IDXGIKeyedMutex>())
        {
            GpuTextureSync.Acquire(mutex, 0);
            mutex.ReleaseSync(1);
            Assert.Throws<TimeoutException>(() => GpuTextureSync.Acquire(mutex, 0, 2));
            GpuTextureSync.Acquire(mutex, 1, 2);
            mutex.ReleaseSync(0);
        }
    }

    [Fact]
    public void ExactSeekUsesThePrecedingFrameInsteadOfReusingThePreviousPosition()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "gpu-seek-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "source.mp4");
            Run("-f", "lavfi", "-i", "nullsrc=s=96x64:r=10:d=4,geq=r='N*5':g=0:b=0",
                "-c:v", "libx264", "-g", "20", "-pix_fmt", "yuv420p", source);
            using var surface = new GpuVideoSurface(source, exactDecode: true);
            foreach (var (seconds, frame) in new[] { (0.05, 0), (2.15, 21), (.45, 4), (3.95, 39) })
            {
                Assert.True(surface.RenderOffscreen(seconds, .001));
                var pixel = surface.ReadSourcePixel(48, 32);
                Assert.InRange((int)pixel.Red, Math.Max(0, frame * 5 - 9), frame * 5 + 9);
            }
            var rotated = Path.Combine(folder, "rotated.mp4");
            Run("-display_rotation", "90", "-i", source, "-c", "copy", rotated);
            using var vertical = new GpuVideoSurface(rotated, exactDecode: true);
            var effect = new Clip();
            GpuFilterEffect.Add(effect, GpuFilterEffect.BlurId);
            vertical.FilterSettings = GpuFilterEffect.Read(effect);
            Assert.True(vertical.RenderOffscreen(1.15, .001));
            using var capture = vertical.ReadbackBitmap();
            Assert.Equal(64, capture.Width);
            Assert.Equal(96, capture.Height);
            var analysis = vertical.CaptureAnalysisFrame(128);
            Assert.Equal(64, analysis.Width);
            Assert.Equal(96, analysis.Height);
            Assert.True(vertical.RenderOffscreen(1.25, .001));
            using var afterAnalysis = vertical.ReadbackBitmap();
            Assert.InRange((int)afterAnalysis.GetPixel(32, 48).Red, 50, 75);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void GroupsApplyOpacityAndEffectMasksAndKeepRepeatedChildIdentitiesSeparate()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "gpu-group-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var pixels = new SKBitmap(32, 32))
            {
                pixels.Erase(SKColors.Red);
                using var image = SKImage.FromBitmap(pixels);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            using var source = new GpuVideoSurface(path, still: true);
            Assert.True(source.RenderOffscreen(0, .001));
            var project = ProjectStore.CreateNew();
            var parent = new Clip { Id = "same", MaskLayersInitialized = true };
            parent.MaskLayers.Add(new ClipMaskLayer { Shape = new ShapeMask {
                Points = [new(.05, .05), new(.5, .95)] } });
            var inner = new Clip { Id = "same" };
            MonochromeEffect.Set(inner, true);
            var leaf = new Clip { Id = "leaf" };
            var track = new Track { Kind = "video" };
            var layers = new[] { new NestedRenderLayer(leaf, track, 0, 0, [parent, inner], [0, 0], [0, 0, 0]) };
            Assert.True(NestedRenderTree.NeedsGroupComposition(layers));
            using var group = new GpuGroupSurface(32, 32) {
                MasksForClip = (clip, time) => GpuEffectProfile.ResolveMaskGroups(project, clip,
                    clip, time, 0, null) };
            group.RenderOffscreen(NestedRenderTree.Build(layers), _ =>
                new GpuFrameReference(source.SharedHandle, 32, 32, 0, 1, true));
            using var result = group.ReadbackBitmap();
            Assert.InRange((int)result.GetPixel(8, 16).Red, 40, 70);
            Assert.InRange(Math.Abs(result.GetPixel(8, 16).Red - result.GetPixel(8, 16).Green), 0, 2);
            Assert.Equal(0, result.GetPixel(28, 16).Alpha);

            MonochromeEffect.Set(parent, true);
            parent.MaskLayers[0].Target = SamMaskTarget.ForEffect(parent.Effects.Last());
            MonochromeEffect.Set(inner, false);
            group.RenderOffscreen(NestedRenderTree.Build(layers), _ =>
                new GpuFrameReference(source.SharedHandle, 32, 32, 0, 1, true));
            using var maskedEffect = group.ReadbackBitmap();
            Assert.InRange(Math.Abs(maskedEffect.GetPixel(8, 16).Red - maskedEffect.GetPixel(8, 16).Green), 0, 2);
            Assert.True(maskedEffect.GetPixel(28, 16).Red > 240);
            Assert.True(maskedEffect.GetPixel(28, 16).Green < 5);
        }
        finally { File.Delete(path); }
    }

    private static void Run(params string[] arguments)
    {
        var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-y", "-v", "error" }.Concat(arguments)) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(10000));
        Assert.True(process.ExitCode == 0, errors.GetAwaiter().GetResult());
    }
}
