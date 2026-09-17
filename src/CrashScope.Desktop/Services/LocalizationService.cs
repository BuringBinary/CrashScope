using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrashScope.Desktop.Services;

public class LocalizationService
{
    private const string ConfigFileName = "app_settings.json";

    private readonly string _localesDir;
    private readonly string _settingsDir;
    private readonly string _settingsPath;

    private string _currentLocale = "en-US";
    private JsonObject? _strings;

    public string CurrentLocale => _currentLocale;
    public IReadOnlyList<string> AvailableLocales { get; }

    public event Action? LocaleChanged;

    public LocalizationService()
    {
        _localesDir = Path.Combine(AppContext.BaseDirectory, "Locales");
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashScope");
        _settingsPath = Path.Combine(_settingsDir, ConfigFileName);

        AvailableLocales = LoadAvailableLocales();
        LoadSettings();
    }

    private IReadOnlyList<string> LoadAvailableLocales()
    {
        if (!Directory.Exists(_localesDir))
            return new[] { "en-US" };

        return Directory.GetFiles(_localesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(f => f is not null)
            .Cast<string>()
            .OrderBy(f => f)
            .ToList()
            .AsReadOnly();
    }

    public string GetLocaleDisplayName(string code)
    {
        var path = Path.Combine(_localesDir, $"{code}.json");
        try
        {
            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json)?.AsObject();
            var name = root?["meta"]?["name"]?.GetValue<string>();
            return name ?? code;
        }
        catch
        {
            return code;
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonNode.Parse(json)?.AsObject();
                var lang = settings?["language"]?.GetValue<string>();
                if (lang is not null && AvailableLocales.Contains(lang))
                    _currentLocale = lang;
            }
        }
        catch
        {
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var settings = new
            {
                language = _currentLocale
            };
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    public void SetLocale(string code)
    {
        if (!AvailableLocales.Contains(code))
            return;

        if (_currentLocale == code)
            return;

        _currentLocale = code;
        _strings = null;
        SaveSettings();
        LocaleChanged?.Invoke();
    }

    public string T(string key)
    {
        if (_strings is null)
            _strings = LoadStrings(_currentLocale);

        var parts = key.Split('.');
        JsonNode? current = _strings;
        foreach (var part in parts)
        {
            current = current?[part];
            if (current is null)
                return key;
        }
        return current?.GetValue<string>() ?? key;
    }

    private JsonObject LoadStrings(string code)
    {
        var path = Path.Combine(_localesDir, $"{code}.json");
        try
        {
            var json = File.ReadAllText(path);
            return JsonNode.Parse(json)?.AsObject()
                   ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}