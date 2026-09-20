using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrashScope.Desktop.Services;

/// <summary>
/// Persists the small UI behaviour toggles that are not owned by another service.
/// </summary>
/// <remarks>
/// Shares <c>app_settings.json</c> with <see cref="ThemeService"/> and
/// <see cref="LocalizationService"/>, and uses the same read-modify-write merge so services
/// writing different keys do not clobber each other. Like those services it never throws:
/// a settings file that is missing, unreadable or corrupt degrades to defaults, because a
/// preferences problem must not stop the recorder from running.
/// </remarks>
public class UiSettingsService
{
    private const string ConfigFileName = "app_settings.json";
    private const string MinimizeToTrayKey = "minimizeToTray";
    private const string CloseToTrayKey = "closeToTray";
    private const string SchemaKey = "uiSettingsSchema";
    private const string FloatingBarLeftKey = "floatingBarLeft";
    private const string FloatingBarTopKey = "floatingBarTop";

    /// <summary>
    /// Schema 2 changed the <see cref="MinimizeToTray"/> default from true to false.
    /// </summary>
    private const int CurrentSchema = 2;

    private readonly string _settingsDir;
    private readonly string _settingsPath;

    // Minimise defaults to the standard Windows behaviour - the taskbar. A minimise button
    // that makes the window vanish into the tray violates what the glyph promises, and the
    // user has no reason to expect the tray is where it went. Close-to-tray is the opposite:
    // a background recorder should keep running when the window is dismissed.
    private bool _minimizeToTray = false;
    private bool _closeToTray = true;

    /// <summary>Minimise button hides to the tray instead of minimising to the taskbar.</summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray == value) return;
            _minimizeToTray = value;
            Save();
        }
    }

    /// <summary>Close button hides to the tray instead of exiting the application.</summary>
    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (_closeToTray == value) return;
            _closeToTray = value;
            Save();
        }
    }

    /// <summary>Last remembered floating-bar X position; null until the bar is moved once.</summary>
    public double? FloatingBarLeft
    {
        get => _floatingBarLeft;
        set
        {
            if (_floatingBarLeft == value) return;
            _floatingBarLeft = value;
            Save();
        }
    }

    /// <summary>Last remembered floating-bar Y position; null until the bar is moved once.</summary>
    public double? FloatingBarTop
    {
        get => _floatingBarTop;
        set
        {
            if (_floatingBarTop == value) return;
            _floatingBarTop = value;
            Save();
        }
    }

    private double? _floatingBarLeft;
    private double? _floatingBarTop;

    public UiSettingsService()
    {
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashScope");
        _settingsPath = Path.Combine(_settingsDir, ConfigFileName);

        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;

            var settings = JsonNode.Parse(File.ReadAllText(_settingsPath))?.AsObject();
            if (settings is null) return;

            if (settings[CloseToTrayKey]?.GetValue<bool>() is bool close)
                _closeToTray = close;

            if (settings[FloatingBarLeftKey]?.GetValue<double>() is double left)
                _floatingBarLeft = left;
            if (settings[FloatingBarTopKey]?.GetValue<double>() is double top)
                _floatingBarTop = top;

            var schema = settings[SchemaKey]?.GetValue<int>() ?? 1;
            if (schema >= CurrentSchema)
            {
                if (settings[MinimizeToTrayKey]?.GetValue<bool>() is bool minimize)
                    _minimizeToTray = minimize;
                return;
            }

            // Schema 1 shipped with minimizeToTray defaulting to true, and the settings page
            // wrote both keys together whenever either checkbox changed. A stored "true" from
            // that build therefore cannot be distinguished from a deliberate choice, and it
            // makes the minimise button hide the window - the behaviour this schema bump
            // exists to undo. Drop it and adopt the new default; an explicit "false" is kept
            // because it already agrees with that default.
            if (settings[MinimizeToTrayKey]?.GetValue<bool>() is false)
                _minimizeToTray = false;

            Save();
        }
        catch
        {
            // Corrupt or unreadable settings fall back to the defaults set above.
        }
    }

    private void Save()
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

            settings[MinimizeToTrayKey] = _minimizeToTray;
            settings[CloseToTrayKey] = _closeToTray;
            if (_floatingBarLeft.HasValue) settings[FloatingBarLeftKey] = _floatingBarLeft.Value;
            if (_floatingBarTop.HasValue) settings[FloatingBarTopKey] = _floatingBarTop.Value;
            // Stamping the schema is what makes the migration in Load() run exactly once.
            settings[SchemaKey] = CurrentSchema;

            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Preferences are best-effort; never break the app over a write failure.
        }
    }
}

