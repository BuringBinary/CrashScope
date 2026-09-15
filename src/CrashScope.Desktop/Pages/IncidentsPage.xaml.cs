using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CrashScope.Desktop.Pages;

public partial class IncidentsPage : UserControl
{
    private readonly AppService _service;
    private readonly DiagnosticExportService _exportService = new();
    private IReadOnlyList<IncidentInfo> _incidents = Array.Empty<IncidentInfo>();
    private IncidentInfo? _selectedIncident;

    public IncidentsPage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshList();
    }

    public void RefreshList()
    {
        _incidents = _service.GetIncidents();
        TotalIncidentsText.Text = _incidents.Count.ToString();

        if (_incidents.Count > 0)
        {
            LastIncidentText.Text = _incidents[0].Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            var modeCounts = _incidents.GroupBy(i => i.Mode)
                .OrderByDescending(g => g.Count())
                .First();
            TopCauseText.Text = modeCounts.Key;
        }
        else
        {
            LastIncidentText.Text = "--";
            TopCauseText.Text = "--";
        }

        ExportButton.IsEnabled = _incidents.Count > 0;

        IncidentListBox.ItemsSource = _incidents.Select(i => new IncidentListItem
        {
            Timestamp = i.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            Title = $"{i.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm} — {i.Mode}",
            Mode = i.Mode,
            Confidence = $"{i.Confidence:P0} confidence",
            ModeBackground = GetModeBrush(i.Mode),
            Incident = i
        }).ToList();
    }

    private static SolidColorBrush GetModeBrush(string mode) => mode switch
    {
        "HardFreeze" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
        "BlueScreen" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C5CE7")),
        "DriverTimeoutRecovery" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")),
        "HardwareError" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E81123")),
        "AppCrash" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F")),
        "PowerLoss" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"))
    };

    private void IncidentListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedCount = IncidentListBox.SelectedItems.Count;
        CompareButton.IsEnabled = selectedCount == 2;

        if (IncidentListBox.SelectedItem is IncidentListItem item)
        {
            _selectedIncident = item.Incident;
            ShowDetail(item.Incident);
        }
    }

    private void ShowDetail(IncidentInfo incident)
    {
        DetailTitle.Text = $"{incident.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} — {incident.Mode}";

        var hypotheses = incident.Analysis.Hypotheses
            .Select(h => new HypothesisItem
            {
                Name = $"{h.Mode}",
                ScoreDisplay = $"Score: {h.Score:+0;-#} | {h.Confidence:P0}"
            }).ToList();
        HypothesesList.ItemsSource = hypotheses;

        QuestionsList.ItemsSource = incident.Analysis.OpenQuestions ?? Array.Empty<string>();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIncident is null)
        {
            MessageBox.Show("Please select an incident first.", "CrashScope",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Diagnostic Package",
            Filter = "ZIP Archive|*.zip",
            FileName = $"CrashScope-{_selectedIncident.DirectoryName}.zip",
            DefaultExt = ".zip"
        };

        if (dialog.ShowDialog() != true) return;

        ExportButton.IsEnabled = false;
        ExportButton.Content = "\uE74C  Exporting...";

        try
        {
            var outputDir = Path.GetDirectoryName(dialog.FileName)!;
            var redact = RedactCheckBox.IsChecked == true;

            var zipPath = await _exportService.ExportAsync(
                _selectedIncident, outputDir, redact);

            var result = MessageBox.Show(
                $"Diagnostic package exported successfully!\n\n{zipPath}\n\nOpen containing folder?",
                "CrashScope",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Export failed: {ex.Message}",
                "CrashScope",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportButton.IsEnabled = _incidents.Count > 0;
            ExportButton.Content = "\uE74C  Export Diagnostic ZIP";
        }
    }

    private void CrossAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        if (_incidents.Count < 1) return;

        var analyses = _incidents
            .Select(i => i.Analysis)
            .ToList();

        var report = CrossIncidentAnalyzer.Analyze(analyses);

        PopulateCrossAnalysis(report);
        CrossAnalysisPanel.Visibility = Visibility.Visible;
    }

    private void CloseAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        CrossAnalysisPanel.Visibility = Visibility.Collapsed;
    }

    private void CompareButton_Click(object sender, RoutedEventArgs e)
    {
        if (IncidentListBox.SelectedItems.Count != 2) return;

        var selected = IncidentListBox.SelectedItems
            .Cast<IncidentListItem>()
            .Select(item => item.Incident)
            .ToList();

        PopulateComparison(selected[0], selected[1]);
        ComparisonPanel.Visibility = Visibility.Visible;
    }

    private void CloseComparisonButton_Click(object sender, RoutedEventArgs e)
    {
        ComparisonPanel.Visibility = Visibility.Collapsed;
    }

    private void PopulateComparison(IncidentInfo left, IncidentInfo right)
    {
        var leftAnalysis = left.Analysis;
        var rightAnalysis = right.Analysis;

        ComparisonSubtitle.Text = $"{left.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm}  vs  {right.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm}";

        LeftHeader.Text = $"◀  {left.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm}";
        RightHeader.Text = $"{right.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm}  ▶";

        var modeBrush = GetModeBrush(left.Mode);
        var leftModeColor = left.Mode == right.Mode
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"))
            : modeBrush;
        LeftMode.Text = left.Mode;
        LeftMode.Foreground = leftModeColor;

        var rightModeColor = left.Mode == right.Mode
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"))
            : GetModeBrush(right.Mode);
        RightMode.Text = right.Mode;
        RightMode.Foreground = rightModeColor;

        LeftConfidence.Text = $"{left.Confidence:P0}";
        RightConfidence.Text = $"{right.Confidence:P0}";

        LeftGpuLoad.Text = leftAnalysis.FinalGpuLoadPercent?.ToString("F1") + "%" ?? "N/A";
        RightGpuLoad.Text = rightAnalysis.FinalGpuLoadPercent?.ToString("F1") + "%" ?? "N/A";

        var leftTransCount = leftAnalysis.GpuTransitions.Count;
        var rightTransCount = rightAnalysis.GpuTransitions.Count;
        LeftGpuTransitions.Text = $"{leftTransCount} events";
        RightGpuTransitions.Text = $"{rightTransCount} events";
        LeftGpuTransitions.Foreground = leftTransCount > rightTransCount
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));
        RightGpuTransitions.Foreground = rightTransCount > leftTransCount
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));

        var leftEtwGpu = leftAnalysis.Timeline.Count(e => e.Category == "ETW-GPU");
        var rightEtwGpu = rightAnalysis.Timeline.Count(e => e.Category == "ETW-GPU");
        LeftEtwGpu.Text = $"{leftEtwGpu} events";
        RightEtwGpu.Text = $"{rightEtwGpu} events";
        LeftEtwGpu.Foreground = leftEtwGpu > 0
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));
        RightEtwGpu.Foreground = rightEtwGpu > 0
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));

        var leftEtwPower = leftAnalysis.Timeline.Count(e => e.Category == "ETW-Power");
        var rightEtwPower = rightAnalysis.Timeline.Count(e => e.Category == "ETW-Power");
        LeftEtwPower.Text = $"{leftEtwPower} events";
        RightEtwPower.Text = $"{rightEtwPower} events";

        var leftEtwWhea = leftAnalysis.Timeline.Count(e => e.Category == "ETW-WHEA");
        var rightEtwWhea = rightAnalysis.Timeline.Count(e => e.Category == "ETW-WHEA");
        LeftEtwWhea.Text = $"{leftEtwWhea} events";
        RightEtwWhea.Text = $"{rightEtwWhea} events";
        LeftEtwWhea.Foreground = leftEtwWhea > 0
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E81123"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));
        RightEtwWhea.Foreground = rightEtwWhea > 0
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E81123"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));

        var leftDisplays = leftAnalysis.Graphics?.LatestDisplayChange?.After;
        var rightDisplays = rightAnalysis.Graphics?.LatestDisplayChange?.After;
        var leftMonitorCount = leftDisplays?.Count ?? 0;
        var rightMonitorCount = rightDisplays?.Count ?? 0;
        LeftMonitors.Text = $"{leftMonitorCount} monitor(s)";
        RightMonitors.Text = $"{rightMonitorCount} monitor(s)";

        var leftHasVirtual = leftDisplays?.Any(d => d.IsVirtual) ?? false;
        var rightHasVirtual = rightDisplays?.Any(d => d.IsVirtual) ?? false;
        LeftVirtualDisplay.Text = leftHasVirtual ? "YES \u26A0" : "No";
        RightVirtualDisplay.Text = rightHasVirtual ? "YES \u26A0" : "No";
        LeftVirtualDisplay.Foreground = leftHasVirtual
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));
        RightVirtualDisplay.Foreground = rightHasVirtual
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"));

        var leftDrivers = leftAnalysis.Graphics?.DriverInventory
            ?.Select(d => $"{d.AdapterName}: {d.DriverVersion ?? "N/A"}")
            .ToList() ?? [];
        var rightDrivers = rightAnalysis.Graphics?.DriverInventory
            ?.Select(d => $"{d.AdapterName}: {d.DriverVersion ?? "N/A"}")
            .ToList() ?? [];
        LeftDrivers.ItemsSource = leftDrivers.Count > 0 ? leftDrivers : new[] { "No data" };
        RightDrivers.ItemsSource = rightDrivers.Count > 0 ? rightDrivers : new[] { "No data" };

        var leftApps = leftAnalysis.Graphics?.GraphicsApps
            ?.Select(a => $"{a.ProcessName} ({a.Category})")
            .ToList() ?? [];
        var rightApps = rightAnalysis.Graphics?.GraphicsApps
            ?.Select(a => $"{a.ProcessName} ({a.Category})")
            .ToList() ?? [];

        var commonApps = leftApps.Intersect(rightApps).ToList();
        var leftOnly = leftApps.Except(rightApps).ToList();
        var rightOnly = rightApps.Except(leftApps).ToList();

        var leftAppDisplay = new List<string>();
        leftAppDisplay.AddRange(commonApps.Select(a => $"\u2714 {a}"));
        leftAppDisplay.AddRange(leftOnly.Select(a => $"  {a}"));
        LeftApps.ItemsSource = leftAppDisplay.Count > 0 ? leftAppDisplay : new[] { "No data" };

        var rightAppDisplay = new List<string>();
        rightAppDisplay.AddRange(commonApps.Select(a => $"\u2714 {a}"));
        rightAppDisplay.AddRange(rightOnly.Select(a => $"  {a}"));
        RightApps.ItemsSource = rightAppDisplay.Count > 0 ? rightAppDisplay : new[] { "No data" };

        var leftHyps = leftAnalysis.Hypotheses
            .OrderByDescending(h => h.Score)
            .Select(h => $"{h.Mode}: +{h.Score} ({h.Confidence:P0})")
            .ToList();
        var rightHyps = rightAnalysis.Hypotheses
            .OrderByDescending(h => h.Score)
            .Select(h => $"{h.Mode}: +{h.Score} ({h.Confidence:P0})")
            .ToList();
        LeftHypotheses.ItemsSource = leftHyps.Count > 0 ? leftHyps : new[] { "No data" };
        RightHypotheses.ItemsSource = rightHyps.Count > 0 ? rightHyps : new[] { "No data" };
    }

    private void PopulateCrossAnalysis(CrossIncidentReport report)
    {
        if (report.StabilityTrends.Count > 0)
        {
            var trend = report.StabilityTrends[0];
            StabilityScoreText.Text = trend.StabilityScore.ToString();
            MtbfText.Text = $"MTBF: {trend.MtbfHours:F1}h  ·  {trend.IncidentCount} incidents";
            TrendText.Text = $"Trend: {trend.TrendDirection}";

            StabilityScoreText.Foreground = trend.StabilityScore switch
            {
                >= 80 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F")),
                >= 50 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
            };

            TrendArrowText.Text = trend.TrendDirection switch
            {
                "Improving" => "\uE74D",
                "Worsening" => "\uE74E",
                _ => "\uE74C"
            };
            TrendArrowText.Foreground = trend.TrendDirection switch
            {
                "Improving" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F")),
                "Worsening" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"))
            };
        }

        AnalysisSummaryText.Text = report.Summary;

        PatternsList.ItemsSource = report.Patterns
            .OrderByDescending(p => p.Confidence)
            .Select(p => new PatternDisplayItem
            {
                Description = p.Description,
                OccurrenceDisplay = $"{p.OccurrenceCount}/{p.TotalIncidents}"
            }).ToList();

        DriversList.ItemsSource = report.DriverCorrelations
            .Select(d => new DriverDisplayItem
            {
                DriverDisplay = $"{d.AdapterName} — {d.DriverVersion}",
                FailuresDisplay = $"{d.IncidentCount} incidents · {string.Join(", ", d.FailureModes)}",
                RateDisplay = $"{d.FailuresPerDay:F1}/day"
            }).ToList();

        RecommendationsList.ItemsSource = report.Recommendations
            .Select(r =>
            {
                var (bg, label) = r.Priority switch
                {
                    >= 90 => ("#FF6B6B", "CRITICAL"),
                    >= 75 => ("#FFD93D", "HIGH"),
                    >= 55 => ("#6C5CE7", "MEDIUM"),
                    _ => ("#8B8BA0", "LOW")
                };
                return new RecommendationDisplayItem
                {
                    Title = r.Title,
                    Description = r.Description,
                    PriorityLabel = label,
                    PriorityBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg))
                };
            }).ToList();
    }
}

public sealed class IncidentListItem
{
    public string Timestamp { get; set; } = "";
    public string Title { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Confidence { get; set; } = "";
    public SolidColorBrush ModeBackground { get; set; } = new(Colors.Gray);
    public IncidentInfo Incident { get; set; } = null!;
}

public sealed class HypothesisItem
{
    public string Name { get; set; } = "";
    public string ScoreDisplay { get; set; } = "";
}

public sealed class PatternDisplayItem
{
    public string Description { get; set; } = "";
    public string OccurrenceDisplay { get; set; } = "";
}

public sealed class DriverDisplayItem
{
    public string DriverDisplay { get; set; } = "";
    public string FailuresDisplay { get; set; } = "";
    public string RateDisplay { get; set; } = "";
}

public sealed class RecommendationDisplayItem
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string PriorityLabel { get; set; } = "";
    public System.Windows.Media.Brush PriorityBackground { get; set; } = new SolidColorBrush(Colors.Gray);
}