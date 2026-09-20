using CrashScope.Core.Models;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

/// <summary>
/// Read-only per-physical-disk health view fed by the recorder's storage pipeline
/// (PhysicalDisk performance counters + LibreHardwareMonitor sensors). Shows the
/// last received sample when the recorder is stopped, and names the storage
/// collector's health instead of pretending values are live.
/// </summary>
public partial class DiskHealthPage : UserControl
{
    private readonly AppService _service;
    private StorageSample? _lastSample;

    public DiskHealthPage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _service.StorageUpdated += OnStorageUpdated;
        _service.StatusChanged += OnStatusChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Render();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RefreshVolumes();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _service.StorageUpdated -= OnStorageUpdated;
        _service.StatusChanged -= OnStatusChanged;
    }

    private void OnStorageUpdated(StorageSample sample)
        => Dispatcher.InvokeAsync(() =>
        {
            _lastSample = sample;
            Render();
        });

    private void OnStatusChanged() => Dispatcher.InvokeAsync(Render);

    private void Render()
    {
        DiskList.ItemsSource = DiskHealthView.Build(_lastSample?.Disks ?? []);

        var storage = _service.CollectorHealth.FirstOrDefault(c => c.Collector == "Storage");
        CollectorText.Text = DiskHealthView.SummarizeCollector(storage?.Status, storage?.Detail);

        StatusText.Text = (_service.IsRecording, _lastSample) switch
        {
            (true, null) => "Recording — waiting for the first storage sample (about one per 5 seconds).",
            (true, { } s) => $"Live — last sample at {s.Timestamp.LocalDateTime:HH:mm:ss}.",
            (false, null) => "Recorder stopped — no live readings; disk metrics flow while recording.",
            (false, { } s) => $"Recorder stopped — showing the last received sample from {s.Timestamp.LocalDateTime:HH:mm:ss}."
        };
    }

    private void RefreshVolumes()
    {
        try
        {
            var lines = DriveInfo.GetDrives().Where(d => d.IsReady)
                .Select(d => $"{d.Name} {d.DriveType} — {d.TotalSize / 1024 / 1024 / 1024:F0} GB total, {d.AvailableFreeSpace / 1024 / 1024 / 1024:F0} GB free");
            VolumesText.Text = string.Join("\n", lines.DefaultIfEmpty("No ready volumes."));
        }
        catch (Exception ex)
        {
            VolumesText.Text = $"Volume query failed: {ex.Message}";
        }
    }
}
