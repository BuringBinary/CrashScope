using System.Diagnostics;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class SelfOverheadTracker
{
    private static readonly string[] HostNames = ["CrashScope.Desktop", "CrashScope.Agent", "CrashScope.Service"];
    private readonly int _processorCount;
    private readonly Func<IReadOnlyList<SelfProcessReading>> _capture;
    private readonly Dictionary<ProcessIdentity, CpuBaseline> _processBaselines = new();
    private readonly Dictionary<Guid, EtwBaseline> _etwBaselines = new();

    public SelfOverheadTracker() : this(Environment.ProcessorCount, CaptureProcesses) { }
    internal SelfOverheadTracker(int processorCount, Func<IReadOnlyList<SelfProcessReading>> capture)
    {
        _processorCount = Math.Max(1, processorCount);
        _capture = capture;
    }

    public CollectorOverheadSample Capture(DateTimeOffset now, IReadOnlyList<EtwCoverageSnapshot> coverage)
    {
        var readings = _capture();
        var live = new HashSet<ProcessIdentity>();
        var processes = new List<CollectorProcessMetrics>();
        foreach (var reading in readings.OrderBy(r => r.ProcessId))
        {
            var identity = new ProcessIdentity(reading.ProcessId, reading.StartTime?.UtcTicks ?? 0);
            live.Add(identity);
            float? cpu = null;
            if (reading.TotalProcessorTime is TimeSpan total
                && _processBaselines.TryGetValue(identity, out var previous))
                cpu = CalculateCpuPercent(previous.TotalProcessorTime, total, previous.Timestamp, now, _processorCount);
            if (reading.TotalProcessorTime is TimeSpan current)
                _processBaselines[identity] = new CpuBaseline(current, now);
            processes.Add(new CollectorProcessMetrics(reading.ProcessId, reading.ProcessName, reading.StartTime,
                cpu, reading.PrivateBytes, reading.WorkingSetBytes, reading.HandleCount, reading.ThreadCount));
        }
        foreach (var key in _processBaselines.Keys.Where(k => !live.Contains(k)).ToArray())
            _processBaselines.Remove(key);

        var etw = coverage.Select(item => BuildEtwRate(item, now)).ToArray();
        var cpuValues = processes.Where(p => p.CpuPercent.HasValue).Select(p => p.CpuPercent!.Value).ToArray();
        return new CollectorOverheadSample(now, processes,
            cpuValues.Length == 0 ? null : cpuValues.Sum(),
            processes.Sum(p => p.PrivateBytes ?? 0), processes.Sum(p => p.WorkingSetBytes ?? 0),
            GC.GetTotalMemory(forceFullCollection: false), _processorCount, etw);
    }

    internal static float? CalculateCpuPercent(TimeSpan previousCpu, TimeSpan currentCpu,
        DateTimeOffset previousAt, DateTimeOffset currentAt, int processorCount)
    {
        var elapsed = (currentAt - previousAt).TotalSeconds;
        var cpuSeconds = (currentCpu - previousCpu).TotalSeconds;
        if (elapsed <= 0 || cpuSeconds < 0 || processorCount <= 0) return null;
        return (float)Math.Clamp(cpuSeconds / elapsed / processorCount * 100.0, 0, 100);
    }

    private EtwRateMetrics BuildEtwRate(EtwCoverageSnapshot item, DateTimeOffset now)
    {
        double? received = null, matched = null, written = null;
        if (_etwBaselines.TryGetValue(item.ProviderId, out var old))
        {
            var seconds = (now - old.Timestamp).TotalSeconds;
            if (seconds > 0 && item.ReceivedEvents >= old.Received && item.MatchedEvents >= old.Matched && item.WrittenEvents >= old.Written)
            {
                received = (item.ReceivedEvents - old.Received) / seconds;
                matched = (item.MatchedEvents - old.Matched) / seconds;
                written = (item.WrittenEvents - old.Written) / seconds;
            }
        }
        _etwBaselines[item.ProviderId] = new EtwBaseline(now, item.ReceivedEvents, item.MatchedEvents, item.WrittenEvents);
        return new EtwRateMetrics(item.Provider, item.ProviderId, received, matched, written,
            item.ReceivedEvents, item.MatchedEvents, item.WrittenEvents, item.BufferDroppedEvents, item.OsLostEvents);
    }

    private static IReadOnlyList<SelfProcessReading> CaptureProcesses()
    {
        var processes = new Dictionary<int, Process>();
        void Add(Process process)
        {
            if (processes.TryAdd(process.Id, process)) return;
            process.Dispose();
        }

        Add(Process.GetCurrentProcess());
        foreach (var name in HostNames)
            foreach (var process in Process.GetProcessesByName(name))
                Add(process);

        var result = new List<SelfProcessReading>();
        foreach (var process in processes.Values)
        {
            try
            {
                result.Add(new SelfProcessReading(process.Id, Try(() => process.ProcessName) ?? $"PID {process.Id}",
                    Try(() => new DateTimeOffset(process.StartTime)), Try(() => process.TotalProcessorTime),
                    Try(() => process.PrivateMemorySize64), Try(() => process.WorkingSet64),
                    Try(() => process.HandleCount), Try(() => process.Threads.Count)));
            }
            catch { }
            finally { process.Dispose(); }
        }
        return result;
    }

    private static T? Try<T>(Func<T> read) where T : struct
    {
        try { return read(); } catch { return null; }
    }
    private static string? Try(Func<string> read)
    {
        try { return read(); } catch { return null; }
    }

    internal sealed record SelfProcessReading(int ProcessId, string ProcessName, DateTimeOffset? StartTime,
        TimeSpan? TotalProcessorTime, long? PrivateBytes, long? WorkingSetBytes, int? HandleCount, int? ThreadCount);
    private readonly record struct ProcessIdentity(int ProcessId, long StartTicks);
    private readonly record struct CpuBaseline(TimeSpan TotalProcessorTime, DateTimeOffset Timestamp);
    private readonly record struct EtwBaseline(DateTimeOffset Timestamp, long Received, long Matched, long Written);
}
