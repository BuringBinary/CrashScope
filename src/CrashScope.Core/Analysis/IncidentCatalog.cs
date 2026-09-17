using System.Text.Json;

namespace CrashScope.Core.Analysis;

public sealed record IncidentCatalogEntry(
    string Id,
    DateTimeOffset Timestamp,
    string LeadingMode,
    double Confidence,
    int HypothesisCount,
    bool HasSummary,
    bool HasTimeline);

/// <summary>
/// Enumerates and reads incident bundles on disk:
/// <c>&lt;root&gt;/&lt;id&gt;/{analysis.json, summary.md, timeline.md}</c>.
/// Read-only and corruption-tolerant: a bundle that fails to parse is skipped,
/// never fatal. Shared by the Desktop incident list and the agent's RPC plane.
/// </summary>
public static class IncidentCatalog
{
    public static IReadOnlyList<IncidentCatalogEntry> List(string incidentsRoot)
    {
        if (!Directory.Exists(incidentsRoot))
            return [];

        var result = new List<IncidentCatalogEntry>();
        foreach (var dir in Directory.EnumerateDirectories(incidentsRoot))
        {
            try
            {
                var analysisPath = Path.Combine(dir, "analysis.json");
                if (!File.Exists(analysisPath))
                    continue;

                // Extract only the five fields the catalog needs, schema-tolerantly:
                // a bundle written by a newer or older analyzer revision must still
                // list. Full-model deserialization would couple this reader to every
                // ctor parameter IncidentAnalysis ever grows.
                using var doc = JsonDocument.Parse(File.ReadAllText(analysisPath));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("previousSession", out var session) ||
                    session.ValueKind != JsonValueKind.Object)
                    continue;

                var timestamp = session.TryGetProperty("lastHeartbeatAt", out var heartbeat) &&
                                heartbeat.ValueKind == JsonValueKind.String
                    ? heartbeat.GetDateTimeOffset()
                    : DateTimeOffset.MinValue;

                var leadingMode = "Unclassified";
                double leadingConfidence = 0;
                var hypothesisCount = 0;
                if (root.TryGetProperty("hypotheses", out var hypotheses) && hypotheses.ValueKind == JsonValueKind.Array)
                {
                    hypothesisCount = hypotheses.GetArrayLength();
                    foreach (var hypothesis in hypotheses.EnumerateArray())
                    {
                        var score = hypothesis.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                            ? s.GetDouble()
                            : 0;
                        if (score <= 0)
                            continue;

                        // The analyzer orders hypotheses by score, so the first
                        // positive entry is the leading one (same rule as consumers).
                        if (hypothesis.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String)
                            leadingMode = m.GetString() ?? "Unclassified";
                        if (hypothesis.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number)
                            leadingConfidence = c.GetDouble();
                        break;
                    }
                }

                result.Add(new IncidentCatalogEntry(
                    Path.GetFileName(dir),
                    timestamp,
                    leadingMode,
                    leadingConfidence,
                    hypothesisCount,
                    File.Exists(Path.Combine(dir, "summary.md")),
                    File.Exists(Path.Combine(dir, "timeline.md"))));
            }
            catch
            {
                // Partially written or corrupted bundle: skip it, keep the rest.
            }
        }

        return result.OrderByDescending(e => e.Timestamp).ToList();
    }

    public static string? ReadSummary(string incidentsRoot, string id)
    {
        var path = BundlePath(incidentsRoot, id, "summary.md");
        return path is null || !File.Exists(path) ? null : SafeRead(path);
    }

    public static string? ReadTimeline(string incidentsRoot, string id)
    {
        var path = BundlePath(incidentsRoot, id, "timeline.md");
        return path is null || !File.Exists(path) ? null : SafeRead(path);
    }

    /// <summary>
    /// Bundle paths are always <c>root/id/file</c>; ids arriving from outside this
    /// process are validated here as defense in depth (the RPC layer checks too).
    /// </summary>
    private static string? BundlePath(string incidentsRoot, string id, string fileName)
    {
        if (string.IsNullOrWhiteSpace(id) || Path.GetFileName(id) != id || id.Contains("..", StringComparison.Ordinal))
            return null;

        return Path.Combine(incidentsRoot, id, fileName);
    }

    private static string? SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }
}
