using CrashScope.Core.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CrashScope.Desktop;

/// <summary>
/// Builds a per-export redaction vocabulary and applies it consistently, so the same
/// process name always maps to the same <c>[PROCESS_n]</c> token across every file in
/// the diagnostic ZIP.
/// </summary>
/// <remarks>
/// Redaction is vocabulary driven on purpose: replacing every capitalised word would
/// mangle ordinary telemetry text. The vocabulary is seeded from the analysis (graphics
/// apps, per-process GPU/VRAM samples, WER app names in event evidence) and extended by
/// scanning the incident directory's text tails for process-name JSON fields.
/// <para>
/// Window titles are not redacted because CrashScope never records them - the capture
/// layer deliberately drops titles, command lines, paths and content.
/// </para>
/// </remarks>
public sealed partial class ExportRedactor
{
    /// <summary>Redacts nothing; used when the user opts out of redaction.</summary>
    public static ExportRedactor Passthrough { get; } = new();

    private const int MaxVocabulary = 2000;
    private const int MinNameLength = 2;
    private const int MaxNameLength = 120;
    private const int MaxScannedFileBytes = 4 * 1024 * 1024;

    private static readonly string[] RedactableExtensions =
        [".json", ".jsonl", ".md", ".txt", ".csv"];

    [GeneratedRegex(@"(?<profile>[A-Za-z]:\\Users)\\(?<user>[^\\]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UserProfileRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\b")]
    private static partial Regex IpAddressRegex();

    [GeneratedRegex("\"(?<field>processName|appName)\"\\s*:\\s*\"(?<value>[^\"]{1,120})\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProcessNameFieldRegex();

    private readonly Dictionary<string, string> _processTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Regex? _processRegex;
    private readonly Regex? _hostnameRegex;

    private ExportRedactor()
    {
    }

    public ExportRedactor(string incidentDirectory, IncidentAnalysis analysis)
    {
        var vocabulary = BuildVocabulary(incidentDirectory, analysis);
        var index = 1;
        foreach (var name in vocabulary)
            _processTokens[name] = $"[PROCESS_{index++}]";

        if (_processTokens.Count > 0)
        {
            // Longest name first: without word-boundary-safe ordering a short name that is
            // a substring of a longer one (steam vs steamwebhelper) would win the match.
            var alternation = string.Join("|", _processTokens.Keys
                .OrderByDescending(k => k.Length)
                .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(Regex.Escape));
            _processRegex = new Regex($@"\b(?:{alternation})\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        var machineName = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machineName) && machineName.Length >= MinNameLength)
            _hostnameRegex = new Regex($@"\b{Regex.Escape(machineName)}\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>Process-name tokens assigned by this redactor, in match order.</summary>
    public IReadOnlyDictionary<string, string> ProcessTokens => _processTokens;

    public string Redact(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        // Order matters: profile paths first so a username that is also a process name
        // cannot leak through the path, then host/IP, then the process vocabulary.
        content = UserProfileRegex().Replace(content, @"[USERPROFILE]\[USERNAME]");
        if (_hostnameRegex is not null)
            content = _hostnameRegex.Replace(content, "[HOSTNAME]");
        content = IpAddressRegex().Replace(content, "[IP_ADDRESS]");
        if (_processRegex is not null)
            content = _processRegex.Replace(content, m =>
                _processTokens.TryGetValue(m.Value, out var token) ? token : m.Value);

        return content;
    }

    private static IReadOnlyList<string> BuildVocabulary(string incidentDirectory, IncidentAnalysis analysis)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in analysis.Graphics?.GraphicsApps ?? [])
            Add(names, app.ProcessName);

        if (analysis.Graphics?.FinalGpuEngines is { } engines)
            foreach (var process in engines.Processes)
                Add(names, process.ProcessName);

        foreach (var question in analysis.OpenQuestions ?? [])
            CollectFromEventText(names, question);

        foreach (var hypothesis in analysis.Hypotheses ?? [])
            foreach (var evidence in hypothesis.Evidence)
                CollectFromEventText(names, evidence.Description);

        ScanIncidentDirectory(incidentDirectory, names);

        return names
            .OrderByDescending(n => n.Length)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectFromEventText(HashSet<string> names, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (Match match in ProcessNameFieldRegex().Matches(text))
            Add(names, match.Groups["value"].Value);
    }

    private static void ScanIncidentDirectory(string incidentDirectory, HashSet<string> names)
    {
        if (string.IsNullOrWhiteSpace(incidentDirectory) || !Directory.Exists(incidentDirectory))
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(incidentDirectory, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            if (names.Count >= MaxVocabulary)
                return;

            try
            {
                if (!RedactableExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    continue;
                if (new FileInfo(file).Length > MaxScannedFileBytes)
                    continue;

                CollectFromEventText(names, File.ReadAllText(file, Encoding.UTF8));
            }
            catch
            {
                // Best effort: an unreadable tail must not abort the export.
            }
        }
    }

    private static void Add(HashSet<string> names, string? value)
    {
        if (names.Count >= MaxVocabulary || string.IsNullOrWhiteSpace(value))
            return;

        var trimmed = value.Trim();
        if (trimmed.Length < MinNameLength || trimmed.Length > MaxNameLength)
            return;

        names.Add(trimmed);
    }
}

