using System.Text.Json;
using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public static class IncidentAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IncidentAnalysis Analyze(
        SessionState previous,
        IReadOnlyList<WindowsEventEvidence> events,
        IReadOnlyList<ExternalEvidenceFile> external,
        string incidentDirectory)
    {
        var catalog = ReadJsonFile<SensorCatalogEntry[]>(Path.Combine(incidentDirectory, "sensor-catalog.json"));
        var telemetry = ReadJsonLines<TelemetrySnapshot>(Path.Combine(incidentDirectory, "telemetry-tail.jsonl"));
        var foreground = ReadJsonLines<ForegroundEvent>(Path.Combine(incidentDirectory, "foreground-tail.jsonl"));
        var processEvents = ReadJsonLines<ProcessEvent>(Path.Combine(incidentDirectory, "process-events-tail.jsonl"));
        var processSnapshots = ReadJsonLines<ProcessSnapshot>(Path.Combine(incidentDirectory, "process-snapshots-tail.jsonl"));

        var transitions = GpuTransitionDetector.Detect(catalog ?? [], telemetry);
        var facts = BuildFacts(previous, catalog, telemetry);
        var graphics = GraphicsContextBuilder.Build(incidentDirectory, events, processEvents, processSnapshots);
        var evaluation = HypothesisEvaluator.Evaluate(events, external, transitions, facts, graphics);
        var timeline = TimelineBuilder.Build(
            previous, catalog, telemetry, foreground, processEvents, events, external, transitions, graphics);

        var analysis = new IncidentAnalysis(
            previous,
            facts.LastTelemetrySampleAt,
            facts.TelemetryLagSeconds,
            facts.FinalIdleSeconds,
            facts.FinalGpuLoadPercent,
            transitions,
            timeline,
            evaluation.Hypotheses,
            evaluation.OpenQuestions,
            graphics);

        WriteOutputs(analysis, incidentDirectory);
        return analysis;
    }

    private static SessionFacts BuildFacts(
        SessionState previous,
        IReadOnlyList<SensorCatalogEntry>? catalog,
        IReadOnlyList<TelemetrySnapshot> telemetry)
    {
        DateTimeOffset? lastSampleAt = telemetry.Count > 0 ? telemetry[^1].Timestamp : null;
        var lag = lastSampleAt.HasValue
            ? Math.Max(0, (previous.LastHeartbeatAt - lastSampleAt.Value).TotalSeconds)
            : 0;

        var finalIdle = telemetry.Count > 0 ? telemetry[^1].Foreground.IdleSeconds : 0;

        float? finalGpuLoad = null;
        if (telemetry.Count > 0)
        {
            var gpuLoadIndex = SensorIndices.GpuCoreLoad(catalog);
            if (gpuLoadIndex.HasValue && gpuLoadIndex.Value < telemetry[^1].SensorValues.Count)
                finalGpuLoad = telemetry[^1].SensorValues[gpuLoadIndex.Value];
        }

        return new SessionFacts(
            lastSampleAt, lag, finalIdle, finalGpuLoad,
            SensorIndices.HasGpuSensors(catalog),
            previous.LastHeartbeatAt);
    }

    private static void WriteOutputs(IncidentAnalysis analysis, string incidentDirectory)
    {
        File.WriteAllText(
            Path.Combine(incidentDirectory, "analysis.json"),
            JsonSerializer.Serialize(analysis, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        ReportGenerator.WriteTimeline(analysis, incidentDirectory);
        ReportGenerator.WriteSummary(analysis, incidentDirectory);
    }

    internal static T? ReadJsonFile<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch
        {
            return null;
        }
    }

    internal static List<T> ReadJsonLines<T>(string path) where T : class
    {
        var result = new List<T>();
        try
        {
            if (!File.Exists(path))
                return result;

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var item = JsonSerializer.Deserialize<T>(line, JsonOptions);
                    if (item is not null)
                        result.Add(item);
                }
                catch
                {
                    // Skip a torn/partial final line.
                }
            }
        }
        catch
        {
            // Missing or unreadable tail: continue with what parsed.
        }

        return result;
    }
}
