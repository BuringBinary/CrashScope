using System.IO;
using System.Text.Json;
using CrashScope.Core.Analysis;
using CrashScope.Core.Models;

namespace CrashScope.Desktop;

public sealed record IncidentReadIssue(string? RecordId, string Code, string Detail);
public sealed record IncidentReadResult(IReadOnlyList<IncidentInfo> Items, IReadOnlyList<IncidentReadIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;
    public bool IsCompleteEmpty => Items.Count == 0 && !HasIssues;
    public static IncidentReadResult Success(IReadOnlyList<IncidentInfo> items) => new(items, Array.Empty<IncidentReadIssue>());
    public static IncidentReadResult Failed(Exception error) => new(Array.Empty<IncidentInfo>(),
        new[] { new IncidentReadIssue(null, "rootUnavailable", error.Message) });

    public string Summary(Func<string, string> t, string locale)
    {
        if (!HasIssues) return Items.Count == 0 ? t("readUi.empty") : "";
        if (Issues.Any(i => i.RecordId is null)) return t("readUi.rootUnavailable");
        var format = Items.Count == 0 ? "readUi.failed" : "readUi.partial";
        return string.Format(IncidentPresentation.Culture(locale), t(format), Items.Count, Issues.Count);
    }
    public string Details(Func<string, string> t) => string.Join("\n", Issues.Take(20).Select(i =>
        $"{i.RecordId ?? t("readUi.folder")}: {t("readUi." + i.Code)}\n{i.Detail}"));
}

/// <summary>Read-only, serialized catalog snapshot. Bad entries remain visible as issues, never silently healthy emptiness.</summary>
public sealed class IncidentCatalogReader
{
    private readonly Func<string, string[]> _enumerate;
    private readonly Func<string, IncidentAnalysis> _load;
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime Modified, long Length, IncidentAnalysis Analysis)> _cache = new();

    public IncidentCatalogReader(Func<string, string[]>? enumerate = null, Func<string, IncidentAnalysis>? load = null)
    {
        _enumerate = enumerate ?? Directory.GetDirectories;
        _load = load ?? LoadAnalysis;
    }

    public IncidentReadResult Read(string root)
    {
        lock (_gate)
        {
            string[] directories;
            try { directories = _enumerate(root); }
            catch (DirectoryNotFoundException) { return IncidentReadResult.Success(Array.Empty<IncidentInfo>()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { return IncidentReadResult.Failed(ex); }
            var items = new List<IncidentInfo>();
            var issues = new List<IncidentReadIssue>();
            foreach (var directory in directories)
            {
                var id = Path.GetFileName(directory);
                try
                {
                    var analysis = _load(directory);
                    if (analysis.PreviousSession is null || analysis.Hypotheses is null)
                        throw new InvalidDataException("Required session or hypothesis data is missing.");
                    var leading = analysis.Hypotheses.FirstOrDefault(h => h.Score > 0);
                    items.Add(new IncidentInfo(id, directory, analysis.PreviousSession.LastHeartbeatAt,
                        leading?.Mode.ToString() ?? "Unclassified", leading?.Confidence ?? 0,
                        analysis.OpenQuestions ?? Array.Empty<string>(), analysis));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    var code = ex switch
                    {
                        FileNotFoundException or DirectoryNotFoundException => "missingAnalysis",
                        UnauthorizedAccessException or System.Security.SecurityException => "accessDenied",
                        JsonException or InvalidDataException => "invalidAnalysis",
                        IOException => "readError",
                        _ => "evaluationError"
                    };
                    issues.Add(new IncidentReadIssue(id, code, ex.Message));
                }
            }
            items.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return new(items.ToArray(), issues.ToArray());
        }
    }

    private IncidentAnalysis LoadAnalysis(string directory)
    {
        var path = Path.Combine(directory, "analysis.json");
        // File.Exists/Directory.Exists suppress access errors; use operations that preserve them.
        var info = new FileInfo(path);
        var length = info.Length;
        var modified = info.LastWriteTimeUtc;
        if (_cache.TryGetValue(path, out var cached) && cached.Modified == modified && cached.Length == length)
            return cached.Analysis;
        var stored = JsonSerializer.Deserialize<IncidentAnalysis>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Analysis JSON is null.");
        if (stored.PreviousSession is null || stored.Hypotheses is null) throw new InvalidDataException("Incomplete analysis JSON.");
        var analysis = IncidentAnalyzer.ReevaluateReadOnly(stored, directory);
        _cache[path] = (modified, length, analysis);
        return analysis;
    }
}
