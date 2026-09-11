using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using CrashScope.Agent.Models;

namespace CrashScope.Agent.Diagnostics;

public static class CrashEvidenceCollector
{
    private static readonly HashSet<int> InterestingIds = [41, 4101, 6008, 1001, 17, 18, 19, 20, 46, 47];
    private static readonly string[] InterestingProviders =
    [
        "Microsoft-Windows-Kernel-Power",
        "Microsoft-Windows-WHEA-Logger",
        "Display",
        "EventLog",
        "Microsoft-Windows-WER-SystemErrorReporting"
    ];

    public static void CollectForUncleanSession(SessionState previous, string incidentDirectory)
    {
        Directory.CreateDirectory(incidentDirectory);
        File.WriteAllText(
            Path.Combine(incidentDirectory, "previous-session.json"),
            JsonSerializer.Serialize(previous, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        var start = previous.LastHeartbeatAt.AddMinutes(-10).UtcDateTime;
        var end = DateTime.UtcNow;
        var evidence = ReadSystemEvents(start, end);
        File.WriteAllText(
            Path.Combine(incidentDirectory, "windows-events.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        CopyTail(previous.SessionDirectory, incidentDirectory);
        WriteSummary(previous, evidence, incidentDirectory);
    }

    private static IReadOnlyList<WindowsEventEvidence> ReadSystemEvents(DateTime startUtc, DateTime endUtc)
    {
        var output = new List<WindowsEventEvidence>();
        var startText = startUtc.ToString("o");
        var endText = endUtc.ToString("o");
        var queryText = $"*[System[TimeCreated[@SystemTime >= '{startText}' and @SystemTime <= '{endText}']]]";

        try
        {
            var query = new EventLogQuery("System", PathType.LogName, queryText)
            {
                ReverseDirection = false,
                TolerateQueryErrors = true
            };

            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (!InterestingIds.Contains(record.Id) &&
                        !InterestingProviders.Any(p => string.Equals(p, record.ProviderName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string? message = null;
                    try { message = record.FormatDescription(); } catch { }

                    output.Add(new WindowsEventEvidence(
                        record.TimeCreated is null ? null : new DateTimeOffset(record.TimeCreated.Value),
                        record.ProviderName,
                        record.Id,
                        record.LevelDisplayName,
                        message));
                }
            }
        }
        catch (Exception ex)
        {
            output.Add(new WindowsEventEvidence(DateTimeOffset.Now, "CrashScope", -1, "Error", ex.Message));
        }

        return output;
    }

    private static void CopyTail(string previousSessionDirectory, string incidentDirectory)
    {
        if (!Directory.Exists(previousSessionDirectory))
            return;

        CopyFileIfExists(Path.Combine(previousSessionDirectory, "sensor-catalog.json"), Path.Combine(incidentDirectory, "sensor-catalog.json"));
        CopyTailFile(Path.Combine(previousSessionDirectory, "telemetry.jsonl"), Path.Combine(incidentDirectory, "telemetry-tail.jsonl"), 300);
        CopyTailFile(Path.Combine(previousSessionDirectory, "foreground-events.jsonl"), Path.Combine(incidentDirectory, "foreground-tail.jsonl"), 200);
        CopyTailFile(Path.Combine(previousSessionDirectory, "process-events.jsonl"), Path.Combine(incidentDirectory, "process-events-tail.jsonl"), 400);
        CopyTailFile(Path.Combine(previousSessionDirectory, "process-snapshots.jsonl"), Path.Combine(incidentDirectory, "process-snapshots-tail.jsonl"), 20);
    }

    private static void CopyFileIfExists(string source, string destination)
    {
        try
        {
            if (File.Exists(source))
                File.Copy(source, destination, overwrite: true);
        }
        catch
        {
            // Incident creation should continue even if optional metadata is unavailable.
        }
    }

    private static void CopyTailFile(string source, string destination, int lines)
    {
        try
        {
            if (!File.Exists(source)) return;
            var tail = File.ReadLines(source).TakeLast(lines);
            File.WriteAllLines(destination, tail);
        }
        catch
        {
            // Incident creation should continue even if one file is unavailable.
        }
    }

    private static void WriteSummary(SessionState previous, IReadOnlyList<WindowsEventEvidence> events, string incidentDirectory)
    {
        var hasKernelPower = events.Any(e => e.Id == 41);
        var hasUnexpectedShutdown = events.Any(e => e.Id == 6008);
        var hasWhea = events.Any(e => string.Equals(e.Provider, "Microsoft-Windows-WHEA-Logger", StringComparison.OrdinalIgnoreCase));
        var hasDisplay = events.Any(e => e.Id == 4101 || string.Equals(e.Provider, "Display", StringComparison.OrdinalIgnoreCase));

        var lines = new List<string>
        {
            "# Preliminary incident summary",
            "",
            $"- Previous session: `{previous.SessionId}`",
            $"- Last heartbeat: `{previous.LastHeartbeatAt:O}`",
            $"- Clean shutdown marker: `{previous.CleanShutdown}`",
            $"- Kernel-Power 41 found: `{hasKernelPower}`",
            $"- Unexpected shutdown 6008 found: `{hasUnexpectedShutdown}`",
            $"- WHEA evidence found: `{hasWhea}`",
            $"- Display/TDR evidence found: `{hasDisplay}`",
            "",
            "> This is evidence collection, not a root-cause verdict. Correlation/classification is planned for v0.2."
        };

        File.WriteAllLines(Path.Combine(incidentDirectory, "summary.md"), lines);
    }
}
