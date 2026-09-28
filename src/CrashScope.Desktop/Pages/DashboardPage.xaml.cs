using CrashScope.Core.Analysis;
using CrashScope.Desktop.Services;
using CrashScope.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

public partial class DashboardPage : UserControl
{
    private readonly AppService _service;
    private readonly SensorHistoryBuffer _history = new(maxPoints: 300);

    private IReadOnlyList<SensorCatalogEntry>? _mappedCatalog;
    private string? _mappedSession;
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
    private string _gpuDisplayName = "GPU";
    internal int HealthLoadVersion;

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

    // Chart colours come from the active theme. OxyPlot cannot bind to DynamicResource,
    // so ApplyChartTheme re-resolves and re-applies them on every theme change.
    private static OxyColor CpuLineColor => ChartPalette.Resolve(ChartPalette.CpuKey);
    private static OxyColor GpuLineColor => ChartPalette.Resolve(ChartPalette.GpuKey);
    private static OxyColor GpuClockColor => ChartPalette.Resolve(ChartPalette.ClockKey);
    private static OxyColor GpuPowerColor => ChartPalette.Resolve(ChartPalette.PowerKey);
    private static OxyColor TransitionDropColor => ChartPalette.Resolve(ChartPalette.DropKey);
    private static OxyColor RamLineColor => ChartPalette.Resolve(ChartPalette.RamKey);
    private static OxyColor GridColor => ChartPalette.Resolve(ChartPalette.GridKey);
    private static OxyColor TextMuted => ChartPalette.Resolve(ChartPalette.MutedKey);

