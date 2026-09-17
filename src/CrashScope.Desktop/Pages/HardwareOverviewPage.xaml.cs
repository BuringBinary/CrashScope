using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using CrashScope.Core.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

public partial class HardwareOverviewPage : UserControl
{
    private readonly AppService _service;
    private List<SensorCatalogEntry> _allSensors = new();
    private string _filter = "";

    public HardwareOverviewPage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _service.SensorUpdated += OnSensorUpdated;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshStaticInfo();
        RefreshSensors(null);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _service.SensorUpdated -= OnSensorUpdated;
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values)
    {
        Dispatcher.InvokeAsync(() => RefreshSensors(values));
    }

    private void RefreshStaticInfo()
    {
        try
        {
            var catalog = _service.SensorCatalog;
            _allSensors = catalog.ToList();

            // CPU / GPU from catalog names
            var cpuEntries = catalog.Where(c => c.HardwareType.ToLower().Contains("cpu")).Take(5).Select(c => $"{c.HardwareName} - {c.SensorName}");
            CpuInfoText.Text = string.Join("\n", cpuEntries.DefaultIfEmpty("CPU sensors detecting..."));

            var gpuEntries = catalog.Where(c => c.HardwareType.ToLower().Contains("gpu")).Take(5).Select(c => $"{c.HardwareName} - {c.SensorName}");
            GpuInfoText.Text = string.Join("\n", gpuEntries.DefaultIfEmpty("GPU sensors detecting..."));

            // Memory from catalog
            var mem = catalog.Where(c => c.SensorName.ToLower().Contains("memory") || c.HardwareType.ToLower().Contains("memory")).Take(5).Select(c => $"{c.HardwareName} - {c.SensorName}");
            MemoryInfoText.Text = string.Join("\n", mem.DefaultIfEmpty("Memory sensors: --"));

            // Disk via DriveInfo
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => $"{d.Name} {d.DriveType} {d.TotalSize/1024/1024/1024:F0}GB total, {d.AvailableFreeSpace/1024/1024/1024:F0}GB free");
                DiskInfoText.Text = string.Join("\n", drives);
            }
            catch (Exception ex)
            {
                DiskInfoText.Text = $"Disk query failed: {ex.Message}";
            }

            // Board - placeholder, WMI would need extra perms, keep safe
            BoardInfoText.Text = $"Machine: {Environment.MachineName}\nOS: {Environment.OSVersion}\n64-bit: {Environment.Is64BitOperatingSystem}\nProcessor count: {Environment.ProcessorCount}";

            // Display
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens.Select(s => $"{s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height} Primary:{s.Primary}");
                DisplayInfoText.Text = string.Join("\n", screens);
            }
            catch
            {
                DisplayInfoText.Text = "Display: unable to query (Forms not available)";
            }

            // Network
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Take(5)
                    .Select(n => $"{n.Name} {n.NetworkInterfaceType} {n.OperationalStatus} {n.Speed/1000/1000}Mbps");
                NetworkInfoText.Text = string.Join("\n", nics);
            }
            catch (Exception ex)
            {
                NetworkInfoText.Text = $"Network query failed: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            CpuInfoText.Text = $"Error: {ex.Message}";
        }
    }

    private void RefreshSensors(IReadOnlyList<float?>? values)
    {
        if (_allSensors.Count == 0)
        {
            _allSensors = _service.SensorCatalog.ToList();
        }

        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? _allSensors
            : _allSensors.Where(s => s.SensorName.Contains(_filter, StringComparison.OrdinalIgnoreCase) || s.HardwareName.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var display = filtered.Select((s, idx) =>
        {
            string valText = "--";
            if (values != null && idx < values.Count && values[idx].HasValue)
                valText = values[idx]!.Value.ToString("F1");
            return new SensorRow { Name = $"{s.HardwareName} - {s.SensorName}", Type = s.HardwareType.ToString(), ValueText = valText };
        }).ToList();

        SensorList.ItemsSource = display;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text ?? "";
        RefreshSensors(null);
    }

    private sealed class SensorRow
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string ValueText { get; set; } = "";
    }
}
