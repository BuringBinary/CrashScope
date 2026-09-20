using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
// This project enables both WPF and WinForms, so ImplicitUsings pulls in
// System.Windows.Forms and "Application" becomes ambiguous. Pin it to WPF.
using Application = System.Windows.Application;

namespace CrashScope.Desktop.Services;

/// <summary>
/// Owns the UI language. Strings live in <c>Locales/*.json</c>; on load they are flattened
/// into a <see cref="ResourceDictionary"/> (key <c>L.section.key</c>) merged into
/// <see cref="Application.Resources"/>, so XAML binds with <c>{DynamicResource L.nav.home}</c>
/// and re-renders instantly when the dictionary is swapped. Code-behind uses <see cref="T"/> /
/// <see cref="F"/> (or the static <see cref="Loc"/> helper).
/// </summary>
/// <remarks>
/// The persisted preference is one of <see cref="SystemLocale"/> ("follow Windows display
/// language", the default) or a concrete locale code. <see cref="CurrentLocale"/> is always the
/// concrete, effective locale. Shares <c>app_settings.json</c> with the other services via
/// read-modify-write so keys do not clobber each other, and never throws: a missing or corrupt
/// locale file degrades to raw keys, because a translation problem must never stop the recorder.
/// </remarks>
public class LocalizationService
{
    private const string ConfigFileName = "app_settings.json";
    private const string LanguageKey = "language";

    /// <summary>Sentinel preference meaning "follow the Windows display language".</summary>
    public const string SystemLocale = "system";
    public const string DefaultLocale = "en-US";
    public const string ResourceKeyPrefix = "L.";

    private readonly string _localesDir;
    private readonly string _settingsDir;
    private readonly string _settingsPath;
    private readonly Application? _app;

    private string _preference = SystemLocale;
    private string _currentLocale = DefaultLocale;
    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private ResourceDictionary? _resourceDict;

    /// <summary>Effective locale code currently rendered (never <see cref="SystemLocale"/>).</summary>
    public string CurrentLocale => _currentLocale;

    /// <summary>Stored preference: <see cref="SystemLocale"/> or a code from <see cref="AvailableLocales"/>.</summary>
    public string Preference => _preference;

    public bool IsFollowingSystem => _preference == SystemLocale;

    /// <summary>Concrete locale codes shipped in the Locales folder (e.g. en-US, zh-CN).</summary>
    public IReadOnlyList<string> AvailableLocales { get; }

    /// <summary>Raised after the effective locale changed and resources were swapped.</summary>
    public event Action? LocaleChanged;

    public LocalizationService() : this(Application.Current) { }

    public LocalizationService(Application? app)
    {
        _app = app;
        _localesDir = Path.Combine(AppContext.BaseDirectory, "Locales");
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashScope");
        _settingsPath = Path.Combine(_settingsDir, ConfigFileName);

        AvailableLocales = LoadAvailableLocales();
        LoadSettings();
        _currentLocale = Resolve(_preference);
        Apply(_currentLocale);
    }

    // ---- resolution -------------------------------------------------------------------

