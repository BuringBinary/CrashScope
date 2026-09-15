using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using System.Xml.Linq;
using CrashScope.Core.Analysis;
using CrashScope.Core.Models;

namespace CrashScope.Core.Diagnostics;

public static class CrashEvidenceCollector
{
    // provider -> interesting event ids, per log name.
    private static readonly Dictionary<string, Dictionary<string, int[]>> SourceRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["System"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft-Windows-Kernel-Power"] = [41, 42, 107, 109, 506, 507],
                ["Microsoft-Windows-Power-Troubleshooter"] = [1],
                ["Microsoft-Windows-WHEA-Logger"] = [17, 18, 19, 20, 46, 47],
                ["Display"] = [4101],
                ["EventLog"] = [6008],
                ["Microsoft-Windows-WER-SystemErrorReporting"] = [1001],
                ["Windows Error Reporting"] = [1001],
                ["Application Error"] = [1000],
                ["Microsoft-Windows-UserPnp"] = [20001, 20002, 20003]
            },
            ["Application"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft-Windows-WER-SystemErrorReporting"] = [1001],
                ["Windows Error Reporting"] = [1001],
                ["Application Error"] = [1000]
            },
            ["Microsoft-Windows-TerminalServices-LocalSessionManager/Operational"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft-Windows-TerminalServices-LocalSessionManager"] = [21, 22, 24, 25, 39, 40]
            },
            ["Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft-Windows-TerminalServices-RemoteConnectionManager"] = [1149]
            }
        };

    public static IncidentAnalysis CollectForUncleanSession(SessionState previous, string incidentDirectory)
    {
        Directory.CreateDirectory(incidentDirectory);
        File.WriteAllText(
            Path.Combine(incidentDirectory, "previous-session.json"),
            JsonSerializer.Serialize(previous, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        var start = previous.LastHeartbeatAt.AddMinutes(-10).UtcDateTime;
        var end = DateTime.UtcNow;
        var evidence = ReadWindowsEvents(start, end);
        File.WriteAllText(
            Path.Combine(incidentDirectory, "windows-events.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        CopyTail(previous.SessionDirectory, incidentDirectory);

        var external = ExternalEvidenceScanner.Scan(previous.LastHeartbeatAt.AddMinutes(-10), DateTimeOffset.Now);
        File.WriteAllText(
            Path.Combine(incidentDirectory, "external-evidence.json"),
            JsonSerializer.Serialize(external, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        return IncidentAnalyzer.Analyze(previous, evidence, external, incidentDirectory);
    }

    private static IReadOnlyList<WindowsEventEvidence> ReadWindowsEvents(DateTime startUtc, DateTime endUtc)
    {
        var output = new List<WindowsEventEvidence>();
        foreach (var (logName, rules) in SourceRules)
            ReadLog(output, logName, rules, startUtc, endUtc, reportErrors: logName is "System" or "Application");
        return output;
    }

    private static void ReadLog(
        List<WindowsEventEvidence> output,
        string logName,
        Dictionary<string, int[]> rules,
        DateTime startUtc,
        DateTime endUtc,
        bool reportErrors)
    {
        var startText = startUtc.ToString("o");
        var endText = endUtc.ToString("o");
        var queryText = $"*[System[TimeCreated[@SystemTime >= '{startText}' and @SystemTime <= '{endText}']]]";

        try
        {
            var query = new EventLogQuery(logName, PathType.LogName, queryText)
            {
                ReverseDirection = false,
                TolerateQueryErrors = true
            };

            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (!rules.TryGetValue(record.ProviderName ?? "", out var ids) || !ids.Contains(record.Id))
                        continue;

                    string? message = null;
                    try { message = record.FormatDescription(); } catch { }

                    output.Add(new WindowsEventEvidence(
                        record.TimeCreated is null ? null : new DateTimeOffset(record.TimeCreated.Value),
                        record.ProviderName,
                        record.Id,
                        record.LevelDisplayName,
                        message,
                        ExtractEventData(record)));
                }
            }
        }
        catch (Exception ex)
        {
            if (reportErrors)
                output.Add(new WindowsEventEvidence(DateTimeOffset.Now, "CrashScope", -1, "Error", $"Failed to read '{logName}' log: {ex.Message}"));
        }
    }

    private static IReadOnlyDictionary<string, string?>? ExtractEventData(EventRecord record)
    {
        try
        {
            var data = XDocument.Parse(record.ToXml())
                .Root?
                .Element("EventData")?
                .Elements("Data");
            if (data is null)
                return null;

            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data)
            {
                var name = item.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                var value = item.Value;
                if (value.Length > 64 || value.Any(char.IsControl))
                    value = null;

                result[name] = value;
            }

            return result.Count == 0 ? null : result;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyTail(string previousSessionDirectory, string incidentDirectory)
    {
        if (!Directory.Exists(previousSessionDirectory))
            return;

        CopyTailFile(Path.Combine(previousSessionDirectory, "telemetry.jsonl"), Path.Combine(incidentDirectory, "telemetry-tail.jsonl"), 300);
        CopyTailFile(Path.Combine(previousSessionDirectory, "foreground-events.jsonl"), Path.Combine(incidentDirectory, "foreground-tail.jsonl"), 200);
        CopyTailFile(Path.Combine(previousSessionDirectory, "process-events.jsonl"), Path.Combine(incidentDirectory, "process-events-tail.jsonl"), 400);
        CopyTailFile(Path.Combine(previousSessionDirectory, "process-snapshots.jsonl"), Path.Combine(incidentDirectory, "process-snapshots-tail.jsonl"), 20);
        CopyTailFile(Path.Combine(previousSessionDirectory, "gpu-engines.jsonl"), Path.Combine(incidentDirectory, "gpu-engines-tail.jsonl"), 60);
        CopyTailFile(Path.Combine(previousSessionDirectory, "display-events.jsonl"), Path.Combine(incidentDirectory, "display-events-tail.jsonl"), 50);
        CopyTailFile(Path.Combine(previousSessionDirectory, "power-events.jsonl"), Path.Combine(incidentDirectory, "power-events-tail.jsonl"), 50);
        CopyTailFile(Path.Combine(previousSessionDirectory, "remote-session-events.jsonl"), Path.Combine(incidentDirectory, "remote-session-events-tail.jsonl"), 50);
        CopyTailFile(Path.Combine(previousSessionDirectory, "storage.jsonl"), Path.Combine(incidentDirectory, "storage-tail.jsonl"), 60);
        CopyTailFile(Path.Combine(previousSessionDirectory, "etw-gpu.jsonl"), Path.Combine(incidentDirectory, "etw-gpu-tail.jsonl"), 500);
        CopyTailFile(Path.Combine(previousSessionDirectory, "etw-kernel-power.jsonl"), Path.Combine(incidentDirectory, "etw-kernel-power-tail.jsonl"), 500);
        CopyTailFile(Path.Combine(previousSessionDirectory, "etw-whea.jsonl"), Path.Combine(incidentDirectory, "etw-whea-tail.jsonl"), 500);
        CopyFileIfExists(Path.Combine(previousSessionDirectory, "sensor-catalog.json"), Path.Combine(incidentDirectory, "sensor-catalog.json"));
        CopyFileIfExists(Path.Combine(previousSessionDirectory, "driver-inventory.json"), Path.Combine(incidentDirectory, "driver-inventory.json"));
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

    private static void CopyFileIfExists(string source, string destination)
    {
        try
        {
            if (File.Exists(source))
                File.Copy(source, destination, overwrite: true);
        }
        catch
        {
            // Sensor catalog / driver inventory are optional for incident creation.
        }
    }
}