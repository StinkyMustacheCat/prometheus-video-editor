using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

public sealed record Sam2MaskFrame(int Width, int Height, byte[] Alpha, double Score)
{
    public string? Backend { get; init; }
}

/// <summary>Binary, in-memory bridge to the resident SAM 2.1 worker.</summary>
public sealed class Sam2Bridge : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _worker;
    private volatile bool _invalidWorker;
    private Task<string>? _stderr;
    private readonly CancellationTokenSource _closing = new();
    private int _disposed;
    private ProcessingChoice? _choice;
    private string? _selectionSignature;
    private byte[]? _preparedFrame;
    private int _preparedWidth, _preparedHeight;
    private bool _imageReady;
    private string? _fallbackReason;

    private static string ModelRoot => RuntimeDependencies.SamRoot;
    private static string Python => RuntimeDependencies.SamPython;
    private static string Script => Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_SCRIPT") ??
        Path.Combine(AppContext.BaseDirectory, "Runtime", "sam2_bridge.py");

    private static string GraphRoot => Path.Combine(ModelRoot, "onnx");

    internal static bool HasOnnxRuntime =>
        (Directory.Exists(Path.Combine(GraphRoot, "ort-directml", "onnxruntime")) ||
         Directory.Exists(Path.Combine(GraphRoot, "ort-webgpu", "onnxruntime"))) &&
        new[] { "vision_encoder.onnx", "vision_encoder.onnx_data",
            "prompt_encoder_mask_decoder.onnx", "prompt_encoder_mask_decoder.onnx_data" }
        .All(file => File.Exists(Path.Combine(GraphRoot, file)));

    public static bool IsAvailable => File.Exists(Python) && File.Exists(Script) &&
        (HasOnnxRuntime || (File.Exists(Path.Combine(ModelRoot, "sam2.1_hiera_small.pt")) &&
            Directory.Exists(Path.Combine(ModelRoot, "repo"))));

    public async Task WarmupAsync(CancellationToken cancellation = default)
    {
        await ExecuteAsync(async token =>
        {
            await SendAsync(new { op = "ping" }, null, token);
            using var response = await ReceiveAsync(token);
            EnsureOk(response.RootElement);
            return true;
        }, cancellation);
    }

    public async Task PrepareAsync(int width, int height, byte[] bgra, CancellationToken cancellation = default)
    {
        if (width < 1 || height < 1 || bgra.Length != (long)width * height * 4 || bgra.Length > 32_000_000)
            throw new ArgumentException("El fotograma SAM no tiene dimensiones BGRA válidas.");
        await ExecuteAsync(async token =>
        {
            await SendAsync(new { op = "prepare", width, height, data_length = bgra.Length }, bgra, token);
            using var response = await ReceiveAsync(token);
            EnsureOk(response.RootElement);
            _preparedFrame = (byte[])bgra.Clone();
            _preparedWidth = width; _preparedHeight = height;
            _imageReady = true;
            return true;
        }, cancellation);
    }

    public async Task<Sam2MaskFrame> PredictAsync((double X, double Y, bool Positive)[] prompts,
        CancellationToken cancellation = default)
    {
        if (prompts.Length is < 1 or > 32 || prompts.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new ArgumentException("Los puntos SAM no son válidos.");
        return await ExecuteAsync(async token =>
        {
            await SendAsync(new
            {
                op = "predict",
                points = prompts.Select(p => new[] { p.X, p.Y }).ToArray(),
                labels = prompts.Select(p => p.Positive ? 1 : 0).ToArray()
            }, null, token);
            using var response = await ReceiveAsync(token);
            var root = response.RootElement;
            EnsureOk(root);
            var width = root.GetProperty("width").GetInt32();
            var height = root.GetProperty("height").GetInt32();
            var length = root.GetProperty("data_length").GetInt32();
            if (width < 1 || height < 1 || length != (long)width * height || length > 8_000_000)
                throw new InvalidDataException("La máscara SAM tiene dimensiones inválidas.");
            var alpha = new byte[length];
            await _worker!.StandardOutput.BaseStream.ReadExactlyAsync(alpha, token);
            return new Sam2MaskFrame(width, height, alpha, root.GetProperty("score").GetDouble())
            {
                Backend = root.TryGetProperty("backend", out var backend) ? backend.GetString() : null
            };
        }, cancellation, restoreFrame: true);
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellation, bool restoreFrame = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closing.Token);
        await _gate.WaitAsync(deadline.Token);
        try
        {
            if (_disposed != 0) throw new ObjectDisposedException(nameof(Sam2Bridge));
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var choices = ProcessingService.Candidates(ProcessingFunction.SamImage);
            if (_selectionSignature != choices[0].Signature) { KillWorker(); _selectionSignature = choices[0].Signature; _choice = null; _fallbackReason = null; }
            var first = _choice == null ? 0 : Math.Max(0, Array.FindIndex(choices, c => c.Backend == _choice.Backend));
            string? fallbackReason = _fallbackReason;
            for (var index = first; index < choices.Length; index++)
            {
                _choice = choices[index];
                try
                {
                    var fresh = _worker is not { HasExited: false } || _invalidWorker;
                    StartIfNeeded(_choice);
                    using var stop = deadline.Token.Register(KillWorker);
                    if (fresh)
                    {
                        await SendAsync(new { op = "ping" }, null, deadline.Token);
                        using var ready = await ReceiveAsync(deadline.Token);
                        EnsureOk(ready.RootElement);
                        if (ready.RootElement.GetProperty("backend").GetString() != _choice.Backend)
                            throw new InvalidDataException("SAM inició un proveedor distinto al elegido.");
                    }
                    if (restoreFrame && !_imageReady && _preparedFrame != null)
                        {
                            await SendAsync(new { op = "prepare", width = _preparedWidth, height = _preparedHeight,
                                data_length = _preparedFrame.Length }, _preparedFrame, deadline.Token);
                            using var prepared = await ReceiveAsync(deadline.Token);
                            EnsureOk(prepared.RootElement);
                            _imageReady = true;
                        }
                    var value = await action(deadline.Token);
                    _fallbackReason = fallbackReason;
                    ProcessingService.Record(_choice, reason: fallbackReason);
                    return value;
                }
                catch (Exception error)
                {
                    KillWorker();
                    cancellation.ThrowIfCancellationRequested();
                    _closing.Token.ThrowIfCancellationRequested();
                    ProcessingService.Record(_choice, reason: error.Message, failed: true);
                    if (deadline.IsCancellationRequested)
                        throw new TimeoutException("SAM 2.1 superó el tiempo de espera. Puedes volver a intentarlo.");
                    fallbackReason = _choice.Label + ": " + error.Message;
                    if (index == choices.Length - 1) throw;
                }
            }
            throw new InvalidOperationException("No hay un proveedor SAM utilizable.");
        }
        finally { _gate.Release(); }
    }

    private void KillWorker()
    {
        _invalidWorker = true;
        _imageReady = false;
        if (_worker is { } worker) ExternalProcess.Kill(worker);
    }

    private void StartIfNeeded(ProcessingChoice choice)
    {
        if (_worker is { HasExited: false } && !_invalidWorker) return;
        _worker?.Dispose();
        _imageReady = false;
        if (!IsAvailable) throw new FileNotFoundException("No se encontró el motor local SAM 2.1.");
        var info = new ProcessStartInfo(Python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Directory.Exists(Path.Combine(ModelRoot, "repo"))
                ? Path.Combine(ModelRoot, "repo") : ModelRoot
        };
        info.ArgumentList.Add("-B");
        info.ArgumentList.Add("-u");
        info.ArgumentList.Add(Script);
        info.ArgumentList.Add("--model-root");
        info.ArgumentList.Add(ModelRoot);
        RuntimeDependencies.PreparePython(info);
        ProcessingService.Configure(info, choice);
        _invalidWorker = false;
        _worker = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar SAM 2.1.");
        _stderr = ExternalProcess.ReadDiagnosticsAsync(_worker.StandardError);
    }

    private async Task SendAsync(object header, byte[]? data, CancellationToken cancellation)
    {
        var stream = _worker!.StandardInput.BaseStream;
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header) + "\n");
        await stream.WriteAsync(line, cancellation);
        if (data != null) await stream.WriteAsync(data, cancellation);
        await stream.FlushAsync(cancellation);
    }

    private async Task<JsonDocument> ReceiveAsync(CancellationToken cancellation)
    {
        var stream = _worker!.StandardOutput.BaseStream;
        using var line = new MemoryStream();
        var single = new byte[1];
        while (line.Length < 16_384)
        {
            if (await stream.ReadAsync(single, cancellation) == 0)
            {
                var error = "";
                if (_stderr != null)
                    try { error = await _stderr.WaitAsync(TimeSpan.FromMilliseconds(500)); }
                    catch (TimeoutException) { }
                throw new IOException("SAM 2.1 terminó sin responder. " + error.Trim());
            }
            if (single[0] == '\n') return JsonDocument.Parse(line.ToArray());
            line.WriteByte(single[0]);
        }
        throw new InvalidDataException("La respuesta de SAM 2.1 es demasiado larga.");
    }

    private static void EnsureOk(JsonElement response)
    {
        if (!response.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException("SAM 2.1: " + response.GetProperty("error").GetString());
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _closing.Cancel();
        KillWorker();
        await _gate.WaitAsync();
        try
        {
            _worker?.Dispose();
            _worker = null;
            _preparedFrame = null;
        }
        finally { _gate.Release(); }
        // Queued callers still release the managed gate after observing cancellation.
    }
}
