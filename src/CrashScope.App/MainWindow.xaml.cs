using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CrashScope.App.Services;
using CrashScope.App.ViewModels;
using CrashScope.Contracts;

namespace CrashScope.App;

public partial class MainWindow : Window
{
    private readonly AgentRpcClient _client = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<double> _cpuLoadHistory = new();
    private readonly Queue<double> _cpuTempHistory = new();
    private readonly Queue<double> _gpuLoadHistory = new();
    private readonly Queue<double> _gpuTempHistory = new();
    private IReadOnlyList<SensorCatalogDto>? _catalog;
    private bool _refreshing;

    public ObservableCollection<HardwareGroupViewModel> HardwareGroups { get; } = new();
    public ObservableCollection<IncidentCardViewModel> Incidents { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        Loaded += OnLoaded;
        Closed += OnClosed;
        _refreshTimer.Tick += OnRefreshTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        await RefreshIncidentsAsync();
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async void OnRefreshTick(object? sender, EventArgs e) => await RefreshAsync();

    private void DashboardNavButton_Click(object sender, RoutedEventArgs e)
    {
        DashboardPage.Visibility = Visibility.Visible;
        IncidentsPage.Visibility = Visibility.Collapsed;
        SetActiveNavigation(DashboardNavButton, IncidentsNavButton);
    }

    private async void IncidentsNavButton_Click(object sender, RoutedEventArgs e)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        IncidentsPage.Visibility = Visibility.Visible;
        SetActiveNavigation(IncidentsNavButton, DashboardNavButton);
        await RefreshIncidentsAsync();
    }

