using CrashScope.Core.Models;
using System.Text.RegularExpressions;

namespace CrashScope.Core.Monitoring;

internal sealed record DiskPerformance(float? TimePercent, float? ReadMs, float? WriteMs, float? Queue);

internal static class StorageSampleBuilder
{
    public static int? InstanceIndex(string name)
    {
        var match = Regex.Match(name, @"^(\d+)(?:\s|$)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var index) ? index : null;
    }

    public static int? HardwareIndex(string name)
    {
        var match = Regex.Match(name, @"(?:^Disk (\d+)\b|\(Disk (\d+)\))", RegexOptions.IgnoreCase);
        var text = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return match.Success && int.TryParse(text, out var index) ? index : null;
    }

    public static float? Valid(float? value) => value is { } v && float.IsFinite(v) && v >= 0 ? v : null;
    public static bool HasMetrics(DiskMetrics d) => d.TemperatureCelsius is { } t && float.IsFinite(t)
        || !string.IsNullOrWhiteSpace(d.HealthStatus) || new[] { d.DiskTimePercent, d.AvgReadLatencyMs, d.AvgWriteLatencyMs, d.QueueDepth }.Any(v => Valid(v).HasValue);

    public static StorageSample Build(DateTimeOffset now, IReadOnlyList<DiskMetrics> hardware,
        IReadOnlyDictionary<int, DiskPerformance> performance, IReadOnlyDictionary<int, string?> models)
    {
        var result = new List<DiskMetrics>();
        var attributed = new HashSet<int>();
        foreach (var disk in hardware)
        {
            var index = HardwareIndex(disk.Name);
            if (index is null)
            {
                var matching = models.Where(m => string.Equals(m.Value, disk.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Length == 1) index = matching[0].Key;
            }
            // Unattributed sensors remain separate; never copy whole-machine aggregates onto each drive.
            if (index is { } i && attributed.Add(i))
                result.Add(WithPerformance(disk with { DiskIndex = i, Model = models.GetValueOrDefault(i) }, performance.GetValueOrDefault(i)));
            else result.Add(disk);
        }
        foreach (var index in models.Keys.Concat(performance.Keys).Distinct().Order().Where(i => !attributed.Contains(i)))
        {
            var disk = new DiskMetrics($"Disk {index}", models.GetValueOrDefault(index), null, null, null, null, null, null, index);
            result.Add(WithPerformance(disk, performance.GetValueOrDefault(index)));
        }
        return new StorageSample(now, result);
    }

    private static DiskMetrics WithPerformance(DiskMetrics disk, DiskPerformance? p) => p is null ? disk : disk with
    {
        DiskTimePercent = Valid(p.TimePercent), AvgReadLatencyMs = Valid(p.ReadMs),
        AvgWriteLatencyMs = Valid(p.WriteMs), QueueDepth = Valid(p.Queue),
        PerformanceSource = "Windows PhysicalDisk performance counters"
    };
}
