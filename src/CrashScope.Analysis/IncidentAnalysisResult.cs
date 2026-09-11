namespace CrashScope.Analysis;

public sealed record IncidentAnalysisResult(
    string Classification,
    double Confidence,
    IReadOnlyList<AnalysisEvidence> Evidence,
    IReadOnlyList<AnalysisEvidence> MissingEvidence);

public sealed record AnalysisEvidence(
    string Signal,
    string Reason,
    double Weight);
