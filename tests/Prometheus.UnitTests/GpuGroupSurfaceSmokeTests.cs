using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.UnitTests;

public class GpuGroupSurfaceSmokeTests
{
    [Fact]
    public void CreatesSharedGroupTextureAndCompilesBothShaders()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var surface = new GpuGroupSurface(64, 36);
        Assert.Equal(64, surface.Width);
        Assert.Equal(36, surface.Height);
        Assert.NotEqual(IntPtr.Zero, surface.SharedHandle);
    }

    [Theory]
    [InlineData(VideoTransitionOperations.Dissolve, 128, 0, 128)]
    [InlineData(VideoTransitionOperations.DipBlack, 0, 0, 0)]
    [InlineData(VideoTransitionOperations.DipWhite, 255, 255, 255)]
    public void NativeTransitionsBlendBothInputsOnTheGpu(string kind, int expectedRed, int expectedGreen, int expectedBlue)
    {
        if (!OperatingSystem.IsWindows()) return;
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out var device, out _, out var context).CheckError();
        using (device)
        using (context)
        using (var left = NewInput())
        using (var right = NewInput())
        using (var group = new GpuGroupSurface(24, 16))
        {
            var leftHandle = Paint(left, new Color4(1, 0, 0, 1));
            var rightHandle = Paint(right, new Color4(0, 0, 1, 1));
            var a = new Clip();
            var b = new Clip();
            var track = new Track { Kind = "video" };
            var cross = kind == VideoTransitionOperations.Dissolve;
            var transition = new VideoTransition { Kind = kind, LeftClipId = a.Id, RightClipId = b.Id, DurationFrames = 24 };
            var frame = new VideoTransitionFrame(new VideoTransitionWindow(transition, a, b, 60, 1, .8, 1.2),
                1, cross ? .5 : 0, cross ? .5 : 0, cross ? 0 : 1);
            var layers = new[]
            {
                new NestedRenderLayer(a, track, 0, 1, [], [], [0], frame),
                new NestedRenderLayer(b, track, 0, 1, [], [], [0], frame)
            };
            group.RenderOffscreen(NestedRenderTree.Build(layers), id =>
                new GpuFrameReference(id == a.Id ? leftHandle : rightHandle, 24, 16, 0, 1, true));
            using var result = group.ReadbackBitmap();
            var pixel = result.GetPixel(12, 8);
            Assert.InRange((int)pixel.Red, expectedRed - 2, expectedRed + 2);
            Assert.InRange((int)pixel.Green, expectedGreen - 2, expectedGreen + 2);
            Assert.InRange((int)pixel.Blue, expectedBlue - 2, expectedBlue + 2);
            Assert.Equal(255, pixel.Alpha);
        }

        ID3D11Texture2D NewInput() => device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            24, 16, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource,
            miscFlags: ResourceOptionFlags.SharedKeyedMutex));
        IntPtr Paint(ID3D11Texture2D input, Color4 color)
        {
            using var target = device.CreateRenderTargetView(input);
            using var mutex = input.QueryInterface<IDXGIKeyedMutex>();
            using var resource = input.QueryInterface<IDXGIResource>();
            mutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(target, color);
            context.Flush();
            mutex.ReleaseSync(0);
            return resource.SharedHandle;
        }
    }

    [Fact]
    public void ParentMonochromeRunsAfterTwoChildrenAreComposited()
    {
        if (!OperatingSystem.IsWindows()) return;
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out var device, out _, out var context).CheckError();
        using (device)
        using (context)
        using (var red = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                   32, 32, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource,
                   miscFlags: ResourceOptionFlags.SharedKeyedMutex)))
        using (var green = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                   32, 32, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource,
                   miscFlags: ResourceOptionFlags.SharedKeyedMutex)))
        using (var redTarget = device.CreateRenderTargetView(red))
        using (var greenTarget = device.CreateRenderTargetView(green))
        using (var redResource = red.QueryInterface<IDXGIResource>())
        using (var greenResource = green.QueryInterface<IDXGIResource>())
        using (var redMutex = red.QueryInterface<IDXGIKeyedMutex>())
        using (var greenMutex = green.QueryInterface<IDXGIKeyedMutex>())
        using (var group = new GpuGroupSurface(32, 32))
        {
            redMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(redTarget, new Color4(1, 0, 0, 1));
            redMutex.ReleaseSync(0);
            greenMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(greenTarget, new Color4(0, 1, 0, 1));
            greenMutex.ReleaseSync(0);
            context.Flush();

            var parent = new Clip { Id = "parent" };
            MonochromeEffect.Set(parent, true);
            var bottom = new Clip { Id = "bottom" };
            var top = new Clip { Id = "top", Visual = new ClipVisualState { Opacity = 0.5 } };
            var track = new Track { Kind = "video" };
            var layers = new[]
            {
                new NestedRenderLayer(bottom, track, 0, 0, [parent], [0], [0, 0]),
                new NestedRenderLayer(top, track, 0, 0, [parent], [0], [0, 1])
            };
            var tree = NestedRenderTree.Build(layers);
            group.RenderOffscreen(tree, id => id == bottom.Id
                ? new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true));
            using var image = group.ReadbackBitmap();
            var pixel = image.GetPixel(16, 16);
            Assert.InRange((int)pixel.Red, 100, 140);
            Assert.InRange(Math.Abs(pixel.Red - pixel.Green), 0, 2);
            Assert.InRange(Math.Abs(pixel.Green - pixel.Blue), 0, 2);

            group.VisualOverrideForClip = clip => clip.Id == parent.Id
                ? new ClipVisualState { PositionX = 0.5 } : null;
            group.RenderOffscreen(tree, id => id == bottom.Id
                ? new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true));
            using var movedGroup = group.ReadbackBitmap();
            Assert.True(movedGroup.GetPixel(3, 16).Red < 5);
            Assert.True(movedGroup.GetPixel(28, 16).Red > 50);
            group.VisualOverrideForClip = null;

            KeyframeAnimation.Set(parent, "visual.position_x", 0, 0);
            KeyframeAnimation.Set(parent, "visual.position_x", 1, 0.5);
            var animatedLayers = layers.Select(layer => layer with
            { LocalTimelineSeconds = 0.5, ContainerTimelineSeconds = new[] { 0.5 } }).ToArray();
            group.RenderOffscreen(NestedRenderTree.Build(animatedLayers), id => id == bottom.Id
                ? new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true));
            using var animatedGroup = group.ReadbackBitmap();
            Assert.True(animatedGroup.GetPixel(3, 16).Red < 5);
            Assert.True(animatedGroup.GetPixel(24, 16).Red > 50);
            parent.KeyframeTracks.Clear();

            parent.Effects.Clear();
            ImageGeometryEffect.Add(parent, ImageGeometryEffect.FlipHorizontalId);
            top.Visual.Opacity = 1;
            top.Visual.ScaleX = 0.5;
            top.Visual.PositionX = -0.25;
            group.RenderOffscreen(tree, id => id == bottom.Id
                ? new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true));
            using var flipped = group.ReadbackBitmap();
            var left = flipped.GetPixel(4, 16);
            var right = flipped.GetPixel(28, 16);
            Assert.True(left.Green > left.Red + 100);
            Assert.True(right.Red > right.Green + 100);
            parent.Visual.FlipX = true;
            group.RenderOffscreen(tree, id => id == bottom.Id
                ? new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true));
            using var doubleFlipped = group.ReadbackBitmap();
            Assert.True(doubleFlipped.GetPixel(4, 16).Red > doubleFlipped.GetPixel(4, 16).Green + 100);
            parent.Visual.FlipX = false;

            parent.Effects.Clear();
            parent.Visual.RotationDegrees = 90;
            group.RenderOffscreen(tree, id => id == bottom.Id
                ? new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true));
            using var rotatedGroup = group.ReadbackBitmap();
            Assert.True(rotatedGroup.GetPixel(16, 4).Red > rotatedGroup.GetPixel(16, 4).Green + 100);
            Assert.True(rotatedGroup.GetPixel(16, 28).Green > rotatedGroup.GetPixel(16, 28).Red + 100);
            parent.Visual.RotationDegrees = 0;

            parent.Effects.Clear();
            GpuFilterEffect.Add(parent, GpuFilterEffect.BlurId);
            group.RenderOffscreen(tree, id => id == bottom.Id
                ? new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true));
            using var blurred = group.ReadbackBitmap();
            var boundary = blurred.GetPixel(16, 16);
            Assert.True(boundary.Red > 15 && boundary.Green > 15,
                $"El desenfoque debe mezclar el borde: R={boundary.Red}, G={boundary.Green}");

            parent.Effects.Clear();
            GpuFilterEffect.Add(parent, GpuFilterEffect.ChromaId);
            top.Visual = new ClipVisualState();
            greenMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(greenTarget, new Color4(0, 0.35f, 0, 1));
            greenMutex.ReleaseSync(0);
            context.Flush();
            var keyLayers = new[]
            {
                new NestedRenderLayer(bottom, track, 0, 0, [], [], [0]),
                new NestedRenderLayer(top, track, 0, 0, [parent], [0], [1, 0])
            };
            group.RenderOffscreen(NestedRenderTree.Build(keyLayers), id => id == bottom.Id
                ? new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true));
            using var keyed = group.ReadbackBitmap();
            var revealed = keyed.GetPixel(16, 16);
            Assert.True(revealed.Red > 200 && revealed.Green < 30,
                $"El croma debe revelar la capa roja: R={revealed.Red}, G={revealed.Green}");

            GpuFilterEffect.Add(parent, GpuFilterEffect.ChromaMultiId);
            greenMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(greenTarget, new Color4(0, 0, 0.35f, 1));
            greenMutex.ReleaseSync(0);
            context.Flush();
            group.RenderOffscreen(NestedRenderTree.Build(keyLayers), id => id == bottom.Id
                ? new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true));
            using var multiKeyed = group.ReadbackBitmap();
            var secondReveal = multiKeyed.GetPixel(16, 16);
            Assert.True(secondReveal.Red > 200 && secondReveal.Blue < 30,
                $"El segundo color debe revelar la capa roja: R={secondReveal.Red}, B={secondReveal.Blue}");

            redMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(redTarget, new Color4(0, 0, 0, 1));
            redMutex.ReleaseSync(0);
            greenMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(greenTarget, new Color4(1, 1, 1, 1));
            greenMutex.ReleaseSync(0);
            context.Flush();
            parent.Effects.Clear();
            GpuDetailEffect.Add(parent, GpuDetailEffect.GlowId);
            top.Visual.ScaleX = 0.5;
            top.Visual.PositionX = -0.25;
            group.RenderOffscreen(tree, id => id == bottom.Id
                ? new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true));
            using var glowing = group.ReadbackBitmap();
            var halo = glowing.GetPixel(18, 16);
            Assert.True(halo.Red > 5, $"El resplandor debe extenderse fuera del borde: R={halo.Red}");

            redMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(redTarget, new Color4(0.4f, 0.4f, 0.4f, 1));
            redMutex.ReleaseSync(0);
            greenMutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(greenTarget, new Color4(0.6f, 0.6f, 0.6f, 1));
            greenMutex.ReleaseSync(0);
            context.Flush();
            parent.Effects.Clear();
            GpuFrameReference Gray(string id) => id == bottom.Id
                ? new GpuFrameReference(redResource.SharedHandle, 32, 32, 0, 1, true)
                : new GpuFrameReference(greenResource.SharedHandle, 32, 32, 0, 1, true);
            group.RenderOffscreen(tree, id => Gray(id));
            using var plain = group.ReadbackBitmap();
            GpuDetailEffect.Add(parent, GpuDetailEffect.SharpenId);
            group.RenderOffscreen(tree, id => Gray(id));
            using var sharpened = group.ReadbackBitmap();
            Assert.True(sharpened.GetPixel(15, 16).Red > plain.GetPixel(15, 16).Red + 4);

            var cubePath = Path.Combine(Path.GetTempPath(), "prometheus-lut-" + Guid.NewGuid().ToString("N") + ".cube");
            try
            {
                File.WriteAllText(cubePath, "LUT_3D_SIZE 2\n" + string.Join("\n",
                    from b in Enumerable.Range(0, 2)
                    from g in Enumerable.Range(0, 2)
                    from r in Enumerable.Range(0, 2)
                    select $"{1 - r} {1 - g} {1 - b}"));
                var cube = GpuLutEffect.ParseCube(cubePath);
                Assert.Equal(2, cube.Size);
                parent.Effects.Clear();
                GpuLutEffect.Add(parent);
                group.LutPathForClip = clip => clip.Id == parent.Id ? cubePath : null;
                group.RenderOffscreen(tree, id => Gray(id));
                using var lutFrame = group.ReadbackBitmap();
                Assert.True(lutFrame.GetPixel(28, 16).Red > plain.GetPixel(28, 16).Red + 35,
                    "El LUT invertido debe transformar píxeles de la composición padre en GPU.");
                parent.Effects[0].Parameters["interpolation"].Text = "tetrahedral";
                group.RenderOffscreen(tree, id => Gray(id));
                using var tetraFrame = group.ReadbackBitmap();
                Assert.InRange(Math.Abs(tetraFrame.GetPixel(28, 16).Red - lutFrame.GetPixel(28, 16).Red), 0, 2);
            }
            finally { File.Delete(cubePath); }
        }
    }
}
