using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using SkiaSharp;

namespace Prometheus.UnitTests;

public class MaskCapture310Tests
{
    [Theory]
    [InlineData("groups")]
    [InlineData("stack")]
    [InlineData("legacy")]
    public void FullResolutionCaptureDoesNotUpscalePreviewMasksOrReplacePreviewPasses(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "mask-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "white.png");
        try
        {
            using (var image = new SKBitmap(1024, 256))
            {
                image.Erase(SKColors.White);
                using var png = SKImage.FromBitmap(image);
                using var data = png.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(file, data.ToArray());
            }
            using var small = new GpuVideoSurface(file, still: true, previewLongEdge: 128);
            using var native = new GpuVideoSurface(file, still: true, previewLongEdge: 0);
            var shape = new ShapeMask { Kind = "polygon",
                Points = [new(.173, .133), new(.811, .777), new(.221, .911)] };
            foreach (var surface in new[] { small, native })
            {
                if (mode == "legacy")
                {
                    var project = ProjectStore.CreateNew();
                    project.Assets.Add(new Asset { Id = "image", Source = new MediaSource { Kind = "local", Uri = file },
                        Streams = [new StreamInfo { Id = "image-stream", Kind = "image" }] });
                    var clip = new Clip { AssetId = "image", StreamId = "image-stream",
                        ShapeMask = shape, SamMaskTarget = SamMaskTarget.Opacity };
                    GpuEffectProfile.Configure(surface, project, clip, 0, 0, null);
                }
                else
                {
                    var layers = new[] { new GpuMaskLayerFrame("shape", null, SamMaskRefinement.Neutral, "add", shape) };
                    if (mode == "groups")
                        surface.MaskGroups = new Dictionary<int, IReadOnlyList<GpuMaskLayerFrame>> { [15] = layers };
                    else surface.MaskStack = layers;
                }
                Assert.True(surface.RenderOffscreen(0, .001));
            }
            using var actual = small.ReadbackBitmap();
            using var expected = native.ReadbackBitmap();
            Assert.Equal(128, small.Width);
            Assert.Equal(32, small.Height);
            Assert.Equal(1024, actual.Width);
            Assert.Equal(256, actual.Height);
            var partial = 0;
            for (var y = 0; y < actual.Height; y++)
            for (var x = 0; x < actual.Width; x++)
            {
                Assert.Equal(expected.GetPixel(x, y).Alpha, actual.GetPixel(x, y).Alpha);
                if (actual.GetPixel(x, y).Alpha is > 0 and < 255) partial++;
            }
            Assert.InRange(partial, 1, 2000);
            // A second capture must work, with temporary GPU resources already released.
            using var repeated = small.ReadbackBitmap();
            Assert.Equal(actual.GetPixel(250, 150), repeated.GetPixel(250, 150));
            Assert.True(small.RenderOffscreen(0, .001));
            Assert.Equal(128, small.Width);
            Assert.Equal(32, small.Height);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
