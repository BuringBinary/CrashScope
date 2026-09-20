namespace CrashScope.Core.Models;

/// <summary>A display-ready per-disk health row.</summary>
public sealed record DiskHealthRow(
    string Name,
    string Model,
    string IndexText,
    string TemperatureText,
    string SmartHealthText,
    string DiskTimeText,
    string ReadLatencyText,
    string WriteLatencyText,
    string QueueDepthText,
    string SourcesText,
    bool HasAnyMetric);

/// <summary>
/// Pure presentation logic for the disk health page. Missing or non-finite values
/// render as "--"; a measured zero (e.g. an idle queue depth) stays visible.
/// Rows are ordered by physical disk index, unknown indexes last, then by name.
/// </summary>
public static class DiskHealthView
{
    public static IReadOnlyList<DiskHealthRow> Build(IReadOnlyList<DiskMetrics> disks)
    {
        List<DiskHealthRow> rows = new();
        if (disks.Count == 0) return rows;

        foreach (var disk in disks.OrderBy(d => d.DiskIndex.HasValue ? 0 : 1)
                                  .ThenBy(d => d.DiskIndex ?? int.MaxValue)
                                  .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new DiskHealthRow(
                disk.Name,
                string.IsNullOrWhiteSpace(disk.Model) ? "Unknown model" : disk.Model.Trim(),
                disk.DiskIndex.HasValue ? $"Disk {disk.DiskIndex.Value}" : "Index unknown",
                Format(disk.TemperatureCelsius, "F1", " °C"),
                string.IsNullOrWhiteSpace(disk.HealthStatus) ? "--" : disk.HealthStatus!.Trim(),
                Format(disk.DiskTimePercent, "F1", " %"),
                Format(disk.AvgReadLatencyMs, "F2", " ms"),
                Format(disk.AvgWriteLatencyMs, "F2", " ms"),
                Format(disk.QueueDepth, "F0", ""),
                JoinSources(disk.PerformanceSource, disk.SensorSource),
                HasAnyMetric(disk)));
        }

        return rows;
    }

    public static bool HasAnyMetric(DiskMetrics disk)
        => IsFinite(disk.TemperatureCelsius)
           || !string.IsNullOrWhiteSpace(disk.HealthStatus)
           || IsFinite(disk.DiskTimePercent)
           || IsFinite(disk.AvgReadLatencyMs)
           || IsFinite(disk.AvgWriteLatencyMs)
           || IsFinite(disk.QueueDepth);

    public static string SummarizeCollector(string? status, string? detail)
        => string.IsNullOrWhiteSpace(status) ? "Storage collector: unknown"
         : string.IsNullOrWhiteSpace(detail) ? $"Storage collector: {status}"
         : $"Storage collector: {status} — {detail}";

    private static string Format(float? value, string format, string unit)
        => value.HasValue && IsFinite(value.Value) ? $"{value.Value.ToString(format)}{unit}" : "--";

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinite(float? value) => value.HasValue && IsFinite(value.Value);

    private static string JoinSources(string? performanceSource, string? sensorSource)
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(performanceSource)) parts.Add($"perf: {performanceSource}");
        if (!string.IsNullOrWhiteSpace(sensorSource)) parts.Add($"sensors: {sensorSource}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "--";
    }
}
