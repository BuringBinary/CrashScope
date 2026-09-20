using System.Diagnostics;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class ProcessTracker
{
    private readonly Func<IReadOnlyList<ProcessInfo>> _capture;
    private Dictionary<int, ProcessInfo> _previous = new();
    private bool _initialized;

    public ProcessTracker() : this(CaptureProcesses) { }

    internal ProcessTracker(Func<IReadOnlyList<ProcessInfo>> capture)
        => _capture = capture ?? throw new ArgumentNullException(nameof(capture));

    public IReadOnlyList<ProcessEvent> Poll(DateTimeOffset now)
    {
        var current = CaptureMap();
        var events = new List<ProcessEvent>();

        if (_initialized)
        {
            foreach (var (pid, process) in current)
            {
                if (_previous.TryGetValue(pid, out var previous))
                {
                    // Creation time is the stable generation identity. Keep the name
                    // fallback for protected processes where StartTime is unavailable.
                    if (!SameGeneration(previous, process))
                    {
                        events.Add(ToEvent(now, "stop", previous, "pid-reused"));
                        events.Add(ToEvent(now, "start", process, "running"));
                    }
                }
                else
                {
                    events.Add(ToEvent(now, "start", process, "running"));
                }
            }

            foreach (var (pid, process) in _previous)
            {
                if (!current.ContainsKey(pid))
                    events.Add(ToEvent(now, "stop", process, "exited-or-inaccessible"));
            }
        }

        _previous = current;
        _initialized = true;
        return events;
    }

    public ProcessSnapshot CaptureSnapshot(DateTimeOffset now)
    {
        var items = _capture()
            .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ProcessId)
            .ToArray();
        return new ProcessSnapshot(now, items);
    }

    private Dictionary<int, ProcessInfo> CaptureMap()
        => _capture().GroupBy(p => p.ProcessId).ToDictionary(g => g.Key, g => g.First());

    internal static bool SameGeneration(ProcessInfo previous, ProcessInfo current)
    {
        if (!string.Equals(previous.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase))
            return false;
        return previous.StartTime is null || current.StartTime is null || previous.StartTime == current.StartTime;
    }

    private static ProcessEvent ToEvent(DateTimeOffset now, string kind, ProcessInfo process, string state)
        => new(now, kind, process.ProcessId, process.ProcessName, process.StartTime, process.PrivateBytes,
            process.WorkingSetBytes, process.HandleCount, process.ExitCode, state);

    private static IReadOnlyList<ProcessInfo> CaptureProcesses()
    {
        var result = new List<ProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                DateTimeOffset? startTime = Try(() => new DateTimeOffset(process.StartTime));
                var privateBytes = Try(() => process.PrivateMemorySize64);
                var workingSetBytes = Try(() => process.WorkingSet64);
                var handleCount = Try(() => process.HandleCount);
                var hasExited = Try(() => process.HasExited);
                int? exitCode = hasExited == true ? Try(() => process.ExitCode) : null;
                result.Add(new ProcessInfo(process.Id, name, startTime, privateBytes, workingSetBytes,
                    handleCount, hasExited, exitCode));
            }
            catch
            {
                // Access denied / race with process exit: skip it.
            }
            finally
            {
                process.Dispose();
            }
        }
        return result;
    }

    private static T? Try<T>(Func<T> read) where T : struct
    {
        try { return read(); }
        catch { return null; }
    }
}