    public DashboardPage()
    {
        InitializeComponent();
        _service = App.Current.Service;

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
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _service.SensorUpdated -= OnSensorUpdated;
        _service.SensorUpdated += OnSensorUpdated;
        _service.GpuEngineUpdated -= OnGpuEngineUpdated;
        _service.GpuEngineUpdated += OnGpuEngineUpdated;
        _service.StatusChanged -= OnStatusChanged;
        _service.StatusChanged += OnStatusChanged;
        var theme = App.TryCurrent?.Theme;
        if (theme is not null)
        {
            theme.ThemeChanged -= OnThemeChanged;
            theme.ThemeChanged += OnThemeChanged;
        }
        ApplyChartTheme();
        BuildSensorMap();
        _ = DashboardHealth.RefreshAsync(this);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _service.SensorUpdated -= OnSensorUpdated;
        _service.GpuEngineUpdated -= OnGpuEngineUpdated;
        _service.StatusChanged -= OnStatusChanged;
        var theme = App.TryCurrent?.Theme;
        if (theme is not null) theme.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (IsLoaded) ApplyChartTheme();
    });

    /// <summary>
    /// Re-resolve every chart colour from the active theme and refresh the plots.
    /// Series data is left untouched, so switching themes does not clear chart history.
    /// </summary>
    private void ApplyChartTheme()
    {
        _cpuSeries.Color = CpuLineColor;
        _gpuSeries.Color = GpuLineColor;
        _gpuClockSeries.Color = GpuClockColor;
        _gpuPowerSeries.Color = GpuPowerColor;
        _ramSeries.Color = RamLineColor;

        _gpuTransitionMarkers.MarkerFill = TransitionDropColor;
        _gpuTransitionMarkers.MarkerStroke = ChartPalette.Resolve(ChartPalette.MutedKey);

        StyleModel(_cpuPlotModel, TextMuted);
        StyleModel(_gpuPlotModel, TextMuted);
        StyleModel(_ramPlotModel, TextMuted);
        StyleModel(_gpuClockPowerModel, TextMuted);

        // The dual-axis plot colour-codes each axis to match its series.
        foreach (var axis in _gpuClockPowerModel.Axes)
        {
            var accent = axis.Key switch
            {
                "ClockAxis" => GpuClockColor,
                "PowerAxis" => GpuPowerColor,
                _ => TextMuted
            };
            axis.TextColor = accent;
            axis.TitleColor = accent;
        }

        _cpuPlotModel.InvalidatePlot(false);
        _gpuPlotModel.InvalidatePlot(false);
        _ramPlotModel.InvalidatePlot(false);
        _gpuClockPowerModel.InvalidatePlot(false);
    }

    private static void StyleModel(PlotModel model, OxyColor muted)
    {
        model.TextColor = muted;
        var grid = GridColor;
        foreach (var axis in model.Axes)
        {
            axis.TicklineColor = grid;
            axis.AxislineColor = grid;
            axis.TextColor = muted;
            axis.TitleColor = muted;
        }
    }

    private void OnStatusChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (IsLoaded) BuildSensorMap();
    });

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
            MarkerStroke = ChartPalette.Resolve(ChartPalette.MutedKey),
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
        var session = _service.CurrentSession?.SessionId;
        if (ReferenceEquals(catalog, _mappedCatalog) && session == _mappedSession) return;
        _mappedCatalog = catalog;
        _mappedSession = session;
        _history.Clear();
        _transitionItems.Clear();
        GpuTransitionsList.ItemsSource = null;
        GpuTransitionsPanel.Visibility = Visibility.Collapsed;
        _gpuTransitionMarkers.Points.Clear();
        if (_transitionMonitor is not null) _transitionMonitor.TransitionDetected -= OnTransitionDetected;
        var map = DesktopSensorMap.Build(catalog);
        _cpuModelIdx = map.CpuModel;
        _cpuLoadIdx = map.CpuLoad;
        _cpuTempIdx = map.CpuTemp;
        _cpuClockIdx = map.CpuClock;
        _gpuModelIdx = map.GpuModel;
        _gpuLoadIdx = map.GpuLoad;
        _gpuTempIdx = map.GpuTemp;
        _gpuClockIdx = map.GpuClock;
        _gpuPowerIdx = map.GpuPower;
        _gpuVramUsedIdx = map.GpuVramUsed;
        _gpuVramTotalIdx = map.GpuVramTotal;
        _ramUsedIdx = map.RamUsed;
        _ramAvailIdx = map.RamAvailable;
        _ramLoadIdx = map.RamLoad;
        CpuModelText.Text = _cpuModelIdx >= 0 ? catalog[_cpuModelIdx].HardwareName : "CPU: unavailable / not recording";
        _gpuDisplayName = _gpuModelIdx >= 0 ? catalog[_gpuModelIdx].HardwareName : "GPU: unavailable / not recording";
        GpuModelText.Text = _gpuDisplayName;
        _transitionMonitor = new GpuTransitionMonitor(_gpuLoadIdx, _gpuClockIdx, _gpuPowerIdx);
        _transitionMonitor.TransitionDetected += OnTransitionDetected;
        UpdateDisplay([]);
        UpdateCharts();
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values)
    {
        var catalog = _service.SensorCatalog;
        var session = _service.CurrentSession?.SessionId;
        var displayValues = values.Select((v, i) => i < catalog.Count ? SensorValueQuality.ForDisplay(catalog[i], v) : null).ToArray();
        Dispatcher.InvokeAsync(() =>
        {
            // Drop queued callbacks from an unloaded page or a prior recording/catalog.
            if (!IsLoaded || session != _service.CurrentSession?.SessionId || !ReferenceEquals(catalog, _service.SensorCatalog)) return;
            BuildSensorMap();
            _history.Push(displayValues);
            UpdateDisplay(displayValues);
            UpdateCharts();
            _transitionMonitor?.Check(_history.GetHistory());
        });
    }

    private void OnTransitionDetected(GpuTransition transition)
    {
        Dispatcher.InvokeAsync(() =>
        {
            GpuTransitionsPanel.Visibility = Visibility.Visible;

            var item = new GpuTransitionItem
            {
                Timestamp = transition.Timestamp.ToString("HH:mm:ss"),
                SensorLabel = $"{transition.SensorName} ({transition.SensorType})",
                TransitionDetail = DescribeTransition(transition, _gpuDisplayName),
                MarkerColor = transition.IsDrop
                    ? SeverityPalette.Resolve(SeverityPalette.Critical)
                    : SeverityPalette.Resolve(SeverityPalette.Info)
            };

            _transitionItems.Insert(0, item);
            while (_transitionItems.Count > 50)
                _transitionItems.RemoveAt(_transitionItems.Count - 1);

            GpuTransitionsList.ItemsSource = null;
            GpuTransitionsList.ItemsSource = _transitionItems.ToList();
        });
    }

    // Display-side formatting: the monitor reports measurements, this page owns
    // presentation text (the same layer as every other UI string). A future
    // localization pass swaps the wording here, not in the monitoring logic.
    private static string DescribeTransition(GpuTransition transition, string gpuName)
    {
        var unit = transition.SensorType switch
        {
            "Load" => "%",
            "Clock" => "MHz",
            "Power" => "W",
            _ => ""
        };
        var direction = transition.IsDrop ? "drop" : "surge";
        return $"{gpuName} {transition.SensorName}: {direction} from ~{FormatValue(transition.FromValue)}{unit}" +
               $" to ~{FormatValue(transition.ToValue)}{unit} over {transition.ElapsedSeconds:0.#}s";
    }

    private static string FormatValue(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

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
        float? Value(int i) => i >= 0 && i < values.Count ? values[i] : null;
        string Show(int i, string unit = "", string format = "F0") => Value(i) is { } v ? v.ToString(format) + unit : Loc.T("common.notAvailable");
        CpuLoadText.Text = Show(_cpuLoadIdx);
        CpuTempText.Text = Show(_cpuTempIdx, "°C");
        CpuClockText.Text = Show(_cpuClockIdx, " MHz");
        GpuLoadText.Text = Show(_gpuLoadIdx);
        GpuTempText.Text = Show(_gpuTempIdx, "°C");
        GpuClockText.Text = Show(_gpuClockIdx, " MHz");
        GpuPowerText.Text = Show(_gpuPowerIdx, " W");
        GpuVramText.Text = Value(_gpuVramUsedIdx) is { } vram ? $"{vram / 1024f:F1} GiB" : Loc.T("common.notAvailable");
        RamUsedText.Text = Show(_ramUsedIdx, format: "F1");
        RamAvailText.Text = Show(_ramAvailIdx, " GiB", "F1");
        RamPercentText.Text = Show(_ramLoadIdx, "%");
        RamTotalText.Text = Value(_ramUsedIdx) is { } used && Value(_ramAvailIdx) is { } available
            ? Loc.F("dashboard.ramTotal", used + available) : Loc.T("common.notAvailable");
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
                var key = engine.EngineType.Length > 0 ? engine.EngineType : Loc.T("common.unknown");
                if (aggregated.TryGetValue(key, out var existing))
                    aggregated[key] = existing + engine.UtilizationPercent;
                else
                    aggregated[key] = engine.UtilizationPercent;
            }
        }

        _lastEngineUsages = aggregated;

        Dispatcher.InvokeAsync(() => { if (IsLoaded) UpdateGpuEngines(aggregated, sample.Timestamp); });
    }

    private void UpdateGpuEngines(Dictionary<string, float> engines, DateTimeOffset timestamp)
    {
        GpuEnginesPanel.Visibility = Visibility.Visible;
        GpuEnginesTimestamp.Text = Loc.F("dashboard.updatedAt", timestamp);

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
                    Value = value.HasValue ? $"{value.Value:F1}{unit}" : Loc.T("dashboard.sensorUnavailable")
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
    public static async Task RefreshAsync(DashboardPage page)
    {
        // Reading off the UI thread keeps first navigation responsive while large
        // analyses re-evaluate; the version guards against stale unload/reload races.
        var version = ++page.HealthLoadVersion;
        IncidentReadResult result;
        try { result = await App.Current.Service.ReadIncidentsAsync(); }
        catch (Exception ex) { result = IncidentReadResult.Failed(ex); }
        if (!page.IsLoaded || version != page.HealthLoadVersion) return;

        var incidents = result.Items;
        if (incidents.Count == 0)
        {
            page.HealthScoreText.Text = "--";
            page.HealthTrendText.Text = Loc.T("common.notAvailable");
            page.HealthTrendArrow.Text = "\uE74C";
            page.HealthTrendArrow.Foreground = SeverityPalette.Resolve(SeverityPalette.Neutral);
            page.HealthScoreText.Foreground = SeverityPalette.Resolve(SeverityPalette.Neutral);
            if (result.HasIssues)
            {
                // A failed read must not look like a clean "no incidents" bill of health.
                page.HealthMtbfText.Text = result.Summary(Loc.T, App.Current.Localization.CurrentLocale);
                page.HealthSummaryText.Text = result.Details(Loc.T);
            }
            else
            {
                page.HealthMtbfText.Text = Loc.T("dashboard.health.noIncidents");
                page.HealthSummaryText.Text = Loc.T("dashboard.health.runRecorder");
            }
            return;
        }

        var analyses = incidents.Select(i => i.Analysis).ToList();
        var report = CrossIncidentAnalyzer.Analyze(analyses);

        if (report.StabilityTrends.Count > 0)
        {
            var trend = report.StabilityTrends[0];
            page.HealthScoreText.Text = trend.StabilityScore.ToString();
            page.HealthMtbfText.Text = Loc.F("dashboard.health.mtbf", trend.MtbfHours, trend.IncidentCount);
            page.HealthTrendText.Text = trend.TrendDirection;
            page.HealthSummaryText.Text = report.Summary;

            page.HealthScoreText.Foreground = trend.StabilityScore switch
            {
                >= 80 => SeverityPalette.Resolve(SeverityPalette.Info),
                >= 50 => SeverityPalette.Resolve(SeverityPalette.High),
                _ => SeverityPalette.Resolve(SeverityPalette.Critical)
            };

            page.HealthTrendArrow.Text = trend.TrendDirection switch
            {
                "Improving" => "\uE74D",
                "Worsening" => "\uE74E",
                _ => "\uE74C"
            };
            page.HealthTrendArrow.Foreground = trend.TrendDirection switch
            {
                "Improving" => SeverityPalette.Resolve(SeverityPalette.Info),
                "Worsening" => SeverityPalette.Resolve(SeverityPalette.Critical),
                _ => SeverityPalette.Resolve(SeverityPalette.Neutral)
            };
            page.HealthTrendText.Foreground = trend.TrendDirection switch
            {
                "Improving" => SeverityPalette.Resolve(SeverityPalette.Info),
                "Worsening" => SeverityPalette.Resolve(SeverityPalette.Critical),
                _ => SeverityPalette.Resolve(SeverityPalette.Neutral)
            };
        }

        // Partial reads keep the score, but missing records stay visible.
        if (result.HasIssues)
            page.HealthSummaryText.Text =
                result.Summary(Loc.T, App.Current.Localization.CurrentLocale) + "\n" + page.HealthSummaryText.Text;

        page.HealthCard.Visibility = Visibility.Visible;
    }
}