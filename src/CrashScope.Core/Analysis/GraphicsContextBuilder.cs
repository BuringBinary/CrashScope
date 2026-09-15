using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

internal static class GraphicsContextBuilder
{
    public static GraphicsContext Build(
        string incidentDirectory,
        IReadOnlyList<WindowsEventEvidence> events,
        IReadOnlyList<ProcessEvent> processEvents,
        IReadOnlyList<ProcessSnapshot> processSnapshots)
    {
        var display = IncidentAnalyzer.ReadJsonLines<DisplayTopologyEvent>(
            Path.Combine(incidentDirectory, "display-events-tail.jsonl"));
        var power = IncidentAnalyzer.ReadJsonLines<PowerSchemeEvent>(
            Path.Combine(incidentDirectory, "power-events-tail.jsonl"));
        var remote = IncidentAnalyzer.ReadJsonLines<RemoteSessionEvent>(
            Path.Combine(incidentDirectory, "remote-session-events-tail.jsonl"));
        var gpuEngines = IncidentAnalyzer.ReadJsonLines<GpuEngineSample>(
            Path.Combine(incidentDirectory, "gpu-engines-tail.jsonl"));
        var drivers = IncidentAnalyzer.ReadJsonFile<DriverInventoryEntry[]>(
            Path.Combine(incidentDirectory, "driver-inventory.json"));

        return new GraphicsContext(
            BuildAppPresence(processEvents, processSnapshots),
            display,
            power,
            remote,
            drivers ?? [],
            events.Where(IsDriverChangeEvent).ToList(),
            events.Where(IsRemoteLogonEvent).ToList(),
            gpuEngines.Count > 0 ? gpuEngines[^1] : null);
    }

    private static bool IsDriverChangeEvent(WindowsEventEvidence evidence)
        => evidence.Provider?.Contains("UserPnp", StringComparison.OrdinalIgnoreCase) == true
            && evidence.Id is 20001 or 20002 or 20003;

    private static bool IsRemoteLogonEvent(WindowsEventEvidence evidence)
        => evidence.Provider?.Contains("TerminalServices", StringComparison.OrdinalIgnoreCase) == true;

    private static List<GraphicsAppPresence> BuildAppPresence(
        IReadOnlyList<ProcessEvent> processEvents,
        IReadOnlyList<ProcessSnapshot> processSnapshots)
    {
        // name -> (category, first seen, last seen, was seen in the final snapshot)
        var tracker = new Dictionary<string, (GraphicsAppCategory Category, DateTimeOffset? FirstSeen, DateTimeOffset? LastSeen, bool InFinalSnapshot)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in processSnapshots)
        {
            var isFinal = ReferenceEquals(snapshot, processSnapshots[^1]);
            foreach (var process in snapshot.Processes)
            {
                var category = GraphicsAppCatalog.Classify(process.ProcessName);
                if (category is null)
                    continue;

                var current = tracker.GetValueOrDefault(process.ProcessName);
                tracker[process.ProcessName] = (
                    category.Value,
                    current.FirstSeen ?? snapshot.Timestamp,
                    snapshot.Timestamp,
                    current.InFinalSnapshot || isFinal);
            }
        }

        foreach (var processEvent in processEvents)
        {
            var category = GraphicsAppCatalog.Classify(processEvent.ProcessName);
            if (category is null)
                continue;

            var current = tracker.GetValueOrDefault(processEvent.ProcessName);
            var seen = processEvent.Timestamp;
            var isStop = string.Equals(processEvent.Event, "stop", StringComparison.OrdinalIgnoreCase);

            tracker[processEvent.ProcessName] = isStop
                ? (category.Value, current.FirstSeen ?? seen, seen, current.InFinalSnapshot)
                : (category.Value, current.FirstSeen ?? seen, current.LastSeen, current.InFinalSnapshot);
        }

        // A stop event after the final snapshot means the app was running at the end.
        var finalSnapshotTime = processSnapshots.Count > 0 ? processSnapshots[^1].Timestamp : DateTimeOffset.MinValue;
        var lastEventByProcess = processEvents
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        return tracker
            .Select(kv =>
            {
                var running = kv.Value.InFinalSnapshot;
                if (lastEventByProcess.TryGetValue(kv.Key, out var lastEvent) &&
                    lastEvent.Timestamp >= finalSnapshotTime &&
                    !string.Equals(lastEvent.Event, "stop", StringComparison.OrdinalIgnoreCase))
                {
                    running = true;
                }

                return new GraphicsAppPresence(
                    kv.Key,
                    kv.Value.Category,
                    kv.Value.FirstSeen,
                    kv.Value.LastSeen,
                    running);
            })
            .OrderBy(a => a.Category)
            .ThenBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
