using CrashScope.Core.Models;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CrashScope.Desktop;

public sealed class DiagnosticExportService
{
    public async Task<string> ExportAsync(
        IncidentInfo incident,
        string outputDirectory,
        bool redactPersonalData = true,
        IProgress<int>? progress = null)
    {
        var incidentName = SanitizeFileName(incident.DirectoryName);
        var zipPath = Path.Combine(outputDirectory, $"CrashScope-{incidentName}.zip");

        // One redactor per export keeps [PROCESS_n] tokens stable across every entry.
        _redactor = redactPersonalData
            ? new ExportRedactor(incident.FullPath, incident.Analysis)
            : ExportRedactor.Passthrough;

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            AddIncidentFiles(zip, incident.FullPath);
            progress?.Report(40);

            AddSummary(zip, incident);
            progress?.Report(70);

            AddRedactionLog(zip, redactPersonalData);
            progress?.Report(100);
        });

        return zipPath;
    }

    private ExportRedactor _redactor = ExportRedactor.Passthrough;

    private void AddIncidentFiles(ZipArchive zip, string incidentPath)
    {
        if (!Directory.Exists(incidentPath)) return;

        foreach (var filePath in Directory.GetFiles(incidentPath, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(incidentPath, filePath);
            var entry = zip.CreateEntry($"incident/{relativePath}", CompressionLevel.Optimal);

            using var output = entry.Open();

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (RedactableTextExtensions.Contains(ext))
            {
                var content = ReadAllText(filePath);
                content = _redactor.Redact(content);
                using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);
                writer.Write(content);
            }
            else
            {
                using var input = File.OpenRead(filePath);
                input.CopyTo(output);
            }
        }
    }

    private static readonly HashSet<string> RedactableTextExtensions =
        [".json", ".jsonl", ".md", ".txt", ".csv"];

    private void AddSummary(ZipArchive zip, IncidentInfo incident)
    {
        var redact = _redactor != ExportRedactor.Passthrough;
        var sb = new StringBuilder();
        sb.AppendLine("=== CrashScope Diagnostic Export ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Incident: {incident.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Classification: {incident.Mode}");
        sb.AppendLine("The UI ranking is re-evaluated read-only. Original evidence files may retain an older saved ranking; they are preserved for provenance.");
        sb.AppendLine($"Evidence support (heuristic, not probability): {incident.Confidence:P0}");
        sb.AppendLine();

        if (redact)
        {
            sb.AppendLine("[PRIVACY NOTE: This export has been redacted. ");
            sb.AppendLine("Process names, user profile paths, user names, ");
            sb.AppendLine("IP addresses, and the machine name have been anonymized.]");
            sb.AppendLine();
        }

        sb.AppendLine("--- Hypothesis Ranking ---");
        foreach (var h in incident.Analysis.Hypotheses.OrderByDescending(h => h.Score))
        {
            sb.AppendLine($"  {h.Mode}: Score={h.Score:+0;-#}, EvidenceSupport={h.Confidence:P0}");
            foreach (var evidence in h.Evidence)
            {
                var prefix = evidence.Supports ? "+" : "-";
                var desc = _redactor.Redact(evidence.Description);
                sb.AppendLine($"    {prefix} [{evidence.Weight}] {desc}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("--- Timeline ---");
        foreach (var tl in incident.Analysis.Timeline)
        {
            var desc = _redactor.Redact(tl.Description);
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
        writer.WriteLine("  - Windows user profile paths -> [USERPROFILE]\\[USERNAME]");
        writer.WriteLine("  - Process names -> [PROCESS_N] (consistent per process across the whole export)");
        writer.WriteLine("  - IPv4 addresses -> [IP_ADDRESS]");
        writer.WriteLine("  - Machine name -> [HOSTNAME]");
        writer.WriteLine();
        writer.WriteLine("Not redacted, because never recorded in the first place:");
        writer.WriteLine("  - Window titles, command lines, file contents, typed text");
        writer.WriteLine();
        writer.WriteLine("To export without redaction, uncheck 'Redact personal data' in the export dialog.");
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