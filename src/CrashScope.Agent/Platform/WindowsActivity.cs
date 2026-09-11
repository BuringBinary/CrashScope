using System.Diagnostics;
using System.Runtime.InteropServices;
using CrashScope.Agent.Models;

namespace CrashScope.Agent.Platform;

public static class WindowsActivity
{
    public static ForegroundState CaptureForeground()
    {
        var hwnd = GetForegroundWindow();
        int? pid = null;
        string? name = null;

        if (hwnd != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(hwnd, out var processId);
            if (processId != 0)
            {
                pid = unchecked((int)processId);
                try
                {
                    using var process = Process.GetProcessById(pid.Value);
                    name = process.ProcessName;
                }
                catch
                {
                    // Process may exit between Win32 lookup and Process.GetProcessById.
                }
            }
        }

        return new ForegroundState(pid, name, GetIdleSeconds());
    }

    public static long GetIdleSeconds()
    {
        var info = new LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
        };

        if (!GetLastInputInfo(ref info))
            return 0;

        // LASTINPUTINFO uses the 32-bit system tick count. Unsigned subtraction
        // intentionally handles the normal ~49.7 day tick-count wraparound.
        var now = unchecked((uint)Environment.TickCount);
        var elapsed = unchecked(now - info.dwTime);
        return elapsed / 1000;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
