using System.Runtime.InteropServices;
using SC.ScriptForge.Core;

namespace SC.ScriptForge.Windows;

/// <summary>DXGI's pointer-sized memory counters avoid Win32_VideoController.AdapterRAM's 32-bit truncation.</summary>
internal static class DxgiInventory
{
    public static IReadOnlyList<InventoryRecord> Read()
    {
        IntPtr factory = IntPtr.Zero;
        try
        {
            var iid = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369"); Marshal.ThrowExceptionForHR(CreateDXGIFactory(ref iid, out factory));
            var enumerate = Method<EnumAdapters>(factory, 7); var records = new List<InventoryRecord>();
            for (uint index = 0; index < 32; index++)
            {
                var hr = enumerate(factory, index, out var adapter);
                if (hr == unchecked((int)0x887A0002)) break; Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 8)(adapter, out var desc));
                    if (desc.VendorId == 0x1414 && desc.DeviceId == 0x8c) continue; // Microsoft software rasterizer, not a physical GPU.
                    records.Add(new(desc.Description.Trim(), [
                        new("model", "Модель (DXGI)", "DXGI/IDXGIAdapter.GetDesc") { Status = DataStatus.Success, Text = desc.Description.Trim() },
                        new("vram", "Выделенная видеопамять", "DXGI/IDXGIAdapter.GetDesc") { Status = DataStatus.Success, Number = desc.DedicatedVideoMemory.ToUInt64(), Unit = "bytes", Detail = "Выделенная память адаптера. Для интегрированной графики значение может быть 0; общая системная память сюда не включена." },
                        new("shared", "Доступная общая память", "DXGI/IDXGIAdapter.GetDesc") { Status = DataStatus.Success, Number = desc.SharedSystemMemory.ToUInt64(), Unit = "bytes" }
                    ]));
                }
                finally { if (adapter != IntPtr.Zero) Marshal.Release(adapter); }
            }
            return records;
        }
        catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException)
        { return [new("Видеопамять", [new("vram", "Выделенная видеопамять", "DXGI/IDXGIAdapter.GetDesc") { Status = DataStatus.Unavailable, Detail = "DXGI недоступен в текущем сеансе. 32-битное WMI AdapterRAM не используется." }])]; }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
    }
    private static T Method<T>(IntPtr instance, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long Luid; // Never exported or logged.
    }
    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory(ref Guid iid, out IntPtr factory);
}
