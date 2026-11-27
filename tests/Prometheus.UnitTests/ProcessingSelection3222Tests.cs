using System.Diagnostics;
using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class ProcessingSelection3222Tests
{
    private static ProcessingAdapter Nvidia(int index = 0) => new(index, "nvidia", 100 + index, "RTX", 0x10de, 0x2503, "1", false, false, 12UL << 30, 16UL << 30, 10UL << 30, 0, true);
    private static ProcessingAdapter Apu(int index = 0) => new(index, "apu", 200 + index, "Radeon", 0x1002, 0x1506, "1", false, true, 512UL << 20, 4UL << 30, 3UL << 30, 0, true);
    private static ProcessingHardwareSnapshot Hardware(params ProcessingAdapter[] adapters) => new("Ryzen", 8, 8UL << 30, 7UL << 30, 3UL << 30,
        "Windows", adapters, adapters.FirstOrDefault()?.Luid, null);
    private static readonly ProcessingInstall Full = new(true, true, true, true, true, true);
    private static ProcessingSettings Settings(ProcessingFunction function, string backend, string? adapter = null) =>
        new() { Functions = new() { [function.ToString()] = new(backend, adapter) } };

    [Theory]
    [InlineData("SamImage")]
    [InlineData("SamTrack")]
    [InlineData("Whisper")]
    public void NvidiaAutoKeepsCudaWhenAmdRuntimesAreAlsoInstalled(string name)
    {
        var function = Enum.Parse<ProcessingFunction>(name);
        var choice = ProcessingService.Evaluate(function, Hardware(Nvidia()), Full, new());
        Assert.Equal("cuda", choice.Backend);
        Assert.Equal("nvidia", choice.Adapter!.Key);
        Assert.True(choice.Automatic);
    }

    [Theory]
    [InlineData("SamImage", "directml")]
    [InlineData("SamTrack", "directml")]
    [InlineData("Whisper", "cpu")]
    public void ApuAutoUsesItsMeasuredRoutes(string name, string expected)
    {
        var function = Enum.Parse<ProcessingFunction>(name);
        var choice = ProcessingService.Evaluate(function, Hardware(Apu()), Full, new());
        Assert.Equal(expected, choice.Backend);
        Assert.Equal(4, choice.CpuThreads);
    }

    [Fact]
    public void RyzenCpuAndSharedMemoryDoNotMakeNvidiaAnApu()
    {
        var hardware = Hardware(Nvidia()) with { InstalledRamBytes = 32UL << 30, LogicalProcessors = 24 };
        var choice = ProcessingService.Evaluate(ProcessingFunction.SamImage, hardware, Full, new());
        Assert.False(hardware.Adapters[0].UnifiedMemory);
        Assert.Equal("cuda", choice.Backend);
        Assert.Equal(0, choice.CpuThreads);
    }

    [Fact]
    public void HybridKeepsCompositorIdentityWhileAiCanChooseNvidia()
    {
        var hardware = Hardware(Apu(), Nvidia(1));
        Assert.Equal("apu", ProcessingService.Evaluate(ProcessingFunction.Graphics, hardware, Full, new()).Adapter!.Key);
        Assert.Equal("amf", ProcessingService.Evaluate(ProcessingFunction.H264, hardware, Full, new()).Backend);
        Assert.Equal("nvidia", ProcessingService.Evaluate(ProcessingFunction.SamImage, hardware, Full, new()).Adapter!.Key);
    }

    [Fact]
    public void DmlSelectionCarriesTheChosenDxgiIndex()
    {
        var hardware = Hardware(Nvidia(), Apu(2));
        var choice = ProcessingService.Evaluate(ProcessingFunction.SamImage, hardware, Full,
            Settings(ProcessingFunction.SamImage, "directml", "apu"));
        Assert.Equal(2, choice.Adapter!.Index);
        var start = new ProcessStartInfo("python.exe");
        ProcessingService.Configure(start, choice);
        Assert.Equal("2", start.Environment["PROMETHEUS_PROCESSING_DML_DEVICE"]);
        Assert.Equal("202", start.Environment["PROMETHEUS_PROCESSING_ADAPTER_LUID"]);
    }

    [Fact]
    public void ExplicitCpuDoesNotSilentlyBecomeCuda()
    {
        var choice = ProcessingService.Evaluate(ProcessingFunction.Whisper, Hardware(Nvidia()), Full, Settings(ProcessingFunction.Whisper, "cpu"));
        Assert.Equal("cpu", choice.Backend); Assert.False(choice.Automatic); Assert.Null(choice.Adapter);
    }

    [Fact]
    public void ExternalChoiceWinsJobAndLocalChoices()
    {
        var choice = ProcessingService.Evaluate(ProcessingFunction.H264, Hardware(Nvidia()), Full,
            Settings(ProcessingFunction.H264, "nvidia"), "cpu", "nvidia");
        Assert.Equal("cpu", choice.Backend); Assert.Equal("Configuración externa", choice.Origin);
    }

    [Fact]
    public void ExplicitJobWinsGlobalExportPreference()
    {
        var choice = ProcessingService.Evaluate(ProcessingFunction.Hevc, Hardware(Nvidia()), Full,
            Settings(ProcessingFunction.Hevc, "cpu"), jobBackend: "nvidia");
        Assert.Equal("nvidia", choice.Backend); Assert.Equal("Este trabajo", choice.Origin);
    }

    [Fact]
    public void RemovedAdapterIsNotReplacedWithAnotherWithoutNotice()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProcessingService.Evaluate(ProcessingFunction.SamImage,
            Hardware(Nvidia()), Full, Settings(ProcessingFunction.SamImage, "cuda", "removed")));
        Assert.Contains("ya no está disponible", error.Message);
    }

    [Fact]
    public void ForcedCudaOnAmdFailsClearly()
    {
        Assert.Throws<InvalidOperationException>(() => ProcessingService.Evaluate(ProcessingFunction.SamImage,
            Hardware(Apu()), Full, Settings(ProcessingFunction.SamImage, "cuda")));
    }

    [Fact]
    public void WithoutGpuAiCanUseInstalledCpuRoutes()
    {
        var software = Apu() with { Software = true, D3D11 = false };
        Assert.Equal("onnx-cpu", ProcessingService.Evaluate(ProcessingFunction.SamImage, Hardware(software), Full, new()).Backend);
        Assert.Equal("torch-cpu", ProcessingService.Evaluate(ProcessingFunction.SamTrack, Hardware(software), Full, new()).Backend);
    }

    [Fact]
    public void FoldersWithoutACompatibleNvidiaDoNotEnableCuda()
    {
        var install = Full with { Dml = false, WebGpu = false };
        Assert.Equal("onnx-cpu", ProcessingService.Evaluate(ProcessingFunction.SamImage, Hardware(Apu()), install, new()).Backend);
        Assert.Throws<InvalidOperationException>(() => ProcessingService.Evaluate(ProcessingFunction.SamImage, Hardware(Apu()), install,
            Settings(ProcessingFunction.SamImage, "cuda")));
    }

    [Fact]
    public void CpuThreadPreferenceIsCarriedIntoTheChildOnly()
    {
        var settings = Settings(ProcessingFunction.Whisper, "cpu"); settings.CpuThreads = 2;
        var choice = ProcessingService.Evaluate(ProcessingFunction.Whisper, Hardware(Nvidia()), Full, settings);
        var before = Environment.GetEnvironmentVariable("PROMETHEUS_CPU_THREADS");
        var start = new ProcessStartInfo("python.exe");
        ProcessingService.Configure(start, choice);
        Assert.Equal("2", start.Environment["PROMETHEUS_CPU_THREADS"]);
        Assert.Equal(before, Environment.GetEnvironmentVariable("PROMETHEUS_CPU_THREADS"));
    }

    [Fact]
    public void ManualAmfCannotBeAdvertisedForNvidiaCompositor()
    {
        Assert.Throws<InvalidOperationException>(() => ProcessingService.Evaluate(ProcessingFunction.H264, Hardware(Nvidia()), Full,
            Settings(ProcessingFunction.H264, "amf")));
    }

    [Fact]
    public void WhisperCudaIsCheckedIndependentlyOfTorchSam()
    {
        var install = Full with { CudaSam = false };
        Assert.Equal("cuda", ProcessingService.Evaluate(ProcessingFunction.Whisper, Hardware(Nvidia()), install, new()).Backend);
        Assert.Equal("directml", ProcessingService.Evaluate(ProcessingFunction.SamImage, Hardware(Nvidia()), install, new()).Backend);
    }

    [Fact]
    public void ExplicitCpuCanRecoverFromARemovedGpuPreference()
    {
        Assert.Equal("cpu", ProcessingService.Evaluate(ProcessingFunction.Whisper, Hardware(Nvidia()), Full,
            Settings(ProcessingFunction.Whisper, "cpu", "removed")).Backend);
    }
}
