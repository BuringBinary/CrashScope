using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class StorageTracker : IDisposable
{
    private static readonly string[] PerfCategories = ["PhysicalDisk", "LogicalDisk"];

    // LHM storage hardware names look like "ST1000DM010-2EP102 (Disk 0)" or "Disk 0".
    private static readonly Regex DiskIndexRegex = new(@"^Disk (?<bare>\d+)|\(Disk (?<num>\d+)\)", RegexOptions.Compiled);

    // PhysicalDisk instance names look like "0", "0 C: D:", "1".
    private static readonly Regex InstanceIndexRegex = new(@"^(?<idx>\d+)", RegexOptions.Compiled);

    private readonly Computer _storageComputer;
    private readonly UpdateVisitor _visitor = new();
    private readonly List<PerformanceCounter> _diskTimeCounters = new();
    private readonly List<PerformanceCounter> _readLatencyCounters = new();
    private readonly List<PerformanceCounter> _writeLatencyCounters = new();
    private readonly List<PerformanceCounter> _queueDepthCounters = new();
    private readonly List<int?> _diskInstanceIndexes = new();
    private readonly Dictionary<int, string?> _modelsByDiskIndex = new();

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
        LoadDiskModels();
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

                PerformanceCounter? time = null, read = null, write = null, queue = null;
                try
                {
                    time = new PerformanceCounter(category, "% Disk Time", instance, true);
                    read = new PerformanceCounter(category, "Avg. Disk sec/Read", instance, true);
                    write = new PerformanceCounter(category, "Avg. Disk sec/Write", instance, true);
                    queue = new PerformanceCounter(category, "Current Disk Queue Length", instance, true);
                }
                catch
                {
                    // Skip instances that do not support these counters.
                    time?.Dispose(); read?.Dispose(); write?.Dispose(); queue?.Dispose();
                    continue;
                }

                _diskTimeCounters.Add(time);
                _readLatencyCounters.Add(read);
                _writeLatencyCounters.Add(write);
                _queueDepthCounters.Add(queue);
                _diskInstanceIndexes.Add(ExtractInstanceIndex(instance));
            }

            _perfCountersAvailable = _diskInstanceIndexes.Count > 0;

            // Prime the counters with a first read (averaged counters need 2 samples).
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

    // WMI round trips are expensive (100-500 ms); disk models do not change during
    // a session, so resolve them exactly once instead of per capture.
    private void LoadDiskModels()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
            foreach (ManagementObject disk in searcher.Get())
            {
                var rawIndex = disk["Index"]?.ToString();
                if (!int.TryParse(rawIndex, out var index))
                    continue;

                var model = disk["Model"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(model))
                    _modelsByDiskIndex[index] = model;
            }
        }
        catch
        {
            // Best effort: disk model labels stay null.
        }
    }

    public StorageSample Capture(DateTimeOffset now)
    {
        if (!IsAvailable)
            return new StorageSample(now, Array.Empty<DiskMetrics>());

        _storageComputer.Accept(_visitor);

        // Consume each performance counter exactly once per capture. Re-reading a
        // ratio counter within the same interval returns ~0, so values are read
        // first and then attributed (or aggregated) without touching the counters
        // again.
        var metricsByIndex = new Dictionary<int, (float? Time, float? ReadMs, float? WriteMs, float? Queue)>();
        float? aggTime = null, aggReadMs = null, aggWriteMs = null, aggQueue = null;

        if (_perfCountersAvailable)
        {
            for (var i = 0; i < _diskInstanceIndexes.Count; i++)
            {
                var time = ReadCounter(_diskTimeCounters, i);
                var readMs = ReadCounterMs(_readLatencyCounters, i);
                var writeMs = ReadCounterMs(_writeLatencyCounters, i);
                var queue = ReadCounter(_queueDepthCounters, i);

                if (_diskInstanceIndexes[i] is int diskIndex && !metricsByIndex.ContainsKey(diskIndex))
                    metricsByIndex[diskIndex] = (time, readMs, writeMs, queue);

                aggTime = Max(aggTime, time);
                aggReadMs = Max(aggReadMs, readMs);
                aggWriteMs = Max(aggWriteMs, writeMs);
                if (queue.HasValue)
                    aggQueue = (aggQueue ?? 0) + queue;
            }
        }

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
            var diskIndex = ExtractDiskIndex(name);
            var model = diskIndex.HasValue && _modelsByDiskIndex.TryGetValue(diskIndex.Value, out var m)
                ? m
                : null;

            float? diskTime, readLatency, writeLatency, queueDepth;
            if (diskIndex.HasValue && metricsByIndex.TryGetValue(diskIndex.Value, out var perDisk))
                (diskTime, readLatency, writeLatency, queueDepth) = perDisk;
            else
                (diskTime, readLatency, writeLatency, queueDepth) = (aggTime, aggReadMs, aggWriteMs, aggQueue);

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

    private static int? ExtractDiskIndex(string name)
    {
        var match = DiskIndexRegex.Match(name);
        if (!match.Success)
            return null;

        var group = match.Groups["num"].Success ? match.Groups["num"] : match.Groups["bare"];
        return group.Success ? int.Parse(group.Value) : null;
    }

    private static int? ExtractInstanceIndex(string instance)
    {
        var match = InstanceIndexRegex.Match(instance);
        return match.Success ? int.Parse(match.Groups["idx"].Value) : null;
    }

    private static float? Max(float? a, float? b)
        => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

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

