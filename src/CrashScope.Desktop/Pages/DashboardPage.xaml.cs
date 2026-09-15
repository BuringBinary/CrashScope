using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

public partial class DashboardPage : UserControl
{
    private readonly AppService _service;
    private readonly SensorHistoryBuffer _history = new(maxPoints: 300);

    private int _cpuLoadIdx = -1;
    private int _cpuTempIdx = -1;
    private int _cpuClockIdx = -1;
    private int _cpuModelIdx = -1;
    private int _gpuLoadIdx = -1;
    private int _gpuTempIdx = -1;
    private int _gpuClockIdx = -1;
    private int _gpuPowerIdx = -1;
    private int _gpuVramUsedIdx = -1;
    private int _gpuVramTotalIdx = -1;
    private int _gpuModelIdx = -1;
    private int _ramUsedIdx = -1;
    private int _ramAvailIdx = -1;
    private int _ramLoadIdx = -1;

    private GpuTransitionMonitor? _transitionMonitor;

    private readonly PlotModel _cpuPlotModel;
    private readonly PlotModel _gpuPlotModel;
    private readonly PlotModel _gpuClockPowerModel;
    private readonly PlotModel _ramPlotModel;
    private readonly LineSeries _cpuSeries;
    private readonly LineSeries _gpuSeries;
    private readonly LineSeries _gpuClockSeries;
    private readonly LineSeries _gpuPowerSeries;
    private readonly ScatterSeries _gpuTransitionMarkers;
    private readonly LineSeries _ramSeries;

    private readonly List<GpuTransitionItem> _transitionItems = new();

    private static readonly OxyColor CpuLineColor = OxyColor.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly OxyColor GpuLineColor = OxyColor.FromRgb(0x00, 0xD6, 0x8F);
    private static readonly OxyColor GpuClockColor = OxyColor.FromRgb(0x6C, 0x5C, 0xE7);
    private static readonly OxyColor GpuPowerColor = OxyColor.FromRgb(0xFF, 0xD9, 0x3D);
    private static readonly OxyColor TransitionDropColor = OxyColor.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly OxyColor TransitionSurgeColor = OxyColor.FromRgb(0x00, 0xD6, 0x8F);
    private static readonly OxyColor RamLineColor = OxyColor.FromRgb(0xFF, 0xD9, 0x3D);
    private static readonly OxyColor GridColor = OxyColor.FromRgb(0x2A, 0x2B, 0x4A);
    private static readonly OxyColor TextMuted = OxyColor.FromRgb(0x8B, 0x8B, 0xA0);

    public DashboardPage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _service.SensorUpdated += OnSensorUpdated;
        _service.GpuEngineUpdated += OnGpuEngineUpdated;

        BuildSensorMap();

        _cpuSeries = CreateLineSeries(CpuLineColor, "CPU Load");
        _gpuSeries = CreateLineSeries(GpuLineColor, "GPU Load");
        _gpuClockSeries = CreateLineSeries(GpuClockColor, "GPU Clock");
        _gpuPowerSeries = CreateLineSeries(GpuPowerColor, "GPU Power");
        _gpuTransitionMarkers = CreateTransitionScatterSeries();
        _ramSeries = CreateLineSeries(RamLineColor, "RAM Used");

        _cpuPlotModel = CreatePlotModel("CPU Load %", _cpuSeries);

        _gpuPlotModel = CreatePlotModel("GPU Load %", _gpuSeries);
        _gpuPlotModel.Series.Add(_gpuTransitionMarkers);

        _gpuClockPowerModel = CreateDualAxisPlotModel("Clock MHz", "Power W", _gpuClockSeries, _gpuPowerSeries);

        _ramPlotModel = CreatePlotModel("RAM Used (GB)", _ramSeries);

