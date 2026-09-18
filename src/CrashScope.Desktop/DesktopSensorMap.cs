using CrashScope.Core.Analysis;
using CrashScope.Core.Models;

namespace CrashScope.Desktop;

/// <summary>Indices are rebuilt for each catalog; GPU metrics always belong to one adapter.</summary>
public sealed record DesktopSensorMap(
    int CpuModel, int CpuLoad, int CpuTemp, int CpuClock,
    int GpuModel, int GpuLoad, int GpuTemp, int GpuClock, int GpuPower, int GpuVramUsed, int GpuVramTotal,
    int RamUsed, int RamAvailable, int RamLoad)
{
    public static DesktopSensorMap Build(IReadOnlyList<SensorCatalogEntry> catalog)
    {
        int Find(Func<SensorCatalogEntry, bool> predicate)
        {
            for (var i = 0; i < catalog.Count; i++) if (predicate(catalog[i])) return i;
            return -1;
        }
        var gpuName = SensorIndices.PrimaryGpuHardwareName(catalog);
        bool Cpu(SensorCatalogEntry e) => e.HardwareType == "Cpu";
        bool Gpu(SensorCatalogEntry e) => e.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase)
            && e.HardwareName == gpuName;
        bool Ram(SensorCatalogEntry e) => e.HardwareType == "Memory" && !e.HardwareName.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
        int CpuMetric(string type, string preferred)
        {
            var index = Find(e => Cpu(e) && e.SensorType == type && e.SensorName.Contains(preferred, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : Find(e => Cpu(e) && e.SensorType == type);
        }
        int GpuMetric(string type) => Find(e => Gpu(e) && e.SensorType == type && e.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase));
        return new(
            Find(Cpu), CpuMetric("Load", "Total"), CpuMetric("Temperature", "Tctl"), CpuMetric("Clock", "Cores (Average)"),
            Find(Gpu), GpuMetric("Load"), GpuMetric("Temperature"), GpuMetric("Clock"),
            Find(e => Gpu(e) && e.SensorType == "Power" && (e.SensorName.Contains("Package") || e.SensorName.Contains("Core"))),
            Find(e => Gpu(e) && e.SensorType == "SmallData" && e.SensorName == "GPU Memory Used"),
            Find(e => Gpu(e) && e.SensorType == "SmallData" && e.SensorName == "GPU Memory Total"),
            Find(e => Ram(e) && e.SensorType == "Data" && e.SensorName == "Memory Used"),
            Find(e => Ram(e) && e.SensorType == "Data" && e.SensorName == "Memory Available"),
            Find(e => Ram(e) && e.SensorType == "Load"));
    }
}
