using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CrashScope.Core.Infrastructure;
using CrashScope.Desktop.Services;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CrashScope.Desktop.Pages;

public partial class SettingsPage : UserControl, IRefreshableLocale
{
    private readonly AppService _service;
    private readonly RetentionService _retention;
    private readonly LocalizationService _localization;
    private readonly ThemeService _theme;
    private readonly UiSettingsService _uiSettings;
    private bool _initialized;
    private DispatcherTimer? _statusTimer;

    public SettingsPage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _retention = new RetentionService(_service.RootDirectory);
        _localization = App.Current.Localization;
        _theme = App.Current.Theme;
        _uiSettings = App.Current.UiSettings;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        RetentionDaysSlider.Value = _retention.Settings.MaxRetentionDays;
        MaxSizeSlider.Value = _retention.Settings.MaxTotalSizeMb;
        RedactByDefaultCheckBox.IsChecked = true;

        // Reflect the persisted tray behaviour instead of the XAML default, so the boxes
        // always show what the app will actually do.
        MinimizeToTrayCheckBox.IsChecked = _uiSettings.MinimizeToTray;
        CloseToTrayCheckBox.IsChecked = _uiSettings.CloseToTray;

        DataDirText.Text = _service.RootDirectory;
        RuntimeText.Text = $".NET {Environment.Version}";
        ElevationText.Text = _service.IsElevated ? Loc.T("common.administrator") : Loc.T("common.standardUser");
        if (_service.IsElevated)
            ElevationText.Foreground = FindResource("GpuColor") as Brush;

        PopulateLanguageComboBox();
        PopulateThemeComboBox();

