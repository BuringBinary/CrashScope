using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CrashScope.Desktop.Pages;

public partial class SettingsPage : UserControl
{
    private readonly AppService _service;
    private readonly RetentionService _retention;
    private bool _initialized;
    private DispatcherTimer? _statusTimer;

    public SettingsPage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _retention = new RetentionService(_service.RootDirectory);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        RetentionDaysSlider.Value = _retention.Settings.MaxRetentionDays;
        MaxSizeSlider.Value = _retention.Settings.MaxTotalSizeMb;
        RedactByDefaultCheckBox.IsChecked = true;

        DataDirText.Text = _service.RootDirectory;
        RuntimeText.Text = $".NET {Environment.Version}";
        ElevationText.Text = _service.IsElevated ? "Administrator" : "Standard user";
        if (_service.IsElevated)
            ElevationText.Foreground = FindResource("GpuColor") as Brush;

        UpdateUsage();
    }

    private void UpdateUsage()
    {
        var usageMb = _retention.GetCurrentUsage() / (1024.0 * 1024.0);
        CurrentUsageText.Text = $"{usageMb:F1} MB / {_retention.Settings.MaxTotalSizeMb} MB quota";
    }

    private void RetentionDaysSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        var days = (int)e.NewValue;
        RetentionDaysText.Text = days == 90 ? "90 days (max)" : $"{days} days";
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

        ShowStatus("Settings saved successfully.", "#00D68F");
    }

    private void CopyDataDir_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_service.RootDirectory);
        ShowStatus("Path copied to clipboard.", "#00D68F");
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
}