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
    private IReadOnlyList<SensorCatalogDto>? _catalog;
    private bool _refreshing;

    public ObservableCollection<HardwareGroupViewModel> HardwareGroups { get; } = new();

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
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async void OnRefreshTick(object? sender, EventArgs e) => await RefreshAsync();

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
