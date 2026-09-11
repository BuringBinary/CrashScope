using System.Windows.Media;

namespace CrashScope.App.ViewModels;

public sealed class IncidentMetricSeriesViewModel
{
    public string Label { get; init; } = string.Empty;
    public string LatestValueText { get; init; } = "—";
    public string RangeText { get; init; } = string.Empty;
    public PointCollection Points { get; init; } = [];
}

public sealed class IncidentTimelineEventViewModel
{
    public string TimeText { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public Brush SeverityBrush { get; init; } = Brushes.SlateGray;
}
