using Prometheus.Client.Services;
using SkiaSharp;

namespace Prometheus.UnitTests;

public class GpuColorShaderSmokeTests
{
    [Fact]
    public void NativeColorShaderCreatesAStillImageSurface()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-gpu-smoke-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var pixels = new SKBitmap(2, 2))
            {
                pixels.Erase(SKColors.Orange);
                using var image = SKImage.FromBitmap(pixels);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            using var surface = new GpuVideoSurface(path, still: true);
            Assert.Equal(2, surface.Width);
            Assert.NotEqual(IntPtr.Zero, surface.SharedHandle);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
