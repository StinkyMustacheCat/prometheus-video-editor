using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Prometheus.Client.Services;

public sealed record SamVideoFrame(int Width, int Height, byte[] Bgra);
public sealed record SamTrackedFrame(int Width, int Height, byte[][] ObjectAlphas)
{
    public byte[] UnionAlpha()
    {
        var union = new byte[checked(Width * Height)];
        foreach (var mask in ObjectAlphas)
            for (var i = 0; i < union.Length; i++)
                if (mask[i] > union[i]) union[i] = mask[i];
        return union;
    }
}

/// <summary>Separate resident video tracker; cancelling kills only this worker, not the fast image selector.</summary>
public sealed class Sam2VideoBridge : IDisposable
{
    private const int MaxSeeds = 16;
    private Process? _worker;
    private volatile bool _invalidWorker;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _closing = new();
    private volatile bool _disposed;
    private System.Threading.Tasks.Task<string>? _stderr;
    private ProcessingChoice? _choice;
    private bool _ready;
    private static string ModelRoot => RuntimeDependencies.SamRoot;
    private static string Python => RuntimeDependencies.SamPython;
    private static string Script => Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_TRACK_SCRIPT") ??
        Path.Combine(AppContext.BaseDirectory, "Runtime", "sam2_track.py");

    private static bool HasCpuRuntime => File.Exists(Path.Combine(ModelRoot, "track-cpu", "torch", "__init__.py"));

    internal static string ConfiguredBackend
    {
        get
        {
            return ProcessingService.Resolve(ProcessingFunction.SamTrack).Backend;
        }
    }

    public static bool IsAvailable
    {
        get
        {
            if (!File.Exists(Python) || !File.Exists(Script) ||
                !File.Exists(Path.Combine(ModelRoot, "sam2.1_hiera_small.pt")) ||
                !Directory.Exists(Path.Combine(ModelRoot, "repo"))) return false;
            string backend;
            try { backend = ConfiguredBackend; } catch (InvalidOperationException) { return false; }
            if (backend == "cuda") return true;
            if (!HasCpuRuntime) return false;
            if (backend == "torch-cpu") return true;
            var graphRoot = Path.Combine(ModelRoot, "onnx");
            return backend is "directml" or "webgpu" &&
                Directory.Exists(Path.Combine(graphRoot, "ort-" + backend, "onnxruntime")) &&
                File.Exists(Path.Combine(graphRoot, "vision_encoder.onnx")) &&
                File.Exists(Path.Combine(graphRoot, "vision_encoder.onnx_data"));
        }
    }

    internal string Backend { get; private set; } = "pending";

