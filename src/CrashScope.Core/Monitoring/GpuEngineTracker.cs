using System.Diagnostics;
using System.Text.RegularExpressions;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed partial class GpuEngineTracker
{
    private const string CategoryName = "GPU Engine";
    private const string CounterName = "Utilization Percentage";
    private const int NameCachePolls = 15;

    private readonly Dictionary<string, PerformanceCounter> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string?> _processNames = new();
    private int _nameCacheAge;

    public bool IsAvailable { get; }

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

    public GpuEngineSample? Capture(DateTimeOffset now)
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

        var byProcess = new Dictionary<int, Dictionary<string, float>>();
        foreach (var (instance, counter) in _counters)
        {
            var match = InstancePattern().Match(instance);
            if (!match.Success)
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

            var pid = int.Parse(match.Groups[1].Value);
            var engineType = match.Groups[2].Value;
            if (!byProcess.TryGetValue(pid, out var engines))
            {
                engines = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                byProcess[pid] = engines;
            }
            engines[engineType] = engines.GetValueOrDefault(engineType) + value;
        }

        if (byProcess.Count == 0)
            return null;

        var processes = byProcess
            .Select(kv => new ProcessGpuUsage(
                kv.Key,
                ResolveProcessName(kv.Key),
                kv.Value
                    .OrderByDescending(e => e.Value)
                    .Select(e => new EngineUsage(e.Key, e.Value))
                    .ToList()))
            .OrderByDescending(p => p.Engines.Sum(e => e.UtilizationPercent))
            .ToList();

        return new GpuEngineSample(now, processes);
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
            if (_counters.ContainsKey(instance) || !InstancePattern().IsMatch(instance))
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

    [GeneratedRegex(@"^pid_(\d+)_.+engtype_(.+)$")]
    private static partial Regex InstancePattern();
}
