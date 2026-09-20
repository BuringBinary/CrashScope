using CrashScope.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CrashScope.Desktop;

/// <summary>
/// Standalone raw sensor viewer: the full LibreHardwareMonitor catalog with live values,
/// search, hardware-type filtering and an optional always-on-top mode. Read-only by design.
/// </summary>
public partial class RawSensorWindow : Window
{
    private readonly AppService _service;
    private IReadOnlyList<float?>? _lastValues;
    private ScrollViewer? _listScroll;

    public RawSensorWindow()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _service.SensorUpdated += OnSensorUpdated;
        _service.StatusChanged += OnStatusChanged;
        Closed += (_, _) =>
        {
            _service.SensorUpdated -= OnSensorUpdated;
            _service.StatusChanged -= OnStatusChanged;
        };
        RefreshCatalog();
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values)
        => Dispatcher.InvokeAsync(() =>
        {
            _lastValues = values;
            Rebuild();
            UpdateStatus();
        });

    private void OnStatusChanged() => Dispatcher.InvokeAsync(UpdateStatus);

    private void RefreshCatalog()
    {
        var selected = TypeFilter.SelectedItem as string;
        var types = RawSensorView.ListHardwareTypes(_service.SensorCatalog);
        TypeFilter.ItemsSource = new[] { "All" }.Concat(types).ToList();
        TypeFilter.SelectedItem = selected is "All"
                                  || (selected is not null && types.Contains(selected, StringComparer.OrdinalIgnoreCase))
            ? selected
            : "All";

        var count = _service.SensorCatalog.Count;
        if (_lastValues is not null && _lastValues.Count != count)
            _lastValues = null;

        Rebuild();
        UpdateStatus();
    }

    private void Rebuild()
    {
        // Re-anchoring the list each tick resets scrolling; preserve the offset across updates.
        _listScroll ??= FindScrollViewer(SensorList);
        var offset = _listScroll?.VerticalOffset ?? 0;

        var rows = RawSensorView.Build(
            _service.SensorCatalog, _lastValues, SearchBox.Text, TypeFilter.SelectedItem as string);
        var source = new CollectionViewSource { Source = rows };
        source.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RawSensorRow.Group)));
        SensorList.ItemsSource = source.View;

        if (_listScroll is not null)
            _listScroll.ScrollToVerticalOffset(offset);
    }

    private void UpdateStatus()
    {
        var count = _service.SensorCatalog.Count;
        if (count == 0)
        {
            StatusText.Text = _service.IsRecording
                ? "Recording — the sensor catalog is being built; the first tick fills this list."
                : "Sensor catalog is empty — start recording to build it.";
        }
        else if (_service.IsRecording)
        {
            StatusText.Text = $"Live — {count} sensors, updating on every telemetry tick.";
        }
        else
        {
            StatusText.Text = _lastValues is null
                ? "Recorder stopped — no live readings yet; values appear while recording."
                : $"Recorder stopped — showing the last received values for {count} sensors.";
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => Rebuild();

    private void TopmostToggle_Changed(object sender, RoutedEventArgs e)
        => Topmost = TopmostToggle.IsChecked == true;

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshCatalog();

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }
}
