using CrashScope.Core.Analysis;
using CrashScope.Desktop.Services;
using CrashScope.Core.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
// System.Drawing is in the global usings via UseWindowsForms; pin Brush to WPF.
using Brush = System.Windows.Media.Brush;

namespace CrashScope.Desktop.Pages;

public partial class IncidentsPage : UserControl, IRefreshableLocale
{
    private readonly Func<Task<IncidentReadResult>> _loadIncidents;
    private bool _isLoading;
    private int _loadVersion;
    private readonly Func<string> _locale;
    private readonly Func<string, string> _translate;
    private string? _requestedId;
    private bool _isExporting;
    private bool _redactSeeded;
    public event Action? BackRequested;
    private string T(string key) => _translate(key);
    private string F(string key, params object[] args) => string.Format(IncidentPresentation.Culture(_locale()), T(key), args);
    private readonly DiagnosticExportService _exportService = new();
    private IReadOnlyList<IncidentInfo> _incidents = Array.Empty<IncidentInfo>();
    private IncidentInfo? _selectedIncident;

    public IncidentsPage(string? incidentId = null, bool returnHome = false)
        : this(() => App.Current.Service.ReadIncidentsAsync(), () => App.Current.Localization.CurrentLocale, Loc.T, incidentId, returnHome) { }

    public IncidentsPage(Func<IReadOnlyList<IncidentInfo>> loadIncidents, Func<string> locale,
        Func<string, string> translate, string? incidentId = null, bool returnHome = false)
        : this(() => Task.FromResult(IncidentReadResult.Success(loadIncidents())), locale, translate, incidentId, returnHome) { }

