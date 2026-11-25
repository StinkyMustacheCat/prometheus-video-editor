using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.DXGI;
using System.Runtime.InteropServices;

namespace Prometheus.UnitTests;

public class SamMaskTests
{
    [Fact]
    public void AssignedEffectSurvivesClipSplitAndSequenceDuplicate()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var clip = new Clip { TrackId = sequence.Tracks[0].Id };
        MonochromeEffect.Set(clip, true);
        clip.SamMaskTarget = SamMaskTarget.ForEffect(clip.Effects.Single());
        sequence.Clips.Add(clip);

        var (_, right) = CollisionEngine.SplitClip(sequence, clip.Id, 0.5);
        Assert.NotNull(right);
        Assert.NotEqual(clip.Effects.Single().Id, right.Effects.Single().Id);
        Assert.Equal(SamMaskTarget.ForEffect(right.Effects.Single()), right.SamMaskTarget);

        var duplicated = SequenceOperations.Duplicate(project, sequence.Id);
        foreach (var copied in duplicated.Clips)
            Assert.Equal(SamMaskTarget.ForEffect(copied.Effects.Single()), copied.SamMaskTarget);
    }

    [Fact]
    public void SavedMaskSurvivesProjectRoundTripAndOnlyMatchesItsFrame()
    {
        var project = ProjectStore.CreateNew();
        var clip = new Clip();
        project.ActiveSequence!.Clips.Add(clip);
        var alpha = Enumerable.Range(0, 32).Select(i => (byte)(i % 2 == 0 ? 255 : 0)).ToArray();
        clip.SamMasks.Add(SamMaskKeyframe.Create(2.0, 8, 4, alpha));

        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        var savedClip = restored.ActiveSequence!.Clips.Single();
        Assert.Equal(alpha, Assert.Single(savedClip.SamMasks).DecodeAlpha());
        Assert.NotNull(SamMaskKeyframe.At(savedClip, 2.0, 60));
        Assert.Null(SamMaskKeyframe.At(savedClip, 2.1, 60));
    }

    [Fact]
    public void SavedMaskCutsOutPixelsOnGpu()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-sam-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = new SKBitmap(16, 8))
            {
                image.Erase(SKColors.Red);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var alpha = new byte[16 * 8];
            for (var y = 0; y < 8; y++)
                for (var x = 0; x < 8; x++) alpha[y * 16 + x] = 255;
            using var gpu = new GpuVideoSurface(path, still: true);
            gpu.SamMask = SamMaskKeyframe.Create(0, 16, 8, alpha);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using var masked = gpu.ReadbackBitmap();
            Assert.True(masked.GetPixel(2, 4).Alpha > 245);
            Assert.True(masked.GetPixel(13, 4).Alpha < 10);

            gpu.SamMask = null;
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using var restored = gpu.ReadbackBitmap();
            Assert.True(restored.GetPixel(13, 4).Alpha > 245);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExportRendererUsesTrackedMaskAtTheSelectedFrame()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "prometheus-sam-export-" +
            Guid.NewGuid().ToString("N"));
        var path = root + ".png";
        var projectPath = root + ".pthproj";
        try
        {
            using (var image = new SKBitmap(16, 8))
            {
                image.Erase(SKColors.Red);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            sequence.Width = 16;
            sequence.Height = 8;
            var alpha = new byte[16 * 8];
            for (var y = 0; y < 8; y++)
                for (var x = 0; x < 8; x++) alpha[y * 16 + x] = 255;
            var clip = new Clip { AssetId = "still", TrackId = sequence.Tracks[0].Id,
                Duration = Prometheus.Core.Primitives.Rational.Time(1.0 / 30) };
            var track = new SamMaskTrack { StartSourceSeconds = 0,
                FramesPerSecond = 30, FrameCount = 1, Width = 16, Height = 8 };
            SamMaskTrackStore.Write(projectPath, track,
                [SamMaskKeyframe.Create(0, 16, 8, alpha)]);
            clip.SamMaskTracks.Add(track);
            sequence.Clips.Add(clip);
            project.Assets.Add(new Asset { Id = "still", Source = new MediaSource
                { Kind = "local", Uri = path } });

            using var renderer = new GpuExportFrameRenderer(project, projectPath);
            await renderer.RenderAsync(0);
            byte left = 0, right = 0;
            renderer.WithOutputTexture((device, context, texture) =>
            {
                using var staging = device.CreateTexture2D(new Texture2DDescription(
                    Format.B8G8R8A8_UNorm, 16, 8, 1, 1, BindFlags.None,
                    ResourceUsage.Staging, CpuAccessFlags.Read));
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None);
                try
                {
                    left = Marshal.ReadByte(mapped.DataPointer, 4 * (int)mapped.RowPitch + 2 * 4 + 3);
                    right = Marshal.ReadByte(mapped.DataPointer, 4 * (int)mapped.RowPitch + 13 * 4 + 3);
                }
                finally { context.Unmap(staging, 0); }
            });
            Assert.True(left > 245);
            Assert.True(right < 10);
        }
        finally
        {
            File.Delete(path);
            var dataDirectory = ProjectStorage.GetDataDirectory(projectPath);
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AssignedEffectIsLimitedToSamMaskInPreviewAndExport()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-sam-effect-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = new SKBitmap(16, 8))
            {
                image.Erase(SKColors.Red);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            sequence.Width = 16;
            sequence.Height = 8;
            var clip = new Clip { AssetId = "still", TrackId = sequence.Tracks[0].Id,
                Duration = Prometheus.Core.Primitives.Rational.Time(1) };
            MonochromeEffect.Set(clip, true);
            clip.SamMaskTarget = SamMaskTarget.ForEffect(clip.Effects.Single());
            var alpha = new byte[16 * 8];
            for (var y = 0; y < 8; y++)
                for (var x = 0; x < 8; x++) alpha[y * 16 + x] = 255;
            clip.SamMasks.Add(SamMaskKeyframe.Create(0, 16, 8, alpha));
            sequence.Clips.Add(clip);
            project.Assets.Add(new Asset { Id = "still", Source = new MediaSource
                { Kind = "local", Uri = path } });
            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
            Assert.Equal(clip.SamMaskTarget, restored.ActiveSequence!.Clips.Single().SamMaskTarget);

            using var gpu = new GpuVideoSurface(path, still: true);
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var preview = gpu.ReadbackBitmap())
            {
                var inside = preview.GetPixel(2, 4);
                var outside = preview.GetPixel(13, 4);
                Assert.InRange(Math.Abs(inside.Red - inside.Green), 0, 5);
                Assert.True(inside.Red < 100 && inside.Alpha > 245);
                Assert.True(outside.Red > 240 && outside.Green < 15 && outside.Alpha > 245);
            }

            var opposite = new byte[16 * 8];
            for (var y = 0; y < 8; y++)
                for (var x = 8; x < 16; x++) opposite[y * 16 + x] = 255;
            clip.SamMasks.Add(SamMaskKeyframe.Create(0.2, 16, 8, opposite));
            GpuEffectProfile.Configure(gpu, project, clip, 0.2, 0.2, null);
            Assert.True(gpu.RenderOffscreen(0.2, 0.01));
            using (var nextMask = gpu.ReadbackBitmap())
            {
                Assert.True(nextMask.GetPixel(2, 4).Red > 240);
                var effected = nextMask.GetPixel(13, 4);
                Assert.InRange(Math.Abs(effected.Red - effected.Green), 0, 5);
            }

            GpuEffectProfile.Configure(gpu, project, clip, 0.1, 0.1, null);
            Assert.True(gpu.RenderOffscreen(0.1, 0.01));
            using (var persistedMask = gpu.ReadbackBitmap())
            {
                var outside = persistedMask.GetPixel(2, 4);
                var inside = persistedMask.GetPixel(13, 4);
                Assert.True(outside.Red > 240 && outside.Green < 15 && outside.Alpha > 245);
                Assert.InRange(Math.Abs(inside.Red - inside.Green), 0, 5);
            }

            clip.SamMaskTarget = SamMaskTarget.Unassigned;
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var withoutMask = gpu.ReadbackBitmap())
            {
                var outside = withoutMask.GetPixel(13, 4);
                Assert.InRange(Math.Abs(outside.Red - outside.Green), 0, 5);
                Assert.True(outside.Alpha > 245);
            }
            clip.SamMaskTarget = SamMaskTarget.Opacity;
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var opacity = gpu.ReadbackBitmap())
                Assert.True(opacity.GetPixel(2, 4).Alpha < 10);

            using var renderer = new GpuExportFrameRenderer(restored, null);
            await renderer.RenderAsync(0.5);
            byte insideRed = 0, insideGreen = 0, insideAlpha = 0;
            byte outsideRed = 0, outsideGreen = 0, outsideAlpha = 0;
            renderer.WithOutputTexture((device, context, texture) =>
            {
                using var staging = device.CreateTexture2D(new Texture2DDescription(
                    Format.B8G8R8A8_UNorm, 16, 8, 1, 1, BindFlags.None,
                    ResourceUsage.Staging, CpuAccessFlags.Read));
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var row = 4 * (int)mapped.RowPitch;
                    insideRed = Marshal.ReadByte(mapped.DataPointer, row + 2 * 4 + 2);
                    insideGreen = Marshal.ReadByte(mapped.DataPointer, row + 2 * 4 + 1);
                    insideAlpha = Marshal.ReadByte(mapped.DataPointer, row + 2 * 4 + 3);
                    outsideRed = Marshal.ReadByte(mapped.DataPointer, row + 13 * 4 + 2);
                    outsideGreen = Marshal.ReadByte(mapped.DataPointer, row + 13 * 4 + 1);
                    outsideAlpha = Marshal.ReadByte(mapped.DataPointer, row + 13 * 4 + 3);
                }
                finally { context.Unmap(staging, 0); }
            });
            Assert.InRange(Math.Abs(insideRed - insideGreen), 0, 5);
            Assert.True(insideRed < 100 && insideAlpha > 245);
            Assert.True(outsideRed > 240 && outsideGreen < 15 && outsideAlpha > 245);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AssignedBlurUsesMaskWithoutMakingTheRestTransparent()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-sam-blur-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = new SKBitmap(32, 8))
            {
                image.Erase(SKColors.Red);
                using var canvas = new SKCanvas(image);
                using var paint = new SKPaint { Color = SKColors.White };
                canvas.DrawRect(16, 0, 16, 8, paint);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var clip = new Clip();
            GpuFilterEffect.Add(clip, GpuFilterEffect.BlurId);
            clip.SamMaskTarget = SamMaskTarget.ForEffect(clip.Effects.Single());
            var alpha = new byte[32 * 8];
            for (var y = 0; y < 8; y++)
                for (var x = 0; x < 16; x++) alpha[y * 32 + x] = 255;
            clip.SamMasks.Add(SamMaskKeyframe.Create(0, 32, 8, alpha));
            using var gpu = new GpuVideoSurface(path, still: true);
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using var preview = gpu.ReadbackBitmap();
            Assert.True(preview.GetPixel(15, 4).Green > 15);
            Assert.True(preview.GetPixel(17, 4).Green > 245);
            Assert.True(preview.GetPixel(17, 4).Alpha > 245);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SamBorderControlsRunOnGpu()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-sam-border-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = new SKBitmap(32, 16))
            {
                image.Erase(SKColors.Red);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var alpha = new byte[32 * 16];
            for (var y = 4; y < 12; y++)
                for (var x = 8; x < 24; x++) alpha[y * 32 + x] = 255;
            using var gpu = new GpuVideoSurface(path, still: true);
            gpu.SamMask = SamMaskKeyframe.Create(0, 32, 16, alpha);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var neutral = gpu.ReadbackBitmap())
                Assert.True(neutral.GetPixel(6, 8).Alpha < 10);

            gpu.SamMaskRefinement = new SamMaskRefinement(0, 4, false);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var expanded = gpu.ReadbackBitmap())
                Assert.True(expanded.GetPixel(6, 8).Alpha > 200);

            gpu.SamMaskRefinement = new SamMaskRefinement(0, -2, false);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var contracted = gpu.ReadbackBitmap())
            {
                Assert.True(contracted.GetPixel(9, 8).Alpha < 80);
                Assert.True(contracted.GetPixel(16, 8).Alpha > 200);
            }

            gpu.SamMaskRefinement = new SamMaskRefinement(4, 0, false);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var softened = gpu.ReadbackBitmap())
                Assert.InRange(softened.GetPixel(7, 8).Alpha, 10, 245);

            gpu.SamMaskRefinement = new SamMaskRefinement(0, 0, true);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var inverted = gpu.ReadbackBitmap())
            {
                Assert.True(inverted.GetPixel(2, 8).Alpha > 245);
                Assert.True(inverted.GetPixel(16, 8).Alpha < 10);
            }
        }
        finally { File.Delete(path); }
    }
}
