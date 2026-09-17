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
    private int _ramTotalIdx = -1;

    public event Action<string>? NavigateRequested;

    public HomePage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _service.StatusChanged += OnStatusChanged;
        _service.SensorUpdated += OnSensorUpdated;
        BuildSensorMap();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _service.StatusChanged -= OnStatusChanged;
        _service.SensorUpdated -= OnSensorUpdated;
    }

    private void BuildSensorMap()
    {
        var catalog = _service.SensorCatalog;
        for (int i = 0; i < catalog.Count; i++)
        {
            var sensorName = catalog[i].SensorName.ToLowerInvariant();
            var hwType = catalog[i].HardwareType.ToLowerInvariant();
            if (hwType.Contains("cpu") && sensorName.Contains("temperature")) _cpuModelIdx = i;
            else if (hwType.Contains("gpu")) _gpuModelIdx = i;
            else if (sensorName.ToLower().Contains("memory") && sensorName.ToLower().Contains("used")) _ramUsedIdx = i;
            else if (sensorName.ToLower().Contains("memory") && sensorName.ToLower().Contains("total")) _ramTotalIdx = i;
        }
    }

    private void OnStatusChanged()
    {
        Dispatcher.InvokeAsync(RefreshAll);
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values)
    {
        Dispatcher.InvokeAsync(() => RefreshHardware(values));
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
        if (_service.SensorCatalog.Count == 0)
        {
            CpuText.Text = "CPU: Detecting...";
            GpuText.Text = "GPU: Detecting...";
            RamText.Text = "RAM: --";
            return;
        }

        if (_cpuModelIdx >= 0)
            CpuText.Text = $"CPU: {_service.SensorCatalog[_cpuModelIdx].HardwareName} - {_service.SensorCatalog[_cpuModelIdx].SensorName}";
        else
            CpuText.Text = $"CPU: {_service.SensorCatalog.Count} sensors detected";

        if (_gpuModelIdx >= 0)
            GpuText.Text = $"GPU: {_service.SensorCatalog[_gpuModelIdx].HardwareName} - {_service.SensorCatalog[_gpuModelIdx].SensorName}";
        else
            GpuText.Text = "GPU: Detecting...";

        if (values != null && _ramUsedIdx >= 0 && _ramUsedIdx < values.Count)
        {
            var used = values[_ramUsedIdx];
            if (used.HasValue)
                RamText.Text = $"RAM Used: {used.Value:F0} MB";
        }
    }

    private void RefreshDataRoot()
    {
        DataRootText.Text = $"Data: {_service.RootDirectory}";
        ElevationText.Text = $"Privilege: {(_service.IsElevated ? "Administrator" : "Standard user")} | Ver: {Environment.Version}";
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
        var analysis = await Task.Run(_service.CheckPreviousUncleanSession);
        CheckButton.Content = "检查上次异常";
        CheckButton.IsEnabled = true;
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