        CpuChart.Model = _cpuPlotModel;
        GpuChart.Model = _gpuPlotModel;
        GpuClockPowerChart.Model = _gpuClockPowerModel;
        RamChart.Model = _ramPlotModel;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DashboardHealth.Refresh(this);
    }

    private static LineSeries CreateLineSeries(OxyColor color, string title)
    {
        return new LineSeries
        {
            Color = color,
            StrokeThickness = 1.5,
            MarkerType = MarkerType.None,
            Title = title,
            CanTrackerInterpolatePoints = false
        };
    }

    private static ScatterSeries CreateTransitionScatterSeries()
    {
        return new ScatterSeries
        {
            MarkerType = MarkerType.Diamond,
            MarkerSize = 6,
            MarkerFill = TransitionDropColor,
            MarkerStroke = OxyColors.White,
            MarkerStrokeThickness = 1,
            Title = "Transitions"
        };
    }

    private static PlotModel CreatePlotModel(string title, LineSeries series)
    {
        var model = new PlotModel
        {
            Title = null,
            PlotAreaBorderColor = OxyColors.Transparent,
            TextColor = TextMuted
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 0,
            MaximumPadding = 0.15,
            TicklineColor = GridColor,
            AxislineColor = GridColor,
            TextColor = TextMuted,
            Title = title,
            TitleColor = TextMuted,
            TitleFontSize = 9,
            FontSize = 9
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = -300,
            Maximum = 0,
            TicklineColor = GridColor,
            AxislineColor = GridColor,
            TextColor = TextMuted,
            Title = "Seconds ago",
            TitleColor = TextMuted,
            TitleFontSize = 9,
            FontSize = 9
        });

        model.Series.Add(series);
        return model;
    }

    private static PlotModel CreateDualAxisPlotModel(
        string leftTitle, string rightTitle,
        LineSeries leftSeries, LineSeries rightSeries)
    {
        var model = new PlotModel
        {
            Title = null,
            PlotAreaBorderColor = OxyColors.Transparent,
            TextColor = TextMuted
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 0,
            MaximumPadding = 0.15,
            TicklineColor = GridColor,
            AxislineColor = GridColor,
            TextColor = GpuClockColor,
            Title = leftTitle,
            TitleColor = GpuClockColor,
            TitleFontSize = 9,
            FontSize = 9,
            Key = "ClockAxis"
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Right,
            Minimum = 0,
            MaximumPadding = 0.15,
            TicklineColor = GridColor,
            AxislineColor = GridColor,
            TextColor = GpuPowerColor,
            Title = rightTitle,
            TitleColor = GpuPowerColor,
            TitleFontSize = 9,
            FontSize = 9,
            Key = "PowerAxis"
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = -300,
            Maximum = 0,
            TicklineColor = GridColor,
            AxislineColor = GridColor,
            TextColor = TextMuted,
            Title = "Seconds ago",
            TitleColor = TextMuted,
            TitleFontSize = 9,
            FontSize = 9
        });

        leftSeries.YAxisKey = "ClockAxis";
        rightSeries.YAxisKey = "PowerAxis";

        model.Series.Add(leftSeries);
        model.Series.Add(rightSeries);
        return model;
    }

    private void BuildSensorMap()
    {
        var catalog = _service.SensorCatalog;
        for (var i = 0; i < catalog.Count; i++)
        {
            var entry = catalog[i];
            var hw = entry.HardwareType;
            var st = entry.SensorType;
            var sn = entry.SensorName;

            if (hw == "Cpu")
            {
                if (st == "Load" && sn.Contains("Total", StringComparison.OrdinalIgnoreCase))
                    _cpuLoadIdx = i;
                else if (st == "Temperature" && sn.Contains("Package", StringComparison.OrdinalIgnoreCase))
                    _cpuTempIdx = i;
                else if (st == "Clock" && sn.Contains("Package", StringComparison.OrdinalIgnoreCase))
                    _cpuClockIdx = i;

                if (_cpuModelIdx < 0)
                    CpuModelText.Text = entry.HardwareName;
                _cpuModelIdx = i;
            }
            else if (hw.Contains("Gpu", StringComparison.OrdinalIgnoreCase))
            {
                if (st == "Load" && sn.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    _gpuLoadIdx = i;
                else if (st == "Temperature" && sn.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    _gpuTempIdx = i;
                else if (st == "Clock" && sn.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    _gpuClockIdx = i;
                else if (st == "Power" && sn.Contains("Package", StringComparison.OrdinalIgnoreCase))
                    _gpuPowerIdx = i;
                else if (st == "SmallData" && sn.Contains("Used", StringComparison.OrdinalIgnoreCase))
                    _gpuVramUsedIdx = i;
                else if (st == "SmallData" && sn.Contains("Total", StringComparison.OrdinalIgnoreCase))
                    _gpuVramTotalIdx = i;

                if (_gpuModelIdx < 0)
                    GpuModelText.Text = entry.HardwareName;
                _gpuModelIdx = i;
            }
            else if (hw == "Memory")
            {
                if (st == "Data" && sn.Contains("Used", StringComparison.OrdinalIgnoreCase))
                    _ramUsedIdx = i;
                else if (st == "Data" && sn.Contains("Available", StringComparison.OrdinalIgnoreCase))
                    _ramAvailIdx = i;
                else if (st == "Load" && sn.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                    _ramLoadIdx = i;
            }
        }

        if (_cpuLoadIdx < 0) _cpuLoadIdx = FindFirst(catalog, "Cpu", "Load");
        if (_cpuTempIdx < 0) _cpuTempIdx = FindFirst(catalog, "Cpu", "Temperature");
        if (_cpuClockIdx < 0) _cpuClockIdx = FindFirst(catalog, "Cpu", "Clock");
        if (_gpuLoadIdx < 0) _gpuLoadIdx = FindFirstGpu(catalog, "Load");
        if (_gpuTempIdx < 0) _gpuTempIdx = FindFirstGpu(catalog, "Temperature");
        if (_gpuClockIdx < 0) _gpuClockIdx = FindFirstGpu(catalog, "Clock");
        if (_gpuPowerIdx < 0) _gpuPowerIdx = FindFirstGpu(catalog, "Power");
        if (_gpuVramUsedIdx < 0) _gpuVramUsedIdx = FindFirst(catalog, "Gpu", "SmallData");
        if (_ramUsedIdx < 0) _ramUsedIdx = FindFirst(catalog, "Memory", "Data");
        if (_ramLoadIdx < 0) _ramLoadIdx = FindFirst(catalog, "Memory", "Load");

        var gpuName = _gpuModelIdx >= 0 && _gpuModelIdx < catalog.Count
            ? catalog[_gpuModelIdx].HardwareName
            : "GPU";

        _transitionMonitor = new GpuTransitionMonitor(_gpuLoadIdx, _gpuClockIdx, _gpuPowerIdx, gpuName);
        _transitionMonitor.TransitionDetected += OnTransitionDetected;
    }

    private static int FindFirst(IReadOnlyList<SensorCatalogEntry> catalog, string hardwareType, string sensorType)
    {
        for (var i = 0; i < catalog.Count; i++)
        {
            if (catalog[i].HardwareType == hardwareType && catalog[i].SensorType == sensorType)
                return i;
        }
        return -1;
    }

    private static int FindFirstGpu(IReadOnlyList<SensorCatalogEntry> catalog, string sensorType)
    {
        for (var i = 0; i < catalog.Count; i++)
        {
            if (catalog[i].HardwareType.Contains("Gpu", StringComparison.OrdinalIgnoreCase) &&
                catalog[i].SensorType == sensorType)
                return i;
        }
        return -1;
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values)
    {
        _history.Push(values);
        Dispatcher.Invoke(() =>
        {
            UpdateDisplay(values);
            UpdateCharts();
        });
        _transitionMonitor?.Check(_history.GetHistory());
    }

    private void OnTransitionDetected(GpuTransition transition)
    {
        Dispatcher.Invoke(() =>
        {
            GpuTransitionsPanel.Visibility = Visibility.Visible;

            var item = new GpuTransitionItem
            {
                Timestamp = transition.Timestamp.ToString("HH:mm:ss"),
                SensorLabel = $"{transition.SensorName} ({transition.SensorType})",
                TransitionDetail = transition.Description,
                MarkerColor = transition.IsDrop
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"))
            };

            _transitionItems.Insert(0, item);
            while (_transitionItems.Count > 50)
                _transitionItems.RemoveAt(_transitionItems.Count - 1);

            GpuTransitionsList.ItemsSource = null;
            GpuTransitionsList.ItemsSource = _transitionItems.ToList();
        });
    }

    private void ClearTransitionsButton_Click(object sender, RoutedEventArgs e)
    {
        _transitionItems.Clear();
        GpuTransitionsList.ItemsSource = null;
        GpuTransitionsPanel.Visibility = Visibility.Collapsed;
        _gpuTransitionMarkers.Points.Clear();
        _gpuPlotModel.InvalidatePlot(true);
    }

    private void UpdateCharts()
    {
        _cpuSeries.Points.Clear();
        var cpuSeries = _history.GetSeries(_cpuLoadIdx);
        var maxX = cpuSeries.Count > 0 ? cpuSeries[^1].X : 0;
        foreach (var (x, y) in cpuSeries)
            _cpuSeries.Points.Add(new DataPoint(x - maxX, y));

        _gpuSeries.Points.Clear();
        var gpuSeries = _history.GetSeries(_gpuLoadIdx);
        foreach (var (x, y) in gpuSeries)
            _gpuSeries.Points.Add(new DataPoint(x - maxX, y));

        _gpuClockSeries.Points.Clear();
        var gpuClockSeries = _history.GetSeries(_gpuClockIdx);
        foreach (var (x, y) in gpuClockSeries)
            _gpuClockSeries.Points.Add(new DataPoint(x - maxX, y));

        _gpuPowerSeries.Points.Clear();
        var gpuPowerSeries = _history.GetSeries(_gpuPowerIdx);
        foreach (var (x, y) in gpuPowerSeries)
            _gpuPowerSeries.Points.Add(new DataPoint(x - maxX, y));

        UpdateTransitionMarkers();

        _ramSeries.Points.Clear();
        var ramSeries = _history.GetSeries(_ramUsedIdx);
        foreach (var (x, y) in ramSeries)
            _ramSeries.Points.Add(new DataPoint(x - maxX, y));

        _cpuPlotModel.InvalidatePlot(true);
        _gpuPlotModel.InvalidatePlot(true);
        _gpuClockPowerModel.InvalidatePlot(true);
        _ramPlotModel.InvalidatePlot(true);
    }

    private void UpdateTransitionMarkers()
    {
        _gpuTransitionMarkers.Points.Clear();
        if (_transitionMonitor is null) return;

        var history = _history.GetHistory();
        if (history.Count == 0) return;

        var latestTimestamp = history[^1].Timestamp;

        var recent = _transitionMonitor.RecentTransitions
            .Where(t => (latestTimestamp - t.Timestamp).TotalSeconds <= 300)
            .ToList();

        foreach (var t in recent)
        {
            if (t.SensorType != "Load") continue;

            var offset = (t.Timestamp - latestTimestamp).TotalSeconds;
            _gpuTransitionMarkers.Points.Add(new ScatterPoint(
                offset,
                t.ToValue,
                size: t.IsDrop ? 6 : 4,
                value: 0));
        }
    }

    private void UpdateDisplay(IReadOnlyList<float?> values)
    {
        if (_cpuLoadIdx >= 0 && _cpuLoadIdx < values.Count)
            CpuLoadText.Text = $"{values[_cpuLoadIdx]:F0}";
        if (_cpuTempIdx >= 0 && _cpuTempIdx < values.Count)
            CpuTempText.Text = $"{values[_cpuTempIdx]:F0}°C";
        if (_cpuClockIdx >= 0 && _cpuClockIdx < values.Count)
            CpuClockText.Text = $"{values[_cpuClockIdx]:F0} MHz";

        if (_gpuLoadIdx >= 0 && _gpuLoadIdx < values.Count)
            GpuLoadText.Text = $"{values[_gpuLoadIdx]:F0}";
        if (_gpuTempIdx >= 0 && _gpuTempIdx < values.Count)
            GpuTempText.Text = $"{values[_gpuTempIdx]:F0}°C";
        if (_gpuClockIdx >= 0 && _gpuClockIdx < values.Count)
            GpuClockText.Text = $"{values[_gpuClockIdx]:F0} MHz";
        if (_gpuPowerIdx >= 0 && _gpuPowerIdx < values.Count)
            GpuPowerText.Text = $"{values[_gpuPowerIdx]:F0} W";

        if (_gpuVramUsedIdx >= 0 && _gpuVramUsedIdx < values.Count)
        {
            var usedGb = (values[_gpuVramUsedIdx] ?? 0) / 1024f;
            GpuVramText.Text = $"{usedGb:F1} GB";
        }

        if (_ramUsedIdx >= 0 && _ramUsedIdx < values.Count)
        {
            var usedGb = (values[_ramUsedIdx] ?? 0);
            RamUsedText.Text = $"{usedGb:F1}";
            RamTotalText.Text = $"{usedGb + (_ramAvailIdx >= 0 ? values[_ramAvailIdx] ?? 0 : 0):F0} GB total";
        }

        if (_ramAvailIdx >= 0 && _ramAvailIdx < values.Count)
            RamAvailText.Text = $"{values[_ramAvailIdx] ?? 0:F1} GB";

        if (_ramLoadIdx >= 0 && _ramLoadIdx < values.Count)
            RamPercentText.Text = $"{values[_ramLoadIdx]:F0}%";

        UpdateSensorList(values);
    }

    private Dictionary<string, float> _lastEngineUsages = new();

    private void OnGpuEngineUpdated(GpuEngineSample sample)
    {
        if (sample.Processes.Count == 0) return;

        var aggregated = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in sample.Processes)
        {
            foreach (var engine in proc.Engines)
            {
                var key = engine.EngineType.Length > 0 ? engine.EngineType : "Unknown";
                if (aggregated.TryGetValue(key, out var existing))
                    aggregated[key] = existing + engine.UtilizationPercent;
                else
                    aggregated[key] = engine.UtilizationPercent;
            }
        }

        _lastEngineUsages = aggregated;

        Dispatcher.Invoke(() => UpdateGpuEngines(aggregated, sample.Timestamp));
    }

    private void UpdateGpuEngines(Dictionary<string, float> engines, DateTimeOffset timestamp)
    {
        GpuEnginesPanel.Visibility = Visibility.Visible;
        GpuEnginesTimestamp.Text = $"Updated {timestamp:HH:mm:ss}";

        var items = engines
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new GpuEngineBarViewModel
            {
                EngineType = kv.Key,
                Utilization = kv.Value,
                DisplayPercent = $"{kv.Value:F1}%",
                BarWidth = Math.Min(kv.Value * 6, 600),
                BarOpacity = Math.Max(0.3, Math.Min(1.0, kv.Value / 100.0))
            })
            .ToList();

        GpuEnginesList.ItemsSource = items;
    }

    private void UpdateSensorList(IReadOnlyList<float?> values)
    {
        var catalog = _service.SensorCatalog;
        var items = new List<SensorDisplayItem>();

        for (var i = 0; i < catalog.Count && i < values.Count; i++)
        {
            var entry = catalog[i];
            var value = values[i];
            if (value.HasValue)
            {
                var unit = entry.SensorType switch
                {
                    "Temperature" => "°C",
                    "Load" => "%",
                    "Clock" => " MHz",
                    "Power" => " W",
                    "Data" => " GB",
                    "SmallData" => " MB",
                    "Throughput" => " MB/s",
                    _ => ""
                };
                items.Add(new SensorDisplayItem
                {
                    Name = $"{entry.HardwareType} — {entry.SensorName}",
                    Value = $"{value.Value:F1}{unit}"
                });
            }
        }

        SensorList.ItemsSource = items;
    }
}

public sealed class SensorDisplayItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class GpuEngineBarViewModel
{
    public string EngineType { get; set; } = "";
    public float Utilization { get; set; }
    public string DisplayPercent { get; set; } = "";
    public double BarWidth { get; set; }
    public double BarOpacity { get; set; }
}

public sealed class GpuTransitionItem
{
    public string Timestamp { get; set; } = "";
    public string SensorLabel { get; set; } = "";
    public string TransitionDetail { get; set; } = "";
    public System.Windows.Media.Brush MarkerColor { get; set; } = new SolidColorBrush(Colors.Gray);
}

internal static class DashboardHealth
{
    public static void Refresh(DashboardPage page)
    {
        var incidents = App.Current.Service.GetIncidents();
        if (incidents.Count == 0)
        {
            page.HealthScoreText.Text = "--";
            page.HealthMtbfText.Text = "No incidents yet";
            page.HealthTrendText.Text = "N/A";
            page.HealthSummaryText.Text = "Run the recorder to capture crash data.";
            page.HealthTrendArrow.Text = "\uE74C";
            page.HealthTrendArrow.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#8B8BA0"));
            page.HealthScoreText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#8B8BA0"));
            return;
        }

        var analyses = incidents.Select(i => i.Analysis).ToList();
        var report = CrossIncidentAnalyzer.Analyze(analyses);

        if (report.StabilityTrends.Count > 0)
        {
            var trend = report.StabilityTrends[0];
            page.HealthScoreText.Text = trend.StabilityScore.ToString();
            page.HealthMtbfText.Text = $"MTBF: {trend.MtbfHours:F1}h  ·  {trend.IncidentCount} incidents";
            page.HealthTrendText.Text = trend.TrendDirection;
            page.HealthSummaryText.Text = report.Summary;

            page.HealthScoreText.Foreground = trend.StabilityScore switch
            {
                >= 80 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F")),
                >= 50 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
            };

            page.HealthTrendArrow.Text = trend.TrendDirection switch
            {
                "Improving" => "\uE74D",
                "Worsening" => "\uE74E",
                _ => "\uE74C"
            };
            page.HealthTrendArrow.Foreground = trend.TrendDirection switch
            {
                "Improving" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F")),
                "Worsening" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"))
            };
            page.HealthTrendText.Foreground = trend.TrendDirection switch
            {
                "Improving" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F")),
                "Worsening" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"))
            };
        }

        page.HealthCard.Visibility = Visibility.Visible;
    }
}