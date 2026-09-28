using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Win32;
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

    public const string Dark = "Dark";
    public const string Light = "Light";
    /// <summary>Preference meaning "track the OS light/dark setting", mirroring the
    /// existing "Follow system" option in the language picker.</summary>
    public const string System = "System";

    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    // The user's stored preference. May be "System", which is NOT a palette name.
    private string _currentTheme = Dark;
    // The palette actually applied. Always "Dark" or "Light", never "System".
    private string _effectiveTheme = Dark;
    private bool _watchingSystem;

    /// <summary>The stored preference, which may be <c>"System"</c>.</summary>
    public string CurrentTheme => _currentTheme;

    /// <summary>The palette actually in effect - always <c>"Dark"</c> or <c>"Light"</c>.</summary>
    public string EffectiveTheme => _effectiveTheme;

    /// <summary>Single source of truth for the selectable preferences, static so it can be
    /// asserted without constructing a WPF Application.</summary>
    public static IReadOnlyList<string> SupportedThemes { get; } = new[] { Dark, Light, System };

    public IReadOnlyList<string> AvailableThemes => SupportedThemes;

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

    /// <summary>
    /// Maps a stored preference onto a concrete palette. Pure so it can be unit tested
    /// without a WPF Application: "System" defers to the OS flag, anything unrecognised
    /// falls back to Dark rather than throwing.
    /// </summary>
    public static string ResolveEffectiveTheme(string? preference, bool systemUsesLight) =>
        preference switch
        {
            Light => Light,
            Dark => Dark,
            System => systemUsesLight ? Light : Dark,
            _ => Dark
        };

    /// <summary>
    /// Reads the per-user "apps use light theme" flag. Returns false (dark) when the value
    /// is missing or unreadable, which matches this app's default rather than Windows'.
    /// </summary>
    public static bool ReadSystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightThemeValue) is int v && v != 0;
        }
        catch
        {
            return false;
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
                var theme = settings?["theme"]?.GetValue<string>();
                if (theme is not null && AvailableThemes.Contains(theme))
                    _currentTheme = theme;
            }
        }
        catch
        {
        }

        _effectiveTheme = ResolveEffectiveTheme(_currentTheme, ReadSystemUsesLightTheme());
        ApplyTheme(_effectiveTheme);
        UpdateSystemWatch();
    }

    /// <summary>
    /// Only listen to OS theme changes while the preference is "System". Subscribing
    /// unconditionally would keep a static SystemEvents handler alive for no reason.
    /// </summary>
    private void UpdateSystemWatch()
    {
        var shouldWatch = _currentTheme == System;
        if (shouldWatch == _watchingSystem)
            return;

        if (shouldWatch)
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        else
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _watchingSystem = shouldWatch;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;

        // Raised on a system thread; resource dictionaries must be touched on the UI thread.
        _app.Dispatcher.BeginInvoke(new Action(ApplySystemThemeIfChanged));
    }

    private void ApplySystemThemeIfChanged()
    {
        if (_currentTheme != System)
            return;

        var resolved = ResolveEffectiveTheme(System, ReadSystemUsesLightTheme());
        if (resolved == _effectiveTheme)
            return;

        _effectiveTheme = resolved;
        ApplyTheme(resolved);
        ThemeChanged?.Invoke();
    }

    /// <summary>Detaches the OS hook. SystemEvents holds a static reference, so an app
    /// that never unsubscribes leaks this service for the process lifetime.</summary>
    public void Shutdown()
    {
        if (!_watchingSystem)
            return;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watchingSystem = false;
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
        UpdateSystemWatch();

        var resolved = ResolveEffectiveTheme(theme, ReadSystemUsesLightTheme());
        SaveSettings();

        // Switching Dark -> System while the OS is already dark changes the preference
        // but not the palette, so skip the redundant dictionary swap and event.
        if (resolved == _effectiveTheme)
            return;

        _effectiveTheme = resolved;
        ApplyTheme(resolved);
        ThemeChanged?.Invoke();
    }

    /// <summary>Re-applies the stored theme after <c>App.InitializeComponent()</c> replaced <c>Resources</c>.</summary>
    public void EnsureResourcesAttached() => ApplyTheme(_effectiveTheme);

    private void ApplyTheme(string theme)
    {
        var resources = _app.Resources;
        var themePath = theme switch
        {
            "Dark" => "Themes/DarkTheme.xaml",
            "Light" => "Themes/LightTheme.xaml",
            _ => "Themes/DarkTheme.xaml"
        };

        // The palette must stay ahead of ControlStyles.xaml so DynamicResource lookups
        // in the shared templates resolve against the active theme. Replacing in place
        // preserves that order; Add() would append the palette after the styles.
        var existing = resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString?.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        var replacement = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };

        if (existing is null)
        {
            resources.MergedDictionaries.Add(replacement);
            return;
        }

        var slot = resources.MergedDictionaries.IndexOf(existing);
        resources.MergedDictionaries[slot] = replacement;
    }

    public string GetThemeDisplayName(string theme) => theme;
}
