using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ShmonovStudio.AI;

// The owned runtime cannot survive an application crash on Windows. This is lifetime control, not a sandbox.
internal sealed class RuntimeProcess : IDisposable
{
    public Process Process { get; }
    private readonly JobHandle? _job;
    public RuntimeProcess(ProcessStartInfo info)
    {
        Process = Process.Start(info) ?? throw new IOException("Не удалось запустить llama.cpp.");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
                if (_job.IsInvalid || !SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(_job, Process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            Process.StandardInput.Close();
            // Do not retain native output: logs may contain model-supplied text in some runtime versions.
            _ = DrainAsync(Process.StandardOutput); _ = DrainAsync(Process.StandardError);
        }
        catch { Dispose(); throw; }
    }
    private static async Task DrainAsync(StreamReader reader)
    { try { var buffer = new char[4096]; while (await reader.ReadAsync(buffer) > 0) { } } catch (Exception e) when (e is IOException or ObjectDisposedException) { } }
    public void Dispose()
    {
        _job?.Dispose();
        try { if (!Process.HasExited) { Process.Kill(entireProcessTree: true); Process.WaitForExit(3000); } } catch (InvalidOperationException) { }
        Process.Dispose();
    }
    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid { public JobHandle() : base(true) { } protected override bool ReleaseHandle() => CloseHandle(handle); }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern JobHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(JobHandle job, int informationClass, ref ExtendedLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
