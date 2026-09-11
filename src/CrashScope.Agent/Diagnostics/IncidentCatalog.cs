using System.Globalization;
using CrashScope.Contracts;

namespace CrashScope.Agent.Diagnostics;

public static class IncidentCatalog
{
    public static IReadOnlyList<IncidentSummaryDto> List(string incidentsRoot)
    {
        if (!Directory.Exists(incidentsRoot))
            return Array.Empty<IncidentSummaryDto>();

        var output = new List<IncidentSummaryDto>();
        foreach (var directory in Directory.EnumerateDirectories(incidentsRoot)
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                output.Add(Read(directory));
            }
            catch
            {
                // One damaged incident should not make the entire catalog unavailable.
            }
        }

        return output;
    }

    private static IncidentSummaryDto Read(string directory)
    {
        var id = Path.GetFileName(directory);
        var summaryPath = Path.Combine(directory, "summary.md");
        var lines = File.Exists(summaryPath) ? File.ReadAllLines(summaryPath) : Array.Empty<string>();

        return new IncidentSummaryDto(
            Id: id,
            LastHeartbeatAt: ParseTimestamp(lines, "- Last heartbeat:"),
            SessionId: ParseBacktickValue(lines, "- Previous session:"),
            KernelPower41: ParseBool(lines, "- Kernel-Power 41 found:"),
            UnexpectedShutdown6008: ParseBool(lines, "- Unexpected shutdown 6008 found:"),
            WheaEvidence: ParseBool(lines, "- WHEA evidence found:"),
            DisplayTdrEvidence: ParseBool(lines, "- Display/TDR evidence found:"),
            DirectoryPath: directory);
    }

    private static string? ParseBacktickValue(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
            return null;

        var first = line.IndexOf('`');
        var last = line.LastIndexOf('`');
        return first >= 0 && last > first ? line[(first + 1)..last] : null;
    }

    private static bool ParseBool(IEnumerable<string> lines, string prefix) =>
        bool.TryParse(ParseBacktickValue(lines, prefix), out var value) && value;

    private static DateTimeOffset? ParseTimestamp(IEnumerable<string> lines, string prefix)
    {
        var value = ParseBacktickValue(lines, prefix);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : null;
    }
}
