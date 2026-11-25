using System;
using Vortice.DXGI;
using System.Runtime.InteropServices;

namespace Prometheus.Graphics;

internal static class GpuTextureSync
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AcquireNative(IntPtr self, ulong key, int milliseconds);

    // WAIT_TIMEOUT and WAIT_ABANDONED are positive HRESULT values.
    public static void Acquire(IDXGIKeyedMutex mutex, ulong key, int milliseconds = 1000)
    {
        // Vortice's void wrapper discards positive wait results. IDXGIKeyedMutex
        // inherits IDXGIDeviceSubObject; AcquireSync is COM vtable slot 8.
        var table = Marshal.ReadIntPtr(mutex.NativePointer);
        var acquire = Marshal.GetDelegateForFunctionPointer<AcquireNative>(
            Marshal.ReadIntPtr(table, 8 * IntPtr.Size));
        var result = acquire(mutex.NativePointer, key, milliseconds);
        if (result == 0) return;
        if (result == 0x102)
            throw new TimeoutException("La textura GPU sigue ocupada.");
        if (result == 0x80)
            throw new InvalidOperationException("Se perdió la superficie GPU compartida.");
        Marshal.ThrowExceptionForHR(result);
        throw new InvalidOperationException($"No se pudo adquirir la textura GPU: {result:X8}");
    }
}
