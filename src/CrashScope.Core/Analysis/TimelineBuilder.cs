using CrashScope.Core.Models;
using System.Globalization;

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
        IReadOnlyList<EtwDxgKrnlEvent>? etwEvents = null,
        IReadOnlyList<EtwProviderEvent>? etwKernelPower = null,
        IReadOnlyList<EtwProviderEvent>? etwWhea = null,
        IReadOnlyList<GpuMemorySample>? gpuMemoryEvents = null,
        IReadOnlyList<MemoryPressureSample>? memoryPressureEvents = null,
        IReadOnlyList<DwmStateEvent>? dwmStateEvents = null)
    {
        var entries = new List<(DateTimeOffset Timestamp, string Category, string Description)>();

        AddSessionBoundaries(entries, previous);

        foreach (var snapshot in telemetry)
            AddTelemetryEntry(entries, snapshot, catalog);

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

        if (gpuMemoryEvents is not null)
        {
            foreach (var gm in gpuMemoryEvents)
            {
                var top = gm.Processes
                    .OrderByDescending(p => p.DedicatedBytes + p.SharedBytes)
                    .Take(3);
                foreach (var p in top)
                {
                    var dedicatedGb = p.DedicatedBytes / (1024.0 * 1024.0 * 1024.0);
                    var sharedMb = p.SharedBytes / (1024.0 * 1024.0);
                    entries.Add((gm.Timestamp, "GPU-VRAM",
                        $"{p.ProcessName ?? $"PID {p.ProcessId}"}: {dedicatedGb:F2} GB dedicated + {sharedMb:F0} MB shared"));
                }
            }
        }

        if (memoryPressureEvents is not null)
        {
            foreach (var mp in memoryPressureEvents)
            {
                if (mp.CommittedPercent >= 80 || mp.AvailableBytes < 1024L * 1024 * 1024)
                {
                    var availGb = mp.AvailableBytes / (1024.0 * 1024.0 * 1024.0);
                    entries.Add((mp.Timestamp, "Memory",
                        $"Commit: {mp.CommittedPercent:F0}%, Available: {availGb:F2} GB"));
                }
            }
        }

        if (dwmStateEvents is not null)
        {
            foreach (var dw in dwmStateEvents)
            {
                // IsHardwareRendering is derived from the composition flag (see
                // DwmStateTracker), so printing the two as independent measurements
                // would overstate what was actually observed.
                var dwmState = dw.IsCompositionEnabled
                    ? "hardware composition active"
                    : "composition disabled - fell back to software rendering";
                entries.Add((dw.Timestamp, "DWM", $"DWM {dwmState}"));
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
        TelemetrySnapshot snapshot,
        IReadOnlyList<SensorCatalogEntry>? catalog)
    {
        // Sensor values are positional, so labelling them by value shape is unsafe: the old
        // heuristic reported the first value in [0,100] as "CPU" (GPU load qualifies) and the
        // last value above 100 as "RAM" in MB (a temperature in Celsius qualifies). In a
        // forensic timeline those labels can simply be wrong, so resolve them against the
        // session's sensor catalog instead.
        if (catalog is null || catalog.Count == 0)
            return;

        var cpuIndex = SensorIndices.CpuTotalLoad(catalog);
        var gpuIndex = SensorIndices.GpuCoreLoad(catalog);
        var usedIndex = SensorIndices.MemoryUsed(catalog);
        var availableIndex = SensorIndices.MemoryAvailable(catalog);

        var parts = new List<string>(3);
        AddPart(parts, snapshot, cpuIndex, "CPU", "F0", "%");
        AddPart(parts, snapshot, gpuIndex, "GPU", "F0", "%");

        var used = ValueAt(snapshot, usedIndex);
        var available = ValueAt(snapshot, availableIndex);
        if (used is not null && available is not null)
            parts.Add($"RAM:{used.Value:F1}/{used.Value + available.Value:F1}GB");
        else if (used is not null)
            parts.Add($"RAM:{used.Value:F1}GB");

        if (parts.Count == 0)
            return;

        var foreground = snapshot.Foreground.ProcessName ?? "idle";
        entries.Add((snapshot.Timestamp, "Telemetry", $"{foreground} | {string.Join(" ", parts)}"));
    }

    private static float? ValueAt(TelemetrySnapshot snapshot, int? index)
        => index is int i && i >= 0 && i < snapshot.SensorValues.Count
            ? snapshot.SensorValues[i]
            : null;

    private static void AddPart(
        List<string> parts,
        TelemetrySnapshot snapshot,
        int? index,
        string label,
        string format,
        string suffix)
    {
        if (ValueAt(snapshot, index) is float value)
            parts.Add($"{label}:{value.ToString(format, CultureInfo.InvariantCulture)}{suffix}");
    }

    private static string TruncateSummary(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(no details)";

        var clean = text.Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length <= maxLength ? clean : clean[..(maxLength - 3)] + "...";
    }
}