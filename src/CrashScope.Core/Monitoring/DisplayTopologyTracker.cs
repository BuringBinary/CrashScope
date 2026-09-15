using System.Runtime.InteropServices;
using System.Text;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class DisplayTopologyTracker
{
    // Friendly names emitted by common virtual display drivers (ToDesk, Parsec,
    // Sunshine/IddSampleDriver, spacedesk, Mirage, Duet, test "dummy" plugs).
    private static readonly string[] VirtualDisplayMarkers =
    [
        "todesk", "parsec", "sunshine", "virtual", "iddsample", "iddcx",
        "mirage", "spacedesk", "ddda", "duet", "dummy", "remote display"
    ];

    private readonly MonitorEnumDelegate _callback;
    private List<DisplayInfo>? _topology;
    private List<DisplayInfo> _enumerationTarget = [];

    public DisplayTopologyTracker()
    {
        _callback = OnMonitorEnum;
    }

    public DisplayTopologyEvent? Poll(DateTimeOffset now)
    {
        var current = Enumerate();
        var previous = _topology;
        _topology = current;

        if (previous is null)
            return new DisplayTopologyEvent(now, "baseline", [], current, Describe(null, current));

        if (SameTopology(previous, current))
            return null;

        return new DisplayTopologyEvent(now, "changed", previous, current, Describe(previous, current));
    }

    private static bool SameTopology(IReadOnlyList<DisplayInfo> before, IReadOnlyList<DisplayInfo> after)
    {
        if (before.Count != after.Count)
            return false;

        return before.OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Zip(after.OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase))
            .All(pair => string.Equals(pair.First.DeviceName, pair.Second.DeviceName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pair.First.MonitorName, pair.Second.MonitorName, StringComparison.OrdinalIgnoreCase)
                && pair.First.Width == pair.Second.Width
                && pair.First.Height == pair.Second.Height);
    }

    private static string Describe(IReadOnlyList<DisplayInfo>? before, IReadOnlyList<DisplayInfo> after)
    {
        var builder = new StringBuilder();
        if (before is null)
        {
            builder.Append("display topology baseline: ").Append(after.Count)
                .Append(after.Count == 1 ? " monitor (" : " monitors (");
        }
        else
        {
            builder.Append("display topology changed: ")
                .Append(before.Count).Append(" -> ").Append(after.Count);

            var added = after
                .Where(a => !before.Any(b => string.Equals(b.DeviceName, a.DeviceName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (added.Count > 0)
                builder.Append("; added: ").Append(string.Join(", ", added.Select(DescribeDisplay)));

            var removed = before
                .Where(b => !after.Any(a => string.Equals(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (removed.Count > 0)
                builder.Append("; removed: ").Append(string.Join(", ", removed.Select(DescribeDisplay)));
            builder.Append(" (");
        }

        builder.Append(string.Join(", ", after.Select(DescribeDisplay)));
        builder.Append(')');
        return builder.ToString();
    }

    private static string DescribeDisplay(DisplayInfo display)
    {
        var suffix = display.IsVirtual ? " [virtual display driver]" : "";
        return $"'{display.MonitorName}' on {display.DeviceName} ({display.Width}x{display.Height}){suffix}";
    }

    private static bool IsVirtualDisplay(string monitorName)
        => VirtualDisplayMarkers.Any(marker => monitorName.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private List<DisplayInfo> Enumerate()
    {
        var result = new List<DisplayInfo>();
        _enumerationTarget = result;
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _callback, IntPtr.Zero);
        }
        catch
        {
            // Display enumeration failed this round: report the empty topology change-free.
        }
        return result;
    }

    private bool OnMonitorEnum(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT rect, IntPtr lParam)
    {
        var target = _enumerationTarget;

        var info = new MONITORINFOEX();
        if (GetMonitorInfo(hMonitor, ref info))
        {
            var deviceName = info.szDevice;
            var monitorName = "<unknown monitor>";

            var displayDevice = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(deviceName, 0, ref displayDevice, 0) &&
                !string.IsNullOrEmpty(displayDevice.DeviceString))
            {
                monitorName = displayDevice.DeviceString;
            }

            var width = info.rcMonitor.Right - info.rcMonitor.Left;
            var height = info.rcMonitor.Bottom - info.rcMonitor.Top;
            target.Add(new DisplayInfo(deviceName, monitorName, IsVirtualDisplay(monitorName), width, height));
        }

        return true;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT rect, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clipRect, MonitorEnumDelegate callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? deviceName, uint deviceNumber, ref DISPLAY_DEVICE device, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}
