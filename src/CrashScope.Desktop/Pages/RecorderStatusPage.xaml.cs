using System.Diagnostics;
using CrashScope.Desktop.Services;
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

public partial class RecorderStatusPage : UserControl, IRefreshableLocale
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

    private void OnStatusChanged() => Dispatcher.InvokeAsync(RefreshStatus);

    public void RefreshLocale() => RefreshStatus();

    private void RefreshStatus()
    {
        if (_service.IsRecording)
        {
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"));
            RecorderStatusLabel.Text = Loc.T("recorder.status.recording");
            StartStopButton.Content = Loc.T("recorder.buttons.stopRecording");
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
            RecorderStatusLabel.Text = _service.RecorderState.ToString().ToUpperInvariant();
            StartStopButton.Content = Loc.T("recorder.buttons.startRecording");
            StartStopButton.Style = (Style)FindResource("AccentButton");
            SessionIdText.Text = "--";
            UptimeText.Text = "--";
            SampleCountText.Text = "0";
        }

        StartStopButton.IsEnabled = !_service.IsStopping;
        RecorderStatusLabel.ToolTip = _service.RecorderError ?? _service.RecoveryError;
        DataDirText.Text = _service.DataDirectory;
        ElevatedText.Text = _service.IsElevated ? Loc.T("recorder.elevatedYes") : Loc.T("recorder.elevatedNo");

        var prev = _service.PreviousSession;
        if (prev is not null)
        {
            var status = prev.CleanShutdown ? Loc.T("recorder.cleanShutdown") : Loc.T("recorder.uncleanShutdown");
            PreviousSessionText.Text = $"{prev.LastHeartbeatAt:yyyy-MM-dd HH:mm:ss} ({status})";
            PreviousSessionText.Foreground = prev.CleanShutdown
                ? (Brush)FindResource("TextPrimary")
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D"));
        }
        else
        {
            PreviousSessionText.Text = Loc.T("common.none");
        }

        AgentVersionText.Text = "v0.5.0-dev";
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        StartStopButton.IsEnabled = false;
        var stopRequested = _service.IsRecording;
        try
        {
            if (stopRequested)
                await _service.StopRecordingAsync();
            else
            {
                await _service.StopRecordingAsync(); // Also release a faulted recorder.
                await Task.Run(_service.StartRecording);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("app.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { RefreshStatus(); }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _service.RootDirectory;
        if (Directory.Exists(path))
            Process.Start("explorer.exe", path);
        else
            MessageBox.Show(Loc.F("recorder.dataDirNotFound", path), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}