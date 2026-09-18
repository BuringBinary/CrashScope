using CrashScope.Core.Analysis;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

internal static class CpuSensorAssessment
{
    public static (string Status, string Detail) Assess(IReadOnlyList<SensorCatalogEntry> catalog, IReadOnlyList<float?> values)
    {
        var states = new List<string>();
        var complete = true;
        foreach (var type in new[] { "Load", "Temperature", "Clock", "Power" })
        {
            var sensors = catalog.Where(e => e.HardwareType.Equals("Cpu", StringComparison.OrdinalIgnoreCase)
                && e.SensorType.Equals(type, StringComparison.OrdinalIgnoreCase)).ToArray();
            var valid = sensors.Count(e => e.Id >= 0 && e.Id < values.Count && SensorValueQuality.ForDisplay(e, values[e.Id]).HasValue);
            var state = sensors.Length == 0 ? "NotExposed" : valid == 0 ? "Unavailable" : valid < sensors.Length ? "Partial" : "Available";
            if (state != "Available") complete = false;
            states.Add($"{type}={state} ({valid}/{sensors.Length})");
        }
        return (complete ? "Collecting" : "Degraded", "Source=LibreHardwareMonitor. " + string.Join("; ", states) +
            ". Missing/null/suspect values remain N/A; raw telemetry is unchanged. " +
            "Permission or driver support cannot be inferred from a missing reading alone.");
    }
}
