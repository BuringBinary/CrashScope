using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class CrossIncidentAnalyzerTests
{
    private static readonly DateTimeOffset BaseTime = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static SessionState MakeSession(string id, DateTimeOffset heartbeat) =>
        new(id, heartbeat.AddHours(-2), heartbeat, false, $@"C:\sessions\{id}", true, "0.7.0");

    private static IncidentAnalysis MakeIncident(
        string id,
        DateTimeOffset heartbeat,
        FailureMode topMode,
        float? finalGpuLoad = null,
        IReadOnlyList<GraphicsAppPresence>? apps = null,
        IReadOnlyList<DisplayTopologyEvent>? displayChanges = null,
        IReadOnlyList<DriverInventoryEntry>? drivers = null,
        IReadOnlyList<TimelineEntry>? timeline = null,
        double confidence = 0.8) =>
        new(
            MakeSession(id, heartbeat),
            heartbeat.AddSeconds(-30),
            30,
            5,
            finalGpuLoad,
            [],
            timeline ?? [],
            [new FailureHypothesis(topMode, 5, confidence, [new EvidenceItem(true, 3, "Test evidence")])],
            [],
            apps != null || displayChanges != null || drivers != null
                ? new GraphicsContext(apps ?? [], displayChanges ?? [], [], [], drivers ?? [], [], [], null)
                : GraphicsContext.Empty);

    private static GraphicsAppPresence OverlayApp(string name) =>
        new(name, GraphicsAppCategory.GameOverlay, BaseTime, BaseTime.AddHours(1), true);

    private static GraphicsAppPresence RemoteApp(string name) =>
        new(name, GraphicsAppCategory.RemoteControl, BaseTime, BaseTime.AddHours(1), true);

    private static DriverInventoryEntry AmdDriver(string version, DateTimeOffset? date = null) =>
        new("AMD Radeon RX 7900 XTX", "VEN_1002", version, date ?? BaseTime, "AMD");

    private static TimelineEntry TdrEntry(DateTimeOffset ts) =>
        new(ts, "ETW-GPU", "TdrEngineResetPrepare");

    private static TimelineEntry WheaEntry(DateTimeOffset ts) =>
        new(ts, "ETW-WHEA", "GenericErrorEvent");

    private static TimelineEntry PowerEntry(DateTimeOffset ts) =>
        new(ts, "ETW-Power", "SleepTransition");

    // ---- Pattern Mining ----

    [Fact]
    public void Analyze_EmptyIncidents_ReturnsEmptyReport()
    {
        var report = CrossIncidentAnalyzer.Analyze([]);

        Assert.Equal(0, report.TotalIncidents);
        Assert.Empty(report.Patterns);
        Assert.Empty(report.Recommendations);
        Assert.Contains("No incidents", report.Summary);
    }

    [Fact]
    public void Analyze_SingleIncident_ReturnsNoCrossPatterns()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        Assert.Equal(1, report.TotalIncidents);
        Assert.Contains("70", report.Summary);
    }

    [Fact]
    public void Analyze_CommonOverlayProcess_DetectedAsPattern()
    {
        var overlay = OverlayApp("steamoverlay.exe");
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery, apps: [overlay]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.HardFreeze, apps: [overlay]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.DriverTimeoutRecovery, apps: [])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var overlayPattern = report.Patterns.FirstOrDefault(p =>
            p.PatternType == "CommonProcess" && p.Description.Contains("steamoverlay"));

        Assert.NotNull(overlayPattern);
        Assert.Equal(2, overlayPattern.OccurrenceCount);
        Assert.Equal(3, overlayPattern.TotalIncidents);
    }

    [Fact]
    public void Analyze_RemoteControlApp_DetectedAsPattern()
    {
        var remote = RemoteApp("ToDesk.exe");
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.HardFreeze, apps: [remote]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.HardFreeze, apps: [remote])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var pattern = report.Patterns.FirstOrDefault(p =>
            p.PatternType == "CommonProcess" && p.Description.Contains("ToDesk"));

        Assert.NotNull(pattern);
        Assert.Equal(2, pattern.OccurrenceCount);
    }

    [Fact]
    public void Analyze_MultipleFailureModes_CountsCorrectly()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.DriverTimeoutRecovery),
            MakeIncident("S4", BaseTime.AddDays(3), FailureMode.HardFreeze),
            MakeIncident("S5", BaseTime.AddDays(4), FailureMode.HardFreeze)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var tdrPattern = report.Patterns.FirstOrDefault(p =>
            p.PatternType == "FailureMode" && p.Description.Contains("DriverTimeoutRecovery"));
        var freezePattern = report.Patterns.FirstOrDefault(p =>
            p.PatternType == "FailureMode" && p.Description.Contains("HardFreeze"));

        Assert.NotNull(tdrPattern);
        Assert.Equal(3, tdrPattern.OccurrenceCount);
        Assert.NotNull(freezePattern);
        Assert.Equal(2, freezePattern.OccurrenceCount);
    }

    [Fact]
    public void Analyze_TemporalCluster_Within24Hours()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddHours(2), FailureMode.DriverTimeoutRecovery),
            MakeIncident("S3", BaseTime.AddHours(4), FailureMode.DriverTimeoutRecovery),
            MakeIncident("S4", BaseTime.AddDays(5), FailureMode.HardFreeze)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var cluster = report.Patterns.FirstOrDefault(p => p.PatternType == "TemporalCluster");
        Assert.NotNull(cluster);
        Assert.Equal(3, cluster.OccurrenceCount);
        Assert.Contains("S1", cluster.IncidentIds);
        Assert.Contains("S2", cluster.IncidentIds);
        Assert.Contains("S3", cluster.IncidentIds);
    }

    [Fact]
    public void Analyze_NoTemporalCluster_WhenSpreadOut()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddDays(5), FailureMode.HardFreeze)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var cluster = report.Patterns.FirstOrDefault(p => p.PatternType == "TemporalCluster");
        Assert.Null(cluster);
    }

    [Fact]
    public void Analyze_VirtualDisplay_DetectedAsPattern()
    {
        var virtualDisplay = new DisplayTopologyEvent(
            BaseTime, "Added",
            [], [new DisplayInfo("Virtual", "Virtual Monitor", true, 1920, 1080)],
            "Virtual display added");

        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.HardFreeze, displayChanges: [virtualDisplay]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery, displayChanges: [virtualDisplay]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.HardFreeze, displayChanges: [])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var pattern = report.Patterns.FirstOrDefault(p => p.PatternType == "VirtualDisplay");
        Assert.NotNull(pattern);
        Assert.Equal(2, pattern.OccurrenceCount);
    }

    // ---- ETW Patterns ----

    [Fact]
    public void Analyze_TdrEtwPattern_AcrossIncidents()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery,
                timeline: [TdrEntry(BaseTime.AddMinutes(-1))]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery,
                timeline: [TdrEntry(BaseTime.AddDays(1).AddMinutes(-1))])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var pattern = report.Patterns.FirstOrDefault(p => p.PatternType == "TdrPattern");
        Assert.NotNull(pattern);
        Assert.Equal(2, pattern.OccurrenceCount);
    }

    [Fact]
    public void Analyze_WheaEtwPattern_AcrossIncidents()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.HardwareError,
                timeline: [WheaEntry(BaseTime.AddMinutes(-1))]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.HardwareError,
                timeline: [WheaEntry(BaseTime.AddDays(1).AddMinutes(-1))]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.HardwareError,
                timeline: [WheaEntry(BaseTime.AddDays(2).AddMinutes(-1))])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var pattern = report.Patterns.FirstOrDefault(p => p.PatternType == "WheaPattern");
        Assert.NotNull(pattern);
        Assert.Equal(3, pattern.OccurrenceCount);
    }

    [Fact]
    public void Analyze_PowerEtwPattern_AcrossIncidents()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.PowerLoss,
                timeline: [PowerEntry(BaseTime.AddMinutes(-1))]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.PowerLoss,
                timeline: [PowerEntry(BaseTime.AddDays(1).AddMinutes(-1))])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var pattern = report.Patterns.FirstOrDefault(p => p.PatternType == "PowerPattern");
        Assert.NotNull(pattern);
        Assert.Equal(2, pattern.OccurrenceCount);
    }

    // ---- GPU State Patterns ----

    [Fact]
    public void Analyze_HighGpuLoad_DetectedAcrossIncidents()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery, finalGpuLoad: 95f),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery, finalGpuLoad: 87f),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.DriverTimeoutRecovery, finalGpuLoad: 45f)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var pattern = report.Patterns.FirstOrDefault(p => p.PatternType == "HighGpuLoad");
        Assert.NotNull(pattern);
        Assert.Equal(2, pattern.OccurrenceCount);
    }

    [Fact]
    public void Analyze_NoHighGpuLoad_WhenBelowThreshold()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery, finalGpuLoad: 75f),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery, finalGpuLoad: 60f)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var pattern = report.Patterns.FirstOrDefault(p => p.PatternType == "HighGpuLoad");
        Assert.Null(pattern);
    }

    // ---- Driver Correlation ----

    [Fact]
    public void Analyze_DriverVersions_Correlated()
    {
        var driverV1 = AmdDriver("24.5.1");
        var driverV2 = AmdDriver("24.8.1", BaseTime.AddMonths(3));

        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery, drivers: [driverV1]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery, drivers: [driverV1]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.HardFreeze, drivers: [driverV1]),
            MakeIncident("S4", BaseTime.AddDays(3), FailureMode.HardFreeze, drivers: [driverV2]),
            MakeIncident("S5", BaseTime.AddDays(4), FailureMode.DriverTimeoutRecovery, drivers: [driverV2]),
            MakeIncident("S6", BaseTime.AddDays(5), FailureMode.DriverTimeoutRecovery, drivers: [driverV2]),
            MakeIncident("S7", BaseTime.AddDays(6), FailureMode.HardFreeze, drivers: [driverV2]),
            MakeIncident("S8", BaseTime.AddDays(7), FailureMode.DriverTimeoutRecovery, drivers: [driverV2])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        Assert.Equal(2, report.DriverCorrelations.Count);

        var v1 = report.DriverCorrelations.FirstOrDefault(d => d.DriverVersion == "24.5.1");
        var v2 = report.DriverCorrelations.FirstOrDefault(d => d.DriverVersion == "24.8.1");

        Assert.NotNull(v1);
        Assert.Equal(3, v1.IncidentCount);
        Assert.NotNull(v2);
        Assert.Equal(5, v2.IncidentCount);
    }

    [Fact]
    public void Analyze_NoDrivers_NoCorrelations()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        Assert.Empty(report.DriverCorrelations);
    }

    // ---- Stability Trend ----

    [Fact]
    public void Analyze_TwoIncidents_CalculatesMtbf()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddHours(6), FailureMode.DriverTimeoutRecovery)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        Assert.Single(report.StabilityTrends);
        var trend = report.StabilityTrends[0];
        Assert.Equal(2, trend.IncidentCount);
        Assert.True(trend.MtbfHours > 0);
        Assert.True(trend.StabilityScore < 100 && trend.StabilityScore > 0);
    }

    [Fact]
    public void Analyze_ZeroIncidents_MaxStability()
    {
        var report = CrossIncidentAnalyzer.Analyze([]);

        Assert.Empty(report.StabilityTrends);
    }

    [Fact]
    public void Analyze_FrequentCrashes_LowStabilityScore()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddHours(1), FailureMode.DriverTimeoutRecovery),
            MakeIncident("S3", BaseTime.AddHours(2), FailureMode.HardFreeze),
            MakeIncident("S4", BaseTime.AddHours(3), FailureMode.DriverTimeoutRecovery)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);
        var trend = report.StabilityTrends[0];

        Assert.True(trend.MtbfHours < 2);
        Assert.True(trend.StabilityScore <= 35);
    }

    [Fact]
    public void Analyze_Worsening_DetectedAsTrend()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddDays(5), FailureMode.DriverTimeoutRecovery),
            MakeIncident("S3", BaseTime.AddDays(6), FailureMode.HardFreeze),
            MakeIncident("S4", BaseTime.AddDays(6).AddHours(1), FailureMode.DriverTimeoutRecovery)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);
        var trend = report.StabilityTrends[0];

        Assert.Equal("Worsening", trend.TrendDirection);
    }

    // ---- Recommendations ----

    [Fact]
    public void Analyze_WheaPattern_GeneratesHardwareRecommendation()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.HardwareError,
                timeline: [WheaEntry(BaseTime.AddMinutes(-1))]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.HardwareError,
                timeline: [WheaEntry(BaseTime.AddDays(1).AddMinutes(-1))])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var rec = report.Recommendations.FirstOrDefault(r =>
            r.Category == RecommendationCategory.General &&
            r.Title.Contains("WHEA"));

        Assert.NotNull(rec);
        Assert.True(rec.Priority >= 90);
    }

    [Fact]
    public void Analyze_TdrPattern_GeneratesHagsRecommendation()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery,
                timeline: [TdrEntry(BaseTime.AddMinutes(-1))]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery,
                timeline: [TdrEntry(BaseTime.AddDays(1).AddMinutes(-1))]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.DriverTimeoutRecovery,
                timeline: [TdrEntry(BaseTime.AddDays(2).AddMinutes(-1))])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var rec = report.Recommendations.FirstOrDefault(r =>
            r.Category == RecommendationCategory.DisableHardwareGpuScheduling);

        Assert.NotNull(rec);
        Assert.Contains("GPU Scheduling", rec.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_VirtualDisplay_GeneratesDisableRecommendation()
    {
        var virtualDisplay = new DisplayTopologyEvent(
            BaseTime, "Added",
            [], [new DisplayInfo("Virtual", "Virtual Monitor", true, 1920, 1080)],
            "Virtual display added");

        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.HardFreeze, displayChanges: [virtualDisplay]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery, displayChanges: [virtualDisplay]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.HardFreeze, displayChanges: [virtualDisplay])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var rec = report.Recommendations.FirstOrDefault(r =>
            r.Category == RecommendationCategory.DisableVirtualDisplay);

        Assert.NotNull(rec);
    }

    [Fact]
    public void Analyze_CriticalStability_GeneratesThermalRecommendation()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddHours(1), FailureMode.HardFreeze),
            MakeIncident("S3", BaseTime.AddHours(2), FailureMode.DriverTimeoutRecovery),
            MakeIncident("S4", BaseTime.AddHours(3), FailureMode.HardFreeze)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var rec = report.Recommendations.FirstOrDefault(r =>
            r.Category == RecommendationCategory.CheckThermals);

        Assert.NotNull(rec);
        Assert.Contains("thermal", rec.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_OverlayProcess_GeneratesDisableOverlayRecommendation()
    {
        var overlay = OverlayApp("steamoverlay.exe");
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery, apps: [overlay]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.HardFreeze, apps: [overlay]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.DriverTimeoutRecovery, apps: [overlay])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var rec = report.Recommendations.FirstOrDefault(r =>
            r.Category == RecommendationCategory.DisableOverlay);

        Assert.NotNull(rec);
        Assert.Contains("overlay", rec.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_DriverRollback_WhenOneDriverBetter()
    {
        var driverV1 = AmdDriver("24.2.1", BaseTime.AddMonths(-3));
        var driverV2 = AmdDriver("24.8.1", BaseTime);

        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery, drivers: [driverV1]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery, drivers: [driverV2]),
            MakeIncident("S3", BaseTime.AddDays(1).AddHours(2), FailureMode.HardFreeze, drivers: [driverV2]),
            MakeIncident("S4", BaseTime.AddDays(1).AddHours(4), FailureMode.DriverTimeoutRecovery, drivers: [driverV2]),
            MakeIncident("S5", BaseTime.AddDays(1).AddHours(6), FailureMode.HardFreeze, drivers: [driverV2])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        var rec = report.Recommendations.FirstOrDefault(r =>
            r.Category == RecommendationCategory.DriverRollback);

        Assert.NotNull(rec);
        Assert.Contains("24.8.1", rec.Title);
    }

    // ---- Report Structure ----

    [Fact]
    public void Analyze_Report_ContainsSummary()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.DriverTimeoutRecovery)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
        Assert.Equal(2, report.TotalIncidents);
    }

    [Fact]
    public void Analyze_Recommendations_SortedByPriority()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.HardwareError,
                timeline: [WheaEntry(BaseTime.AddMinutes(-1))]),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.HardwareError,
                timeline: [WheaEntry(BaseTime.AddDays(1).AddMinutes(-1))]),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.DriverTimeoutRecovery,
                timeline: [TdrEntry(BaseTime.AddDays(2).AddMinutes(-1))]),
            MakeIncident("S4", BaseTime.AddDays(3), FailureMode.DriverTimeoutRecovery,
                timeline: [TdrEntry(BaseTime.AddDays(3).AddMinutes(-1))])
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);
        var sorted = report.Recommendations.OrderByDescending(r => r.Priority).ToList();

        Assert.Equal(sorted[0].Priority, report.Recommendations[0].Priority);
    }

    [Fact]
    public void Analyze_StabilityTrend_ContainsModeBreakdown()
    {
        var incidents = new[]
        {
            MakeIncident("S1", BaseTime, FailureMode.DriverTimeoutRecovery),
            MakeIncident("S2", BaseTime.AddDays(1), FailureMode.HardFreeze),
            MakeIncident("S3", BaseTime.AddDays(2), FailureMode.DriverTimeoutRecovery)
        };

        var report = CrossIncidentAnalyzer.Analyze(incidents);

        Assert.Single(report.StabilityTrends);
        Assert.NotEmpty(report.StabilityTrends[0].ModeBreakdown);
    }
}