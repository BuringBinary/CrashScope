using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

internal static class SensorIndices
{
    public static int? GpuCoreLoad(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => IsGpu(e) && IsType(e, "Load") && ContainsCore(e));

    public static int? GpuCoreClock(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => IsGpu(e) && IsType(e, "Clock") && ContainsCore(e));

    public static int? CpuTotalLoad(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => string.Equals(e.HardwareType, "Cpu", StringComparison.OrdinalIgnoreCase)
            && IsType(e, "Load")
            && e.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase));

    public static bool HasGpuSensors(IReadOnlyList<SensorCatalogEntry>? catalog)
        => catalog?.Any(IsGpu) == true;

    private static bool IsGpu(SensorCatalogEntry entry)
        => entry.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsType(SensorCatalogEntry entry, string sensorType)
        => string.Equals(entry.SensorType, sensorType, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCore(SensorCatalogEntry entry)
        => entry.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase);

    private static int? Find(IReadOnlyList<SensorCatalogEntry>? catalog, Func<SensorCatalogEntry, bool> predicate)
    {
        var entry = catalog?.FirstOrDefault(predicate);
        return entry is null ? null : entry.Id;
    }
}
