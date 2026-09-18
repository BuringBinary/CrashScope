using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using UserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;

namespace CrashScope.Desktop.Pages;

public partial class HomePage : UserControl
{
    private readonly AppService _service;
    private int _cpuModelIdx = -1;
    private int _gpuModelIdx = -1;
    private int _ramUsedIdx = -1;

    public event Action<string>? NavigateRequested;

    public HomePage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _service.StatusChanged -= OnStatusChanged;
        _service.StatusChanged += OnStatusChanged;
        _service.SensorUpdated -= OnSensorUpdated;
        _service.SensorUpdated += OnSensorUpdated;
        RefreshAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _service.StatusChanged -= OnStatusChanged;
        _service.SensorUpdated -= OnSensorUpdated;
    }

    private void BuildSensorMap()
    {
        var map = DesktopSensorMap.Build(_service.SensorCatalog);
        _cpuModelIdx = map.CpuModel;
        _gpuModelIdx = map.GpuModel;
        _ramUsedIdx = map.RamUsed;
    }

    private void OnStatusChanged()
    {
        Dispatcher.InvokeAsync(() => { if (IsLoaded) RefreshAll(); });
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values)
    {
        var catalog = _service.SensorCatalog;
        var session = _service.CurrentSession?.SessionId;
        Dispatcher.InvokeAsync(() =>
        {
            if (IsLoaded && ReferenceEquals(catalog, _service.SensorCatalog) && session == _service.CurrentSession?.SessionId)
                RefreshHardware(values);
        });
    }

    private void RefreshAll()
    {
        RefreshRecorder();
        RefreshHardware(null);
        RefreshIncidents();
        RefreshDataRoot();
    }

    private void RefreshRecorder()
    {
        var state = _service.RecorderState;
        RecorderStateText.Text = state.ToString().ToUpperInvariant();
        SessionText.Text = $"Session: {_service.DataDirectory}";
        SamplesText.Text = $"Samples: {_service.SampleCount}";

        var isRecording = _service.IsRecording;
        StatusDot.Fill = isRecording
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"))
            : state == RecorderState.Faulted
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));

        StartStopButton.Content = isRecording ? "\uE71A  Stop" : "\uE768  Start";
        StartStopButton.IsEnabled = !_service.IsStopping;

        if (!string.IsNullOrWhiteSpace(_service.RecorderError))
        {
            ErrorText.Text = $"Error: {_service.RecorderError}";
            ErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(_service.RecoveryError))
        {
            RecoveryText.Text = $"Recovery: {_service.RecoveryError}";
            RecoveryText.Visibility = Visibility.Visible;
        }
        else
        {
            RecoveryText.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshHardware(IReadOnlyList<float?>? values)
    {
        BuildSensorMap();
        var catalog = _service.SensorCatalog;
        var missing = _service.IsRecording ? "Unavailable" : "Not recording";
        CpuText.Text = "CPU: " + (_cpuModelIdx >= 0 ? catalog[_cpuModelIdx].HardwareName : missing);
        GpuText.Text = "GPU: " + (_gpuModelIdx >= 0 ? catalog[_gpuModelIdx].HardwareName : missing);
        if (values is not null && _ramUsedIdx >= 0 && _ramUsedIdx < values.Count && values[_ramUsedIdx] is { } used)
            RamText.Text = $"RAM Used: {used:F1} GiB";
        else if (!_service.IsRecording || _ramUsedIdx < 0)
            RamText.Text = "RAM: " + missing;
        RefreshCollectionHealth();
    }

    private void RefreshCollectionHealth()
    {
        var health = _service.CollectorHealth;
        CollectionHealthText.Text = !_service.IsRecording ? "Collection: stopped (no live readings)"
            : health.Count == 0 ? "Collection: status not yet available"
            : "Collection: " + string.Join(" · ", health.Select(h => $"{h.Collector}: {h.Status}"));
        CollectionHealthText.ToolTip = health.Count == 0 ? "Start recording to inspect collector health."
            : string.Join("\n", health.Select(h => $"{h.Collector}: {h.Detail}"));
    }

    private void RefreshDataRoot()
    {
        DataRootText.Text = $"Data: {_service.RootDirectory}";
        ElevationText.Text = $"Privilege: {(_service.IsElevated ? "Administrator" : "Standard user")} | App: {typeof(AppService).Assembly.GetName().Version} | Runtime: {Environment.Version}";
    }

    private void RefreshIncidents()
    {
        var incidents = _service.GetIncidents();
        var recent = incidents.Take(5).ToList();
        IncidentsList.ItemsSource = recent;
        IncidentsEmptyText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service.IsRecording)
        {
            _service.StopRecording();
        }
        else
        {
            try
            {
                _service.StartRecording();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(ex.Message, "Recorder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        CheckButton.Content = "检查中...";

        IncidentAnalysis? analysis;
        try
        {
            analysis = await Task.Run(_service.CheckPreviousUncleanSession);
        }
        finally
        {
            // Without finally a throw would strand the button greyed out at "检查中..."
            // until the page is rebuilt. AppService swallows today, but the guarantee
            // belongs here, next to the code that disabled the button.
            CheckButton.Content = "检查上次异常";
            CheckButton.IsEnabled = true;
        }

        if (analysis != null)
        {
            NavigateRequested?.Invoke("Incidents");
        }
        else if (_service.RecoveryError != null)
        {
            WpfMessageBox.Show($"恢复已延期: {_service.RecoveryError}", "Recovery", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            WpfMessageBox.Show("未发现未正常结束的会话。", "Check", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void QuickNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is string tag)
        {
            NavigateRequested?.Invoke(tag);
        }
    }

    private void ViewAllIncidents_Click(object sender, RoutedEventArgs e)
    {
        NavigateRequested?.Invoke("Incidents");
    }
}
