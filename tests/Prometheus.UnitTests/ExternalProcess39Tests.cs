using System.Diagnostics;
using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

[CollectionDefinition("Runtime processes", DisableParallelization = true)]
public class RuntimeProcessesCollection { }

[Collection("Runtime processes")]
public class ExternalProcess39Tests
{
    [Fact]
    public async Task ProbeTimeoutAndCancellationTerminateAStalledProcess()
    {
        var python = RuntimeDependencies.SamPython;
        if (!File.Exists(python)) return;
        ProcessStartInfo Stalled() => new(python) {
            ArgumentList = { "-c", "import time; time.sleep(60)" } };
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            ExternalProcess.RunAsync(Stalled(), TimeSpan.FromMilliseconds(200)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        using var cancel = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExternalProcess.RunAsync(Stalled(), TimeSpan.FromMinutes(1), cancel.Token));
    }

    [Fact]
    public async Task DiagnosticsAreBoundedAndPipesDrainConcurrently()
    {
        var python = RuntimeDependencies.SamPython;
        if (!File.Exists(python)) return;
        var start = new ProcessStartInfo(python) { ArgumentList = {
            "-c", "import sys;sys.stderr.write('x'*100000+'END');sys.stdout.write('OK')" } };
        var result = await ExternalProcess.RunAsync(start, TimeSpan.FromSeconds(5));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("OK", result.Output);
        Assert.Equal(16384, result.Error.Length);
        Assert.EndsWith("END", result.Error);
    }

    [Fact]
    public async Task CancellingABlockedSamUploadRetiresOnlyThatWorkerAndCanRestart()
    {
        var python = RuntimeDependencies.SamPython;
        if (!File.Exists(python)) return;
        var folder = Path.Combine(Path.GetTempPath(), "sam-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "repo"));
        File.WriteAllText(Path.Combine(folder, "sam2.1_hiera_small.pt"), "");
        Directory.CreateDirectory(Path.Combine(folder, "onnx", "ort-directml", "onnxruntime"));
        foreach (var graph in new[] { "vision_encoder.onnx", "vision_encoder.onnx_data", "prompt_encoder_mask_decoder.onnx", "prompt_encoder_mask_decoder.onnx_data" })
            File.WriteAllText(Path.Combine(folder, "onnx", graph), "");
        var script = Path.Combine(folder, "fake.py");
        File.WriteAllText(script, "import sys,json,time,os\\nfor line in sys.stdin.buffer:\\n r=json.loads(line)\\n if r['op']=='ping': print(json.dumps({'ok':True,'backend':os.environ['PROMETHEUS_SAM2_BACKEND']}),flush=True)\\n else: time.sleep(60)\\n".Replace("\\n", "\n"));
        var oldRoot = Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_ROOT");
        var oldScript = Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_SCRIPT");
        var oldBackend = Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_BACKEND");
        try
        {
            Environment.SetEnvironmentVariable("PROMETHEUS_SAM2_ROOT", folder);
            Environment.SetEnvironmentVariable("PROMETHEUS_SAM2_SCRIPT", script);
            Environment.SetEnvironmentVariable("PROMETHEUS_SAM2_BACKEND", "onnx-cpu");
            await using var bridge = new Sam2Bridge();
            await bridge.WarmupAsync();
            using var cancel = new CancellationTokenSource(150);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                bridge.PrepareAsync(1024, 1024, new byte[1024 * 1024 * 4], cancel.Token));
            await bridge.WarmupAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var pending = bridge.PrepareAsync(1024, 1024, new byte[1024 * 1024 * 4]);
            await Task.Delay(100);
            await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROMETHEUS_SAM2_ROOT", oldRoot);
            Environment.SetEnvironmentVariable("PROMETHEUS_SAM2_SCRIPT", oldScript);
            Environment.SetEnvironmentVariable("PROMETHEUS_SAM2_BACKEND", oldBackend);
            Directory.Delete(folder, true);
        }
    }
}
