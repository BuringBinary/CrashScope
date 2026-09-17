using System.Runtime.InteropServices;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class MemoryPressureTracker
{
    public bool IsAvailable { get; } = true;

    public MemoryPressureSample? Capture(DateTimeOffset now)
    {
        var memStatus = new MEMORYSTATUSEX();
        memStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            // A zeroed sample reads as "0 bytes available" and fabricates top-weight
            // "critically low available RAM" evidence. Skip the record instead.
            return null;
        }

        var commitBytes = (long)memStatus.ullTotalPageFile - (long)memStatus.ullAvailPageFile;
        var commitLimit = (long)memStatus.ullTotalPageFile;
        var availableBytes = (long)memStatus.ullAvailPhys;
        var committedPercent = commitLimit > 0
            ? (float)commitBytes / commitLimit * 100f
            : 0f;

        return new MemoryPressureSample(now, commitBytes, commitLimit, availableBytes, committedPercent);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        // Unused by the capture but required: the struct must match the Win32 layout.
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}