using System.Text;
using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public static class ReportGenerator
{
    public static void WriteTimeline(IncidentAnalysis analysis, string incidentDirectory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CrashScope Incident Timeline");
        sb.AppendLine();
        sb.AppendLine($"**Incident**: {analysis.PreviousSession.LastHeartbeatAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Session**: {analysis.PreviousSession.SessionId}");
        sb.AppendLine($"**Started**: {analysis.PreviousSession.StartedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Clean Shutdown**: {analysis.PreviousSession.CleanShutdown}");
        sb.AppendLine();
        sb.AppendLine("## Chronological Events");
        sb.AppendLine();
        sb.AppendLine("| Time | Category | Description |");
        sb.AppendLine("|------|----------|-------------|");

        foreach (var entry in analysis.Timeline)
        {
            var desc = entry.Description.Replace("|", "\\|");
            sb.AppendLine($"| {entry.Timestamp:HH:mm:ss} | {entry.Category} | {desc} |");
        }

        sb.AppendLine();
        sb.AppendLine("## GPU Sensor Transitions");
        sb.AppendLine();

        if (analysis.GpuTransitions.Count > 0)
        {
            sb.AppendLine("| Time | Sensor | From | To | Duration | Trend |");
            sb.AppendLine("|------|--------|------|----|----------|-------|");
            foreach (var t in analysis.GpuTransitions)
            {
                sb.AppendLine($"| {t.Timestamp:HH:mm:ss} | {t.SensorName} | {t.FromValue:F1} | {t.ToValue:F1} | {t.ElapsedSeconds:F1}s | {(t.IsTrend ? "Yes" : "No")} |");
            }
        }
        else
        {
            sb.AppendLine("No GPU sensor transitions detected.");
        }

        File.WriteAllText(Path.Combine(incidentDirectory, "timeline.md"), sb.ToString());
    }

    public static void WriteSummary(IncidentAnalysis analysis, string incidentDirectory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CrashScope Incident Summary");
        sb.AppendLine();
        sb.AppendLine($"**Date**: {analysis.PreviousSession.LastHeartbeatAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Session**: {analysis.PreviousSession.SessionId}");
        sb.AppendLine();
        sb.AppendLine("## Top Hypothesis");
        sb.AppendLine();

        var leading = analysis.Hypotheses.FirstOrDefault(h => h.Score > 0);
        if (leading is not null)
        {
            sb.AppendLine($"**{leading.Mode}** (confidence: {leading.Confidence:P0}, score: {leading.Score})");
            sb.AppendLine();
            sb.AppendLine("### Supporting Evidence");
            foreach (var evidence in leading.Evidence.Where(e => e.Supports))
                sb.AppendLine($"- {evidence.Description} (weight: {evidence.Weight})");

            sb.AppendLine();
            sb.AppendLine("### Contradicting Evidence");
            foreach (var evidence in leading.Evidence.Where(e => !e.Supports))
                sb.AppendLine($"- {evidence.Description} (weight: {evidence.Weight})");
        }
        else
        {
            sb.AppendLine("No strong hypothesis could be formed. See full analysis for details.");
        }

        sb.AppendLine();
        sb.AppendLine("## All Hypotheses");
        sb.AppendLine();
        sb.AppendLine("| Mode | Score | Confidence |");
        sb.AppendLine("|------|-------|------------|");
        foreach (var h in analysis.Hypotheses)
            sb.AppendLine($"| {h.Mode} | {h.Score:+0;-#} | {h.Confidence:P0} |");

        sb.AppendLine();
        sb.AppendLine("## Open Questions");
        sb.AppendLine();
        foreach (var q in analysis.OpenQuestions)
            sb.AppendLine($"- {q}");

        sb.AppendLine();
        sb.AppendLine("## System Snapshot");
        sb.AppendLine();
        sb.AppendLine($"- **Telemetry Lag**: {analysis.TelemetryLagSeconds:F2}s");
        sb.AppendLine($"- **Final Idle**: {analysis.FinalIdleSeconds}s");
        sb.AppendLine($"- **Final GPU Load**: {analysis.FinalGpuLoadPercent?.ToString("F0") ?? "N/A"}%");
        sb.AppendLine($"- **Elevated**: {analysis.PreviousSession.Elevated}");

        if (analysis.Graphics.GraphicsApps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Graphics Apps Detected");
            sb.AppendLine();
            foreach (var app in analysis.Graphics.GraphicsApps)
                sb.AppendLine($"- **{app.ProcessName}** ({app.Category}) - Running at end: {app.RunningAtEnd}");
        }

        File.WriteAllText(Path.Combine(incidentDirectory, "summary.md"), sb.ToString());
    }
}