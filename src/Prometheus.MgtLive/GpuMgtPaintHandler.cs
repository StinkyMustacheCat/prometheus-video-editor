using Prometheus.Graphics;
using CefSharp;
using CefSharp.OffScreen;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.MgtLive;

/// <summary>
/// Copies CEF's callback-scoped GPU frame into one owned shared texture.
/// This is the current output surface, not a history or frame cache.
/// </summary>
internal sealed class GpuMgtPaintHandler : DefaultRenderHandler
{
    private readonly ID3D11Device _device;
    private readonly ID3D11Device1 _device1;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _output;
    private readonly IDXGIResource _resource;
    private readonly IDXGIKeyedMutex _mutex;
    private readonly object _paintGate = new();
    private bool _disposed;
    private long _generation;
    private long _failures;

    public IntPtr SharedHandle => _resource.SharedHandle;
    public long Generation => Interlocked.Read(ref _generation);
    public long Failures => Interlocked.Read(ref _failures);

    public GpuMgtPaintHandler(ChromiumWebBrowser browser, int width, int height) : base(browser)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out _device, out _, out _context).CheckError();
        using var multithread = _device.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);
        _device1 = _device.QueryInterface<ID3D11Device1>();
        _output = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            (uint)width, (uint)height, 1, 1, BindFlags.ShaderResource | BindFlags.RenderTarget,
            miscFlags: ResourceOptionFlags.SharedKeyedMutex));
        _resource = _output.QueryInterface<IDXGIResource>();
        _mutex = _output.QueryInterface<IDXGIKeyedMutex>();
    }

    public override void OnAcceleratedPaint(PaintElementType type, CefSharp.Structs.Rect dirtyRect,
        AcceleratedPaintInfo acceleratedPaintInfo)
    {
        if (type != PaintElementType.View || acceleratedPaintInfo.SharedTextureHandle == IntPtr.Zero) return;
        lock (_paintGate)
        {
        if (_disposed) return;
        // CEF may rotate the handle every frame. Open and copy it before the callback returns.
        try
        {
            using var source = _device1.OpenSharedResource1<ID3D11Texture2D>(acceleratedPaintInfo.SharedTextureHandle);
            GpuTextureSync.Acquire(_mutex, 0);
            try
            {
                _context.CopyResource(_output, source);
                _context.Flush();
                Interlocked.Increment(ref _generation);
            }
            finally { _mutex.ReleaseSync(0); }
        }
        catch (Exception error)
        {
            if (Interlocked.Increment(ref _failures) == 1)
                Console.Error.WriteLine($"CEF GPU: {error.Message} handle={acceleratedPaintInfo.SharedTextureHandle}");
        }
    }

        }
    public new void Dispose()
    {
        lock (_paintGate)
        {
        if (_disposed) return;
        _disposed = true;
        _mutex.Dispose();
        _resource.Dispose();
        _output.Dispose();
        _context.Dispose();
        _device1.Dispose();
        _device.Dispose();
        base.Dispose();
        }
    }
}
