using CrashScope.Core.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashScope.Desktop;

/// <summary>
/// Identity strings an export must not reveal. Injected so tests do not depend on the
/// machine that runs them.
/// </summary>
public sealed record ExportIdentity(string? MachineName, string? UserName, string? UserDomainName)
{
    public static ExportIdentity Current => new(
        TryRead(() => Environment.MachineName),
        TryRead(() => Environment.UserName),
        TryRead(() => Environment.UserDomainName));

    private static string? TryRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}

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
/// <para>
/// Most exported files are JSON written with System.Text.Json's default encoder, which
/// doubles backslashes and writes quotes, non-ASCII and control characters as escapes
/// (<c>\u0027</c>, <c>\u5F20</c>, <c>\r\n</c>). Every rule also matches those spellings,
/// keeps the separator it found so the JSON stays valid, and treats a complete escape as
/// a word boundary.
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

    private const string UserToken = "[USERNAME]";
    private const string HostToken = "[HOSTNAME]";
    private const string DomainToken = "[DOMAIN]";

    // Word edges for names that start or end with an ASCII letter, digit or underscore. A
    // complete JSON escape (\u0027, \n) counts as a boundary even though it ends in a letter
    // or digit. Names are not required to have edges on a non-ASCII side, because CJK text
    // puts no spaces between words.
    private const string NameStart = @"(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[bfnrt])|(?<![A-Za-z0-9_]))";
    private const string NameEnd = @"(?![A-Za-z0-9_])";

    // Tokens inserted by an earlier rule are matched first and kept verbatim, so a process
    // called "hostname" cannot turn [HOSTNAME] into [[PROCESS_1]].
    private const string ExistingToken = @"\[(?:USERPROFILE|USERNAME|HOSTNAME|DOMAIN|IP_ADDRESS|PROCESS_\d+)\]";

    // Account and profile-folder names end at characters Windows does not allow in them.
    // In JSON text a non-ASCII name arrives as \uXXXX escapes; escaped quotes still end it.
    private const string EscapedNameChar = @"\\u(?!00(?:22|27))[0-9A-Fa-f]{4}";

    private static readonly string[] RedactableExtensions =
        [".json", ".jsonl", ".md", ".txt", ".csv"];

    // C:\Users\<name>, the JSON-escaped C:\\Users\\<name> and C:/Users/<name>. The
    // replacement reuses the separator it found, so escaped JSON stays parseable.
    [GeneratedRegex(
        @"[A-Za-z]:(?:(?<sep>\\\\)Users\\\\(?:" + EscapedNameChar + @"|[^\\/:*?""<>|\r\n'])+" +
        @"|(?<sep>/)Users/(?:" + EscapedNameChar + @"|[^\\/:*?""<>|\r\n'])+" +
        @"|(?<sep>\\)Users\\[^\\/:*?""<>|\r\n']+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserProfileRegex();

    // ASCII-only edges, so an address directly after CJK text (地址10.0.0.1) still matches.
    [GeneratedRegex(
        @"(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[bfnrt])|(?<![A-Za-z0-9_]))" +
        @"(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?![A-Za-z0-9_])")]
    private static partial Regex IpAddressRegex();

    // The full eight-group form, or any form containing "::", with an optional zone index.
    // Clock times such as 16:21:41 have neither. IPv4-mapped addresses keep their ::ffff:
    // prefix because the IPv4 rule runs first.
    [GeneratedRegex(
        @"(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[bfnrt])|(?<![0-9A-Za-z:]))" +
        @"(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,7}:" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}" +
        @"|[0-9A-Fa-f]{1,4}:(?::[0-9A-Fa-f]{1,4}){1,6}" +
        @"|:(?::[0-9A-Fa-f]{1,4}){1,7})" +
        @"(?:%[0-9A-Za-z]+)?(?![0-9A-Za-z:])")]
    private static partial Regex Ipv6AddressRegex();

    [GeneratedRegex("\"(?<field>processName|appName)\"\\s*:\\s*\"(?<value>[^\"]{1,120})\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProcessNameFieldRegex();

    private readonly bool _enabled;
    private readonly Dictionary<string, string> _processTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _processSpellings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _machineSpellings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Regex? _processRegex;
    private readonly Regex? _accountRegex;
    private readonly Regex? _jsonAccountRegex;
    private readonly Regex? _userNameRegex;
    private readonly Regex? _hostnameRegex;
    private readonly Regex? _domainRegex;

    private ExportRedactor()
    {
    }

    public ExportRedactor(string incidentDirectory, IncidentAnalysis analysis)
        : this(incidentDirectory, analysis, ExportIdentity.Current)
    {
    }

    public ExportRedactor(string incidentDirectory, IncidentAnalysis analysis, ExportIdentity identity)
    {
        _enabled = true;

        var vocabulary = BuildVocabulary(incidentDirectory, analysis);
        var index = 1;
        foreach (var name in vocabulary)
        {
            var token = $"[PROCESS_{index++}]";
            _processTokens[name] = token;
            foreach (var spelling in Spellings(name))
                _processSpellings.TryAdd(spelling, token);
        }
        _processRegex = BuildNameRegex(vocabulary);

        var machine = identity.MachineName;
        var domain = string.Equals(identity.UserDomainName?.Trim(), machine?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : identity.UserDomainName;

        _machineSpellings.UnionWith(Spellings(machine));
        _hostnameRegex = BuildNameRegex([machine]);
        _domainRegex = BuildNameRegex([domain]);
        _userNameRegex = BuildNameRegex([identity.UserName]);
        _accountRegex = BuildAccountRegex([machine, domain], jsonEscaped: false);
        _jsonAccountRegex = BuildAccountRegex([machine, domain], jsonEscaped: true);
    }

    /// <summary>Process-name tokens assigned by this redactor, in match order.</summary>
    public IReadOnlyDictionary<string, string> ProcessTokens => _processTokens;

    public string Redact(string? content) => Redact(content, jsonEscaped: false);

    /// <param name="content">Text to redact.</param>
    /// <param name="jsonEscaped">
    /// True for .json/.jsonl text. A single backslash there always starts an escape such as
    /// <c>\r</c> or <c>\u4E0A</c>, so it is never read as a MACHINE\user separator.
    /// </param>
    public string Redact(string? content, bool jsonEscaped)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;
        if (!_enabled)
            return content;

        // Order matters: structured forms first (profile paths, MACHINE\user), because only
        // their context identifies an arbitrary account name; then the known identity words,
        // then addresses, then the process vocabulary. Name rules keep earlier tokens intact.
        content = UserProfileRegex().Replace(content, "[USERPROFILE]${sep}" + UserToken);
        var accounts = jsonEscaped ? _jsonAccountRegex : _accountRegex;
        if (accounts is not null)
            content = accounts.Replace(content, m =>
                (_machineSpellings.Contains(m.Groups["qualifier"].Value) ? HostToken : DomainToken)
                + m.Groups["sep"].Value + UserToken);
        content = ReplaceNames(_userNameRegex, content, _ => UserToken);
        content = ReplaceNames(_hostnameRegex, content, _ => HostToken);
        content = ReplaceNames(_domainRegex, content, _ => DomainToken);
        content = IpAddressRegex().Replace(content, "[IP_ADDRESS]");
        content = Ipv6AddressRegex().Replace(content, "[IP_ADDRESS]");
        content = ReplaceNames(_processRegex, content, value =>
            _processSpellings.TryGetValue(value, out var token) ? token : value);

        return content;
    }

    private static string ReplaceNames(Regex? regex, string content, Func<string, string> replacement) =>
        regex is null
            ? content
            : regex.Replace(content, m => m.Groups["token"].Success ? m.Value : replacement(m.Value));

    private static Regex? BuildNameRegex(IEnumerable<string?> names)
    {
        var alternation = Alternation(names);
        return alternation is null
            ? null
            : new Regex($"(?<token>{ExistingToken})|(?:{alternation})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    // MACHINE\user or DOMAIN\user. Only the qualifier is known up front, so the account name
    // is whatever follows it up to a character Windows does not allow in account names;
    // whitespace and quotes end it too. JSON text only uses the escaped double backslash.
    private static Regex? BuildAccountRegex(IEnumerable<string?> qualifiers, bool jsonEscaped)
    {
        var alternation = Alternation(qualifiers);
        if (alternation is null)
            return null;

        var separator = jsonEscaped ? @"\\\\" : @"\\\\|\\";
        return new Regex(
            $@"(?<qualifier>{alternation})(?<sep>{separator})" +
            $@"(?:{EscapedNameChar}|[^\\/""\[\]:;|=,+*?<>'\s])+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    // One alternative per spelling, longest first: in an alternation the first match wins,
    // so a short name must not pre-empt a longer one that contains it (steam vs steam.exe).
    private static string? Alternation(IEnumerable<string?> names)
    {
        var alternatives = names
            .SelectMany(name => Spellings(name).Select(spelling => (Name: name!.Trim(), Spelling: spelling)))
            .DistinctBy(x => x.Spelling, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Spelling.Length)
            .ThenBy(x => x.Spelling, StringComparer.OrdinalIgnoreCase)
            .Select(x => (IsAsciiWordChar(x.Name[0]) ? NameStart : "")
                + Regex.Escape(x.Spelling)
                + (IsAsciiWordChar(x.Name[^1]) ? NameEnd : ""))
            .ToList();
        return alternatives.Count == 0 ? null : string.Join("|", alternatives);
    }

    private static bool IsAsciiWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    // The name itself plus the spelling System.Text.Json's default encoder writes for it.
    private static IReadOnlyList<string> Spellings(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];

        var trimmed = name.Trim();
        if (trimmed.Length < MinNameLength || trimmed.Length > MaxNameLength)
            return [];

        string escaped;
        try
        {
            escaped = JsonEncodedText.Encode(trimmed).ToString();
        }
        catch (ArgumentException)
        {
            // Invalid UTF-16 (a lone surrogate) has no JSON spelling to look for.
            return [trimmed];
        }
        return escaped == trimmed ? [trimmed] : [trimmed, escaped];
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
            Add(names, UnescapeJson(match.Groups["value"].Value));
    }

    // Values captured from raw JSON text still carry their escapes (\u4E09...). Keep the
    // real name, so its plain and escaped spellings share one token.
    private static string UnescapeJson(string value)
    {
        if (!value.Contains('\\'))
            return value;

        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
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

