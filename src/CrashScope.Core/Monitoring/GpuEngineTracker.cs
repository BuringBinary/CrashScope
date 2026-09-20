using System.Diagnostics;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class GpuEngineTracker : IDisposable
{
    private const string CategoryName = "GPU Engine";
    private const string CounterName = "Utilization Percentage";
    private const int NameCachePolls = 15;

    private readonly Dictionary<string, PerformanceCounter> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string?> _processNames = new();
    private readonly GpuAdapterIdentityResolver _adapterResolver = new();
    private int _nameCacheAge;
    private bool _disposed;

    public bool IsAvailable { get; }
    public string HealthStatus { get; private set; } = "AwaitingSample";
    public string HealthDetail { get; private set; } = "Waiting for GPU engine counters.";

    public GpuEngineTracker()
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

    public GpuEngineSample Capture(DateTimeOffset now)
    {
        if (_disposed)
            return Status(now, "Stopped", 0, "Tracker is disposed.");

        if (!IsAvailable)
            return Status(now, "Unavailable", 0, "GPU Engine performance-counter category is unavailable.");

        string[] instances;
        try
        {
            instances = new PerformanceCounterCategory(CategoryName).GetInstanceNames();
        }
        catch (Exception ex)
        {
            return Status(now, "Error", 0, $"Counter enumeration failed: {ex.GetType().Name}: {ex.Message}");
        }

        SyncCounters(instances);
        if (++_nameCacheAge >= NameCachePolls)
        {
            _nameCacheAge = 0;
            _processNames.Clear();
        }

        var byProcess = new Dictionary<int, Dictionary<EngineKey, float>>();
        foreach (var (instance, counter) in _counters)
        {
            if (!GpuCounterInstanceParser.TryParse(instance, out var identity)
                || string.IsNullOrWhiteSpace(identity.EngineType))
                continue;

            float value;
            try
            {
                value = counter.NextValue();
            }
            catch
            {
                // Instance vanished between enumeration and sampling.
                continue;
            }

            if (value < 0.5f)
                continue;

            if (!byProcess.TryGetValue(identity.ProcessId, out var engines))
            {
                engines = new Dictionary<EngineKey, float>();
                byProcess[identity.ProcessId] = engines;
            }
            var key = new EngineKey(identity.AdapterLuid, identity.PhysicalAdapterIndex,
                identity.EngineIndex, identity.EngineType);
            // Duplicate instances for the same physical engine are an enumeration
            // artifact. Preserve one engine percentage instead of summing adapters.
            engines[key] = Math.Max(engines.GetValueOrDefault(key), value);
        }

        if (byProcess.Count == 0)
            return Status(now, "Idle", instances.Length,
                $"Enumerated {instances.Length} counter instance(s); no engine exceeded the 0.5% activity threshold.");

        var processes = byProcess
            .Select(kv => new ProcessGpuUsage(
                kv.Key,
                ResolveProcessName(kv.Key),
                kv.Value
                    .OrderByDescending(e => e.Value)
                    .Select(e => new EngineUsage(e.Key.EngineType, e.Value, e.Key.Luid,
                        e.Key.PhysicalAdapterIndex, e.Key.EngineIndex, _adapterResolver.Resolve(e.Key.Luid)))
                    .ToList()))
            .OrderByDescending(p => p.Engines.Sum(e => e.UtilizationPercent))
            .ToList();

        HealthStatus = "Observed";
        HealthDetail = $"Captured {processes.Count} active process(es) from {instances.Length} counter instance(s).";
        return new GpuEngineSample(now, processes, HealthStatus, instances.Length);
    }

    private GpuEngineSample Status(DateTimeOffset now, string status, int instances, string detail)
    {
        HealthStatus = status;
        HealthDetail = detail;
        return new GpuEngineSample(now, [], status, instances, status == "Error" ? detail : null);
    }

    private void SyncCounters(string[] instances)
    {
        var live = new HashSet<string>(instances, StringComparer.Ordinal);

        foreach (var instance in _counters.Keys.Where(i => !live.Contains(i)).ToList())
        {
            _counters[instance].Dispose();
            _counters.Remove(instance);
        }

        foreach (var instance in instances)
        {
            if (_counters.ContainsKey(instance) || !GpuCounterInstanceParser.TryParse(instance, out var identity)
                || string.IsNullOrWhiteSpace(identity.EngineType))
                continue;

            try
            {
                _counters[instance] = new PerformanceCounter(CategoryName, CounterName, instance, readOnly: true);
            }
            catch
            {
                // Instance disappeared or is inaccessible: skip it.
            }
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
        catch
        {
            // Process exited between counter sampling and name lookup.
        }

        _processNames[pid] = name;
        return name;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var counter in _counters.Values)
            counter.Dispose();
        _counters.Clear();
        _processNames.Clear();
    }

    private readonly record struct EngineKey(
        string? Luid,
        int? PhysicalAdapterIndex,
        int? EngineIndex,
        string EngineType);
}
