using System.Diagnostics;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class ProcessTracker
{
    private Dictionary<int, string> _previous = new();
    private bool _initialized;

    public IReadOnlyList<ProcessEvent> Poll(DateTimeOffset now)
    {
        var current = CaptureMap();
        var events = new List<ProcessEvent>();

        if (_initialized)
        {
            foreach (var (pid, name) in current)
            {
                if (!_previous.TryGetValue(pid, out var oldName) || !string.Equals(oldName, name, StringComparison.OrdinalIgnoreCase))
                    events.Add(new ProcessEvent(now, "start", pid, name));
            }

            foreach (var (pid, name) in _previous)
            {
                if (!current.ContainsKey(pid))
                    events.Add(new ProcessEvent(now, "stop", pid, name));
            }
        }

        _previous = current;
        _initialized = true;
        return events;
    }

    public ProcessSnapshot CaptureSnapshot(DateTimeOffset now)
    {
        var map = CaptureMap();
        var items = map
            .Select(kv => new ProcessInfo(kv.Key, kv.Value))
            .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ProcessId)
            .ToArray();
        return new ProcessSnapshot(now, items);
    }

    private static Dictionary<int, string> CaptureMap()
    {
        var result = new Dictionary<int, string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                result[process.Id] = process.ProcessName;
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
}
