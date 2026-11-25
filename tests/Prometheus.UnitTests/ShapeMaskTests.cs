using System.Runtime.InteropServices;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.UnitTests;

public class ShapeMaskTests
{
    [Theory]
    [InlineData("rectangle")]
    [InlineData("ellipse")]
    [InlineData("polygon")]
    public void RasterizesThreeVectorShapesAndKeepsStableMask(string kind)
    {
        var shape = new ShapeMask { Kind = kind, Points = kind == "polygon"
            ? [new(0.1, 0.1), new(0.55, 0.1), new(0.3, 0.9)]
            : [new(0.1, 0.1), new(0.55, 0.9)] };
        var mask = ShapeMaskRaster.ForSurface(shape, 100, 50);
        var alpha = mask.DecodeAlpha();
        Assert.True(alpha[25 * 100 + 30] > 240);
        Assert.Equal(0, alpha[25 * 100 + 90]);
        Assert.Same(mask, ShapeMaskRaster.ForSurface(shape, 100, 50));

        var project = ProjectStore.CreateNew();
        project.ActiveSequence!.Clips.Add(new Clip { ShapeMask = shape });
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        Assert.Equal(kind, restored.ActiveSequence!.Clips.Single().ShapeMask!.Kind);
        Assert.Equal(shape.Points.Count,
            restored.ActiveSequence.Clips.Single().ShapeMask!.Points.Count);
    }

