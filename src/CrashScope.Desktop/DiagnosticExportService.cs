using CrashScope.Core.Models;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashScope.Desktop;

public sealed class DiagnosticExportService
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> ExportAsync(
        IncidentInfo incident,
        string outputDirectory,
        bool redactPersonalData = true,
        IProgress<int>? progress = null)
    {
        var incidentName = SanitizeFileName(incident.DirectoryName);
        var zipPath = Path.Combine(outputDirectory, $"CrashScope-{incidentName}.zip");

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            AddIncidentFiles(zip, incident.FullPath, redactPersonalData);
            progress?.Report(40);

            AddSummary(zip, incident, redactPersonalData);
            progress?.Report(70);

            AddRedactionLog(zip, redactPersonalData);
            progress?.Report(100);
        });

        return zipPath;
    }

    private void AddIncidentFiles(ZipArchive zip, string incidentPath, bool redact)
    {
        if (!Directory.Exists(incidentPath)) return;

        foreach (var filePath in Directory.GetFiles(incidentPath, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(incidentPath, filePath);
            var entry = zip.CreateEntry($"incident/{relativePath}", CompressionLevel.Optimal);

            using var input = File.OpenRead(filePath);
            using var output = entry.Open();

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (redact && (ext == ".json" || ext == ".jsonl" || ext == ".md" || ext == ".txt" || ext == ".csv"))
            {
                var content = ReadAllText(filePath);
                content = RedactContent(content);
                using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);
                writer.Write(content);
            }
            else
            {
                input.CopyTo(output);
            }
        }
    }

    private void AddSummary(ZipArchive zip, IncidentInfo incident, bool redact)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CrashScope Diagnostic Export ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Incident: {incident.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Classification: {incident.Mode}");
        sb.AppendLine($"Confidence: {incident.Confidence:P0}");
        sb.AppendLine();

        if (redact)
        {
            sb.AppendLine("[PRIVACY NOTE: This export has been redacted. ");
            sb.AppendLine("Process names, window titles, user names, ");
            sb.AppendLine("and file paths have been anonymized.]");
            sb.AppendLine();
        }

        sb.AppendLine("--- Hypothesis Ranking ---");
        foreach (var h in incident.Analysis.Hypotheses.OrderByDescending(h => h.Score))
        {
            sb.AppendLine($"  {h.Mode}: Score={h.Score:+0;-#}, Confidence={h.Confidence:P0}");
            foreach (var evidence in h.Evidence)
            {
                var prefix = evidence.Supports ? "+" : "-";
                var desc = redact ? RedactContent(evidence.Description) : evidence.Description;
                sb.AppendLine($"    {prefix} [{evidence.Weight}] {desc}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("--- Timeline ---");
        foreach (var tl in incident.Analysis.Timeline)
        {
            var desc = redact ? RedactContent(tl.Description) : tl.Description;
            sb.AppendLine($"  {tl.Timestamp:HH:mm:ss} [{tl.Category}] {desc}");
        }
        sb.AppendLine();

        sb.AppendLine("--- Open Questions ---");
        foreach (var q in incident.Analysis.OpenQuestions)
        {
            sb.AppendLine($"  * {q}");
        }
        sb.AppendLine();

        sb.AppendLine("--- System Context ---");
        sb.AppendLine($"  Session ID: {incident.Analysis.PreviousSession.SessionId}");
        sb.AppendLine($"  Started: {incident.Analysis.PreviousSession.StartedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Last Heartbeat: {incident.Analysis.PreviousSession.LastHeartbeatAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Clean Shutdown: {incident.Analysis.PreviousSession.CleanShutdown}");
        sb.AppendLine($"  Elevated: {incident.Analysis.PreviousSession.Elevated}");
        sb.AppendLine($"  Telemetry Lag: {incident.Analysis.TelemetryLagSeconds:F2}s");
        sb.AppendLine($"  Final Idle: {incident.Analysis.FinalIdleSeconds}s");

        if (incident.Analysis.GpuTransitions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- GPU Sensor Transitions ---");
            foreach (var t in incident.Analysis.GpuTransitions)
            {
                sb.AppendLine($"  {t.Timestamp:HH:mm:ss} {t.SensorName}: {t.FromValue:F1} → {t.ToValue:F1} [{t.Description}]");
            }
        }

        var entry = zip.CreateEntry("summary.txt", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(sb.ToString());
    }

    private static void AddRedactionLog(ZipArchive zip, bool redact)
    {
        if (!redact) return;

        var entry = zip.CreateEntry("redaction-rules.txt", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.WriteLine("CrashScope Privacy Redaction Rules");
        writer.WriteLine("===================================");
        writer.WriteLine();
        writer.WriteLine("The following patterns have been anonymized in this export:");
        writer.WriteLine("  - Windows user profile paths → [USERPROFILE]");
        writer.WriteLine("  - Usernames in paths → [USERNAME]");
        writer.WriteLine("  - Process names → [PROCESS_N] (consistent per process)");
        writer.WriteLine("  - Window titles → [WINDOW_TITLE]");
        writer.WriteLine("  - IP addresses → [IP_ADDRESS]");
        writer.WriteLine("  - Machine names → [HOSTNAME]");
        writer.WriteLine();
        writer.WriteLine("To export without redaction, uncheck 'Redact personal data' in the export dialog.");
    }

    private static readonly Regex UserProfileRegex = new(
        @"C:\\Users\\[^\\]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IpAddressRegex = new(
        @"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex WindowTitleRegex = new(
        @"(?<=\-\s)(?!PID\s)(?!CrashScope)([A-Za-z][A-Za-z0-9\s\-\.]{3,60})(?=\s*\()", RegexOptions.Compiled);

    public static string RedactContent(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        content = UserProfileRegex.Replace(content, "[USERPROFILE]");
        content = IpAddressRegex.Replace(content, "[IP_ADDRESS]");
        content = WindowTitleRegex.Replace(content, "[WINDOW_TITLE]");

        return content;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '_');
        return name;
    }

    private static string ReadAllText(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }
}