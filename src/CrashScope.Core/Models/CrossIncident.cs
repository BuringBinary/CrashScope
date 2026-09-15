namespace CrashScope.Core.Models;

public enum RecommendationCategory
{
    DriverRollback,
    DisableVirtualDisplay,
    DisableHardwareGpuScheduling,
    CheckThermals,
    DisableOverlay,
    UpdateDriver,
    ReduceGpuLoad,
    InvestigatePowerPlan,
    General
}

public sealed record PatternFinding(
    string PatternType,
    string Description,
    int OccurrenceCount,
    int TotalIncidents,
    double Confidence,
    IReadOnlyList<string> IncidentIds);

public sealed record DriverCorrelation(
    string DriverVersion,
    string AdapterName,
    string Vendor,
    DateTimeOffset? DriverDate,
    int IncidentCount,
    int DaysActive,
    double FailuresPerDay,
    IReadOnlyList<string> FailureModes);

public sealed record StabilityTrend(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int IncidentCount,
    double MtbfHours,
    int StabilityScore,
    string TrendDirection,
    IReadOnlyList<FailureModeCount> ModeBreakdown);

public sealed record FailureModeCount(
    FailureMode Mode,
    int Count);

public sealed record Recommendation(
    RecommendationCategory Category,
    string Title,
    string Description,
    int Priority,
    double Confidence,
    IReadOnlyList<string> SupportingIncidentIds);

public sealed record CrossIncidentReport(
    DateTimeOffset GeneratedAt,
    int TotalIncidents,
    IReadOnlyList<PatternFinding> Patterns,
    IReadOnlyList<DriverCorrelation> DriverCorrelations,
    IReadOnlyList<StabilityTrend> StabilityTrends,
    IReadOnlyList<Recommendation> Recommendations,
    string Summary);