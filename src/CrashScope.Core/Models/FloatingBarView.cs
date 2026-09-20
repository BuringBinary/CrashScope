namespace CrashScope.Core.Models;

/// <summary>
/// Pure helpers for the desktop floating bar: network-speed deltas between samples
/// and the single-line overlay text. No WPF types here so the logic stays testable.
/// </summary>
public static class FloatingBarView
{
    /// <summary>
    /// Bytes per second between two cumulative counters. Returns null when a delta
    /// cannot be computed yet (first sample or non-positive interval); a counter that
    /// went backwards (interface reset) yields 0 instead of a negative speed.
    /// </summary>
    public static double? NextBytesPerSecond(long current, long? previous, double seconds)
    {
        if (previous is null || seconds <= 0) return null;
        if (current < previous.Value) return 0;
        return (current - previous.Value) / seconds;
    }

    /// <summary>Adaptive B/s → KB/s → MB/s → GB/s formatting with a 1024 base.</summary>
    public static string FormatSpeed(double? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue || !IsFinite(bytesPerSecond.Value) || bytesPerSecond.Value < 0)
            return "--";
        var value = bytesPerSecond.Value;
        if (value < 1024) return $"{value:F0} B/s";
        if (value < 1024 * 1024) return $"{value / 1024:F2} KB/s";
        if (value < 1024L * 1024 * 1024) return $"{value / (1024 * 1024):F2} MB/s";
        return $"{value / (1024L * 1024 * 1024):F2} GB/s";
    }

    /// <summary>
    /// One overlay line: CPU / GPU / RAM from the recorder's sensor stream (missing
    /// groups show "--") plus up/down speed from the OS counters.
    /// </summary>
    public static string Build(
        float? cpuLoad,
        float? gpuLoad,
        float? gpuTemp,
        float? ramUsedGb,
        float? ramAvailableGb,
        double? downBytesPerSecond,
        double? upBytesPerSecond)
    {
        var cpu = CpuSegment(cpuLoad);
        var gpu = GpuSegment(gpuLoad, gpuTemp);
        var ram = RamSegment(ramUsedGb, ramAvailableGb);
        var net = $"↓ {FormatSpeed(downBytesPerSecond)}  ↑ {FormatSpeed(upBytesPerSecond)}";
        return string.Join("  ·  ", new[] { cpu, gpu, ram, net });
    }

    private static string CpuSegment(float? load)
        => $"CPU {(IsFinite(load) ? $"{load!.Value:F0}%" : "--")}";

    private static string GpuSegment(float? load, float? temp)
    {
        var loadText = IsFinite(load) ? $"{load!.Value:F0}%" : "--";
        var tempText = IsFinite(temp) ? $" {temp!.Value:F0}°C" : "";
        return $"GPU {loadText}{tempText}";
    }

    private static string RamSegment(float? usedGb, float? availableGb)
    {
        if (!IsFinite(usedGb) || !IsFinite(availableGb)) return "RAM --";
        return $"RAM {usedGb!.Value:F1}/{usedGb.Value + availableGb!.Value:F1} GB";
    }

    private static bool IsFinite(float? value) => value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
