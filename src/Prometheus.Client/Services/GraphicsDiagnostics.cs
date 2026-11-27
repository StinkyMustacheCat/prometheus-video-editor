using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.Client.Services;

internal static class GraphicsDiagnostics
{
    internal static int Write(string path)
    {
        object graphics;
        var success = false;
        try
        {
            D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
                out var device, out var featureLevel, out var context).CheckError();
            using (device)
            using (context)
            using (var dxgi = device.QueryInterface<IDXGIDevice>())
            {
                dxgi.GetAdapter(out var adapter).CheckError();
                using (adapter)
                {
                    var description = adapter.Description;
                    graphics = new
                    {
                        adapter = description.Description.TrimEnd('\0'),
                        vendorId = $"{description.VendorId:X4}",
                        deviceId = $"{description.DeviceId:X4}",
                        featureLevel = featureLevel.ToString(),
                        dedicatedVideoMemoryBytes = (ulong)description.DedicatedVideoMemory,
                        sharedSystemMemoryBytes = (ulong)description.SharedSystemMemory
                    };
                    success = true;
                }
            }
        }
        catch (Exception error)
        {
            graphics = new { error = error.GetType().Name, hresult = $"0x{error.HResult:X8}" };
        }
        var information = new
        {
            version = typeof(GraphicsDiagnostics).Assembly.GetName().Version?.ToString(3),
            utc = DateTimeOffset.UtcNow,
            windows = Environment.OSVersion.Version.ToString(),
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            logicalProcessors = Environment.ProcessorCount,
            managedMemoryLimitBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            direct3d11 = graphics
            ,processing = ProcessingService.Describe()
        };
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(information, new JsonSerializerOptions { WriteIndented = true }));
            return success ? 0 : 1;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 2;
        }
    }
}
