using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public static class SensorIndices
{
    public static int? GpuCoreLoad(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => IsPrimaryGpu(e, catalog) && IsType(e, "Load") && ContainsCore(e));

    public static int? GpuCoreClock(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => IsPrimaryGpu(e, catalog) && IsType(e, "Clock") && ContainsCore(e));

    public static int? CpuTotalLoad(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => string.Equals(e.HardwareType, "Cpu", StringComparison.OrdinalIgnoreCase)
            && IsType(e, "Load")
            && e.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase));

    /// <summary>Physical RAM used, in GiB. Excludes the "Virtual Memory" (commit charge) hardware.</summary>
    public static int? MemoryUsed(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => IsPhysicalMemory(e) && IsType(e, "Data") && IsNamed(e, "Memory Used"));

    /// <summary>Physical RAM available, in GiB.</summary>
    public static int? MemoryAvailable(IReadOnlyList<SensorCatalogEntry>? catalog)
        => Find(catalog, e => IsPhysicalMemory(e) && IsType(e, "Data") && IsNamed(e, "Memory Available"));

    public static bool HasGpuSensors(IReadOnlyList<SensorCatalogEntry>? catalog)
        => catalog?.Any(IsGpu) == true;

    /// <summary>
    /// Hardware name of the adapter that GPU facts (FinalGpuLoadPercent, GPU-load collapse,
    /// the HighGpuLoad cross-incident pattern) should be reported from.
    /// </summary>
    /// <remarks>
    /// Taking the first GPU in catalog order is wrong on a hybrid machine. LibreHardwareMonitor
    /// enumerates the iGPU first here, so every GPU fact described the integrated adapter while
    /// the discrete card - the part that actually fails - went unreported.
    /// <para>
    /// LHM does not reliably label adapters as GpuDiscrete/GpuIntegrated (on an AMD APU + RDNA4
    /// pair both are reported as "GpuAmd"), so several signals are combined. A cooling-fan sensor
    /// is the strongest one available from the catalog alone: an iGPU is cooled by the CPU
    /// cooler and never exposes one.
    /// </para>
    /// </remarks>
    public static string? PrimaryGpuHardwareName(IReadOnlyList<SensorCatalogEntry>? catalog)
    {
        if (catalog is null)
            return null;

        return catalog
            .Where(IsGpu)
            .GroupBy(e => e.HardwareName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Name = g.Key, Rank = Rank(g), FirstId = g.Min(e => e.Id) })
            .OrderBy(g => g.Rank)
            .ThenBy(g => g.FirstId)
            .Select(g => g.Name)
            .FirstOrDefault();
    }

    private static int Rank(IEnumerable<SensorCatalogEntry> adapterSensors)
    {
        var sensors = adapterSensors as IReadOnlyCollection<SensorCatalogEntry> ?? adapterSensors.ToList();

        if (sensors.Any(e => e.HardwareType.Contains("Discrete", StringComparison.OrdinalIgnoreCase)))
            return 0;
        if (sensors.Any(e => IsType(e, "Fan") || IsType(e, "Control")))
            return 1;
        if (sensors.Any(e => IsType(e, "Power")))
            return 2;
        if (sensors.Any(e => e.HardwareType.Contains("Integrated", StringComparison.OrdinalIgnoreCase)))
            return 4;

        return 3;
    }

    private static bool IsGpu(SensorCatalogEntry entry)
        => entry.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsPhysicalMemory(SensorCatalogEntry entry)
        => string.Equals(entry.HardwareType, "Memory", StringComparison.OrdinalIgnoreCase)
            && !entry.HardwareName.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrimaryGpu(SensorCatalogEntry entry, IReadOnlyList<SensorCatalogEntry>? catalog)
        => IsGpu(entry)
            && string.Equals(entry.HardwareName, PrimaryGpuHardwareName(catalog), StringComparison.OrdinalIgnoreCase);

    private static bool IsType(SensorCatalogEntry entry, string sensorType)
        => string.Equals(entry.SensorType, sensorType, StringComparison.OrdinalIgnoreCase);

    private static bool IsNamed(SensorCatalogEntry entry, string sensorName)
        => string.Equals(entry.SensorName, sensorName, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCore(SensorCatalogEntry entry)
        => entry.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase);

    private static int? Find(IReadOnlyList<SensorCatalogEntry>? catalog, Func<SensorCatalogEntry, bool> predicate)
    {
        var entry = catalog?.FirstOrDefault(predicate);
        return entry is null ? null : entry.Id;
    }
}