    [Theory]
    [InlineData("rectangle")]
    [InlineData("ellipse")]
    [InlineData("polygon")]
    public void GpuRasterizesEachShapeKind(string kind)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-gpu-shape-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new SKBitmap(64, 64))
            {
                bitmap.Erase(SKColors.Red);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var shape = new ShapeMask { Kind = kind, Points = kind == "polygon"
                ? [new(0.2, 0.2), new(0.8, 0.2), new(0.5, 0.8)]
                : [new(0.2, 0.2), new(0.8, 0.8)] };
            var clip = new Clip { AssetId = "image",
                TrackId = project.ActiveSequence!.Tracks[0].Id,
                Duration = Prometheus.Core.Primitives.Rational.Time(1),
                MaskLayersInitialized = true,
                MaskLayers = [new ClipMaskLayer { Kind = "shape", Shape = shape }] };
            project.ActiveSequence.Clips.Add(clip);
            project.Assets.Add(new Asset { Id = "image", Source = new MediaSource
                { Kind = "local", Uri = path } });
            using var gpu = new GpuVideoSurface(path, still: true);
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using var output = gpu.ReadbackBitmap();
            Assert.True(output.GetPixel(32, 32).Alpha > 245);
            Assert.True(output.GetPixel(3, 3).Alpha < 10);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ShapeMaskAppliesAcrossClipInPreviewAndExport()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-shape-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new SKBitmap(32, 16))
            {
                bitmap.Erase(SKColors.Red);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            sequence.Width = 32; sequence.Height = 16;
            var clip = new Clip { AssetId = "still", TrackId = sequence.Tracks[0].Id,
                Duration = Prometheus.Core.Primitives.Rational.Time(2), ShapeMask = new ShapeMask
                { Kind = "rectangle", Points = [new(0, 0), new(0.5, 1)] } };
            sequence.Clips.Add(clip);
            project.Assets.Add(new Asset { Id = "still", Source = new MediaSource
                { Kind = "local", Uri = path } });

            using var gpu = new GpuVideoSurface(path, still: true);
            GpuEffectProfile.Configure(gpu, project, clip, 1.5, 1.5, null);
            Assert.True(gpu.RenderOffscreen(1.5, 0.01));
            using (var preview = gpu.ReadbackBitmap())
            {
                Assert.True(preview.GetPixel(5, 8).Alpha > 245);
                Assert.True(preview.GetPixel(25, 8).Alpha < 10);
            }

            using var renderer = new GpuExportFrameRenderer(project, null);
            await renderer.RenderAsync(1.5);
            byte left = 0, right = 0;
            renderer.WithOutputTexture((device, context, texture) =>
            {
                using var staging = device.CreateTexture2D(new Texture2DDescription(
                    Format.B8G8R8A8_UNorm, 32, 16, 1, 1, BindFlags.None,
                    ResourceUsage.Staging, CpuAccessFlags.Read));
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None);
                try
                {
                    left = Marshal.ReadByte(mapped.DataPointer,
                        8 * (int)mapped.RowPitch + 5 * 4 + 3);
                    right = Marshal.ReadByte(mapped.DataPointer,
                        8 * (int)mapped.RowPitch + 25 * 4 + 3);
                }
                finally { context.Unmap(staging, 0); }
            });
            Assert.True(left > 245);
            Assert.True(right < 10);

            MonochromeEffect.Set(clip, true);
            clip.SamMaskTarget = SamMaskTarget.ForEffect(clip.Effects.Single());
            GpuEffectProfile.Configure(gpu, project, clip, 1.5, 1.5, null);
            Assert.True(gpu.RenderOffscreen(1.5, 0.01));
            using var effectPreview = gpu.ReadbackBitmap();
            var inside = effectPreview.GetPixel(5, 8);
            var outside = effectPreview.GetPixel(25, 8);
            Assert.InRange(Math.Abs(inside.Red - inside.Green), 0, 5);
            Assert.True(outside.Red > 240 && outside.Green < 15 && outside.Alpha > 245);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TwoShapeMasksCombineOnGpuWithoutReplacingOneAnother()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-mask-stack-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new SKBitmap(64, 32))
            {
                bitmap.Erase(SKColors.Red);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var clip = new Clip { AssetId = "image",
                TrackId = project.ActiveSequence!.Tracks[0].Id,
                MaskLayersInitialized = true,
                MaskLayers = [
                    new ClipMaskLayer { Name = "Área", Kind = "shape", Shape = new ShapeMask
                        { Kind = "rectangle", Points = [new(0, 0), new(0.75, 1)] } },
                    new ClipMaskLayer { Name = "Hueco", Kind = "shape", Blend = "subtract",
                        Shape = new ShapeMask { Kind = "rectangle",
                            Points = [new(0.25, 0), new(0.5, 1)] } }
                ] };
            project.ActiveSequence.Clips.Add(clip);
            project.Assets.Add(new Asset { Id = "image", Source = new MediaSource
                { Kind = "local", Uri = path } });
            using var gpu = new GpuVideoSurface(path, still: true);
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var output = gpu.ReadbackBitmap())
            {
                Assert.True(output.GetPixel(5, 16).Alpha > 245);
                Assert.True(output.GetPixel(24, 16).Alpha < 10);
                Assert.True(output.GetPixel(42, 16).Alpha > 245);
                Assert.True(output.GetPixel(58, 16).Alpha < 10);
            }
            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
            var saved = restored.ActiveSequence!.Clips.Single();
            Assert.Equal(2, ClipMaskStack.Read(saved).Count);
            ClipMaskStack.Edit(saved).Clear();
            Assert.Empty(ClipMaskStack.Read(saved));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IndependentMasksLimitDifferentEffectsOnSameClip()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-mask-targets-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new SKBitmap(96, 32))
            {
                bitmap.Erase(new SKColor(64, 64, 64));
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var clip = new Clip { AssetId = "image",
                TrackId = project.ActiveSequence!.Tracks[0].Id,
                Duration = Prometheus.Core.Primitives.Rational.Time(2),
                MaskLayersInitialized = true };
            BasicColorEffect.Set(clip, true, new BasicColorSettings(0.2, 1, 1));
            ExposureEffect.Set(clip, true, new ExposureSettings(1, 0));
            clip.MaskLayers.Add(new ClipMaskLayer { Kind = "shape",
                Target = SamMaskTarget.ForEffect(BasicColorEffect.Find(clip)!),
                Shape = new ShapeMask { Kind = "rectangle",
                    Points = [new(0, 0), new(0.33, 1)] } });
            clip.MaskLayers.Add(new ClipMaskLayer { Kind = "shape",
                Target = SamMaskTarget.ForEffect(ExposureEffect.Find(clip)!),
                Shape = new ShapeMask { Kind = "rectangle",
                    Points = [new(0.34, 0), new(0.66, 1)] } });
            project.ActiveSequence.Clips.Add(clip);
            project.Assets.Add(new Asset { Id = "image", Source = new MediaSource
                { Kind = "local", Uri = path } });
            using var gpu = new GpuVideoSurface(path, still: true);
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using var output = gpu.ReadbackBitmap();
            var basic = output.GetPixel(15, 16);
            var exposure = output.GetPixel(48, 16);
            var untouched = output.GetPixel(80, 16);
            Assert.InRange(basic.Red, 112, 120);
            Assert.InRange(exposure.Red, 124, 132);
            Assert.InRange(untouched.Red, 62, 66);
            Assert.Equal((byte)255, untouched.Alpha);
            project.ActiveSequence.Width = 96;
            project.ActiveSequence.Height = 32;
            using var renderer = new GpuExportFrameRenderer(project, null);
            await renderer.RenderAsync(0);
            byte[] exported = new byte[3];
            renderer.WithOutputTexture((device, context, texture) =>
            {
                using var staging = device.CreateTexture2D(new Texture2DDescription(
                    Format.B8G8R8A8_UNorm, 96, 32, 1, 1, BindFlags.None,
                    ResourceUsage.Staging, CpuAccessFlags.Read));
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None);
                try
                {
                    for (var i = 0; i < 3; i++)
                        exported[i] = Marshal.ReadByte(mapped.DataPointer,
                            16 * (int)mapped.RowPitch + (15 + i * 33) * 4 + 2);
                }
                finally { context.Unmap(staging, 0); }
            });
            Assert.InRange(exported[0], 112, 120);
            Assert.InRange(exported[1], 124, 132);
            Assert.InRange(exported[2], 62, 66);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AnimatedShapeMovesInPreviewAndExport()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-shape-motion-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new SKBitmap(64, 32))
            {
                bitmap.Erase(SKColors.Red);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            project.ActiveSequence!.Width = 64;
            project.ActiveSequence.Height = 32;
            var clip = new Clip { AssetId = "image",
                TrackId = project.ActiveSequence.Tracks[0].Id,
                Duration = Prometheus.Core.Primitives.Rational.Time(2),
                MaskLayersInitialized = true };
            var layer = new ClipMaskLayer { Kind = "shape", Target = SamMaskTarget.Opacity,
                Shape = new ShapeMask { Kind = "rectangle",
                    Points = [new(0, 0), new(0.25, 1)] } };
            ShapeMaskAnimation.Upsert(layer, 0, 30, layer.Shape);
            ShapeMaskAnimation.Upsert(layer, 1, 30, new ShapeMask
                { Kind = "rectangle", Points = [new(0.75, 0), new(1, 1)] });
            clip.MaskLayers.Add(layer);
            project.ActiveSequence.Clips.Add(clip);
            project.Assets.Add(new Asset { Id = "image", Source = new MediaSource
                { Kind = "local", Uri = path } });
            var midpoint = ShapeMaskAnimation.Evaluate(layer, 0.5, 30)!;
            Assert.InRange(midpoint.Points[0].X, 0.37, 0.38);
            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
            Assert.Equal(2, restored.ActiveSequence!.Clips.Single().MaskLayers.Single()
                .ShapeKeyframes.Count);

            using var gpu = new GpuVideoSurface(path, still: true);
            GpuEffectProfile.Configure(gpu, project, clip, 0, 0, null);
            Assert.True(gpu.RenderOffscreen(0, 0.01));
            using (var first = gpu.ReadbackBitmap())
            {
                Assert.True(first.GetPixel(7, 16).Alpha > 245);
                Assert.True(first.GetPixel(56, 16).Alpha < 10);
            }
            GpuEffectProfile.Configure(gpu, project, clip, 1, 0, null);
            Assert.True(gpu.RenderOffscreen(1, 0.01));
            using (var second = gpu.ReadbackBitmap())
            {
                Assert.True(second.GetPixel(7, 16).Alpha < 10);
                Assert.True(second.GetPixel(56, 16).Alpha > 245);
            }
            using var renderer = new GpuExportFrameRenderer(project, null);
            await renderer.RenderAsync(1);
            byte left = 0, right = 0;
            renderer.WithOutputTexture((device, context, texture) =>
            {
                using var staging = device.CreateTexture2D(new Texture2DDescription(
                    Format.B8G8R8A8_UNorm, 64, 32, 1, 1, BindFlags.None,
                    ResourceUsage.Staging, CpuAccessFlags.Read));
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None);
                try
                {
                    left = Marshal.ReadByte(mapped.DataPointer,
                        16 * (int)mapped.RowPitch + 7 * 4 + 3);
                    right = Marshal.ReadByte(mapped.DataPointer,
                        16 * (int)mapped.RowPitch + 56 * 4 + 3);
                }
                finally { context.Unmap(staging, 0); }
            });
            Assert.True(left < 10);
            Assert.True(right > 245);
        }
        finally { File.Delete(path); }
    }
}
