using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

/// <summary>Presentation quality checks only. Original telemetry remains unchanged.</summary>
public static class SensorValueQuality
{
    public static float? ForDisplay(SensorCatalogEntry entry, float? value)
    {
        if (value is null || !float.IsFinite(value.Value)) return null;
        // Do not globally discard zero: idle load, stopped fans and sleeping GPU clocks can be valid.
        var suspectCpuZero = entry.HardwareType == "Cpu" && value <= 0 &&
            (entry.SensorType == "Temperature"
             || (entry.SensorType == "Power" && entry.SensorName == "Package")
             || (entry.SensorType == "Clock" && entry.SensorName == "Cores (Average)"));
        return suspectCpuZero ? null : value;
    }

    public static bool HasSuspectReadings(IReadOnlyList<SensorCatalogEntry> catalog, IReadOnlyList<float?> values)
    {
        for (var i = 0; i < Math.Min(catalog.Count, values.Count); i++)
            if (values[i].HasValue && ForDisplay(catalog[i], values[i]) is null) return true;
        return false;
    }
}
