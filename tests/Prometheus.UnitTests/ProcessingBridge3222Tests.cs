using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

[Collection("Runtime processes")]
public class ProcessingBridge3222Tests
{
    [Fact]
    public async Task AutomaticFallbackUsesANewWorkerAndRestoresTheFrameAfterAChoiceChange()
    {
        if (!File.Exists(RuntimeDependencies.SamPython)) return;
        var choices = ProcessingService.Candidates(ProcessingFunction.SamImage);
        if (choices.Length < 2) return;
        var folder = Path.Combine(Path.GetTempPath(), "processing-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "fake.py");
        File.WriteAllText(script, """
            import sys,json,os
            backend=os.environ['PROMETHEUS_SAM2_BACKEND']
            prepared=False
            for line in sys.stdin.buffer:
                r=json.loads(line)
                if r['op']=='ping':
                    if backend==os.environ['QA_REJECT_BACKEND']:
                        print(json.dumps({'ok':False,'error':'QA unavailable provider'}),flush=True)
                    else: print(json.dumps({'ok':True,'backend':backend}),flush=True)
                elif r['op']=='prepare':
                    sys.stdin.buffer.read(r['data_length'])
                    prepared=True
                    print(json.dumps({'ok':True}),flush=True)
                else:
                    if not prepared: print(json.dumps({'ok':False,'error':'Missing prepared frame'}),flush=True)
                    else:
                        print(json.dumps({'ok':True,'width':2,'height':2,'score':1,'data_length':4,'backend':backend}),flush=True)
                        sys.stdout.buffer.write(bytes([0,255,255,0]));sys.stdout.buffer.flush()
            """);
        var names = new[] { "PROMETHEUS_SAM2_SCRIPT", "PROMETHEUS_SAM2_BACKEND", "QA_REJECT_BACKEND" };
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(names[0], script);
            Environment.SetEnvironmentVariable(names[1], null);
            Environment.SetEnvironmentVariable(names[2], choices[0].Backend);
            await using var bridge = new Sam2Bridge();
            await bridge.PrepareAsync(2, 2, new byte[16]);
            var mask = await bridge.PredictAsync([(1d, 1d, true)]);
            Assert.Equal(choices[1].Backend, mask.Backend);
            Assert.Contains("QA unavailable provider", ProcessingService.LastExecution(ProcessingFunction.SamImage)!.Reason);

            // A manual provider failure must remain visible instead of quietly falling back.
            Environment.SetEnvironmentVariable(names[1], choices[0].Backend);
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.WarmupAsync());

            // Replacing the worker must re-upload the prepared frame, even if the monitor did not change time.
            Environment.SetEnvironmentVariable(names[2], "none");
            var restored = await bridge.PredictAsync([(1d, 1d, true)]);
            Assert.Equal(choices[0].Backend, restored.Backend);
            Assert.Equal(new byte[] { 0, 255, 255, 0 }, restored.Alpha);
        }
        finally
        {
            for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]);
            Directory.Delete(folder, true);
        }
    }
}
