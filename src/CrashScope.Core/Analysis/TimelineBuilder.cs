using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public static class TimelineBuilder
{
    public static IReadOnlyList<TimelineEntry> Build(
        SessionState previous,
        IReadOnlyList<SensorCatalogEntry>? catalog,
        IReadOnlyList<TelemetrySnapshot> telemetry,
        IReadOnlyList<ForegroundEvent> foreground,
        IReadOnlyList<ProcessEvent> processEvents,
        IReadOnlyList<WindowsEventEvidence> events,
        IReadOnlyList<ExternalEvidenceFile> external,
        IReadOnlyList<SensorTransition> transitions,
        GraphicsContext? graphics,
        IReadOnlyList<EtwDxgKrnlEvent>? etwEvents,
        IReadOnlyList<EtwProviderEvent>? etwKernelPower,
        IReadOnlyList<EtwProviderEvent>? etwWhea)
    {
        var entries = new List<(DateTimeOffset Timestamp, string Category, string Description)>();

        AddSessionBoundaries(entries, previous);

        foreach (var snapshot in telemetry)
            AddTelemetryEntry(entries, snapshot);

        foreach (var fg in foreground)
            entries.Add((fg.Timestamp, "Foreground", $"Foreground: {fg.ProcessName ?? "(idle)"} (PID {fg.ProcessId?.ToString() ?? "n/a"})"));

        foreach (var pe in processEvents)
            entries.Add((pe.Timestamp, "Process", $"{pe.Event}: {pe.ProcessName} (PID {pe.ProcessId})"));

        foreach (var evt in events)
        {
            if (evt.TimeCreated.HasValue)
            {
                var prefix = evt.Level?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true ? "ERROR" :
                             evt.Level?.Contains("Warning", StringComparison.OrdinalIgnoreCase) == true ? "WARN" : "INFO";
                var summary = TruncateSummary(evt.Message, 120);
                entries.Add((evt.TimeCreated.Value, "EventLog", $"[{prefix}] {evt.Provider} (ID {evt.Id}): {summary}"));
            }
        }

        foreach (var ext in external)
            entries.Add((ext.Timestamp, "External", $"{ext.Kind}: {ext.Detail ?? Path.GetFileName(ext.Path)} ({ext.SizeBytes} bytes)"));

        foreach (var t in transitions)
            entries.Add((t.Timestamp, "GPU", $"{t.SensorName}: {t.FromValue:F1} → {t.ToValue:F1} [{t.Description}]"));

        if (etwEvents is not null)
        {
            foreach (var etw in etwEvents)
            {
                var detail = etw.Detail is not null ? $" ({TruncateSummary(etw.Detail, 80)})" : "";
                var proc = etw.ProcessName is not null ? $" [{etw.ProcessName}]" : "";
                entries.Add((etw.Timestamp, "ETW-GPU", $"{etw.EventName} (ID {etw.EventId}){proc}{detail}"));
            }
        }

        if (etwKernelPower is not null)
        {
            foreach (var kp in etwKernelPower)
            {
                var detail = kp.Detail is not null ? $" ({TruncateSummary(kp.Detail, 80)})" : "";
                var extra = kp.Level is not null ? $" [{kp.Level}]" : "";
                entries.Add((kp.Timestamp, "ETW-Power", $"{kp.EventName}{extra}{detail}"));
            }
        }

        if (etwWhea is not null)
        {
            foreach (var wh in etwWhea)
            {
                var detail = wh.Detail is not null ? $" ({TruncateSummary(wh.Detail, 80)})" : "";
                var extra = wh.Level is not null ? $" [{wh.Level}]" : "";
                entries.Add((wh.Timestamp, "ETW-WHEA", $"{wh.EventName}{extra}{detail}"));
            }
        }

        if (graphics is not null)
        {
            foreach (var change in graphics.DisplayChanges)
                entries.Add((change.Timestamp, "Display", $"{change.Change}: {change.Description}"));

            foreach (var ps in graphics.PowerSchemeChanges)
                entries.Add((ps.Timestamp, "Power", $"{ps.Change}: {ps.SchemeName} ({ps.Description})"));

            foreach (var rs in graphics.RemoteSessionChanges)
                entries.Add((rs.Timestamp, "Remote", rs.Description));

            if (graphics.FinalGpuEngines is not null)
            {
                var topProcesses = graphics.FinalGpuEngines.Processes
                    .Where(p => p.Engines.Any(e => e.UtilizationPercent > 5))
                    .Take(5)
                    .ToList();
                foreach (var p in topProcesses)
                {
                    var engines = string.Join(", ", p.Engines.Select(e => $"{e.EngineType}:{e.UtilizationPercent:F0}%"));
                    entries.Add((graphics.FinalGpuEngines.Timestamp, "GPUEngines",
                        $"{p.ProcessName ?? $"PID {p.ProcessId}"}: {engines}"));
                }
            }
        }

        return entries
            .OrderBy(e => e.Timestamp)
            .Select(e => new TimelineEntry(e.Timestamp, e.Category, e.Description))
            .ToList();
    }

    private static void AddSessionBoundaries(
        List<(DateTimeOffset, string, string)> entries,
        SessionState previous)
    {
        entries.Add((previous.StartedAt, "Session", $"Session started (elevated: {previous.Elevated})"));
        entries.Add((previous.LastHeartbeatAt, "Session", $"Last heartbeat recorded (clean: {previous.CleanShutdown})"));
    }

    private static void AddTelemetryEntry(
        List<(DateTimeOffset, string, string)> entries,
        TelemetrySnapshot snapshot)
    {
        var parts = new List<string>();

        var nonNullValues = snapshot.SensorValues
            .Select((v, i) => (Index: i, Value: v))
            .Where(x => x.Value.HasValue)
            .ToList();

        if (nonNullValues.Count >= 3)
        {
            var cpu = nonNullValues.FirstOrDefault(x => x.Value > 0 && x.Value <= 100);
            if (cpu != default)
                parts.Add($"CPU:{cpu.Value:F0}%");

            var ram = nonNullValues.LastOrDefault(x => x.Value > 100);
            if (ram != default)
                parts.Add($"RAM:{ram.Value / 1024f:F1}GB");
        }

        if (parts.Count == 0)
            return;

        var fg = snapshot.Foreground.ProcessName ?? "idle";
        entries.Add((snapshot.Timestamp, "Telemetry", $"{fg} | {string.Join(" ", parts)}"));
    }

    private static string TruncateSummary(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(no details)";

        var clean = text.Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length <= maxLength ? clean : clean[..(maxLength - 3)] + "...";
    }
}