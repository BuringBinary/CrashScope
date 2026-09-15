using CrashScope.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CrashScope.Desktop.Pages;

public partial class TimelinePage : UserControl
{
    private readonly AppService _service;
    private IReadOnlyList<IncidentInfo> _incidents = Array.Empty<IncidentInfo>();

    public TimelinePage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _incidents = _service.GetIncidents();
        var items = _incidents.Select(i => new TimelineIncidentItem
        {
            Title = $"{i.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm} — {i.Mode}",
            Incident = i
        }).ToList();

        IncidentSelector.ItemsSource = items;

        if (items.Count > 0)
            IncidentSelector.SelectedIndex = 0;
    }

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

        // Add crash point marker
        entries.Add(new TimelineDisplayEntry
        {
            Time = "T+0",
            Category = "Crash Point",
            Description = $"System failure — {incident.Mode}",
            Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
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

    private static SolidColorBrush GetCategoryBrush(string category) => category switch
    {
        "Telemetry" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C5CE7")),
        "GPU" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F")),
        "Process" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
        "App" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
        "Windows Event" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")),
        "External Evidence" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B8BA0"))
    };
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