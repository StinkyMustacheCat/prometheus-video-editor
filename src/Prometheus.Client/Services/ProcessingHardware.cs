using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace Prometheus.Client.Services;

internal sealed record ProcessingAdapter(int Index, string Key, long Luid, string Name,
    uint VendorId, uint DeviceId, string Driver, bool Software, bool? UnifiedMemory,
    ulong DedicatedBytes, ulong SharedMaximumBytes, ulong BudgetBytes, ulong UsageBytes, bool D3D11)
{
    public bool Nvidia => VendorId == 0x10de;
    public bool Amd => VendorId == 0x1002;
    public string Kind => Software ? "Software" : UnifiedMemory == true ? "Integrada / memoria unificada" :
        UnifiedMemory == false ? "Dedicada" : "Tipo no confirmado";
}

internal sealed record ProcessingHardwareSnapshot(string Cpu, int LogicalProcessors,
    ulong InstalledRamBytes, ulong UsableRamBytes, ulong AvailableRamBytes,
    string Windows, ProcessingAdapter[] Adapters, long? CompositionLuid, string? Error)
{
    public ProcessingAdapter? Composition => Array.Find(Adapters, a => a.Luid == CompositionLuid);
}

internal static class ProcessingHardware
{
    internal static ProcessingHardwareSnapshot Capture()
    {
        var adapters = new List<ProcessingAdapter>();
        string? error = null;
        long? composition = null;
        ulong installed = 0, usable = 0, available = 0;
        var cpu = "Procesador";
        if (OperatingSystem.IsWindows())
        {
            try { cpu = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string)?.Trim() ?? cpu; } catch { }
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref memory)) { usable = memory.TotalPhysical; available = memory.AvailablePhysical; }
            if (GetPhysicallyInstalledSystemMemory(out var kilobytes)) installed = kilobytes * 1024;
        }
        try
        {
            using var factory = CreateDXGIFactory1<IDXGIFactory1>();
            for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
            {
                using (adapter)
                {
                    var d = adapter.Description1;
                    var software = (d.Flags & AdapterFlags.Software) != 0;
                    var driver = "No disponible";
                    if (adapter.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out var version).Success)
                        driver = $"{(ulong)version >> 48}.{((ulong)version >> 32) & 65535}.{((ulong)version >> 16) & 65535}.{(ulong)version & 65535}";
                    ulong budget = 0, usage = 0;
                    try
                    {
                        using var extended = adapter.QueryInterface<IDXGIAdapter3>();
                        var info = extended.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                        budget = info.Budget; usage = info.CurrentUsage;
                    }
                    catch { }
                    var supports = false;
                    if (!software)
                    {
                        var result = D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out var device, out _, out var context);
                        supports = result.Success;
                        context?.Dispose(); device?.Dispose();
                    }
                    adapters.Add(new ProcessingAdapter((int)index,
                        $"{d.VendorId:X4}:{d.DeviceId:X4}:{d.SubsystemId:X8}:{d.Revision:X2}",
                        d.Luid, d.Description.TrimEnd('\0'), d.VendorId, d.DeviceId, driver, software,
                        software ? null : ReadUma(adapter.NativePointer), (ulong)d.DedicatedVideoMemory,
                        (ulong)d.SharedSystemMemory, budget, usage, supports));
                }
            }
            var created = D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out var current, out _, out var currentContext);
            if (created.Success)
            {
                using (current) using (currentContext) using (var dxgi = current.QueryInterface<IDXGIDevice>())
                {
                    dxgi.GetAdapter(out var selected).CheckError();
                    using (selected) composition = selected.Description.Luid;
                }
            }
        }
        catch (Exception exception) { error = exception.GetType().Name + $" (0x{exception.HResult:X8})"; }
        // Identical boards cannot safely be persisted by their enumeration index.
        var ambiguous = adapters.GroupBy(a => a.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (var i = 0; i < adapters.Count; i++)
            if (ambiguous.Contains(adapters[i].Key)) adapters[i] = adapters[i] with { Key = adapters[i].Key + ":session:" + adapters[i].Luid };
        return new(cpu, Environment.ProcessorCount, installed, usable, available,
            Environment.OSVersion.VersionString, adapters.ToArray(), composition, error);
    }

    // D3D12 is queried only for architecture; rendering stays on D3D11/CEF.
    private static bool? ReadUma(IntPtr adapter)
    {
        IntPtr device = IntPtr.Zero;
        try
        {
            var iid = new Guid("189819F1-1DB6-4B57-BE54-1821339B85F7");
            if (D3D12CreateDevice(adapter, 0xb000, ref iid, out device) < 0) return null;
            var table = Marshal.ReadIntPtr(device);
            var check = Marshal.GetDelegateForFunctionPointer<CheckArchitecture>(Marshal.ReadIntPtr(table, 13 * IntPtr.Size));
            var data = new Architecture();
            return check(device, 16, ref data, (uint)Marshal.SizeOf<Architecture>()) >= 0 ? data.Uma != 0 : null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or COMException) { return null; }
        finally { if (device != IntPtr.Zero) Marshal.Release(device); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Architecture { public uint Node; public int Tiled, Uma, Coherent, Isolated; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CheckArchitecture(IntPtr self, int feature, ref Architecture data, uint bytes);
    [DllImport("d3d12.dll")] private static extern int D3D12CreateDevice(IntPtr adapter, int level, ref Guid iid, out IntPtr device);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);
}
