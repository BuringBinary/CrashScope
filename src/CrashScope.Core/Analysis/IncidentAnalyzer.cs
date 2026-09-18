using System.Text.Json;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;

namespace CrashScope.Core.Analysis;

public static class IncidentAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IncidentAnalysis Analyze(
        SessionState previous,
        IReadOnlyList<WindowsEventEvidence> events,
        IReadOnlyList<ExternalEvidenceFile> external,
        string incidentDirectory,
        bool writeOutputs = true)
    {
        var catalog = ReadJsonFile<SensorCatalogEntry[]>(Path.Combine(incidentDirectory, "sensor-catalog.json"));
        var telemetry = ReadJsonLines<TelemetrySnapshot>(Path.Combine(incidentDirectory, "telemetry-tail.jsonl"));
        var foreground = ReadJsonLines<ForegroundEvent>(Path.Combine(incidentDirectory, "foreground-tail.jsonl"));
        var processEvents = ReadJsonLines<ProcessEvent>(Path.Combine(incidentDirectory, "process-events-tail.jsonl"));
        var processSnapshots = ReadJsonLines<ProcessSnapshot>(Path.Combine(incidentDirectory, "process-snapshots-tail.jsonl"));
        var etwEvents = ReadJsonLines<EtwDxgKrnlEvent>(Path.Combine(incidentDirectory, "etw-gpu-tail.jsonl"));
        var etwKernelPower = ReadJsonLines<EtwProviderEvent>(Path.Combine(incidentDirectory, "etw-kernel-power-tail.jsonl"));
        var etwWhea = ReadJsonLines<EtwProviderEvent>(Path.Combine(incidentDirectory, "etw-whea-tail.jsonl"));
        var storageEvents = ReadJsonLines<StorageSample>(Path.Combine(incidentDirectory, "storage-tail.jsonl"));
        var gpuMemoryEvents = ReadJsonLines<GpuMemorySample>(Path.Combine(incidentDirectory, "gpu-memory-tail.jsonl"));
        var memoryPressureEvents = ReadJsonLines<MemoryPressureSample>(Path.Combine(incidentDirectory, "memory-pressure-tail.jsonl"));
        var dwmStateEvents = ReadJsonLines<DwmStateEvent>(Path.Combine(incidentDirectory, "dwm-state-tail.jsonl"));

        var transitions = GpuTransitionDetector.Detect(catalog ?? [], telemetry);
        var facts = BuildFacts(previous, catalog, telemetry);
        var graphics = GraphicsContextBuilder.Build(incidentDirectory, events, processEvents, processSnapshots);
        var evaluation = HypothesisEvaluator.Evaluate(events, external, transitions, facts, graphics, etwEvents, etwKernelPower, etwWhea, storageEvents, gpuMemoryEvents, memoryPressureEvents, dwmStateEvents);
        var timeline = TimelineBuilder.Build(
            previous, catalog, telemetry, foreground, processEvents, events, external, transitions, graphics, etwEvents, etwKernelPower, etwWhea, gpuMemoryEvents, memoryPressureEvents, dwmStateEvents);

        var questions = evaluation.OpenQuestions.ToList();
        AddCollectionQuestions(questions, incidentDirectory, catalog ?? [], telemetry);

        var analysis = new IncidentAnalysis(
            previous,
            facts.LastTelemetrySampleAt,
            facts.TelemetryLagSeconds,
            facts.FinalIdleSeconds,
            facts.FinalGpuLoadPercent,
            transitions,
            timeline,
            evaluation.Hypotheses,
            questions,
            graphics);

        if (writeOutputs) WriteOutputs(analysis, incidentDirectory);
        return analysis;
    }

    private static void AddCollectionQuestions(List<string> questions, string directory,
        IReadOnlyList<SensorCatalogEntry> catalog, IReadOnlyList<TelemetrySnapshot> telemetry)
    {
        var health = ReadJsonFile<CollectorHealth[]>(Path.Combine(directory, "collector-health.json"));
        if (health is null)
            questions.Add("Collector health was not recorded (legacy bundle or unreadable metadata). Empty event files do not establish that collection succeeded.");
        else
            foreach (var item in health.Where(h => h.Status != "Collecting"))
                questions.Add($"Collection {item.Collector}: {item.Status}. {item.Detail}");
        if (telemetry.Any(t => SensorValueQuality.HasSuspectReadings(catalog, t.SensorValues)))
            questions.Add("Some recorded CPU temperature/power/clock readings are zero or non-finite and suspect. Do not interpret them as normal hardware readings; verify permissions and sensor support.");
    }

    public static IncidentAnalysis ReevaluateReadOnly(IncidentAnalysis stored, string incidentDirectory)
    {
        try
        {
            var events = JsonSerializer.Deserialize<WindowsEventEvidence[]>(
                File.ReadAllText(Path.Combine(incidentDirectory, "windows-events.json")), JsonOptions)
                ?? throw new InvalidDataException("Missing Windows event array.");
            var external = JsonSerializer.Deserialize<ExternalEvidenceFile[]>(
                File.ReadAllText(Path.Combine(incidentDirectory, "external-evidence.json")), JsonOptions)
                ?? throw new InvalidDataException("Missing external evidence array.");
            return Analyze(stored.PreviousSession, events, external, incidentDirectory, writeOutputs: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return stored with
            {
                Hypotheses = [new FailureHypothesis(FailureMode.Unclassified, 0, 0, [])],
                OpenQuestions = stored.OpenQuestions.Concat(new[] {
                    "Saved ranking was not reused: raw evidence could not be revalidated. Original files are unchanged. " + ex.GetType().Name
                }).ToArray()
            };
        }
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