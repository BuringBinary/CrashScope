using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public static class RecommendationEngine
{
    public static IReadOnlyList<Recommendation> Generate(
        IReadOnlyList<PatternFinding> patterns,
        IReadOnlyList<DriverCorrelation> driverCorrelations,
        IReadOnlyList<StabilityTrend> trends)
    {
        var recommendations = new List<Recommendation>();

        recommendations.AddRange(GenerateDriverRecommendations(driverCorrelations, patterns, trends));
        recommendations.AddRange(GenerateVirtualDisplayRecommendations(patterns));
        recommendations.AddRange(GenerateOverlayRecommendations(patterns));
        recommendations.AddRange(GenerateWheaRecommendations(patterns));
        recommendations.AddRange(GenerateTdrRecommendations(patterns));
        recommendations.AddRange(GenerateGpuLoadRecommendations(patterns));
        recommendations.AddRange(GenerateStabilityRecommendations(trends, patterns));

        return recommendations
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Confidence)
            .ToList();
    }

    private static IReadOnlyList<Recommendation> GenerateDriverRecommendations(
        IReadOnlyList<DriverCorrelation> drivers,
        IReadOnlyList<PatternFinding> patterns,
        IReadOnlyList<StabilityTrend> trends)
    {
        if (drivers.Count == 0) return [];

        var recommendations = new List<Recommendation>();

        var highFailureDriver = drivers.OrderByDescending(d => d.FailuresPerDay).FirstOrDefault();
        if (highFailureDriver != null && highFailureDriver.FailuresPerDay >= 0.5)
        {
            recommendations.Add(new Recommendation(
                RecommendationCategory.UpdateDriver,
                "Consider updating or rolling back the GPU driver",
                $"{highFailureDriver.AdapterName} driver {highFailureDriver.DriverVersion} is associated with {highFailureDriver.IncidentCount} incidents ({highFailureDriver.FailuresPerDay:F1}/day). Check for a newer stable version from {highFailureDriver.Vendor}.",
                90,
                Math.Min(1.0, highFailureDriver.FailuresPerDay),
                []));
        }

        var stableDriver = drivers.OrderBy(d => d.FailuresPerDay).FirstOrDefault(d => d.IncidentCount >= 1);
        var unstableDriver = drivers.OrderByDescending(d => d.FailuresPerDay).FirstOrDefault(d => d.IncidentCount >= 2);

        if (stableDriver != null && unstableDriver != null &&
            stableDriver.DriverVersion != unstableDriver.DriverVersion &&
            unstableDriver.FailuresPerDay > stableDriver.FailuresPerDay * 2)
        {
            recommendations.Add(new Recommendation(
                RecommendationCategory.DriverRollback,
                $"Roll back GPU driver from {unstableDriver.DriverVersion} to {stableDriver.DriverVersion}",
                $"Driver {unstableDriver.DriverVersion} has {unstableDriver.FailuresPerDay:F1} failures/day while {stableDriver.DriverVersion} has only {stableDriver.FailuresPerDay:F1}/day. Consider rolling back to the more stable version.",
                85,
                0.8,
                []));
        }

        var isWorsening = trends.Any(t => t.TrendDirection == "Worsening");
        var hasTdr = patterns.Any(p => p.PatternType == "TdrPattern");

        if (hasTdr && isWorsening && drivers.Count > 0)
        {
            var latestDriver = drivers.OrderByDescending(d => d.DriverDate).FirstOrDefault();
            if (latestDriver != null)
                recommendations.Add(new Recommendation(
                    RecommendationCategory.DriverRollback,
                    "Try an older GPU driver version",
                    $"TDR events are increasing with driver {latestDriver.DriverVersion}. Try a WHQL-certified older driver from {latestDriver.Vendor}.",
                    80,
                    0.75,
                    []));
        }

        return recommendations;
    }

    private static IReadOnlyList<Recommendation> GenerateVirtualDisplayRecommendations(
        IReadOnlyList<PatternFinding> patterns)
    {
        var virtualDisplay = patterns.FirstOrDefault(p => p.PatternType == "VirtualDisplay");
        if (virtualDisplay == null) return [];

        return
        [
            new Recommendation(
                RecommendationCategory.DisableVirtualDisplay,
                "Disable virtual display drivers",
                $"A virtual display driver was present in {virtualDisplay.OccurrenceCount} of {virtualDisplay.TotalIncidents} incidents. Apps like remote desktop, screen mirroring, or virtual monitor tools can cause GPU instability. Try disabling them temporarily to see if stability improves.",
                75,
                virtualDisplay.Confidence,
                virtualDisplay.IncidentIds)
        ];
    }

    private static IReadOnlyList<Recommendation> GenerateOverlayRecommendations(
        IReadOnlyList<PatternFinding> patterns)
    {
        var overlay = patterns.FirstOrDefault(p =>
            p.PatternType == "CommonProcess" &&
            p.Description.Contains("Overlay", StringComparison.OrdinalIgnoreCase));

        if (overlay == null) return [];

        return
        [
            new Recommendation(
                RecommendationCategory.DisableOverlay,
                "Disable in-game overlays",
                $"{overlay.Description} Try disabling overlays (Steam, Discord, GeForce Experience, etc.) — they are a common trigger for GPU driver timeouts.",
                70,
                overlay.Confidence,
                overlay.IncidentIds)
        ];
    }

    private static IReadOnlyList<Recommendation> GenerateWheaRecommendations(
        IReadOnlyList<PatternFinding> patterns)
    {
        var whea = patterns.FirstOrDefault(p => p.PatternType == "WheaPattern");
        if (whea == null) return [];

        return
        [
            new Recommendation(
                RecommendationCategory.General,
                "Investigate hardware — WHEA errors detected",
                $"Windows Hardware Error Architecture (WHEA) events detected in {whea.OccurrenceCount} incidents. This suggests a potential hardware issue. Check: GPU seating, PCIe power cables, PSU stability, GPU temperatures.",
                95,
                whea.Confidence,
                whea.IncidentIds)
        ];
    }

    private static IReadOnlyList<Recommendation> GenerateTdrRecommendations(
        IReadOnlyList<PatternFinding> patterns)
    {
        var tdr = patterns.FirstOrDefault(p => p.PatternType == "TdrPattern");
        if (tdr == null) return [];

        var recommendations = new List<Recommendation>
        {
            new(
                RecommendationCategory.DisableHardwareGpuScheduling,
                "Disable Hardware-accelerated GPU Scheduling",
                $"TDR events detected in {tdr.OccurrenceCount} incidents. Try disabling HAGS in Windows Settings → Graphics → Default graphics settings. This is a known trigger for AMD driver timeouts.",
                70,
                tdr.Confidence * 0.85,
                tdr.IncidentIds),
            new(
                RecommendationCategory.General,
                "Increase TDR delay (TdrDelay registry key)",
                $"Multiple TDR events suggest the GPU needs more time to recover. Set HKEY_LOCAL_MACHINE\\System\\CurrentControlSet\\Control\\GraphicsDrivers\\TdrDelay to 10 (seconds) to give the driver more recovery time.",
                60,
                tdr.Confidence * 0.7,
                tdr.IncidentIds)
        };

        return recommendations;
    }

    private static IReadOnlyList<Recommendation> GenerateGpuLoadRecommendations(
        IReadOnlyList<PatternFinding> patterns)
    {
        var highLoad = patterns.FirstOrDefault(p => p.PatternType == "HighGpuLoad");
        if (highLoad == null) return [];

        return
        [
            new Recommendation(
                RecommendationCategory.ReduceGpuLoad,
                "Reduce GPU load before crashes",
                $"GPU load was ≥80% in {highLoad.OccurrenceCount} incidents. Try capping frame rate, lowering graphics settings, or undervolting the GPU to reduce thermal stress.",
                55,
                highLoad.Confidence,
                highLoad.IncidentIds)
        ];
    }

    private static IReadOnlyList<Recommendation> GenerateStabilityRecommendations(
        IReadOnlyList<StabilityTrend> trends,
        IReadOnlyList<PatternFinding> patterns)
    {
        var trend = trends.FirstOrDefault();
        if (trend == null) return [];

        if (trend.StabilityScore <= 35 && trend.IncidentCount >= 3)
        {
            return
            [
                new Recommendation(
                    RecommendationCategory.CheckThermals,
                    "Check GPU thermals and airflow",
                    $"System stability is critically low (score: {trend.StabilityScore}/100, MTBF: {trend.MtbfHours:F1}h). Monitor GPU temperatures under load. Clean dust filters, ensure fans are spinning, consider repasting the GPU.",
                    88,
                    0.9,
                    [])
            ];
        }

        if (trend.TrendDirection == "Worsening" && trend.IncidentCount >= 3)
        {
            return
            [
                new Recommendation(
                    RecommendationCategory.General,
                    "Stability is declining — review recent changes",
                    $"Stability score is {trend.StabilityScore}/100 and trending {trend.TrendDirection.ToLowerInvariant()}. Review any recent driver updates, software installations, or Windows updates that may coincide with the increase in incidents.",
                    65,
                    0.7,
                    [])
            ];
        }

        return [];
    }
}