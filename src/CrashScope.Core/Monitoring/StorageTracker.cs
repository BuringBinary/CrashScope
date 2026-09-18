using System.Diagnostics;
using System.Management;
using LibreHardwareMonitor.Hardware;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class StorageTracker : IDisposable
{
    private readonly Computer? _storageComputer;
    private readonly UpdateVisitor _visitor = new();
    private readonly List<DiskCounters> _counters = new();
    private readonly Dictionary<int, string?> _models = new();
    private readonly List<string> _initialErrors = new();
    private bool _hardwareOpen;
    public bool IsAvailable => _hardwareOpen || _counters.Count > 0 || _models.Count > 0;
    public string HealthStatus { get; private set; } = "AwaitingSample";
    public string HealthDetail { get; private set; } = "Waiting for disk metrics.";

    public StorageTracker() : this(true) { }

    // Allows the read-only diagnostic harness to test Windows counters without opening hardware/driver APIs.
    internal StorageTracker(bool enableHardwareSensors)
    {
        if (enableHardwareSensors)
        {
            _storageComputer = new Computer
            {
                IsStorageEnabled = true, IsCpuEnabled = false, IsGpuEnabled = false,
                IsMemoryEnabled = false, IsMotherboardEnabled = false, IsNetworkEnabled = false,
                IsControllerEnabled = false, IsPsuEnabled = false
            };
            try { _storageComputer.Open(); _hardwareOpen = true; }
            catch (Exception ex) { _initialErrors.Add(Describe("LibreHardwareMonitor storage", ex)); }
        }
        // These sources are independent of LHM; failure or an empty LHM catalog must not suppress them.
        InitializeCounters();
        LoadModels();
    }

    private void InitializeCounters()
    {
        try
        {
            // LogicalDisk instances identify volumes, not physical drives. Never use them as physical indexes.
            if (!PerformanceCounterCategory.Exists("PhysicalDisk"))
            { _initialErrors.Add("Windows PhysicalDisk category is unavailable (including possible localized counter names)."); return; }
            foreach (var instance in new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames())
            {
                if (StorageSampleBuilder.InstanceIndex(instance) is not { } index) continue;
                var counters = new List<PerformanceCounter>();
                try
                {
                    foreach (var name in new[] { "% Disk Time", "Avg. Disk sec/Read", "Avg. Disk sec/Write", "Current Disk Queue Length" })
                    {
                        var counter = new PerformanceCounter("PhysicalDisk", name, instance, true);
                        counters.Add(counter);
                        counter.NextValue(); // Prime, validating the actual enumerated instance and counter access.
                    }
                    _counters.Add(new(index, counters.ToArray()));
                }
                catch (Exception ex)
                {
                    foreach (var counter in counters) counter.Dispose();
                    _initialErrors.Add(Describe($"PhysicalDisk {instance}", ex));
                }
            }
        }
        catch (Exception ex) { _initialErrors.Add(Describe("PhysicalDisk discovery", ex)); }
    }

    private void LoadModels()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            using var rows = searcher.Get();
            foreach (ManagementObject disk in rows)
            {
                using (disk)
                    if (int.TryParse(disk["Index"]?.ToString(), out var index))
                        _models[index] = disk["Model"]?.ToString()?.Trim();
            }
        }
        catch (Exception ex) { _initialErrors.Add(Describe("Win32_DiskDrive inventory", ex)); }
    }

    public StorageSample Capture(DateTimeOffset now)
    {
        var errors = new List<string>(_initialErrors);
        var hardware = new List<DiskMetrics>();
        if (_hardwareOpen && _storageComputer is not null)
        {
            try
            {
                _storageComputer.Accept(_visitor);
                foreach (var hw in _storageComputer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
                {
                    float? temp = null;
                    string? health = null;
                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.Value is not { } value || !float.IsFinite(value)) continue;
                        if (sensor.SensorType == SensorType.Temperature) temp = value;
                        if (sensor.SensorType == SensorType.Level && sensor.Name.Contains("Health", StringComparison.OrdinalIgnoreCase))
                            health = $"{value:0}%";
                    }
                    hardware.Add(new(hw.Name, null, temp, health, null, null, null, null,
                        SensorSource: "LibreHardwareMonitor"));
                }
            }
            catch (Exception ex) { errors.Add(Describe("LibreHardwareMonitor capture", ex)); }
        }
        var performance = new Dictionary<int, DiskPerformance>();
        foreach (var disk in _counters)
        {
            var values = new float?[4];
            for (var i = 0; i < 4; i++)
            {
                try
                {
                    var raw = disk.Counters[i].NextValue();
                    values[i] = StorageSampleBuilder.Valid(i is 1 or 2 ? raw * 1000f : raw);
                    if (!values[i].HasValue) errors.Add($"Disk {disk.Index} counter {i}: non-finite/negative reading.");
                }
                catch (Exception ex) { errors.Add(Describe($"Disk {disk.Index} counter {i}", ex)); }
            }
            performance.TryAdd(disk.Index, new(values[0], values[1], values[2], values[3]));
        }
        var sample = StorageSampleBuilder.Build(now, hardware, performance, _models);
        var hasMetrics = sample.Disks.Any(StorageSampleBuilder.HasMetrics);
        HealthStatus = hasMetrics ? errors.Count > 0 ? "Degraded" : "Collecting"
            : errors.Any(e => e.Contains("PermissionDenied", StringComparison.Ordinal)) ? "PermissionDenied"
            : errors.Count > 0 ? "Error" : "NoData";
        HealthDetail = $"PhysicalDisk instances={_counters.Count}; inventory={_models.Count}; LHM disks={hardware.Count}. " +
            (hasMetrics ? "Metrics received with per-disk attribution. " : "Inventory alone is not a disk measurement. ") +
            (hardware.Count == 0 ? "SMART temperature/health is unavailable; Windows I/O counters do not supply it. " : "") +
            string.Join(" | ", errors.Distinct().Take(8));
        return sample;
    }

    private static string Describe(string source, Exception ex) =>
        $"{source}: {(ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005) ? "PermissionDenied" : "Error")} " +
        $"{ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}";

    public void Dispose()
    {
        foreach (var disk in _counters) foreach (var counter in disk.Counters) counter.Dispose();
        _counters.Clear();
        _storageComputer?.Close();
        _hardwareOpen = false;
    }

    private sealed record DiskCounters(int Index, PerformanceCounter[] Counters);
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitParameter(IParameter parameter) { }
        public void VisitSensor(ISensor sensor) { }
    }
}
