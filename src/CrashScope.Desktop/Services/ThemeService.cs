using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
// This project enables both WPF and WinForms, so ImplicitUsings pulls in
// System.Windows.Forms and "Application" becomes ambiguous. Pin it to WPF.
using Application = System.Windows.Application;

namespace CrashScope.Desktop.Services;

public class ThemeService
{
    private const string ConfigFileName = "app_settings.json";

    private readonly string _settingsDir;
    private readonly string _settingsPath;
    private readonly Application _app;

    private string _currentTheme = "Dark";

    public string CurrentTheme => _currentTheme;
    public IReadOnlyList<string> AvailableThemes { get; } = new[] { "Dark", "Light" };

    public event Action? ThemeChanged;

    public ThemeService(Application app)
    {
        _app = app;
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashScope");
        _settingsPath = Path.Combine(_settingsDir, ConfigFileName);

        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonNode.Parse(json)?.AsObject();
                var theme = settings?["theme"]?.GetValue<string>();
                if (theme is not null && AvailableThemes.Contains(theme))
                    _currentTheme = theme;
            }
        }
        catch
        {
        }

        ApplyTheme(_currentTheme);
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            JsonObject settings;
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
            }
            else
            {
                settings = new JsonObject();
            }
            settings["theme"] = _currentTheme;
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    public void SetTheme(string theme)
    {
        if (!AvailableThemes.Contains(theme))
            return;

        if (_currentTheme == theme)
            return;

        _currentTheme = theme;
        ApplyTheme(theme);
        SaveSettings();
        ThemeChanged?.Invoke();
    }

    /// <summary>Re-applies the stored theme after <c>App.InitializeComponent()</c> replaced <c>Resources</c>.</summary>
    public void EnsureResourcesAttached() => ApplyTheme(_currentTheme);

    private void ApplyTheme(string theme)
    {
        var resources = _app.Resources;
        var themeDict = resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString?.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        if (themeDict is not null)
            resources.MergedDictionaries.Remove(themeDict);

        var themePath = theme switch
        {
            "Dark" => "Themes/DarkTheme.xaml",
            "Light" => "Themes/LightTheme.xaml",
            _ => "Themes/DarkTheme.xaml"
        };

        var uri = new Uri(themePath, UriKind.Relative);
        themeDict = new ResourceDictionary { Source = uri };
        resources.MergedDictionaries.Add(themeDict);
    }

    public string GetThemeDisplayName(string theme) => theme;
}