    private async void RefreshIncidentsButton_Click(object sender, RoutedEventArgs e) => await RefreshIncidentsAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing || _lifetime.IsCancellationRequested)
            return;

        _refreshing = true;
        try
        {
            var statusResponse = await _client.SendAsync(AgentRpcProtocol.StatusCommand, _lifetime.Token);
            var status = statusResponse.Status;
            if (!statusResponse.Ok || status is null)
                throw new IOException(statusResponse.Error ?? "CrashScope.Agent status is unavailable.");

            if (_catalog is null || _catalog.Count != status.SensorCount)
            {
                var catalogResponse = await _client.SendAsync(AgentRpcProtocol.SensorCatalogCommand, _lifetime.Token);
                if (!catalogResponse.Ok || catalogResponse.SensorCatalog is null)
                    throw new IOException(catalogResponse.Error ?? "CrashScope.Agent sensor catalog is unavailable.");
                _catalog = catalogResponse.SensorCatalog;
            }

            var telemetryResponse = await _client.SendAsync(AgentRpcProtocol.LatestTelemetryCommand, _lifetime.Token);
            if (!telemetryResponse.Ok)
                throw new IOException(telemetryResponse.Error ?? "CrashScope.Agent telemetry is unavailable.");

            SetOnline(status);
            if (telemetryResponse.Telemetry is { } telemetry)
            {
                SetTelemetry(status, telemetry);
                UpdateKeyMetrics(telemetry);
                RenderHardware(telemetry);
            }
            else
            {
                ForegroundValueText.Text = "—";
                ForegroundDetailText.Text = "Waiting for first sample";
                IdleValueText.Text = "—";
                LastSampleValueText.Text = "—";
                LastSampleDetailText.Text = "Recorder has not sampled yet";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetOffline(ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshIncidentsAsync()
    {
        if (_lifetime.IsCancellationRequested)
            return;

        try
        {
            var response = await _client.SendAsync(AgentRpcProtocol.ListIncidentsCommand, _lifetime.Token);
            if (!response.Ok || response.Incidents is null)
                throw new IOException(response.Error ?? "CrashScope incident catalog is unavailable.");

            Incidents.Clear();
            foreach (var incident in response.Incidents)
                Incidents.Add(ToIncidentViewModel(incident));

            IncidentCountText.Text = Incidents.Count.ToString();
            LatestIncidentText.Text = Incidents.FirstOrDefault()?.TimeText ?? "—";
            IncidentRefreshText.Text = DateTime.Now.ToString("HH:mm:ss");
            IncidentEmptyText.Visibility = Incidents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            IncidentRefreshText.Text = "Unavailable";
            IncidentEmptyText.Text = $"Incident catalog unavailable: {Truncate(ex.Message, 120)}";
            IncidentEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void SetOnline(AgentStatusDto status)
    {
        var success = (Brush)FindResource("SuccessBrush");
        HeaderStatusDot.Fill = success;
        SidebarStatusDot.Fill = success;
        HeaderStatusText.Text = "Recording";
        SidebarStatusText.Text = "Agent online";
        RecorderValueText.Text = "Recording";
        RecorderDetailText.Text = $"v{status.Version} · {(status.Elevated ? "Admin" : "Standard")} · {status.SensorCount} sensors";
        SensorSummaryText.Text = $"{status.SensorCount} sensors · session {ShortId(status.SessionId)}";
    }

    private void SetTelemetry(AgentStatusDto status, TelemetryDto telemetry)
    {
        ForegroundValueText.Text = string.IsNullOrWhiteSpace(telemetry.Foreground.ProcessName)
            ? "Desktop / unknown"
            : telemetry.Foreground.ProcessName;
        ForegroundDetailText.Text = telemetry.Foreground.ProcessId is { } pid
            ? $"PID {pid}"
            : "No foreground PID";

        IdleValueText.Text = FormatIdle(telemetry.Foreground.IdleSeconds);
        LastSampleValueText.Text = telemetry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        var age = DateTimeOffset.Now - telemetry.Timestamp;
        LastSampleDetailText.Text = age.TotalSeconds < 3
            ? "Live · 1 Hz recorder stream"
            : $"Sample age {Math.Max(0, age.TotalSeconds):F0}s";

        if (status.LatestTelemetryAt is { } latest && latest > telemetry.Timestamp)
            LastSampleDetailText.Text = $"Agent latest {latest.ToLocalTime():HH:mm:ss}";
    }

    private void SetOffline(string reason)
    {
        var danger = (Brush)FindResource("DangerBrush");
        HeaderStatusDot.Fill = danger;
        SidebarStatusDot.Fill = danger;
        HeaderStatusText.Text = "Disconnected";
        SidebarStatusText.Text = "Agent offline";
        RecorderValueText.Text = "Offline";
        RecorderDetailText.Text = Truncate(reason, 70);

        if (HardwareGroups.Count > 0)
            LastSampleDetailText.Text = "Showing last received telemetry";
    }

    private void UpdateKeyMetrics(TelemetryDto telemetry)
    {
        var cpuLoad = FindSensorValue(telemetry, sensor =>
            IsHardware(sensor, "Cpu") && sensor.SensorType == "Load" &&
            sensor.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase))
            ?? FindSensorValue(telemetry, sensor => IsHardware(sensor, "Cpu") && sensor.SensorType == "Load");

        var cpuTemp = FindSensorValue(telemetry, sensor =>
            IsHardware(sensor, "Cpu") && sensor.SensorType == "Temperature" &&
            sensor.SensorName.Contains("Package", StringComparison.OrdinalIgnoreCase))
            ?? FindSensorValue(telemetry, sensor => IsHardware(sensor, "Cpu") && sensor.SensorType == "Temperature");

        var gpuLoad = FindSensorValue(telemetry, sensor =>
            IsHardware(sensor, "Gpu") && sensor.SensorType == "Load" &&
            (sensor.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
             sensor.SensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase)))
            ?? FindSensorValue(telemetry, sensor => IsHardware(sensor, "Gpu") && sensor.SensorType == "Load");

        var gpuTemp = FindSensorValue(telemetry, sensor =>
            IsHardware(sensor, "Gpu") && sensor.SensorType == "Temperature" &&
            sensor.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase))
            ?? FindSensorValue(telemetry, sensor => IsHardware(sensor, "Gpu") && sensor.SensorType == "Temperature");

        UpdateMetric(cpuLoad, _cpuLoadHistory, CpuLoadValueText, CpuLoadSparkline, 0, 100, value => $"{value:F0} %");
        UpdateMetric(cpuTemp, _cpuTempHistory, CpuTempValueText, CpuTempSparkline, 20, 110, value => $"{value:F1} °C");
        UpdateMetric(gpuLoad, _gpuLoadHistory, GpuLoadValueText, GpuLoadSparkline, 0, 100, value => $"{value:F0} %");
        UpdateMetric(gpuTemp, _gpuTempHistory, GpuTempValueText, GpuTempSparkline, 20, 110, value => $"{value:F1} °C");
    }

    private float? FindSensorValue(TelemetryDto telemetry, Func<SensorCatalogDto, bool> predicate)
    {
        if (_catalog is null)
            return null;

        foreach (var sensor in _catalog.Where(predicate))
        {
            if (sensor.Id >= 0 && sensor.Id < telemetry.SensorValues.Count && telemetry.SensorValues[sensor.Id] is { } value)
                return value;
        }

        return null;
    }

    private static bool IsHardware(SensorCatalogDto sensor, string token) =>
        sensor.HardwareType.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static void UpdateMetric(
        float? value,
        Queue<double> history,
        System.Windows.Controls.TextBlock valueText,
        System.Windows.Shapes.Polyline sparkline,
        double minimum,
        double maximum,
        Func<double, string> formatter)
    {
        if (value is null)
        {
            valueText.Text = "—";
            return;
        }

        valueText.Text = formatter(value.Value);
        history.Enqueue(value.Value);
        while (history.Count > 60)
            history.Dequeue();

        var samples = history.ToArray();
        var points = new PointCollection(samples.Length);
        var range = Math.Max(1, maximum - minimum);
        for (var index = 0; index < samples.Length; index++)
        {
            var x = samples.Length <= 1 ? 0 : index * 100d / (samples.Length - 1);
            var normalized = Math.Clamp((samples[index] - minimum) / range, 0, 1);
            points.Add(new Point(x, 100 - normalized * 100));
        }

        sparkline.Points = points;
    }

    private void RenderHardware(TelemetryDto telemetry)
    {
        if (_catalog is null)
            return;

        var next = new List<HardwareGroupViewModel>();
        var groups = _catalog
            .GroupBy(sensor => (sensor.HardwareType, sensor.HardwareName))
            .OrderBy(group => HardwareRank(group.Key.HardwareType))
            .ThenBy(group => group.Key.HardwareName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var item = new HardwareGroupViewModel
            {
                HardwareName = group.Key.HardwareName,
                HardwareType = FriendlyHardwareType(group.Key.HardwareType)
            };

            foreach (var sensor in group
                         .OrderBy(sensor => SensorRank(sensor.SensorType))
                         .ThenBy(sensor => sensor.SensorName, StringComparer.OrdinalIgnoreCase))
            {
                if (sensor.Id < 0 || sensor.Id >= telemetry.SensorValues.Count)
                    continue;

                var value = telemetry.SensorValues[sensor.Id];
                if (value is null)
                    continue;

                item.Sensors.Add(new SensorValueViewModel
                {
                    SensorName = sensor.SensorName,
                    SensorType = sensor.SensorType,
                    ValueText = FormatSensorValue(sensor.SensorType, value.Value)
                });
            }

            if (item.Sensors.Count > 0)
                next.Add(item);
        }

        HardwareGroups.Clear();
        foreach (var group in next)
            HardwareGroups.Add(group);
    }

    private static IncidentCardViewModel ToIncidentViewModel(IncidentSummaryDto incident)
    {
        var evidence = new List<string>();
        if (incident.KernelPower41) evidence.Add("Kernel-Power 41");
        if (incident.UnexpectedShutdown6008) evidence.Add("Unexpected shutdown 6008");
        if (incident.WheaEvidence) evidence.Add("WHEA");
        if (incident.DisplayTdrEvidence) evidence.Add("Display/TDR");

        return new IncidentCardViewModel
        {
            Id = incident.Id,
            TimeText = incident.LastHeartbeatAt?.ToLocalTime().ToString("yyyy-MM-dd  HH:mm:ss") ?? incident.Id,
            SessionText = string.IsNullOrWhiteSpace(incident.SessionId) ? "Unknown session" : $"Session {ShortId(incident.SessionId)}",
            EvidenceText = evidence.Count == 0 ? "No classified Windows evidence in the preliminary summary" : string.Join(" · ", evidence),
            DirectoryPath = incident.DirectoryPath,
            EvidenceCount = evidence.Count
        };
    }

    private void SetActiveNavigation(System.Windows.Controls.Button active, System.Windows.Controls.Button inactive)
    {
        active.Background = (Brush)FindResource("AccentSoftBrush");
        active.Foreground = (Brush)FindResource("PrimaryText");
        inactive.Background = Brushes.Transparent;
        inactive.Foreground = (Brush)FindResource("SecondaryText");
    }

    private static string FormatSensorValue(string sensorType, float value) => sensorType switch
    {
        "Temperature" => $"{value:F1} °C",
        "Load" => $"{value:F1} %",
        "Clock" => $"{value:F0} MHz",
        "Power" => $"{value:F1} W",
        "Voltage" => $"{value:F3} V",
        "Fan" => $"{value:F0} RPM",
        "Control" => $"{value:F1} %",
        "Data" => $"{value:F1} GB",
        "SmallData" => $"{value:F1} MB",
        _ => value.ToString("F2")
    };

    private static int SensorRank(string sensorType) => sensorType switch
    {
        "Temperature" => 0,
        "Load" => 1,
        "Clock" => 2,
        "Power" => 3,
        "Voltage" => 4,
        "Fan" => 5,
        "Data" => 6,
        "SmallData" => 7,
        "Control" => 8,
        _ => 9
    };

    private static int HardwareRank(string hardwareType)
    {
        if (hardwareType.Contains("Cpu", StringComparison.OrdinalIgnoreCase)) return 0;
        if (hardwareType.Contains("Gpu", StringComparison.OrdinalIgnoreCase)) return 1;
        if (hardwareType.Contains("Memory", StringComparison.OrdinalIgnoreCase)) return 2;
        if (hardwareType.Contains("Motherboard", StringComparison.OrdinalIgnoreCase)) return 3;
        if (hardwareType.Contains("Storage", StringComparison.OrdinalIgnoreCase)) return 4;
        return 5;
    }

    private static string FriendlyHardwareType(string type)
    {
        if (type.Contains("Cpu", StringComparison.OrdinalIgnoreCase)) return "CPU";
        if (type.Contains("Gpu", StringComparison.OrdinalIgnoreCase)) return "GPU";
        if (type.Contains("Memory", StringComparison.OrdinalIgnoreCase)) return "MEMORY";
        if (type.Contains("Psu", StringComparison.OrdinalIgnoreCase)) return "PSU";
        return type.ToUpperInvariant();
    }

    private static string FormatIdle(long seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m {seconds % 60}s";
        return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
    }

    private static string ShortId(string id) => id.Length <= 8 ? id : id[..8];
    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
