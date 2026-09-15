using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;

namespace CrashScope.Desktop.Pages;

public partial class RecorderStatusPage : UserControl
{
    private readonly AppService _service;
    private readonly DispatcherTimer _uiTimer;

    public RecorderStatusPage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _service.StatusChanged += OnStatusChanged;

        _uiTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => RefreshStatus(), Dispatcher);
        _uiTimer.Start();

        RefreshStatus();
    }

    private void OnStatusChanged() => Dispatcher.Invoke(RefreshStatus);

    private void RefreshStatus()
    {
        if (_service.IsRecording)
        {
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"));
            RecorderStatusLabel.Text = "RECORDING";
            StartStopButton.Content = "\uE71A  Stop Recording";
            StartStopButton.Style = (Style)FindResource("DangerButton");

            var session = _service.CurrentSession;
            if (session is not null)
            {
                SessionIdText.Text = session.SessionId[..12];
                var elapsed = DateTimeOffset.Now - session.StartedAt;
                UptimeText.Text = elapsed.TotalHours >= 1
                    ? $"{elapsed.Hours}h {elapsed.Minutes}m"
                    : $"{elapsed.Minutes}m {elapsed.Seconds}s";
            }
            SampleCountText.Text = _service.SampleCount.ToString("N0");
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"));
            RecorderStatusLabel.Text = "STOPPED";
            StartStopButton.Content = "\uE768  Start Recording";
            StartStopButton.Style = (Style)FindResource("AccentButton");
            SessionIdText.Text = "--";
            UptimeText.Text = "--";
            SampleCountText.Text = "0";
        }

        DataDirText.Text = _service.DataDirectory;
        ElevatedText.Text = _service.IsElevated ? "Yes (Administrator)" : "No (Limited)";

        var prev = _service.PreviousSession;
        if (prev is not null)
        {
            var status = prev.CleanShutdown ? "Clean shutdown" : "Unclean shutdown \u26A0";
            PreviousSessionText.Text = $"{prev.LastHeartbeatAt:yyyy-MM-dd HH:mm:ss} ({status})";
            PreviousSessionText.Foreground = prev.CleanShutdown
                ? (Brush)FindResource("TextPrimary")
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D"));
        }
        else
        {
            PreviousSessionText.Text = "None";
        }

        AgentVersionText.Text = "v0.5.0-dev";
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service.IsRecording)
        {
            _service.StopRecording();
        }
        else
        {
            var analysis = _service.CheckPreviousUncleanSession();
            if (analysis is not null)
            {
                var leading = analysis.Hypotheses.FirstOrDefault(h => h.Score > 0);
                var msg = "Previous unclean session detected.\n\n";
                if (leading is not null)
                    msg += $"Top hypothesis: {leading.Mode} (confidence {leading.Confidence:P0})\n";
                msg += "\nIncident saved. Check Incidents tab for details.";
                MessageBox.Show(msg, "CrashScope", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            _service.StartRecording();
        }
        RefreshStatus();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _service.RootDirectory;
        if (Directory.Exists(path))
            Process.Start("explorer.exe", path);
        else
            MessageBox.Show($"Data directory not found:\n{path}", "CrashScope",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}