        UpdateUsage();
        RefreshStartupState();
        RefreshRecoveryPending();
    }

    private void RefreshStartupState()
    {
        try
        {
            AutoStartCheckBox.IsChecked = IsAutoStartEnabled();
        }
        catch
        {
            AutoStartCheckBox.IsChecked = false;
        }
    }

    private void RefreshRecoveryPending()
    {
        try
        {
            var pendingDir = System.IO.Path.Combine(_service.RootDirectory, "state", "recovery-pending");
            if (System.IO.Directory.Exists(pendingDir))
            {
                var count = System.IO.Directory.GetFiles(pendingDir, "*.json").Length;
                if (count > 0)
                {
                    RecoveryPendingText.Text = Loc.F("settings.recoveryPending", count);
                    RecoveryPendingText.Visibility = Visibility.Visible;
                    return;
                }
            }
            RecoveryPendingText.Visibility = Visibility.Collapsed;
        }
        catch
        {
            RecoveryPendingText.Visibility = Visibility.Collapsed;
        }
    }

    private static string AutoStartKeyPath => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private static string AutoStartValueName => "CrashScope.Desktop";

    private bool IsAutoStartEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, false);
        var val = key?.GetValue(AutoStartValueName) as string;
        return !string.IsNullOrWhiteSpace(val);
    }

    private void SetAutoStart(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, true) ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AutoStartKeyPath);
        if (key == null) throw new InvalidOperationException(Loc.T("settings.startup.runKeyError"));
        if (enable)
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrashScope.Desktop.exe");
            key.SetValue(AutoStartValueName, $"\"{exePath}\"", Microsoft.Win32.RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AutoStartValueName, false);
        }
    }


    private void PopulateLanguageComboBox()
    {
        LanguageComboBox.Items.Clear();

        // First entry follows the Windows display language (the default); it shows which
        // concrete locale that currently resolves to so the choice is never a mystery.
        var systemName = _localization.GetLocaleDisplayName(_localization.ResolveSystemLocale());
        LanguageComboBox.Items.Add(new ComboBoxItem
        {
            Content = $"{Loc.T("settings.appearance.languageSystem")} ({systemName})",
            Tag = LocalizationService.SystemLocale,
            IsSelected = _localization.IsFollowingSystem
        });

        foreach (var code in _localization.AvailableLocales)
        {
            LanguageComboBox.Items.Add(new ComboBoxItem
            {
                Content = _localization.GetLocaleDisplayName(code),
                Tag = code,
                IsSelected = !_localization.IsFollowingSystem && code == _localization.Preference
            });
        }
    }

    /// <summary>Re-renders code-behind text after a language switch; XAML updates itself via DynamicResource.</summary>
    public void RefreshLocale()
    {
        ElevationText.Text = _service.IsElevated ? Loc.T("common.administrator") : Loc.T("common.standardUser");
        var wasInitialized = _initialized;
        _initialized = false;          // suppress SelectionChanged while rebuilding
        PopulateLanguageComboBox();
        PopulateThemeComboBox();
        _initialized = wasInitialized;
        UpdateUsage();
        RefreshRecoveryPending();
    }

    private void PopulateThemeComboBox()
    {
        ThemeComboBox.Items.Clear();
        foreach (var theme in _theme.AvailableThemes)
        {
            ThemeComboBox.Items.Add(new ComboBoxItem
            {
                Content = theme == "Dark" ? Loc.T("settings.appearance.themeDark")
                        : theme == "Light" ? Loc.T("settings.appearance.themeLight") : theme,
                Tag = theme,
                IsSelected = theme == _theme.CurrentTheme
            });
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string code)
        {
            _localization.SetLocale(code);
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            _theme.SetTheme(theme);
        }
    }

    private void UpdateUsage()
    {
        var usageMb = _retention.GetCurrentUsage() / (1024.0 * 1024.0);
        CurrentUsageText.Text = Loc.F("settings.dataRetention.usage", usageMb, _retention.Settings.MaxTotalSizeMb);
    }

    private void RetentionDaysSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        var days = (int)e.NewValue;
        RetentionDaysText.Text = days == 90 ? Loc.F("settings.dataRetention.daysMax", days) : Loc.F("settings.dataRetention.days", days);
    }

    private void MaxSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        var mb = (int)e.NewValue;
        if (mb >= 1024)
            MaxSizeText.Text = $"{mb / 1024.0:F1} GB";
        else
            MaxSizeText.Text = $"{mb} MB";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _retention.Settings.MaxRetentionDays = (int)RetentionDaysSlider.Value;
        _retention.Settings.MaxTotalSizeMb = (int)MaxSizeSlider.Value;
        _retention.Save();

        _retention.Enforce();
        UpdateUsage();

        ShowStatus(Loc.T("settings.buttons.settingsSaved"), "#00D68F");
    }

    private void CopyDataDir_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_service.RootDirectory);
        ShowStatus(Loc.T("settings.buttons.pathCopied"), "#00D68F");
    }

    private void ShowStatus(string message, string colorHex)
    {
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(colorHex));
        SaveStatusText.Visibility = Visibility.Visible;

        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(3),
            DispatcherPriority.Normal,
            (_, _) =>
            {
                SaveStatusText.Visibility = Visibility.Collapsed;
                _statusTimer?.Stop();
            },
            Dispatcher);
        _statusTimer.Start();
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        try
        {
            var enable = AutoStartCheckBox.IsChecked == true;
            SetAutoStart(enable);
            ShowStatus(enable ? Loc.T("settings.startup.autoStartEnabled") : Loc.T("settings.startup.autoStartDisabled"), "#00D68F");
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.F("settings.startup.autoStartFailed", ex.Message), "#FF6B6B");
            RefreshStartupState();
        }
    }

    /// <summary>
    /// Applies the tray behaviour toggles immediately, matching AutoStartCheckBox.
    /// </summary>
    /// <remarks>
    /// These two boxes used to be decorative: nothing read them, so unticking
    /// "close to tray" and pressing Save still hid the window to the tray. They are applied
    /// on change rather than on Save because Save owns retention settings only.
    /// </remarks>
    private void TrayBehaviourCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        _uiSettings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
        _uiSettings.CloseToTray = CloseToTrayCheckBox.IsChecked == true;

        ShowStatus(Loc.T("settings.startup.trayUpdated"), "#00D68F");
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _service.RootDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.F("settings.startup.openFailed", ex.Message), "#FF6B6B");
        }
    }

    private void OpenIncidentsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = _service.IncidentsRoot;
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.F("settings.startup.openFailed", ex.Message), "#FF6B6B");
        }
    }
}