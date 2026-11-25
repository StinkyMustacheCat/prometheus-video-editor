using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>A live MGT scene in an isolated CEF worker with one shared GPU output texture.</summary>
public sealed class MgtLiveGpuSurface : IGpuFrameSource, IDisposable
{
    private readonly Process _worker;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly int _frameCount;
    private ICompositionImportedGpuImage? _imported;
    private CompositionDrawingSurface? _drawingSurface;
    private long _generation;
    private long _surfaceGeneration;
    private double _sourceSeconds;
    private volatile bool _disposed;
    private readonly CancellationTokenSource _closing = new();
    private Task _pendingPresentation = Task.CompletedTask;

    public int Width { get; }
    public int Height { get; }
    public IntPtr SharedHandle { get; }
    public SubtitleSceneRequest? SubtitleScene { get; set; }
    private string? _sentSubtitleSignature;

    private MgtLiveGpuSurface(Process worker, int width, int height, int frameCount, IntPtr handle, long generation)
    {
        _worker = worker;
        Width = width;
        Height = height;
        _frameCount = frameCount;
        SharedHandle = handle;
        _generation = generation;
    }

    public static async Task<MgtLiveGpuSurface> StartAsync(Asset asset, int previewLongEdge,
        CancellationToken cancellation = default, string? resolvedPath = null)
    {
        if (asset.Source.Kind != "mgt" || asset.ContainerFormat is not ("mgt.json" or "mgtproj"))
            throw new NotSupportedException("Este medio no tiene un contrato MGT vivo compatible.");
        var path = resolvedPath ?? asset.EffectiveLocator?.Path ?? throw new FileNotFoundException("Falta el archivo MGT.");
        if (!File.Exists(path)) throw new FileNotFoundException("Falta el archivo MGT.", path);
        if (!MgtLiveAsset.IsDocumentPath(path))
            throw new InvalidDataException("El MGT requiere su documento .mgt.json o .mgtproj; no se puede enviar una vista MOV al renderizador vivo.");
        var stream = asset.Streams.FirstOrDefault(item => item.Kind == "video")
            ?? throw new InvalidDataException("El MGT no declara imagen.");
        // Imported legacy metadata describes the baked preview, not the live design.
        if (stream.Codec != "mgt-live")
            stream = MgtLiveAsset.Inspect(path).Streams.First(item => item.Kind == "video");
        var designWidth = stream.Width ?? throw new InvalidDataException("Falta el ancho MGT.");
        var designHeight = stream.Height ?? throw new InvalidDataException("Falta la altura MGT.");
        var scale = previewLongEdge > 0
            ? Math.Min(1, (double)previewLongEdge / Math.Max(designWidth, designHeight)) : 1;
        var width = Math.Max(1, (int)Math.Round(designWidth * scale));
        var height = Math.Max(1, (int)Math.Round(designHeight * scale));
        var duration = asset.RecommendedDuration?.Seconds ?? 3;
        var executable = WorkerPath();
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        var subtitleScene = asset.Id.StartsWith("subtitle-scene:", StringComparison.Ordinal);
        foreach (var argument in new[] { subtitleScene ? "--serve-subtitles" : "--serve", path, width.ToString(), height.ToString() })
            start.ArgumentList.Add(argument);
        if (subtitleScene) { start.ArgumentList.Add(designWidth.ToString()); start.ArgumentList.Add(designHeight.ToString()); }
        var worker = Process.Start(start) ?? throw new InvalidOperationException("No se inició el trabajador MGT.");
        var errors = DrainErrorsAsync(worker.StandardError);
        try
        {
            var line = await worker.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(20), cancellation);
            var fields = line?.Split(' ');
            if (fields is not { Length: 3 } || fields[0] != "READY" ||
                !long.TryParse(fields[1], out var rawHandle) || !long.TryParse(fields[2], out var generation) ||
                rawHandle == 0 || generation < 1)
            {
                if (!worker.HasExited) worker.Kill(entireProcessTree: true);
                await worker.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
                var detail = await errors.WaitAsync(TimeSpan.FromSeconds(2));
                throw new InvalidOperationException("El trabajador MGT no entregó una textura GPU (salida " +
                    worker.ExitCode + "): " + (string.IsNullOrWhiteSpace(detail) ? line ?? "sin diagnóstico" : detail));
            }
            return new MgtLiveGpuSurface(worker, width, height, Math.Max(1, (int)Math.Round(duration * 60)),
                new IntPtr(rawHandle), generation);
        }
        catch
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            worker.Dispose();
            throw;
        }
    }

    private static async Task<string> DrainErrorsAsync(StreamReader reader)
    {
        var tail = new System.Text.StringBuilder();
        var buffer = new char[1024];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer)) > 0)
            {
                tail.Append(buffer, 0, count);
                if (tail.Length > 4096) tail.Remove(0, tail.Length - 4096);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
        return tail.ToString().Trim();
    }

    public async Task AttachAsync(Compositor compositor, CompositionDrawingSurface drawingSurface)
    {
        Detach();
        var interop = await compositor.TryGetCompositionGpuInterop()
            ?? throw new NotSupportedException("El monitor no permite texturas D3D11 compartidas.");
        if (!interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
            throw new NotSupportedException("El monitor no admite la textura MGT compartida.");
        if (_disposed) return;
        _imported = interop.ImportImage(
            new PlatformHandle(SharedHandle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
            new PlatformGraphicsExternalImageProperties { Width = Width, Height = Height,
                Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm });
        _drawingSurface = drawingSurface;
        _surfaceGeneration = 0;
    }

    public void Detach()
    {
        var imported = _imported as IDisposable;
        if (_pendingPresentation.IsCompleted) imported?.Dispose();
        else _ = _pendingPresentation.ContinueWith(_ => imported?.Dispose(), TaskScheduler.Default);
        _imported = null;
        _drawingSurface = null;
    }

    public async Task<bool> PresentAsync(double seconds, IReadOnlyDictionary<string, JsonElement> overrides,
        bool exact = true, CancellationToken cancellation = default, bool updateVisual = true)
    {
        if (_disposed || updateVisual && (_imported == null || _drawingSurface == null)) return false;
        await RequestFrameAsync(seconds, overrides, updateVisual, exact, cancellation);
        return true;
    }

    internal async Task PresentCurrentFrameAsync()
    {
        if (_disposed || _generation == _surfaceGeneration || _drawingSurface == null || _imported == null) return;
        await (_pendingPresentation = _drawingSurface.UpdateWithKeyedMutexAsync(_imported, 0, 0));
        _surfaceGeneration = _generation;
    }

    /// <summary>Requests an exact live frame for the GPU export compositor without Avalonia.</summary>
    public async Task RenderOffscreenAsync(double seconds, IReadOnlyDictionary<string, JsonElement> overrides,
        CancellationToken cancellation = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MgtLiveGpuSurface));
        await RequestFrameAsync(seconds, overrides, false, true, cancellation);
    }

    private async Task RequestFrameAsync(double seconds, IReadOnlyDictionary<string, JsonElement> overrides,
        bool present, bool exact, CancellationToken cancellation)
    {
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closing.Token);
        cancellation = requestLifetime.Token;
        await _requests.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(MgtLiveGpuSurface));
            if (_worker.HasExited) throw new InvalidOperationException("El trabajador MGT se cerró.");
            var frame = Math.Clamp((int)Math.Round(Math.Max(0, seconds) * 60,
                MidpointRounding.AwayFromZero), 0, _frameCount - 1);
            var scene = SubtitleScene;
            var command = JsonSerializer.Serialize(new { frame, overrides, exact,
                subtitleData = scene?.Signature != _sentSubtitleSignature ? scene?.Data : null,
                clock = scene == null ? null : new { time = seconds, fps = scene.Fps, duration = scene.Duration } });
            await _worker.StandardInput.WriteLineAsync(command.AsMemory(), cancellation);
            await _worker.StandardInput.FlushAsync(cancellation);
            var response = await _worker.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            if (response?.StartsWith($"FRAME {frame} ", StringComparison.Ordinal) != true ||
                !long.TryParse(response.AsSpan($"FRAME {frame} ".Length), out var generation))
                throw new InvalidOperationException("MGT: " + (response ?? "sin respuesta"));
            _generation = generation;
            _sourceSeconds = scene == null ? frame / 60.0 : seconds;
            _sentSubtitleSignature = scene?.Signature;
            if (present) await PresentCurrentFrameAsync();
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            // A response may still be in flight; retiring this worker prevents it from
            // being mistaken for a later request with the same frame number.
            try { if (!_worker.HasExited) _worker.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
        finally { _requests.Release(); }
    }

    public bool TryGetCurrentFrame(out GpuFrameReference frame)
    {
        if (_disposed || _generation == 0) { frame = default; return false; }
        frame = new GpuFrameReference(SharedHandle, Width, Height, _sourceSeconds, _generation, true);
        return true;
    }

    private static string WorkerPath()
    {
        var packed = Path.Combine(AppContext.BaseDirectory, "MgtLive", "Prometheus.MgtLive.exe");
        if (File.Exists(packed)) return packed;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var development = Path.Combine(directory.FullName, "src", "Prometheus.MgtLive", "bin",
                    configuration, "net8.0-windows", "win-x64", "Prometheus.MgtLive.exe");
                if (File.Exists(development)) return development;
            }
        throw new FileNotFoundException("Falta el trabajador MGT en la distribución.", packed);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _closing.Cancel();
        Detach();
        try { if (!_worker.HasExited) _worker.StandardInput.WriteLine("QUIT"); }
        catch { }
        try { if (!_worker.WaitForExit(1000)) _worker.Kill(entireProcessTree: true); }
        catch { }
        _worker.Dispose();
        // Pending requests still own their release; SemaphoreSlim has no native handle unless requested.
    }
}