    public IncidentsPage(Func<Task<IncidentReadResult>> loadIncidents, Func<string> locale,
        Func<string, string> translate, string? incidentId = null, bool returnHome = false)
    {
        _loadIncidents = loadIncidents;
        _locale = locale;
        _translate = translate;
        _requestedId = incidentId;
        InitializeComponent();
        BackButton.Visibility = returnHome ? Visibility.Visible : Visibility.Collapsed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Severity badge brushes are resolved once when the list is built, so a theme
        // switch would otherwise leave them showing the previous theme's colours.
        // TryCurrent, not Current: the compiled page is also hosted by the UI smoke under a plain
        // Application, where the hard cast in App.Current throws InvalidCastException on Loaded.
        var theme = App.TryCurrent?.Theme;
        if (theme is not null)
        {
            theme.ThemeChanged -= OnThemeChanged;
            theme.ThemeChanged += OnThemeChanged;
        }
        // Seed the redaction checkbox from the persisted default exactly once per page;
        // later reloads must not clobber a per-export choice. TryCurrent keeps the page
        // hostable by the plain-Application UI smoke, which falls back to the XAML default.
        var ui = App.TryCurrent?.UiSettings;
        if (!_redactSeeded && ui is not null)
        {
            RedactCheckBox.IsChecked = ui.RedactExportsByDefault;
            _redactSeeded = true;
        }
        RefreshList();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loadVersion++;
        _isLoading = false;
        var theme = App.TryCurrent?.Theme;
        if (theme is not null) theme.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (IsLoaded) RefreshList();
    });

    public void RefreshLocale() => RefreshList();
    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    public void RefreshList() => _ = RefreshListAsync();
    private async void ReloadIncidents_Click(object sender, RoutedEventArgs e) => await RefreshListAsync();

    public async Task RefreshListAsync()
    {
        if (_isLoading) { ReadNoticeText.Text = T("readUi.loading"); return; }
        _isLoading = true;
        var version = ++_loadVersion;
        var selectedIds = IncidentListBox.SelectedItems.Cast<IncidentListItem>().Select(i => i.Incident.DirectoryName).ToArray();
        var requested = _requestedId;
        ReadNoticeText.Text = T("readUi.loading");
        ReadIssuesPanel.Visibility = Visibility.Collapsed;
        ReloadIncidentsButton.IsEnabled = false;
        IncidentListBox.IsEnabled = false;
        ExportButton.IsEnabled = CompareButton.IsEnabled = CrossAnalysisButton.IsEnabled = CopyIdButton.IsEnabled = false;
        IncidentReadResult result;
        try { result = await _loadIncidents(); }
        catch (Exception ex) { result = IncidentReadResult.Failed(ex); }
        if (!IsLoaded || version != _loadVersion) return;
        _isLoading = false;
        ReloadIncidentsButton.IsEnabled = true;
        IncidentListBox.IsEnabled = true;
        ReadNoticeText.SetResourceReference(TextBlock.ForegroundProperty, result.HasIssues ? "WarningColor" : "TextSecondary");
        ReadNoticeText.Text = result.Summary(T, _locale());
        ReadIssueDetails.Text = result.Details(T);
        ReadIssuesPanel.Visibility = result.HasIssues ? Visibility.Visible : Visibility.Collapsed;
        _incidents = result.Items;
        TotalIncidentsText.Text = _incidents.Count.ToString();
        LastIncidentText.Text = _incidents.Count > 0 ? IncidentPresentation.Create(_incidents[0], _locale(), T).TimeLabel : "—";
        TopCauseText.Text = _incidents.Count > 0
            ? IncidentPresentation.Mode(_incidents.GroupBy(i => i.Mode).OrderByDescending(g => g.Count()).First().Key, T) : "—";
        _selectedIncident = null;
        ExportButton.IsEnabled = false;
        CrossAnalysisButton.IsEnabled = _incidents.Count > 0;
        CrossAnalysisPanel.Visibility = Visibility.Collapsed;
        ComparisonPanel.Visibility = Visibility.Collapsed;
        var rows = _incidents.Select(i =>
        {
            var display = IncidentPresentation.Create(i, _locale(), T);
            return new IncidentListItem { Timestamp = display.TimeLabel, Title = display.Title, Mode = display.ModeLabel,
                Confidence = display.SupportLabel, ModeBackground = GetModeBrush(i.Mode), Incident = i };
        }).ToList();
        IncidentListBox.ItemsSource = rows;
        SelectionNotice.Text = T("eventUi.chooseOne");
        if (requested is not null)
        {
            var target = IncidentPresentation.Resolve(_incidents, requested);
            var row = rows.FirstOrDefault(i => ReferenceEquals(i.Incident, target));
            if (row is null) SelectionNotice.Text = T("eventUi.missing");
            else { _requestedId = null; IncidentListBox.SelectedItem = row; IncidentListBox.ScrollIntoView(row); }
        }
        else
            foreach (var row in rows.Where(i => selectedIds.Contains(i.Incident.DirectoryName, StringComparer.Ordinal)))
                IncidentListBox.SelectedItems.Add(row);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var count = IncidentListBox.SelectedItems.Count;
        CompareButton.IsEnabled = count == 2 && !_isLoading;
        _selectedIncident = count == 1 ? (IncidentListBox.SelectedItem as IncidentListItem)?.Incident : null;
        ExportButton.IsEnabled = IncidentPresentation.CanExport(count, _isExporting || _isLoading);
        CopyIdButton.IsEnabled = _selectedIncident is not null && !_isLoading;
        if (_selectedIncident is not null) ShowDetail(_selectedIncident);
        else
        {
            DetailTitle.Text = T("incidents.detail.selectIncident");
            DetailTime.Text = ""; RecordIdText.Text = ""; RawModeText.Text = ""; RawTimeText.Text = "";
            HypothesesList.ItemsSource = null; QuestionsList.ItemsSource = null;
        }
    }

    private void CopyIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIncident is null) return;
        try { System.Windows.Clipboard.SetText(_selectedIncident.DirectoryName); SelectionNotice.Text = T("eventUi.copied"); }
        catch (System.Runtime.InteropServices.ExternalException) { SelectionNotice.Text = T("eventUi.copyFailed"); }
    }

    private static Brush GetModeBrush(string mode) =>
        SeverityPalette.Resolve(SeverityPalette.KeyForMode(mode));

    private void IncidentListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IncidentListBox.SelectedItems.Count > 0)
        {
            _requestedId = null;
            SelectionNotice.Text = T("eventUi.chooseOne");
        }
        UpdateSelection();
    }

    private void ShowDetail(IncidentInfo incident)
    {
        var display = IncidentPresentation.Create(incident, _locale(), T);
        DetailTitle.Text = display.Title + " · " + display.ModeLabel;
        DetailTime.Text = display.TimeLabel;
        DetailTime.ToolTip = display.TimeDetail;
        RecordIdText.Text = incident.DirectoryName;
        RawModeText.Text = incident.Mode;
        RawTimeText.Text = incident.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        // Empty sections state their emptiness explicitly instead of showing a bare heading.
        var hypotheses = incident.Analysis.Hypotheses.Select(h => new HypothesisItem
        {
            Name = IncidentPresentation.Mode(h.Mode.ToString(), T),
            ScoreDisplay = F("eventUi.score", h.Score.ToString("+0;-0;0"), IncidentPresentation.Support(h.Confidence, _locale(), T))
        }).ToList();
        if (hypotheses.Count == 0)
            hypotheses.Add(new HypothesisItem { Name = T("eventUi.noHypotheses"), ScoreDisplay = "" });
        HypothesesList.ItemsSource = hypotheses;
        var questions = incident.Analysis.OpenQuestions ?? Array.Empty<string>();
        QuestionsList.ItemsSource = questions.Count > 0 ? questions : new[] { T("eventUi.noOpenQuestions") };
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isExporting || _isLoading) return;
        if (_selectedIncident is null)
        {
            MessageBox.Show(Loc.T("incidents.export.selectFirst"), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var exportingIncident = _selectedIncident;
        var dialog = new SaveFileDialog
        {
            Title = T("eventUi.exportTitle"),
            Filter = T("eventUi.exportFilter"),
            FileName = $"CrashScope-{_selectedIncident.DirectoryName}.zip",
            DefaultExt = ".zip"
        };

        if (dialog.ShowDialog() != true) return;

        _isExporting = true;
        ExportButton.IsEnabled = false;
        ExportButton.Content = Loc.T("incidents.export.exporting");

        try
        {
            var redact = RedactCheckBox.IsChecked == true;

            var zipPath = await _exportService.ExportAsync(
                exportingIncident, dialog.FileName, redact);

            var result = MessageBox.Show(
                Loc.F("incidents.export.success", zipPath),
                Loc.T("app.title"),
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
                Loc.F("incidents.export.failed", ex.Message),
                Loc.T("app.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isExporting = false;
            UpdateSelection();
            ExportButton.Content = Loc.T("incidents.export.exportZip");
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
            ? SeverityPalette.Resolve(SeverityPalette.Info)
            : modeBrush;
        LeftMode.Text = left.Mode;
        LeftMode.Foreground = leftModeColor;

        var rightModeColor = left.Mode == right.Mode
            ? SeverityPalette.Resolve(SeverityPalette.Info)
            : GetModeBrush(right.Mode);
        RightMode.Text = right.Mode;
        RightMode.Foreground = rightModeColor;

        LeftConfidence.Text = $"{left.Confidence:P0}";
        RightConfidence.Text = $"{right.Confidence:P0}";

        // The old `a?.ToString() + "%" ?? "N/A"` never produced N/A: string concatenation
        // with null yields "%", so missing data rendered as a lone percent sign.
        LeftGpuLoad.Text = leftAnalysis.FinalGpuLoadPercent is { } lg ? lg.ToString("F1") + "%" : T("common.notAvailable");
        RightGpuLoad.Text = rightAnalysis.FinalGpuLoadPercent is { } rg ? rg.ToString("F1") + "%" : T("common.notAvailable");

        var leftTransCount = leftAnalysis.GpuTransitions.Count;
        var rightTransCount = rightAnalysis.GpuTransitions.Count;
        LeftGpuTransitions.Text = Loc.F("incidents.comparison.events", leftTransCount);
        RightGpuTransitions.Text = Loc.F("incidents.comparison.events", rightTransCount);
        LeftGpuTransitions.Foreground = leftTransCount > rightTransCount
            ? SeverityPalette.Resolve(SeverityPalette.Critical)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);
        RightGpuTransitions.Foreground = rightTransCount > leftTransCount
            ? SeverityPalette.Resolve(SeverityPalette.Critical)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);

        var leftEtwGpu = leftAnalysis.Timeline.Count(e => e.Category == "ETW-GPU");
        var rightEtwGpu = rightAnalysis.Timeline.Count(e => e.Category == "ETW-GPU");
        LeftEtwGpu.Text = Loc.F("incidents.comparison.events", leftEtwGpu);
        RightEtwGpu.Text = Loc.F("incidents.comparison.events", rightEtwGpu);
        LeftEtwGpu.Foreground = leftEtwGpu > 0
            ? SeverityPalette.Resolve(SeverityPalette.Critical)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);
        RightEtwGpu.Foreground = rightEtwGpu > 0
            ? SeverityPalette.Resolve(SeverityPalette.Critical)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);

        var leftEtwPower = leftAnalysis.Timeline.Count(e => e.Category == "ETW-Power");
        var rightEtwPower = rightAnalysis.Timeline.Count(e => e.Category == "ETW-Power");
        LeftEtwPower.Text = Loc.F("incidents.comparison.events", leftEtwPower);
        RightEtwPower.Text = Loc.F("incidents.comparison.events", rightEtwPower);

        var leftEtwWhea = leftAnalysis.Timeline.Count(e => e.Category == "ETW-WHEA");
        var rightEtwWhea = rightAnalysis.Timeline.Count(e => e.Category == "ETW-WHEA");
        LeftEtwWhea.Text = Loc.F("incidents.comparison.events", leftEtwWhea);
        RightEtwWhea.Text = Loc.F("incidents.comparison.events", rightEtwWhea);
        LeftEtwWhea.Foreground = leftEtwWhea > 0
            ? SeverityPalette.Resolve(SeverityPalette.Critical)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);
        RightEtwWhea.Foreground = rightEtwWhea > 0
            ? SeverityPalette.Resolve(SeverityPalette.Critical)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);

        var leftDisplays = leftAnalysis.Graphics?.LatestDisplayChange?.After;
        var rightDisplays = rightAnalysis.Graphics?.LatestDisplayChange?.After;
        var leftMonitorCount = leftDisplays?.Count ?? 0;
        var rightMonitorCount = rightDisplays?.Count ?? 0;
        LeftMonitors.Text = Loc.F("incidents.comparison.monitorsCount", leftMonitorCount);
        RightMonitors.Text = Loc.F("incidents.comparison.monitorsCount", rightMonitorCount);

        var leftHasVirtual = leftDisplays?.Any(d => d.IsVirtual) ?? false;
        var rightHasVirtual = rightDisplays?.Any(d => d.IsVirtual) ?? false;
        LeftVirtualDisplay.Text = leftHasVirtual ? "YES \u26A0" : "No";
        RightVirtualDisplay.Text = rightHasVirtual ? "YES \u26A0" : "No";
        LeftVirtualDisplay.Foreground = leftHasVirtual
            ? SeverityPalette.Resolve(SeverityPalette.High)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);
        RightVirtualDisplay.Foreground = rightHasVirtual
            ? SeverityPalette.Resolve(SeverityPalette.High)
            : SeverityPalette.Resolve(SeverityPalette.Neutral);

        var leftDrivers = leftAnalysis.Graphics?.DriverInventory
            ?.Select(d => $"{d.AdapterName}: {d.DriverVersion ?? T("common.notAvailable")}")
            .ToList() ?? [];
        var rightDrivers = rightAnalysis.Graphics?.DriverInventory
            ?.Select(d => $"{d.AdapterName}: {d.DriverVersion ?? T("common.notAvailable")}")
            .ToList() ?? [];
        LeftDrivers.ItemsSource = leftDrivers.Count > 0 ? leftDrivers : new[] { T("eventUi.noData") };
        RightDrivers.ItemsSource = rightDrivers.Count > 0 ? rightDrivers : new[] { T("eventUi.noData") };

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
        LeftApps.ItemsSource = leftAppDisplay.Count > 0 ? leftAppDisplay : new[] { T("eventUi.noData") };

        var rightAppDisplay = new List<string>();
        rightAppDisplay.AddRange(commonApps.Select(a => $"\u2714 {a}"));
        rightAppDisplay.AddRange(rightOnly.Select(a => $"  {a}"));
        RightApps.ItemsSource = rightAppDisplay.Count > 0 ? rightAppDisplay : new[] { T("eventUi.noData") };

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
            MtbfText.Text = Loc.F("dashboard.health.mtbf", trend.MtbfHours, trend.IncidentCount);
            TrendText.Text = Loc.F("dashboard.health.trend", trend.TrendDirection);

            StabilityScoreText.Foreground = trend.StabilityScore switch
            {
                >= 80 => SeverityPalette.Resolve(SeverityPalette.Info),
                >= 50 => SeverityPalette.Resolve(SeverityPalette.High),
                _ => SeverityPalette.Resolve(SeverityPalette.Critical)
            };

            TrendArrowText.Text = trend.TrendDirection switch
            {
                "Improving" => "\uE74D",
                "Worsening" => "\uE74E",
                _ => "\uE74C"
            };
            TrendArrowText.Foreground = trend.TrendDirection switch
            {
                "Improving" => SeverityPalette.Resolve(SeverityPalette.Info),
                "Worsening" => SeverityPalette.Resolve(SeverityPalette.Critical),
                _ => SeverityPalette.Resolve(SeverityPalette.Neutral)
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
                var label = r.Priority switch
                {
                    >= 90 => T("eventUi.priority.critical"),
                    >= 75 => T("eventUi.priority.high"),
                    >= 55 => T("eventUi.priority.medium"),
                    _ => T("eventUi.priority.low")
                };
                return new RecommendationDisplayItem
                {
                    Title = r.Title,
                    Description = r.Description,
                    PriorityLabel = label,
                    PriorityBackground = SeverityPalette.Resolve(SeverityPalette.KeyForPriority(r.Priority))
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
    public Brush ModeBackground { get; set; } = new SolidColorBrush(Colors.Gray);
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