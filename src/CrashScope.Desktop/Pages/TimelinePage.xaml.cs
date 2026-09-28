using CrashScope.Core.Models;
using CrashScope.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;

namespace CrashScope.Desktop.Pages;

public partial class TimelinePage : UserControl
{
    private readonly AppService _service;
    private IReadOnlyList<IncidentInfo> _incidents = Array.Empty<IncidentInfo>();
    private bool _loading;
    private int _loadVersion;

    public TimelinePage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Timeline entry brushes are resolved once in BuildTimeline, so without this the
        // markers keep the previous theme's colours after a switch.
        var theme = App.TryCurrent?.Theme;
        if (theme is not null)
        {
            theme.ThemeChanged -= OnThemeChanged;
            theme.ThemeChanged += OnThemeChanged;
        }

        await RefreshIncidentsAsync();
    }

    private async Task RefreshIncidentsAsync()
    {
        if (_loading) return;
        _loading = true;
        var version = ++_loadVersion;
        TimelineReadNotice.Visibility = Visibility.Visible;
        TimelineReadNotice.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        TimelineReadNotice.Text = Loc.T("readUi.loading");
        try
        {
            // Reading off the UI thread keeps navigation responsive while large analyses
            // re-evaluate; a read failure must never be presented as an empty history.
            IncidentReadResult result;
            try { result = await _service.ReadIncidentsAsync(); }
            catch (Exception ex) { result = IncidentReadResult.Failed(ex); }
            if (!IsLoaded || version != _loadVersion) return;
            _incidents = result.Items;
            var items = _incidents.Select(i => new TimelineIncidentItem
            {
                Title = $"{i.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm} — {i.Mode}",
                Incident = i
            }).ToList();
            IncidentSelector.ItemsSource = items;
            if (items.Count > 0)
                IncidentSelector.SelectedIndex = 0;
            var summary = result.Summary(Loc.T, App.Current.Localization.CurrentLocale);
            TimelineReadNotice.SetResourceReference(TextBlock.ForegroundProperty,
                result.HasIssues ? "WarningColor" : "TextSecondary");
            TimelineReadNotice.ToolTip = result.HasIssues ? result.Details(Loc.T) : null;
            TimelineReadNotice.Text = summary;
            TimelineReadNotice.Visibility = summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            if (version == _loadVersion) _loading = false;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var theme = App.TryCurrent?.Theme;
        if (theme is not null) theme.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (IsLoaded && IncidentSelector.SelectedItem is TimelineIncidentItem item)
            BuildTimeline(item.Incident);
    });

    private void IncidentSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IncidentSelector.SelectedItem is TimelineIncidentItem item)
            BuildTimeline(item.Incident);
    }

    private void BuildTimeline(IncidentInfo incident)
    {
        var entries = new List<TimelineDisplayEntry>();
        var crashTime = incident.Timestamp;

        double minSeconds = -300;
        foreach (var entry in incident.Analysis.Timeline)
        {
            var offset = (entry.Timestamp - crashTime).TotalSeconds;
            if (offset < minSeconds) minSeconds = offset;

            entries.Add(new TimelineDisplayEntry
            {
                Time = $"T{(offset >= 0 ? "+" : "")}{offset:F0}s",
                Category = entry.Category,
                Description = entry.Description,
                Color = GetCategoryBrush(entry.Category)
            });
        }

        // The anchor is the last recorded heartbeat; the exact failure moment is unknown,
        // so the marker must not claim a crash point or an unevidenced system failure.
        entries.Add(new TimelineDisplayEntry
        {
            Time = "T+0",
            Category = Loc.T("timeline.marker.lastRecord"),
            Description = Loc.F("timeline.marker.lastRecordDesc", incident.Mode),
            Color = SeverityPalette.Resolve(SeverityPalette.Critical)
        });

        entries.Sort((a, b) =>
        {
            var aStr = a.Time.Replace("T", "").Replace("s", "").Replace("+", "");
            var bStr = b.Time.Replace("T", "").Replace("s", "").Replace("+", "");
            if (double.TryParse(aStr, out var aVal) && double.TryParse(bStr, out var bVal))
                return aVal.CompareTo(bVal);
            return 0;
        });

        TimelineStartText.Text = $"T{minSeconds:F0}s";
        TimelineEndText.Text = "T+0";
        TimelineList.ItemsSource = entries;
    }

    private static Brush GetCategoryBrush(string category) =>
        SeverityPalette.Resolve(SeverityPalette.KeyForCategory(category));
}

public sealed class TimelineDisplayEntry
{
    public string Time { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public Brush Color { get; set; } = new SolidColorBrush(Colors.Gray);
}

public sealed class TimelineIncidentItem
{
    public string Title { get; set; } = "";
    public IncidentInfo Incident { get; set; } = null!;
}