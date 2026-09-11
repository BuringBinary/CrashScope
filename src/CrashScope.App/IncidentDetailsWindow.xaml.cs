using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CrashScope.App.Services;
using CrashScope.App.ViewModels;
using CrashScope.Contracts;

namespace CrashScope.App;

public partial class IncidentDetailsWindow : Window
{
    private readonly string _incidentId;
    private readonly AgentRpcClient _client = new();
    private readonly CancellationTokenSource _lifetime = new();

    public ObservableCollection<IncidentMetricSeriesViewModel> Metrics { get; } = new();
    public ObservableCollection<IncidentTimelineEventViewModel> Timeline { get; } = new();

    public IncidentDetailsWindow(string incidentId)
    {
        _incidentId = incidentId;
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        IncidentIdText.Text = $"Incident {_incidentId}";
        await LoadAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task LoadAsync()
    {
        try
        {
            var response = await _client.SendAsync(
                AgentRpcProtocol.IncidentDetailCommand,
                _lifetime.Token,
                _incidentId);

            if (!response.Ok || response.IncidentDetail is null)
                throw new IOException(response.Error ?? "Incident detail is unavailable.");

            Render(response.IncidentDetail);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            EvidenceText.Text = $"Unable to load incident: {ex.Message}";
            CatalogText.Text = "Unavailable";
            WindowText.Text = "—";
        }
    }

    private void Render(IncidentDetailDto detail)
    {
        IncidentIdText.Text = $"Incident {detail.Summary.Id} · {SessionText(detail.Summary.SessionId)}";
        EvidenceText.Text = BuildEvidenceText(detail.Summary);
        WindowText.Text = BuildWindowText(detail.WindowStart, detail.WindowEnd);
        CatalogText.Text = detail.SensorCatalogAvailable
            ? "Frozen sensor catalog available · hardware values can be decoded"
            : "Legacy incident · sensor catalog missing, event timeline still available";

        Metrics.Clear();
        foreach (var series in detail.Metrics)
            Metrics.Add(ToMetricViewModel(series));
        MetricsHintText.Text = Metrics.Count > 0
            ? $"{detail.Metrics.Max(series => series.Points.Count)} telemetry samples"
            : "No decodable hardware series";

        Timeline.Clear();
        foreach (var item in detail.Timeline)
            Timeline.Add(ToTimelineViewModel(item));
        TimelineCountText.Text = $"{Timeline.Count} events";
    }

    private IncidentMetricSeriesViewModel ToMetricViewModel(IncidentMetricSeriesDto series)
    {
        var points = new PointCollection();
        if (series.Points.Count > 0)
        {
            var first = series.Points[0].Timestamp;
            var last = series.Points[^1].Timestamp;
            var duration = Math.Max(1, (last - first).TotalSeconds);
            var valueRange = Math.Max(1, series.Maximum - series.Minimum);

            foreach (var point in series.Points)
            {
                var x = (point.Timestamp - first).TotalSeconds / duration * 100d;
                var normalized = Math.Clamp((point.Value - series.Minimum) / valueRange, 0, 1);
                points.Add(new Point(x, 100d - normalized * 100d));
            }
        }

        var latest = series.Points.LastOrDefault();
        var latestText = latest is null ? "—" : $"{latest.Value:F1} {series.Unit}";
        var rangeText = series.Points.Count == 0
            ? "No samples"
            : $"{series.Points[0].Timestamp.ToLocalTime():HH:mm:ss} → {series.Points[^1].Timestamp.ToLocalTime():HH:mm:ss}";

        return new IncidentMetricSeriesViewModel
        {
            Label = series.Label.ToUpperInvariant(),
            LatestValueText = latestText,
            RangeText = rangeText,
            Points = points
        };
    }

    private IncidentTimelineEventViewModel ToTimelineViewModel(IncidentTimelineEventDto item) => new()
    {
        TimeText = item.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
        Kind = item.Kind.ToUpperInvariant(),
        Title = item.Title,
        Detail = item.Detail,
        SeverityBrush = SeverityBrush(item.Severity)
    };

    private Brush SeverityBrush(string severity)
    {
        if (string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase))
            return (Brush)FindResource("DangerBrush");
        if (string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromRgb(245, 158, 11));
        if (string.Equals(severity, "Notice", StringComparison.OrdinalIgnoreCase))
            return (Brush)FindResource("AccentBrush");
        return (Brush)FindResource("SecondaryText");
    }

    private static string BuildEvidenceText(IncidentSummaryDto summary)
    {
        var evidence = new List<string>();
        if (summary.KernelPower41) evidence.Add("Kernel-Power 41");
        if (summary.UnexpectedShutdown6008) evidence.Add("Unexpected shutdown 6008");
        if (summary.WheaEvidence) evidence.Add("WHEA");
        if (summary.DisplayTdrEvidence) evidence.Add("Display/TDR");
        return evidence.Count == 0
            ? "No classified Windows evidence in preliminary summary"
            : string.Join(" · ", evidence);
    }

    private static string BuildWindowText(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null || end is null)
            return "Unknown capture window";
        var duration = end.Value - start.Value;
        return $"{start.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} → {end.Value.ToLocalTime():HH:mm:ss} · {FormatDuration(duration)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1) return $"{Math.Max(0, duration.TotalSeconds):F0}s";
        if (duration.TotalHours < 1) return $"{duration.TotalMinutes:F1}m";
        return $"{duration.TotalHours:F1}h";
    }

    private static string SessionText(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? "unknown session" : $"session {(sessionId.Length <= 8 ? sessionId : sessionId[..8])}";
}
