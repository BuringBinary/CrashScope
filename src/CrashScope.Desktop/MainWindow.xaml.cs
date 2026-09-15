using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CrashScope.Desktop.Pages;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop;

public partial class MainWindow : Window
{
    private readonly AppService _service;
    private readonly TrayIconManager _tray;
    private readonly RetentionService _retention;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        _service = App.Current.Service;
        _retention = new RetentionService(_service.RootDirectory);
        _tray = new TrayIconManager(this, _service);

        _service.StatusChanged += OnRecordingStatusChanged;

        NavigateTo("Recorder");
        EnforceRetentionOnStartup();
        CheckPreviousSessionOnStartup();
    }

    private void EnforceRetentionOnStartup()
    {
        var (sessions, incidents, bytes) = _retention.Enforce();
        var usageMb = _retention.GetCurrentUsage() / (1024.0 * 1024.0);

        StatusBarText.Text = $"Data: {usageMb:F0} MB / {_retention.Settings.MaxTotalSizeMb} MB quota";
        if (sessions > 0 || incidents > 0)
        {
            StatusBarText.Text += $" | Cleaned: {sessions} sessions, {incidents} incidents ({bytes / 1024.0 / 1024.0:F1} MB)";
        }
    }

    private async void CheckPreviousSessionOnStartup()
    {
        await Task.Delay(1500); // let UI render first
        var analysis = _service.CheckPreviousUncleanSession();
        if (analysis is not null)
        {
            var leading = analysis.Hypotheses.FirstOrDefault(h => h.Score > 0);
            var mode = leading?.Mode.ToString() ?? "Unknown";
            var confidence = leading?.Confidence ?? 0;
            _tray.ShowUncleanSessionBalloon(mode, confidence);

            Dispatcher.Invoke(() =>
            {
                // navigate to Incidents page
                NavIncidents.IsChecked = true;
                NavigateTo("Incidents");
            });
        }
    }

    private void OnRecordingStatusChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_service.IsRecording)
            {
                StatusText.Text = "Recorder: Active";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"));
            }
            else
            {
                StatusText.Text = "Recorder: Stopped";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"));
            }
        });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            MaxRestoreButton_Click(sender, e);
        else if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaxRestoreButton.Content = "\uE922";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaxRestoreButton.Content = "\uE923";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void HideToTray()
    {
        _tray.StopBlink();
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _tray.Dispose();
        _service.Dispose();
        base.OnClosing(e);
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string page)
            NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        ContentArea.Children.Clear();
        UserControl content = page switch
        {
            "Recorder" => new RecorderStatusPage(),
            "Dashboard" => new DashboardPage(),
            "LiveMonitor" => new LiveMonitorPage(),
            "Incidents" => new IncidentsPage(),
            "Timeline" => new TimelinePage(),
            "Settings" => new SettingsPage(),
            _ => new RecorderStatusPage()
        };
        ContentArea.Children.Add(content);
        StatusBarText.Text = $"{page} loaded";
    }
}