using CrashScope.Core.Models;
using CrashScope.Desktop.Services;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;

namespace CrashScope.Desktop;

/// <summary>
/// Read-only always-on-top desktop bar: CPU / GPU / RAM from the recorder's sensor
/// stream, network throughput from OS counters. Draggable; position is remembered.
/// Hardware values show "--" when the recorder is stopped or stale; the net speed
/// stays live because it costs nothing.
/// </summary>
public partial class FloatingBarWindow : Window
{
    private const double SensorStaleAfterSeconds = 3.0;

    private static FloatingBarWindow? _current;

    /// <summary>Single-instance entry: reactivates the existing bar instead of stacking copies.</summary>
    public static void ShowSingle()
    {
        if (_current is { } existing)
        {
            existing.Activate();
            return;
        }
        new FloatingBarWindow().Show();
    }

    private readonly AppService _service;
    private readonly UiSettingsService _settings = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DesktopSensorMap _map;
    private IReadOnlyList<float?>? _lastValues;
    private DateTime _lastValuesUtc;
    private double? _downBps;
    private double? _upBps;
    private long? _prevRx;
    private long? _prevTx;
    private DateTime? _prevUtc;

    public FloatingBarWindow()
    {
        InitializeComponent();
        _current = this;
        _service = App.Current.Service;
        _map = DesktopSensorMap.Build(_service.SensorCatalog);
        _service.SensorUpdated += OnSensorUpdated;
        Closed += OnClosed;
        RestorePosition();
        _timer.Tick += (_, _) => { SampleNetwork(); Render(); };
        _timer.Start();
        Render();
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values)
        => Dispatcher.InvokeAsync(() =>
        {
            if (values.Count != _service.SensorCatalog.Count)
                _map = DesktopSensorMap.Build(_service.SensorCatalog);
            _lastValues = values;
            _lastValuesUtc = DateTime.UtcNow;
            Render();
        });

    private void OnClosed(object? sender, EventArgs e)
    {
        _service.SensorUpdated -= OnSensorUpdated;
        _timer.Stop();
        SavePosition();
        if (_current == this) _current = null;
    }

    private void SampleNetwork()
    {
        long rx = 0, tx = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var stats = nic.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
            catch
            {
                // A single unusable adapter must not blank the bar.
            }
        }

        var now = DateTime.UtcNow;
        var seconds = _prevUtc.HasValue ? (now - _prevUtc.Value).TotalSeconds : 0;
        var down = FloatingBarView.NextBytesPerSecond(rx, _prevRx, seconds);
        var up = FloatingBarView.NextBytesPerSecond(tx, _prevTx, seconds);
        if (down.HasValue) _downBps = down;
        if (up.HasValue) _upBps = up;
        _prevRx = rx;
        _prevTx = tx;
        _prevUtc = now;
    }

    private void Render()
    {
        var values = _lastValues;
        var stale = (DateTime.UtcNow - _lastValuesUtc).TotalSeconds > SensorStaleAfterSeconds;
        float? V(int index) => !stale && values is not null && index >= 0 && index < values.Count ? values[index] : null;

        BarText.Text = FloatingBarView.Build(
            V(_map.CpuLoad),
            V(_map.GpuLoad),
            V(_map.GpuTemp),
            V(_map.RamUsed),
            V(_map.RamAvailable),
            _downBps,
            _upBps);
    }

    private void Bar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
            SavePosition();
        }
        catch
        {
            // DragMove throws when the button was already released; harmless.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RestorePosition()
    {
        var area = SystemParameters.WorkArea;
        var left = _settings.FloatingBarLeft;
        var top = _settings.FloatingBarTop;
        Left = left.HasValue ? Clamp(left.Value, area.Left, Math.Max(area.Left, area.Right - Width - 8)) : area.Right - Width - 12;
        Top = top.HasValue ? Clamp(top.Value, area.Top, Math.Max(area.Top, area.Bottom - Height - 8)) : area.Bottom - Height - 48;
    }

    private static double Clamp(double value, double min, double max)
        => double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));

    private void SavePosition()
    {
        _settings.FloatingBarLeft = Left;
        _settings.FloatingBarTop = Top;
    }
}
