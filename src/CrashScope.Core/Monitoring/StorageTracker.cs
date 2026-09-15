using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class StorageTracker : IDisposable
{
    private static readonly string[] PerfCategories = ["PhysicalDisk", "LogicalDisk"];

    private readonly Computer _storageComputer;
    private readonly List<PerformanceCounter> _diskTimeCounters = new();
    private readonly List<PerformanceCounter> _readLatencyCounters = new();
    private readonly List<PerformanceCounter> _writeLatencyCounters = new();
    private readonly List<PerformanceCounter> _queueDepthCounters = new();
    private readonly List<(string Instance, string? Model)> _diskInstances = new();

    private bool _perfCountersAvailable;

    public bool IsAvailable { get; }

    public StorageTracker()
    {
        _storageComputer = new Computer
        {
            IsCpuEnabled = false,
            IsGpuEnabled = false,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsStorageEnabled = true,
            IsNetworkEnabled = false,
            IsControllerEnabled = false,
            IsPsuEnabled = false
        };

        try
        {
            _storageComputer.Open();
            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
            return;
        }

        InitializePerfCounters();
    }

    private void InitializePerfCounters()
    {
        try
        {
            string category = PerfCategories.FirstOrDefault(c => PerformanceCounterCategory.Exists(c)) ?? "";
            if (string.IsNullOrEmpty(category))
            {
                _perfCountersAvailable = false;
                return;
            }

            string[] instances;
            try
            {
                instances = new PerformanceCounterCategory(category).GetInstanceNames();
            }
            catch
            {
                _perfCountersAvailable = false;
                return;
            }

            foreach (var instance in instances)
            {
                if (string.Equals(instance, "_Total", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    _diskTimeCounters.Add(new PerformanceCounter(category, "% Disk Time", instance, true));
                    _readLatencyCounters.Add(new PerformanceCounter(category, "Avg. Disk sec/Read", instance, true));
                    _writeLatencyCounters.Add(new PerformanceCounter(category, "Avg. Disk sec/Write", instance, true));
                    _queueDepthCounters.Add(new PerformanceCounter(category, "Current Disk Queue Length", instance, true));
                    _diskInstances.Add((instance, null));
                }
                catch
                {
                    // Skip instances that don't support these counters.
                }
            }

            _perfCountersAvailable = _diskInstances.Count > 0;

            // Prime the counters with a first read (they need 2 samples for averaged counters).
            if (_perfCountersAvailable)
            {
                foreach (var c in _diskTimeCounters) try { c.NextValue(); } catch { }
                foreach (var c in _readLatencyCounters) try { c.NextValue(); } catch { }
                foreach (var c in _writeLatencyCounters) try { c.NextValue(); } catch { }
                foreach (var c in _queueDepthCounters) try { c.NextValue(); } catch { }
            }
        }
        catch
        {
            _perfCountersAvailable = false;
        }
    }

    public StorageSample Capture(DateTimeOffset now)
    {
        if (!IsAvailable)
            return new StorageSample(now, Array.Empty<DiskMetrics>());

        _storageComputer.Accept(new UpdateVisitor());

        var diskMetrics = new List<DiskMetrics>();
        var storageHardware = _storageComputer.Hardware
            .Where(h => h.HardwareType == HardwareType.Storage);

        foreach (var hw in storageHardware)
        {
            float? tempC = null;
            string? health = null;

            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                    tempC = sensor.Value;
                else if (sensor.SensorType == SensorType.Level && sensor.Value.HasValue
                    && sensor.Name.Contains("Health", StringComparison.OrdinalIgnoreCase))
                    health = $"{sensor.Value:0}%";
            }

            var name = hw.Name;
            var model = FindDiskModel(name);

            float? diskTime = null;
            float? readLatency = null;
            float? writeLatency = null;
            float? queueDepth = null;

            if (_perfCountersAvailable)
            {
                for (var i = 0; i < _diskInstances.Count; i++)
                {
                    var instanceName = _diskInstances[i].Instance;
                    var diskIndex = ExtractDiskIndex(instanceName);
                    var hwIndex = ExtractDiskIndex(name);

                    if (diskIndex.HasValue && hwIndex.HasValue && diskIndex == hwIndex
                        || name.Contains(instanceName, StringComparison.OrdinalIgnoreCase)
                        || instanceName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        diskTime = ReadCounter(_diskTimeCounters, i);
                        readLatency = ReadCounterMs(_readLatencyCounters, i);
                        writeLatency = ReadCounterMs(_writeLatencyCounters, i);
                        queueDepth = ReadCounter(_queueDepthCounters, i);
                        break;
                    }
                }

                if (!diskTime.HasValue)
                {
                    diskTime = AggregateMax(_diskTimeCounters);
                    readLatency = AggregateMaxMs(_readLatencyCounters);
                    writeLatency = AggregateMaxMs(_writeLatencyCounters);
                    queueDepth = AggregateSum(_queueDepthCounters);
                }
            }

            diskMetrics.Add(new DiskMetrics(
                name,
                model,
                tempC,
                health,
                diskTime,
                readLatency,
                writeLatency,
                queueDepth));
        }

        return new StorageSample(now, diskMetrics);
    }

    private static string? FindDiskModel(string name)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, Model FROM Win32_DiskDrive");
            foreach (System.Management.ManagementObject disk in searcher.Get())
            {
                var wmiName = disk["Name"]?.ToString();
                if (wmiName is not null && name.Contains(
                    Path.GetFileName(wmiName.TrimEnd('\\')) ?? wmiName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return disk["Model"]?.ToString()?.Trim();
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static int? ExtractDiskIndex(string name)
    {
        // PhysicalDisk names are like "0 C: D:" or "0"
        // LibreHardwareMonitor names are like "ST1000DM010-2EP102 (Disk 0)"
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var index) ? index : null;
    }

    private static float? ReadCounter(List<PerformanceCounter> counters, int index)
    {
        if (index >= counters.Count) return null;
        try { return counters[index].NextValue(); }
        catch { return null; }
    }

    private static float? ReadCounterMs(List<PerformanceCounter> counters, int index)
    {
        if (index >= counters.Count) return null;
        try { return counters[index].NextValue() * 1000f; }
        catch { return null; }
    }

    private static float? AggregateMax(List<PerformanceCounter> counters)
    {
        float? max = null;
        foreach (var c in counters)
        {
            try { var v = c.NextValue(); if (!max.HasValue || v > max) max = v; }
            catch { }
        }
        return max;
    }

    private static float? AggregateMaxMs(List<PerformanceCounter> counters)
    {
        float? max = null;
        foreach (var c in counters)
        {
            try { var v = c.NextValue() * 1000f; if (!max.HasValue || v > max) max = v; }
            catch { }
        }
        return max;
    }

    private static float? AggregateSum(List<PerformanceCounter> counters)
    {
        float? sum = null;
        foreach (var c in counters)
        {
            try { var v = c.NextValue(); sum = (sum ?? 0) + v; }
            catch { }
        }
        return sum;
    }

    public void Dispose()
    {
        foreach (var c in _diskTimeCounters) c.Dispose();
        foreach (var c in _readLatencyCounters) c.Dispose();
        foreach (var c in _writeLatencyCounters) c.Dispose();
        foreach (var c in _queueDepthCounters) c.Dispose();
        _storageComputer.Close();
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }

        public void VisitParameter(IParameter parameter) { }
        public void VisitSensor(ISensor sensor) { }
    }
}