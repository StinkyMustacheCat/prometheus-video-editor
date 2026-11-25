using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

[Collection("Runtime processes")]
public class RuntimeLocationTests
{
    [Fact]
    public void NoConfigurationUsesTheDistributedRuntimeEvenWhenItIsMissing()
    {
        var before = Environment.GetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT", null);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Runtime"), RuntimeDependencies.RuntimeRoot);
        }
        finally { Environment.SetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT", before); }
    }

    [Fact]
    public void ExplicitRuntimeLocationIsRespectedWithoutAnImplicitLegacyFallback()
    {
        var before = Environment.GetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT");
        var configured = Path.Combine(Path.GetTempPath(), "portable-moved-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT", configured);
            Assert.Equal(configured, RuntimeDependencies.RuntimeRoot);
        }
        finally { Environment.SetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT", before); }
    }
    [Fact]
    public void SharedMultimediaUsesItsOwnDirectoryAndKeepsExplicitToolOverrides()
    {
        var beforeRoot = Environment.GetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT");
        var beforeTool = Environment.GetEnvironmentVariable("PROMETHEUS_FFMPEG");
        var root = Path.Combine(Path.GetTempPath(), "multimedia-layout-" + Guid.NewGuid());
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "ffmpeg"));
            var ffmpeg = Path.Combine(folder.FullName, "ffmpeg.exe");
            File.WriteAllBytes(ffmpeg, []);
            Environment.SetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT", root);
            Environment.SetEnvironmentVariable("PROMETHEUS_FFMPEG", null);
            Assert.Equal(ffmpeg, LocalMediaService.Tool("ffmpeg"));
            Environment.SetEnvironmentVariable("PROMETHEUS_FFMPEG", "explicit-ffmpeg.exe");
            Assert.Equal("explicit-ffmpeg.exe", LocalMediaService.Tool("ffmpeg"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT", beforeRoot);
            Environment.SetEnvironmentVariable("PROMETHEUS_FFMPEG", beforeTool);
            File.Delete(Path.Combine(root, "ffmpeg", "ffmpeg.exe"));
            Directory.Delete(Path.Combine(root, "ffmpeg"));
            Directory.Delete(root);
        }
    }
}
