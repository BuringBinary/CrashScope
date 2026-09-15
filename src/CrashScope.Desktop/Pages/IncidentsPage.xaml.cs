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