    /// <summary>
    /// Maps the Windows display language (<see cref="CultureInfo.CurrentUICulture"/>) onto a
    /// shipped locale: any Chinese variant (zh-CN / zh-TW / zh-HK / zh-Hans / zh-Hant) selects
    /// zh-CN because that is the only Chinese file we ship; everything else falls back to en-US.
    /// </summary>
    public string ResolveSystemLocale()
    {
        CultureInfo ui;
        try { ui = CultureInfo.CurrentUICulture; }
        catch { return DefaultLocale; }

        // Exact match first (e.g. a future zh-TW.json), then language-level match.
        if (AvailableLocales.Contains(ui.Name, StringComparer.OrdinalIgnoreCase))
            return AvailableLocales.First(l => string.Equals(l, ui.Name, StringComparison.OrdinalIgnoreCase));

        var lang = ui.TwoLetterISOLanguageName;
        var byLanguage = AvailableLocales.FirstOrDefault(l =>
            l.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l, lang, StringComparison.OrdinalIgnoreCase));
        return byLanguage ?? DefaultLocale;
    }

    private string Resolve(string preference)
    {
        if (preference == SystemLocale) return ResolveSystemLocale();
        return AvailableLocales.Contains(preference) ? preference : DefaultLocale;
    }

    private IReadOnlyList<string> LoadAvailableLocales()
    {
        try
        {
            if (!Directory.Exists(_localesDir))
                return new[] { DefaultLocale };

            var list = Directory.GetFiles(_localesDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(f => !string.IsNullOrEmpty(f))
                .Cast<string>()
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return list.Count > 0 ? list.AsReadOnly() : new[] { DefaultLocale };
        }
        catch
        {
            return new[] { DefaultLocale };
        }
    }

    /// <summary>Native display name from the locale file's <c>meta.name</c> (e.g. 简体中文).</summary>
    public string GetLocaleDisplayName(string code)
    {
        if (code == SystemLocale)
            return T("settings.appearance.languageSystem");

        var path = Path.Combine(_localesDir, $"{code}.json");
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            return root?["meta"]?["name"]?.GetValue<string>() ?? code;
        }
        catch
        {
            return code;
        }
    }

    // ---- persistence ------------------------------------------------------------------

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var settings = JsonNode.Parse(File.ReadAllText(_settingsPath))?.AsObject();
            var lang = settings?[LanguageKey]?.GetValue<string>();
            if (lang == SystemLocale || (lang is not null && AvailableLocales.Contains(lang)))
                _preference = lang;
        }
        catch
        {
            // Corrupt settings fall back to "system".
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            JsonObject settings = new();
            if (File.Exists(_settingsPath))
                settings = JsonNode.Parse(File.ReadAllText(_settingsPath))?.AsObject() ?? new JsonObject();

            settings[LanguageKey] = _preference;
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Preferences are best-effort; never break the app over a write failure.
        }
    }

    // ---- switching --------------------------------------------------------------------

    /// <summary>Sets the preference (<see cref="SystemLocale"/> or a locale code) and applies it.</summary>
    public void SetLocale(string preference)
    {
        if (preference != SystemLocale && !AvailableLocales.Contains(preference))
            return;

        var effective = Resolve(preference);
        var preferenceChanged = _preference != preference;
        _preference = preference;
        if (preferenceChanged) SaveSettings();

        if (effective == _currentLocale)
            return;

        _currentLocale = effective;
        Apply(effective);
        LocaleChanged?.Invoke();
    }

    private void Apply(string code)
    {
        _strings = LoadStrings(code);

        if (_app is null) return;
        try
        {
            var resources = _app.Resources;
            if (_resourceDict is not null)
                resources.MergedDictionaries.Remove(_resourceDict);

            var dict = new ResourceDictionary();
            foreach (var (key, value) in _strings)
                dict[ResourceKeyPrefix + key] = value;

            resources.MergedDictionaries.Add(dict);
            _resourceDict = dict;
        }
        catch
        {
            // If the dictionary swap fails the T()/F() path still works; XAML shows old text.
        }
    }

    // ---- lookup -----------------------------------------------------------------------

    /// <summary>Returns the string for a dotted key, or the key itself when missing.</summary>
    public string T(string key)
        => _strings.TryGetValue(key, out var value) ? value : key;

    /// <summary>Formats a localized composite string (<c>string.Format</c> semantics).</summary>
    public string F(string key, params object?[] args)
    {
        var template = T(key);
        try { return string.Format(CultureInfo.CurrentCulture, template, args); }
        catch (FormatException) { return template; }
    }

    private Dictionary<string, string> LoadStrings(string code)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        // Fallback layer first so a key missing from a translation shows English, not the raw key.
        if (code != DefaultLocale)
            Flatten(LoadJson(DefaultLocale), string.Empty, result);
        Flatten(LoadJson(code), string.Empty, result);
        return result;
    }

    private JsonObject? LoadJson(string code)
    {
        var path = Path.Combine(_localesDir, $"{code}.json");
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        }
        catch
        {
            return null;
        }
    }

    private static void Flatten(JsonObject? node, string prefix, Dictionary<string, string> into)
    {
        if (node is null) return;
        foreach (var (name, child) in node)
        {
            var key = prefix.Length == 0 ? name : prefix + "." + name;
            switch (child)
            {
                case JsonObject obj:
                    Flatten(obj, key, into);
                    break;
                case JsonValue val when val.TryGetValue<string>(out var s):
                    into[key] = s;
                    break;
                case JsonValue val:
                    into[key] = val.ToJsonString().Trim('"');
                    break;
            }
        }
    }
}

/// <summary>Terse static access for code-behind: <c>Loc.T("key")</c> / <c>Loc.F("key", a, b)</c>.</summary>
public static class Loc
{
    private static LocalizationService? Service => (Application.Current as App)?.Localization;

    public static string T(string key) => Service?.T(key) ?? key;

    public static string F(string key, params object?[] args)
        => Service?.F(key, args) ?? key;
}
