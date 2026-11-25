using System.Diagnostics;
using System.IO;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Rational = Prometheus.Core.Primitives.Rational;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.UnitTests;

// Validation intentionally has a short wall-clock script budget. Do not run
// its cold JIT/shader checks concurrently with unrelated hardware workloads.
[CollectionDefinition("GPU timed validation", DisableParallelization = true)]
public sealed class GpuTimedValidationCollection { }

[Collection("GPU timed validation")]
public class MgtGpuTransitionTests
{
    [Theory]
    [InlineData(1e100, false)]
    [InlineData(1e-100, false)]
    [InlineData(0.01, true)]
    public void NumericStepMustFitTheParameterEditor(double step, bool accepted)
    {
        var parameter = new MgtParameter { Type = "number", Step = step };
        var value = System.Text.Json.JsonSerializer.SerializeToElement(0.5);
        Assert.Equal(accepted, MgtGpuTransition.Accepts(parameter, value));
    }

    [Fact]
    public void FactoryTransitionsDrawBothEndpointsAndSurviveNonSequentialRender()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(AppContext.BaseDirectory, "Runtime", "MgtTransitions");
        var definitions = Directory.GetFiles(root, "*.mgt.json").Select(path => MgtGpuTransition.Parse(File.ReadAllText(path))).ToArray();
        Assert.Equal(12, definitions.Length);
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out var device, out _, out var context).CheckError();
        using (device)
        using (context)
        using (var left = Input())
        using (var right = Input())
        {
            var a = Paint(left, new Color4(1, 0, 0, 1));
            var b = Paint(right, new Color4(0, 0, 1, 1));
            foreach (var template in definitions)
            {
                MgtGpuCanvasProgram.Check(template);
                foreach (var (w, h) in new[] { (96, 54), (54, 96) })
                {
                    using var compositor = new GpuGroupSurface(w, h) { TransitionDesignWidth = w * 20, TransitionDesignHeight = h * 20 };
                    var project = ProjectStore.CreateNew();
                    var sequence = project.ActiveSequence!;
                    var track = sequence.Tracks.First(t => t.Kind == "video");
                    var outgoing = MakeClip(track, 0);
                    var incoming = MakeClip(track, 2);
                    sequence.Clips.AddRange([outgoing, incoming]);
                    var transition = VideoTransitionOperations.Apply(project, outgoing.Id, incoming.Id,
                        template.Id, 60, template);
                    var window = VideoTransitionOperations.Window(sequence, transition)!;
                    foreach (var index in new[] { transition.DurationFrames - 1, 0, 12, 29, 47, 29 })
                    {
                        var time = window.Start + index / window.Fps;
                        var layers = NestedRenderPlan.At(project, time, "video");
                        Assert.Equal(2, layers.Count);
                        compositor.RenderOffscreen(NestedRenderTree.Build(layers, sequence), id => new GpuFrameReference(
                            id == outgoing.Id ? a : b, 96, 54, 0, 1, true));
                        using var bitmap = compositor.ReadbackBitmap();
                        var color = bitmap.GetPixel(w / 2, h / 2);
                        if (index == 0) Assert.True(color.Red >= 245 && color.Blue < 10, template.Id);
                        if (index == transition.DurationFrames - 1)
                            Assert.True(color.Blue >= 245 && color.Red < 10, template.Id);
                    }
                }
            }
        }

        ID3D11Texture2D Input() => device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            96, 54, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource, miscFlags: ResourceOptionFlags.SharedKeyedMutex));
        IntPtr Paint(ID3D11Texture2D texture, Color4 color)
        {
            using var mutex = texture.QueryInterface<IDXGIKeyedMutex>();
            using var resource = texture.QueryInterface<IDXGIResource>();
            using var target = device.CreateRenderTargetView(texture);
            mutex.AcquireSync(0, 1000);
            context.ClearRenderTargetView(target, color);
            context.Flush();
            mutex.ReleaseSync(0);
            return resource.SharedHandle;
        }
    }

    private static Clip MakeClip(Track track, double time) => new()
    {
        TrackId = track.Id, TimelineStart = Rational.Time(time), Duration = Rational.Time(2),
        SourceIn = Rational.Time(1), AvailableSourceDurationSeconds = 5
    };
}
