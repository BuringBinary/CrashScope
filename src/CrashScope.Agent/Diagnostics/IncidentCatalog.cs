using System.Globalization;
using System.Text.Json;
using CrashScope.Agent.Models;
using CrashScope.Contracts;

namespace CrashScope.Agent.Diagnostics;

public static class IncidentCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<IncidentSummaryDto> List(string incidentsRoot)
    {
        if (!Directory.Exists(incidentsRoot))
            return Array.Empty<IncidentSummaryDto>();

        var output = new List<IncidentSummaryDto>();
        foreach (var directory in Directory.EnumerateDirectories(incidentsRoot)
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                output.Add(ReadSummary(directory));
            }
            catch
            {
                // One damaged incident should not make the entire catalog unavailable.
            }
        }

        return output;
    }

    public static IncidentDetailDto? ReadDetail(string incidentsRoot, string incidentId)
    {
        if (string.IsNullOrWhiteSpace(incidentId) || !Directory.Exists(incidentsRoot))
            return null;

        var safeId = Path.GetFileName(incidentId);
        if (!string.Equals(safeId, incidentId, StringComparison.Ordinal))
            return null;

        var root = Path.GetFullPath(incidentsRoot);
        var directory = Path.GetFullPath(Path.Combine(root, safeId));
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(directory))
            return null;

        var summary = ReadSummary(directory);
        var telemetry = ReadJsonLines<TelemetrySnapshot>(Path.Combine(directory, "telemetry-tail.jsonl"));
        var foreground = ReadJsonLines<ForegroundEvent>(Path.Combine(directory, "foreground-tail.jsonl"));
        var processEvents = ReadJsonLines<ProcessEvent>(Path.Combine(directory, "process-events-tail.jsonl"));
        var windowsEvents = ReadJsonFile<List<WindowsEventEvidence>>(Path.Combine(directory, "windows-events.json")) ?? [];
        var catalog = ReadJsonFile<List<SensorCatalogEntry>>(Path.Combine(directory, "sensor-catalog.json"));

        var metrics = BuildMetricSeries(telemetry, catalog);
        var timeline = BuildTimeline(summary, telemetry, foreground, processEvents, windowsEvents, catalog);

        DateTimeOffset? windowStart = timeline.Count > 0 ? timeline[0].Timestamp : summary.LastHeartbeatAt;
        DateTimeOffset? windowEnd = timeline.Count > 0 ? timeline[^1].Timestamp : summary.LastHeartbeatAt;

        return new IncidentDetailDto(
            Summary: summary,
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            SensorCatalogAvailable: catalog is { Count: > 0 },
            Timeline: timeline,
            Metrics: metrics);
    }

    private static IncidentSummaryDto ReadSummary(string directory)
    {
        var id = Path.GetFileName(directory);
        var summaryPath = Path.Combine(directory, "summary.md");
        var lines = File.Exists(summaryPath) ? File.ReadAllLines(summaryPath) : Array.Empty<string>();

        return new IncidentSummaryDto(
            Id: id,
            LastHeartbeatAt: ParseTimestamp(lines, "- Last heartbeat:"),
            SessionId: ParseBacktickValue(lines, "- Previous session:"),
            KernelPower41: ParseBool(lines, "- Kernel-Power 41 found:"),
            UnexpectedShutdown6008: ParseBool(lines, "- Unexpected shutdown 6008 found:"),
            WheaEvidence: ParseBool(lines, "- WHEA evidence found:"),
            DisplayTdrEvidence: ParseBool(lines, "- Display/TDR evidence found:"),
            DirectoryPath: directory);
    }

    private static IReadOnlyList<IncidentTimelineEventDto> BuildTimeline(
        IncidentSummaryDto summary,
        IReadOnlyList<TelemetrySnapshot> telemetry,
        IReadOnlyList<ForegroundEvent> foreground,
        IReadOnlyList<ProcessEvent> processEvents,
        IReadOnlyList<WindowsEventEvidence> windowsEvents,
        IReadOnlyList<SensorCatalogEntry>? catalog)
    {
        var timeline = new List<IncidentTimelineEventDto>();

        for (var index = 0; index < telemetry.Count; index++)
        {
            if (index % 10 != 0 && index != telemetry.Count - 1)
                continue;

            var sample = telemetry[index];
            timeline.Add(new IncidentTimelineEventDto(
                sample.Timestamp,
                "Telemetry",
                "Telemetry sample",
                BuildTelemetryDetail(sample, catalog),
                "Info"));
        }

        foreach (var item in foreground)
        {
            var title = string.IsNullOrWhiteSpace(item.ProcessName) ? "Desktop / unknown" : item.ProcessName;
            var detail = item.ProcessId is { } pid
                ? $"Foreground changed · PID {pid} · idle {FormatDuration(item.IdleSeconds)}"
                : $"Foreground changed · idle {FormatDuration(item.IdleSeconds)}";
            timeline.Add(new IncidentTimelineEventDto(item.Timestamp, "Foreground", title, detail, "Info"));
        }

        foreach (var item in processEvents)
        {
            var action = string.Equals(item.Event, "started", StringComparison.OrdinalIgnoreCase) ? "started" :
                string.Equals(item.Event, "stopped", StringComparison.OrdinalIgnoreCase) ? "stopped" : item.Event;
            timeline.Add(new IncidentTimelineEventDto(
                item.Timestamp,
                "Process",
                $"{item.ProcessName} {action}",
                $"PID {item.ProcessId}",
                string.Equals(action, "stopped", StringComparison.OrdinalIgnoreCase) ? "Notice" : "Info"));
        }

        foreach (var item in windowsEvents)
        {
            if (item.TimeCreated is not { } timestamp)
                continue;

            var provider = string.IsNullOrWhiteSpace(item.Provider) ? "Windows" : item.Provider;
            timeline.Add(new IncidentTimelineEventDto(
                timestamp,
                "Windows",
                $"{provider} · Event {item.Id}",
                Truncate(string.IsNullOrWhiteSpace(item.Message) ? item.Level ?? "Windows event" : item.Message, 420),
                SeverityForWindowsEvent(item)));
        }

        if (summary.LastHeartbeatAt is { } heartbeat)
        {
            timeline.Add(new IncidentTimelineEventDto(
                heartbeat,
                "Heartbeat",
                "Last CrashScope heartbeat",
                "The recorder did not successfully persist another heartbeat after this point.",
                "Critical"));
        }

        return timeline
            .OrderBy(item => item.Timestamp)
            .ThenBy(item => TimelineRank(item.Kind))
            .ToArray();
    }

    private static IReadOnlyList<IncidentMetricSeriesDto> BuildMetricSeries(
        IReadOnlyList<TelemetrySnapshot> telemetry,
        IReadOnlyList<SensorCatalogEntry>? catalog)
    {
        if (telemetry.Count == 0 || catalog is not { Count: > 0 })
            return Array.Empty<IncidentMetricSeriesDto>();

        var specs = new[]
        {
            new MetricSpec("cpu-load", "CPU Load", "%", 0, 100, FindCpuLoad(catalog)),
            new MetricSpec("cpu-temp", "CPU Temp", "°C", 20, 110, FindCpuTemp(catalog)),
            new MetricSpec("gpu-load", "GPU Load", "%", 0, 100, FindGpuLoad(catalog)),
            new MetricSpec("gpu-temp", "GPU Temp", "°C", 20, 110, FindGpuTemp(catalog))
        };

        var output = new List<IncidentMetricSeriesDto>();
        foreach (var spec in specs)
        {
            if (spec.SensorId is not { } sensorId)
                continue;

            var points = telemetry
                .Where(sample => sensorId >= 0 && sensorId < sample.SensorValues.Count && sample.SensorValues[sensorId] is not null)
                .Select(sample => new IncidentMetricPointDto(sample.Timestamp, sample.SensorValues[sensorId]!.Value))
                .ToArray();

            if (points.Length > 0)
                output.Add(new IncidentMetricSeriesDto(spec.Key, spec.Label, spec.Unit, spec.Minimum, spec.Maximum, points));
        }

        return output;
    }

    private static string BuildTelemetryDetail(TelemetrySnapshot sample, IReadOnlyList<SensorCatalogEntry>? catalog)
    {
        var parts = new List<string>();
        if (catalog is { Count: > 0 })
        {
            AddMetric(parts, "CPU", ReadValue(sample, FindCpuLoad(catalog)), "%", 0);
            AddMetric(parts, "CPU", ReadValue(sample, FindCpuTemp(catalog)), "°C", 1);
            AddMetric(parts, "GPU", ReadValue(sample, FindGpuLoad(catalog)), "%", 0);
            AddMetric(parts, "GPU", ReadValue(sample, FindGpuTemp(catalog)), "°C", 1);
        }

        var foreground = string.IsNullOrWhiteSpace(sample.Foreground.ProcessName) ? "Desktop / unknown" : sample.Foreground.ProcessName;
        parts.Add($"Foreground {foreground}");

        if (parts.Count == 1 && catalog is not { Count: > 0 })
            parts.Add($"{sample.SensorValues.Count} sensor values · catalog unavailable for this older incident");

        return string.Join(" · ", parts);
    }

    private static void AddMetric(List<string> parts, string label, float? value, string unit, int decimals)
    {
        if (value is null)
            return;
        parts.Add($"{label} {value.Value.ToString($"F{decimals}", CultureInfo.InvariantCulture)}{unit}");
    }

    private static float? ReadValue(TelemetrySnapshot sample, int? sensorId)
    {
        if (sensorId is not { } id || id < 0 || id >= sample.SensorValues.Count)
            return null;
        return sample.SensorValues[id];
    }

    private static int? FindCpuLoad(IReadOnlyList<SensorCatalogEntry> catalog) =>
        FindSensor(catalog, "Cpu", "Load", "Total") ?? FindSensor(catalog, "Cpu", "Load", null);

    private static int? FindCpuTemp(IReadOnlyList<SensorCatalogEntry> catalog) =>
        FindSensor(catalog, "Cpu", "Temperature", "Package") ?? FindSensor(catalog, "Cpu", "Temperature", null);

    private static int? FindGpuLoad(IReadOnlyList<SensorCatalogEntry> catalog) =>
        FindSensor(catalog, "Gpu", "Load", "Core") ?? FindSensor(catalog, "Gpu", "Load", "GPU") ?? FindSensor(catalog, "Gpu", "Load", null);

    private static int? FindGpuTemp(IReadOnlyList<SensorCatalogEntry> catalog) =>
        FindSensor(catalog, "Gpu", "Temperature", "Core") ?? FindSensor(catalog, "Gpu", "Temperature", null);

    private static int? FindSensor(IReadOnlyList<SensorCatalogEntry> catalog, string hardwareToken, string sensorType, string? nameToken)
    {
        foreach (var sensor in catalog)
        {
            if (!sensor.HardwareType.Contains(hardwareToken, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(sensor.SensorType, sensorType, StringComparison.OrdinalIgnoreCase))
                continue;
            if (nameToken is not null && !sensor.SensorName.Contains(nameToken, StringComparison.OrdinalIgnoreCase))
                continue;
            return sensor.Id;
        }
        return null;
    }

    private static string SeverityForWindowsEvent(WindowsEventEvidence item)
    {
        if (item.Id == 41 || item.Id == 6008 || item.Id == 1001 ||
            string.Equals(item.Provider, "Microsoft-Windows-WHEA-Logger", StringComparison.OrdinalIgnoreCase))
            return "Critical";
        if (item.Id == 4101 || string.Equals(item.Provider, "Display", StringComparison.OrdinalIgnoreCase))
            return "Warning";
        return "Notice";
    }

    private static int TimelineRank(string kind) => kind switch
    {
        "Telemetry" => 0,
        "Foreground" => 1,
        "Process" => 2,
        "Windows" => 3,
        "Heartbeat" => 4,
        _ => 5
    };

    private static IReadOnlyList<T> ReadJsonLines<T>(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<T>();

        var output = new List<T>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var item = JsonSerializer.Deserialize<T>(line, JsonOptions);
                if (item is not null)
                    output.Add(item);
            }
            catch (JsonException)
            {
                // Preserve the rest of the incident even if one JSONL record was torn during a crash.
            }
        }
        return output;
    }

    private static T? ReadJsonFile<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ParseBacktickValue(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
            return null;

        var first = line.IndexOf('`');
        var last = line.LastIndexOf('`');
        return first >= 0 && last > first ? line[(first + 1)..last] : null;
    }

    private static bool ParseBool(IEnumerable<string> lines, string prefix) =>
        bool.TryParse(ParseBacktickValue(lines, prefix), out var value) && value;

    private static DateTimeOffset? ParseTimestamp(IEnumerable<string> lines, string prefix)
    {
        var value = ParseBacktickValue(lines, prefix);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : null;
    }

    private static string FormatDuration(long seconds) => seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60}s";
    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    private sealed record MetricSpec(string Key, string Label, string Unit, double Minimum, double Maximum, int? SensorId);
}