    internal void Prepare(CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Sam2VideoBridge));
            if (_ready && _worker is { HasExited: false } && !_invalidWorker) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closing.Token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var choices = ProcessingService.Candidates(ProcessingFunction.SamTrack);
            string? fallback = null;
            for (var index = 0; index < choices.Length; index++)
            {
                _choice = choices[index]; Backend = _choice.Backend;
                try
                {
                    StartIfNeeded();
                    using var stop = deadline.Token.Register(KillWorker);
                    _worker!.StandardInput.WriteLine("{\"op\":\"ping\"}");
                    _worker.StandardInput.Flush();
                    using var response = ReadHeader();
                    EnsureOk(response.RootElement);
                    if (response.RootElement.GetProperty("backend").GetString() != Backend)
                        throw new InvalidDataException("El seguimiento inició otro proveedor.");
                    _ready = true;
                    ProcessingService.Record(_choice, reason: fallback);
                    return;
                }
                catch (Exception error)
                {
                    KillWorker(); cancellation.ThrowIfCancellationRequested(); _closing.Token.ThrowIfCancellationRequested();
                    ProcessingService.Record(_choice, reason: error.Message, failed: true);
                    if (deadline.IsCancellationRequested) throw new TimeoutException("El seguimiento SAM no pudo iniciar a tiempo.");
                    fallback = _choice.Label + ": " + error.Message;
                    if (index == choices.Length - 1) throw;
                }
            }
        }
    }

    public IReadOnlyList<SamTrackedFrame> TrackWindow(IReadOnlyList<SamVideoFrame> frames,
        IReadOnlyList<byte[]> seedMasks, bool reverse, CancellationToken cancellation, Action<int>? frameReceived = null)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Sam2VideoBridge));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closing.Token);
            deadline.CancelAfter(TimeSpan.FromMinutes(Backend == "cuda" ? 5 : 15));
            var requestedCancellation = cancellation;
            cancellation = deadline.Token;

            if (frames.Count is < 2 or > 121)
                throw new ArgumentException("SAM requiere entre 2 y 121 fotogramas por tramo.");
            if (seedMasks.Count == 0)
                throw new ArgumentException("Selecciona una máscara inicial para seguir el movimiento con SAM.");
            var width = frames[0].Width;
            var height = frames[0].Height;
            if (width < 1 || height < 1 || (long)width * height > 8_000_000 ||
                frames.Any(frame => frame.Width != width || frame.Height != height ||
                    frame.Bgra.Length != checked(width * height * 4)) ||
                seedMasks.Any(mask => mask.Length != width * height))
                throw new ArgumentException("Los fotogramas SAM tienen dimensiones distintas.");
            cancellation.ThrowIfCancellationRequested();
            if (seedMasks.Count > MaxSeeds)
            {
                // Keep separate objects up to the worker limit and preserve every pixel in the remaining regions.
                var combined = (byte[])seedMasks[MaxSeeds - 1].Clone();
                for (var index = MaxSeeds; index < seedMasks.Count; index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var mask = seedMasks[index];
                    for (var pixel = 0; pixel < combined.Length; pixel++)
                        if (mask[pixel] > combined[pixel]) combined[pixel] = mask[pixel];
                }
                seedMasks = seedMasks.Take(MaxSeeds - 1).Append(combined).ToArray();
            }
            Prepare(cancellation);
            using var registration = cancellation.Register(KillWorker);
            try
            {
                var input = _worker!.StandardInput.BaseStream;
                var header = JsonSerializer.Serialize(new
                {
                    op = "track",
                    width,
                    height,
                    frame_count = frames.Count,
                    anchor_index = reverse ? frames.Count - 1 : 0,
                    seed_count = seedMasks.Count,
                    reverse
                });
                input.Write(Encoding.UTF8.GetBytes(header + "\n"));
                foreach (var seed in seedMasks) { cancellation.ThrowIfCancellationRequested(); input.Write(seed); }
                foreach (var frame in frames) { cancellation.ThrowIfCancellationRequested(); input.Write(frame.Bgra); }
                input.Flush();

                using (var ready = ReadHeader())
                {
                    EnsureOk(ready.RootElement);
                    if (ready.RootElement.GetProperty("frame_count").GetInt32() != frames.Count ||
                        ready.RootElement.GetProperty("width").GetInt32() != width ||
                        ready.RootElement.GetProperty("height").GetInt32() != height)
                        throw new InvalidDataException("SAM devolvió una ventana de tamaño distinto.");
                }
                var result = new SamTrackedFrame?[frames.Count];
                for (var i = 0; i < result.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    using var response = ReadHeader();
                    var root = response.RootElement;
                    EnsureOk(root);
                    var index = root.GetProperty("frame_index").GetInt32();
                    var length = root.GetProperty("data_length").GetInt32();
                    var objectCount = root.GetProperty("object_count").GetInt32();
                    if (index < 0 || index >= result.Length || result[index] != null ||
                        objectCount < 1 || objectCount > MaxSeeds ||
                        length != width * height * objectCount)
                        throw new InvalidDataException("SAM devolvió una máscara de tamaño distinto.");
                    var masks = new byte[objectCount][];
                    for (var objectIndex = 0; objectIndex < objectCount; objectIndex++)
                    {
                        masks[objectIndex] = new byte[width * height];
                        _worker.StandardOutput.BaseStream.ReadExactly(masks[objectIndex]);
                    }
                    result[index] = new SamTrackedFrame(width, height, masks);
                    frameReceived?.Invoke(index);
                }
                using (var complete = ReadHeader())
                {
                    EnsureOk(complete.RootElement);
                    if (!complete.RootElement.GetProperty("complete").GetBoolean())
                        throw new InvalidDataException("SAM no terminó el seguimiento.");
                }
                return result.Select(mask => mask!).ToArray();
            }
            catch (Exception) when (cancellation.IsCancellationRequested)
            {
                requestedCancellation.ThrowIfCancellationRequested();
                _closing.Token.ThrowIfCancellationRequested();
                throw new TimeoutException("SAM video superó el tiempo de espera de la ventana.");
            }
            catch { KillWorker(); throw; }

        }
    }

    private void StartIfNeeded()
    {
        if (_worker is { HasExited: false } && !_invalidWorker) return;
        _worker?.Dispose();
        if (!File.Exists(Python) || !File.Exists(Script) || _choice == null)
            throw new FileNotFoundException("No se encontró el motor de seguimiento SAM 2.1.");
        var info = new ProcessStartInfo(Python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.Combine(ModelRoot, "repo")
        };
        info.ArgumentList.Add("-B");
        info.ArgumentList.Add("-u");
        info.ArgumentList.Add(Script);
        info.ArgumentList.Add("--model-root");
        info.ArgumentList.Add(ModelRoot);
        info.Environment["PROMETHEUS_SAM2_TRACK_BACKEND"] = Backend;
        info.Environment["TQDM_DISABLE"] = "1";
        RuntimeDependencies.PreparePython(info);
        ProcessingService.Configure(info, _choice!);
        _invalidWorker = false;
        _worker = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar SAM 2.1 video.");
        _stderr = ExternalProcess.ReadDiagnosticsAsync(_worker.StandardError);
    }

    private JsonDocument ReadHeader()
    {
        var stream = _worker!.StandardOutput.BaseStream;
        using var line = new MemoryStream();
        while (line.Length < 16_384)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                var error = "";
                if (_stderr != null)
                    try { error = _stderr.WaitAsync(TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult().Trim(); }
                    catch (TimeoutException) { }
                throw new IOException("SAM video terminó sin responder. " + error);
            }
            if (next == '\n') return JsonDocument.Parse(line.ToArray());
            line.WriteByte((byte)next);
        }
        throw new InvalidDataException("Respuesta SAM video demasiado larga.");
    }

    private static void EnsureOk(JsonElement root)
    {
        if (!root.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException("SAM video: " + root.GetProperty("error").GetString());
    }

    private void KillWorker()
    {
        _invalidWorker = true;
        try { if (_worker is { HasExited: false }) _worker.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _closing.Cancel();
        KillWorker();
        lock (_gate)
        {
            _worker?.Dispose();
            _worker = null;
        }
    }
}
