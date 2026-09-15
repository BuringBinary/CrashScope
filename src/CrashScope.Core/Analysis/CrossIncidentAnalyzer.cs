using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public static class CrossIncidentAnalyzer
{
    public static CrossIncidentReport Analyze(IReadOnlyList<IncidentAnalysis> incidents)
    {
        if (incidents.Count == 0)
            return new CrossIncidentReport(
                DateTimeOffset.UtcNow, 0, [], [], [], [],
                "No incidents available for cross-incident analysis.");

        var patterns = MinePatterns(incidents);
        var driverCorrelations = CorrelateDrivers(incidents);
        var trends = CalculateTrends(incidents);
        var recommendations = RecommendationEngine.Generate(patterns, driverCorrelations, trends);

        var summary = BuildSummary(patterns, driverCorrelations, trends);

        return new CrossIncidentReport(
            DateTimeOffset.UtcNow,
            incidents.Count,
            patterns,
            driverCorrelations,
            trends,
            recommendations,
            summary);
    }

    private static IReadOnlyList<PatternFinding> MinePatterns(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var findings = new List<PatternFinding>();

        findings.AddRange(MineCommonProcesses(incidents));
        findings.AddRange(MineCommonDisplayTopology(incidents));
        findings.AddRange(MineCommonFailureModes(incidents));
        findings.AddRange(MineTemporalClusters(incidents));
        findings.AddRange(MineCommonEtwPatterns(incidents));
        findings.AddRange(MineCommonGpuState(incidents));

        return findings;
    }

    private static IReadOnlyList<PatternFinding> MineCommonProcesses(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var processCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var incident in incidents)
        {
            var sessionId = incident.PreviousSession.SessionId;
            var apps = incident.Graphics?.GraphicsApps ?? [];

            foreach (var app in apps)
            {
                var key = $"{app.ProcessName}|{app.Category}";
                if (!processCounts.ContainsKey(key))
                    processCounts[key] = [];
                processCounts[key].Add(sessionId);
            }
        }

        return processCounts
            .Where(kvp => kvp.Value.Count >= 2)
            .Select(kvp =>
            {
                var parts = kvp.Key.Split('|');
                var cat = Enum.Parse<GraphicsAppCategory>(parts[1]);
                var desc = cat switch
                {
                    GraphicsAppCategory.GameOverlay => $"Overlay process '{parts[0]}' was running",
                    GraphicsAppCategory.RemoteControl => $"Remote control process '{parts[0]}' was running",
                    GraphicsAppCategory.GameLauncher => $"Game launcher '{parts[0]}' was running",
                    GraphicsAppCategory.HardwareAcceleratedApp => $"Hardware-accelerated app '{parts[0]}' was running",
                    GraphicsAppCategory.CaptureTool => $"Capture tool '{parts[0]}' was running",
                    _ => $"Process '{parts[0]}' was running"
                };
                return new PatternFinding(
                    "CommonProcess",
                    $"{desc} in {kvp.Value.Count} of {incidents.Count} incidents.",
                    kvp.Value.Count,
                    incidents.Count,
                    (double)kvp.Value.Count / incidents.Count,
                    kvp.Value);
            })
            .OrderByDescending(p => p.Confidence)
            .ToList();
    }

    private static IReadOnlyList<PatternFinding> MineCommonDisplayTopology(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var virtualDisplayIncidents = new List<string>();
        var multiMonitorIncidents = new List<string>();

        foreach (var incident in incidents)
        {
            var sessionId = incident.PreviousSession.SessionId;
            var changes = incident.Graphics?.DisplayChanges ?? [];

            foreach (var dc in changes)
            {
                if (dc.After.Any(d => d.IsVirtual))
                    virtualDisplayIncidents.Add(sessionId);
                if (dc.After.Count >= 2)
                    multiMonitorIncidents.Add(sessionId);
            }

            if (changes.Count == 0 && incident.Graphics?.LatestDisplayChange != null)
            {
                var latest = incident.Graphics.LatestDisplayChange;
                if (latest.After.Any(d => d.IsVirtual))
                    virtualDisplayIncidents.Add(sessionId);
                if (latest.After.Count >= 2)
                    multiMonitorIncidents.Add(sessionId);
            }
        }

        var findings = new List<PatternFinding>();

        var virtualUnique = virtualDisplayIncidents.Distinct().ToList();
        if (virtualUnique.Count >= 2)
            findings.Add(new PatternFinding(
                "VirtualDisplay",
                $"Virtual display driver was present in {virtualUnique.Count} of {incidents.Count} incidents.",
                virtualUnique.Count, incidents.Count,
                (double)virtualUnique.Count / incidents.Count,
                virtualUnique));

        var multiUnique = multiMonitorIncidents.Distinct().ToList();
        if (multiUnique.Count >= 2)
            findings.Add(new PatternFinding(
                "MultiMonitor",
                $"Multiple monitors connected in {multiUnique.Count} of {incidents.Count} incidents.",
                multiUnique.Count, incidents.Count,
                (double)multiUnique.Count / incidents.Count,
                multiUnique));

        return findings;
    }

    private static IReadOnlyList<PatternFinding> MineCommonFailureModes(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var modeCounts = new Dictionary<FailureMode, int>();
        var modeIncidentIds = new Dictionary<FailureMode, List<string>>();

        foreach (var incident in incidents)
        {
            var topHypothesis = incident.Hypotheses
                .OrderByDescending(h => h.Confidence)
                .FirstOrDefault();

            if (topHypothesis == null) continue;

            if (!modeCounts.ContainsKey(topHypothesis.Mode))
                modeCounts[topHypothesis.Mode] = 0;
            modeCounts[topHypothesis.Mode]++;

            if (!modeIncidentIds.ContainsKey(topHypothesis.Mode))
                modeIncidentIds[topHypothesis.Mode] = [];
            modeIncidentIds[topHypothesis.Mode].Add(incident.PreviousSession.SessionId);
        }

        return modeCounts
            .Select(kvp => new PatternFinding(
                "FailureMode",
                $"{kvp.Key} occurred {kvp.Value} times ({kvp.Value * 100 / incidents.Count}% of incidents).",
                kvp.Value,
                incidents.Count,
                (double)kvp.Value / incidents.Count,
                modeIncidentIds[kvp.Key]))
            .OrderByDescending(p => p.OccurrenceCount)
            .ToList();
    }

    private static IReadOnlyList<PatternFinding> MineTemporalClusters(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var findings = new List<PatternFinding>();
        if (incidents.Count < 3) return findings;

        var sessions = incidents
            .OrderBy(i => i.PreviousSession.LastHeartbeatAt)
            .ToList();

        var clusterWindow = TimeSpan.FromHours(24);
        var clusters = new List<List<IncidentAnalysis>>();
        var current = new List<IncidentAnalysis> { sessions[0] };

        for (var i = 1; i < sessions.Count; i++)
        {
            var gap = sessions[i].PreviousSession.LastHeartbeatAt - current[^1].PreviousSession.LastHeartbeatAt;
            if (gap <= clusterWindow)
            {
                current.Add(sessions[i]);
            }
            else
            {
                if (current.Count >= 2) clusters.Add(current);
                current = [sessions[i]];
            }
        }
        if (current.Count >= 2) clusters.Add(current);

        foreach (var cluster in clusters)
        {
            var ids = cluster.Select(c => c.PreviousSession.SessionId).ToList();
            var span = cluster[^1].PreviousSession.LastHeartbeatAt - cluster[0].PreviousSession.LastHeartbeatAt;
            findings.Add(new PatternFinding(
                "TemporalCluster",
                $"{cluster.Count} incidents occurred within {span.TotalHours:F1} hours.",
                cluster.Count,
                incidents.Count,
                0.7,
                ids));
        }

        return findings;
    }

    private static IReadOnlyList<PatternFinding> MineCommonEtwPatterns(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var findings = new List<PatternFinding>();

        var tdrIncidentIds = new List<string>();
        var wheaIncidentIds = new List<string>();
        var powerIncidentIds = new List<string>();

        foreach (var incident in incidents)
        {
            var sessionId = incident.PreviousSession.SessionId;
            var entries = incident.Timeline;

            if (entries.Any(e => e.Category == "ETW-GPU" && e.Description.Contains("Tdr")))
                tdrIncidentIds.Add(sessionId);

            if (entries.Any(e => e.Category == "ETW-WHEA"))
                wheaIncidentIds.Add(sessionId);

            if (entries.Any(e => e.Category == "ETW-Power"))
                powerIncidentIds.Add(sessionId);
        }

        if (tdrIncidentIds.Count >= 2)
            findings.Add(new PatternFinding(
                "TdrPattern",
                $"TDR events detected in {tdrIncidentIds.Count} of {incidents.Count} incidents.",
                tdrIncidentIds.Count, incidents.Count,
                (double)tdrIncidentIds.Count / incidents.Count,
                tdrIncidentIds));

        if (wheaIncidentIds.Count >= 2)
            findings.Add(new PatternFinding(
                "WheaPattern",
                $"WHEA hardware errors in {wheaIncidentIds.Count} of {incidents.Count} incidents.",
                wheaIncidentIds.Count, incidents.Count,
                (double)wheaIncidentIds.Count / incidents.Count,
                wheaIncidentIds));

        if (powerIncidentIds.Count >= 2)
            findings.Add(new PatternFinding(
                "PowerPattern",
                $"Power-related ETW events in {powerIncidentIds.Count} of {incidents.Count} incidents.",
                powerIncidentIds.Count, incidents.Count,
                (double)powerIncidentIds.Count / incidents.Count,
                powerIncidentIds));

        return findings;
    }

    private static IReadOnlyList<PatternFinding> MineCommonGpuState(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var findings = new List<PatternFinding>();
        var highGpuLoadIds = new List<string>();

        foreach (var incident in incidents)
        {
            if (incident.FinalGpuLoadPercent >= 80f)
                highGpuLoadIds.Add(incident.PreviousSession.SessionId);
        }

        if (highGpuLoadIds.Count >= 2)
            findings.Add(new PatternFinding(
                "HighGpuLoad",
                $"GPU load was ≥80% at last sample in {highGpuLoadIds.Count} of {incidents.Count} incidents.",
                highGpuLoadIds.Count, incidents.Count,
                (double)highGpuLoadIds.Count / incidents.Count,
                highGpuLoadIds));

        return findings;
    }

    private static IReadOnlyList<DriverCorrelation> CorrelateDrivers(IReadOnlyList<IncidentAnalysis> incidents)
    {
        var driverIncidents = new Dictionary<string, List<IncidentAnalysis>>();

        foreach (var incident in incidents)
        {
            var drivers = incident.Graphics?.DriverInventory ?? [];
            if (drivers.Count == 0) continue;

            foreach (var d in drivers)
            {
                var key = $"{d.AdapterName}|{d.DriverVersion ?? "Unknown"}";
                if (!driverIncidents.ContainsKey(key))
                    driverIncidents[key] = [];
                driverIncidents[key].Add(incident);
            }
        }

        if (driverIncidents.Count == 0)
            return [];

        var allDriverVersions = driverIncidents.Keys.ToList();

        return driverIncidents
            .Select(kvp =>
            {
                var incidentsForDriver = kvp.Value;
                var parts = kvp.Key.Split('|');
                var adapter = parts[0];
                var version = parts[1];
                var rep = incidentsForDriver[0].Graphics?.DriverInventory
                    .FirstOrDefault(d => (d.DriverVersion ?? "Unknown") == version);

                var timestamps = incidentsForDriver
                    .Select(i => i.PreviousSession.LastHeartbeatAt)
                    .OrderBy(t => t)
                    .ToList();

                var firstSeen = timestamps[0];
                var lastSeen = timestamps[^1];
                var daysActive = Math.Max(1, (lastSeen - firstSeen).TotalDays);

                var failureModes = incidentsForDriver
                    .SelectMany(i => i.Hypotheses)
                    .OrderByDescending(h => h.Confidence)
                    .Take(1)
                    .Select(h => h.Mode.ToString())
                    .Distinct()
                    .ToList();

                return new DriverCorrelation(
                    version,
                    adapter,
                    rep?.Vendor ?? "Unknown",
                    rep?.DriverDate,
                    incidentsForDriver.Count,
                    (int)Math.Ceiling(daysActive),
                    IncidentsPerDay(incidentsForDriver.Count, daysActive),
                    failureModes);
            })
            .OrderByDescending(d => d.FailuresPerDay)
            .ToList();
    }

    private static double IncidentsPerDay(int count, double days) =>
        Math.Round(count / days, 2);

    private static IReadOnlyList<StabilityTrend> CalculateTrends(IReadOnlyList<IncidentAnalysis> incidents)
    {
        if (incidents.Count < 2)
        {
            var only = incidents.Count == 1 ? incidents[0] : null;
            return
            [
                new StabilityTrend(
                    only?.PreviousSession.StartedAt ?? DateTimeOffset.UtcNow.AddDays(-7),
                    only?.PreviousSession.LastHeartbeatAt ?? DateTimeOffset.UtcNow,
                    incidents.Count,
                    0,
                    incidents.Count == 0 ? 100 : 70,
                    "Stable",
                    [])
            ];
        }

        var ordered = incidents.OrderBy(i => i.PreviousSession.LastHeartbeatAt).ToList();
        var totalSpan = ordered[^1].PreviousSession.LastHeartbeatAt - ordered[0].PreviousSession.LastHeartbeatAt;
        var totalHours = Math.Max(1, totalSpan.TotalHours);

        var mtbf = totalHours / ordered.Count;
        var stabilityScore = CalculateStabilityScore(ordered.Count, mtbf);

        var modeBreakdown = ordered
            .SelectMany(i => i.Hypotheses)
            .GroupBy(h => h.Mode)
            .Select(g => new FailureModeCount(g.Key, g.Count()))
            .OrderByDescending(m => m.Count)
            .ToList();

        var trendDirection = DetermineTrend(ordered);

        return
        [
            new StabilityTrend(
                ordered[0].PreviousSession.LastHeartbeatAt,
                ordered[^1].PreviousSession.LastHeartbeatAt,
                ordered.Count,
                Math.Round(mtbf, 1),
                stabilityScore,
                trendDirection,
                modeBreakdown)
        ];
    }

    private static int CalculateStabilityScore(int incidentCount, double mtbfHours)
    {
        if (incidentCount == 0) return 100;
        if (mtbfHours >= 168) return 95;
        if (mtbfHours >= 72) return 80;
        if (mtbfHours >= 24) return 65;
        if (mtbfHours >= 8) return 50;
        if (mtbfHours >= 2) return 35;
        return 20;
    }

    private static string DetermineTrend(IReadOnlyList<IncidentAnalysis> ordered)
    {
        if (ordered.Count < 3) return "Stable";

        var mid = ordered.Count / 2;
        var firstHalf = ordered.Take(mid).ToList();
        var secondHalf = ordered.Skip(mid).ToList();

        var firstSpan = firstHalf[^1].PreviousSession.LastHeartbeatAt - firstHalf[0].PreviousSession.LastHeartbeatAt;
        var secondSpan = secondHalf[^1].PreviousSession.LastHeartbeatAt - secondHalf[0].PreviousSession.LastHeartbeatAt;

        var firstRate = firstSpan.TotalHours > 0 ? firstHalf.Count / firstSpan.TotalHours : 0;
        var secondRate = secondSpan.TotalHours > 0 ? secondHalf.Count / secondSpan.TotalHours : 0;

        if (secondRate > firstRate * 1.5) return "Worsening";
        if (secondRate < firstRate * 0.5) return "Improving";
        return "Stable";
    }

    private static string BuildSummary(
        IReadOnlyList<PatternFinding> patterns,
        IReadOnlyList<DriverCorrelation> drivers,
        IReadOnlyList<StabilityTrend> trends)
    {
        var parts = new List<string>();

        if (trends.Count > 0)
        {
            var t = trends[0];
            parts.Add($"Stability: {t.StabilityScore}/100 ({t.TrendDirection}), {t.IncidentCount} incidents, MTBF {t.MtbfHours:F1}h.");
        }

        var topMode = patterns
            .Where(p => p.PatternType == "FailureMode")
            .OrderByDescending(p => p.OccurrenceCount)
            .FirstOrDefault();

        if (topMode != null)
            parts.Add($"Most common failure: {topMode.Description}");

        var topDriver = drivers.OrderByDescending(d => d.FailuresPerDay).FirstOrDefault();
        if (topDriver != null)
            parts.Add(
                $"Highest failure rate driver: {topDriver.DriverVersion} ({topDriver.FailuresPerDay:F1}/day).");

        if (patterns.Any(p => p.PatternType == "TdrPattern"))
            parts.Add("TDR pattern detected across multiple incidents.");

        return string.Join(" ", parts);
    }
}