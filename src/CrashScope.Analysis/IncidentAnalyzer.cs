namespace CrashScope.Analysis;

public sealed class IncidentAnalyzer
{
    public IncidentAnalysisResult Analyze(IncidentEvidence evidence)
    {
        var signals = new List<AnalysisEvidence>();
        var missing = new List<AnalysisEvidence>();

        if (evidence.KernelPower41)
            signals.Add(new("Kernel-Power 41", "System rebooted without a clean shutdown marker.", 0.35));

        if (evidence.DisplayTdr)
            signals.Add(new("Display/TDR", "Graphics driver timeout evidence detected.", 0.45));

        if (evidence.Whea)
            signals.Add(new("WHEA", "Hardware error reporting evidence detected.", 0.55));

        if (!evidence.HasTelemetryTail)
            missing.Add(new("Telemetry tail", "No pre-failure telemetry window available.", 0));

        var ordered = signals.OrderByDescending(x => x.Weight).ToArray();
        var classification = ordered.FirstOrDefault()?.Signal ?? "Unknown failure";
        var confidence = Math.Clamp(ordered.Sum(x => x.Weight), 0, 0.95);

        return new IncidentAnalysisResult(classification, confidence, ordered, missing);
    }
}

public sealed record IncidentEvidence(
    bool KernelPower41,
    bool DisplayTdr,
    bool Whea,
    bool HasTelemetryTail);
