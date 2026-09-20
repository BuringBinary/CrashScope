using System.Diagnostics;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class GpuMemoryTracker : IDisposable
{
    private const string CategoryName = "GPU Process Memory";
    private const string DedicatedCounter = "Dedicated Usage";
    private const string SharedCounter = "Shared Usage";
    private const int NameCachePolls = 8;

    private readonly Dictionary<string, PerformanceCounter> _dedicatedCounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PerformanceCounter> _sharedCounters = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string?> _processNames = new();
    private readonly GpuAdapterIdentityResolver _adapterResolver = new();
    private int _nameCacheAge;

    public bool IsAvailable { get; }

    public GpuMemoryTracker()
    {
        try
        {
            IsAvailable = PerformanceCounterCategory.Exists(CategoryName);
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public GpuMemorySample? Capture(DateTimeOffset now)
    {
        if (!IsAvailable)
            return null;

        string[] instances;
        try
        {
            instances = new PerformanceCounterCategory(CategoryName).GetInstanceNames();
        }
        catch
        {
            return null;
        }

        SyncCounters(instances);
        if (++_nameCacheAge >= NameCachePolls)
        {
            _nameCacheAge = 0;
            _processNames.Clear();
        }

        var byProcess = new Dictionary<int, Dictionary<AdapterKey, AdapterTotals>>();
        foreach (var (instance, dedicatedCtr) in _dedicatedCounters)
        {
            if (!GpuCounterInstanceParser.TryParse(instance, out var identity))
                continue;

            float dedicatedRaw;
            try
            {
                dedicatedRaw = dedicatedCtr.NextValue();
            }
            catch
            {
                continue;
            }

            long dedicatedBytes = dedicatedRaw > 0 ? (long)dedicatedRaw : 0;

            long sharedBytes = 0;
            if (_sharedCounters.TryGetValue(instance, out var sharedCtr))
            {
                try
                {
                    var sharedRaw = sharedCtr.NextValue();
                    sharedBytes = sharedRaw > 0 ? (long)sharedRaw : 0;
                }
                catch { }
            }

            var totalBytes = dedicatedBytes + sharedBytes;
            if (totalBytes <= 0)
                continue;

            if (!byProcess.TryGetValue(identity.ProcessId, out var adapters))
                byProcess[identity.ProcessId] = adapters = new();
            var key = new AdapterKey(identity.AdapterLuid, identity.PhysicalAdapterIndex);
            var existing = adapters.GetValueOrDefault(key);
            adapters[key] = new AdapterTotals(existing.Dedicated + dedicatedBytes, existing.Shared + sharedBytes);
        }

        if (byProcess.Count == 0)
            return null;

        var processes = byProcess
            .Select(kv =>
            {
                var adapters = kv.Value
                    .Select(a => new GpuAdapterMemory(a.Key.Luid, a.Key.PhysicalAdapterIndex,
                        _adapterResolver.Resolve(a.Key.Luid), a.Value.Dedicated, a.Value.Shared))
                    .OrderBy(a => a.PhysicalAdapterIndex ?? int.MaxValue)
                    .ThenBy(a => a.AdapterLuid, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new ProcessGpuMemory(kv.Key, ResolveProcessName(kv.Key),
                    adapters.Sum(a => a.DedicatedBytes), adapters.Sum(a => a.SharedBytes), adapters);
            })
            .Where(p => p.DedicatedBytes + p.SharedBytes > 0)
            .OrderByDescending(p => p.DedicatedBytes + p.SharedBytes)
            .ToList();

        return processes.Count > 0 ? new GpuMemorySample(now, processes) : null;
    }

    private void SyncCounters(string[] instances)
    {
        var live = new HashSet<string>(instances, StringComparer.Ordinal);

        foreach (var instance in _dedicatedCounters.Keys.Where(i => !live.Contains(i)).ToList())
        {
            _dedicatedCounters[instance].Dispose();
            _dedicatedCounters.Remove(instance);
            if (_sharedCounters.TryGetValue(instance, out var sc))
            {
                sc.Dispose();
                _sharedCounters.Remove(instance);
            }
        }

        foreach (var instance in instances)
        {
            if (_dedicatedCounters.ContainsKey(instance) || !GpuCounterInstanceParser.TryParse(instance, out _))
                continue;

            try
            {
                _dedicatedCounters[instance] = new PerformanceCounter(CategoryName, DedicatedCounter, instance, readOnly: true);
            }
            catch { }

            try
            {
                _sharedCounters[instance] = new PerformanceCounter(CategoryName, SharedCounter, instance, readOnly: true);
            }
            catch { }
        }
    }

    private string? ResolveProcessName(int pid)
    {
        if (_processNames.TryGetValue(pid, out var cached))
            return cached;

        string? name = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch { }

        _processNames[pid] = name;
        return name;
    }

    public void Dispose()
    {
        foreach (var c in _dedicatedCounters.Values) c.Dispose();
        foreach (var c in _sharedCounters.Values) c.Dispose();
        _dedicatedCounters.Clear();
        _sharedCounters.Clear();
        _processNames.Clear();
    }

    private readonly record struct AdapterKey(string? Luid, int? PhysicalAdapterIndex);
    private readonly record struct AdapterTotals(long Dedicated, long Shared);
}