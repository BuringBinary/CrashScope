using System.IO;
using CrashScope.Desktop.Services;
using System.Management;
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

    private static readonly object IdentityGate = new();
    private static string? _cachedCpuIdentity;
    private static string? _cachedGpuIdentity;
    private static string? _cachedMemoryIdentity;
    private static string? _cachedBoardIdentity;
    private static bool _identityQueryRunning;

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
        _ = LoadStaticHardwareAsync();
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
            _allSensors = _service.SensorCatalog.ToList();
            ApplyIdentityTexts();

            // Disk via DriveInfo
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => $"{d.Name} {d.DriveType} {d.TotalSize/1024/1024/1024:F0}GB total, {d.AvailableFreeSpace/1024/1024/1024:F0}GB free");
                DiskInfoText.Text = string.Join("\n", drives);
            }
            catch (Exception ex)
            {
                DiskInfoText.Text = Loc.F("hardware.diskQueryFailed", ex.Message);
            }

            // Display
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens.Select(s => $"{s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height} Primary:{s.Primary}");
                DisplayInfoText.Text = string.Join("\n", screens);
            }
            catch
            {
                DisplayInfoText.Text = Loc.T("hardware.displayUnavailable");
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
                NetworkInfoText.Text = Loc.F("hardware.networkQueryFailed", ex.Message);
            }
        }
        catch (Exception ex)
        {
            CpuInfoText.Text = Loc.F("common.error", ex.Message);
        }
    }

    private void ApplyIdentityTexts()
    {
        var catalog = _service.SensorCatalog;
        CpuInfoText.Text = JoinNonEmpty(_cachedCpuIdentity, CatalogSummary(catalog, "cpu"))
            ?? "CPU sensors detecting...";
        GpuInfoText.Text = JoinNonEmpty(_cachedGpuIdentity, CatalogSummary(catalog, "gpu"))
            ?? "GPU sensors detecting...";
        MemoryInfoText.Text = JoinNonEmpty(_cachedMemoryIdentity, CatalogSummary(catalog, "memory"))
            ?? "Memory sensors: --";
        BoardInfoText.Text = _cachedBoardIdentity
            ?? $"Machine: {Environment.MachineName}\nOS: {Environment.OSVersion}\n64-bit process: {Environment.Is64BitProcess}";
    }

    private static string? CatalogSummary(IReadOnlyList<SensorCatalogEntry> catalog, string key)
    {
        var lines = catalog
            .Where(c => c.HardwareType.Contains(key, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(c => $"{c.HardwareName} - {c.SensorName}")
            .ToList();
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    private static string? JoinNonEmpty(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return joined.Length > 0 ? joined : null;
    }

    private async Task LoadStaticHardwareAsync()
    {
        lock (IdentityGate)
        {
            if (_identityQueryRunning) return;
            if (_cachedCpuIdentity is not null && _cachedBoardIdentity is not null) return;
            _identityQueryRunning = true;
        }

        try
        {
            var (cpu, gpu, memory, board) = await Task.Run(QueryStaticIdentity).ConfigureAwait(true);
            _cachedCpuIdentity = cpu;
            _cachedGpuIdentity = gpu;
            _cachedMemoryIdentity = memory;
            _cachedBoardIdentity = board;
            ApplyIdentityTexts();
        }
        catch
        {
            // WMI identity is best-effort; the sensor summaries remain visible.
        }
        finally
        {
            lock (IdentityGate) _identityQueryRunning = false;
        }
    }

    private static (string? Cpu, string? Gpu, string? Memory, string? Board) QueryStaticIdentity()
    {
        string? cpu = null, gpu = null, memory = null, board = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            var lines = new List<string>();
            foreach (var item in searcher.Get())
                lines.Add($"{item["Name"]} — {item["NumberOfCores"]}C/{item["NumberOfLogicalProcessors"]}T @ {item["MaxClockSpeed"]} MHz");
            if (lines.Count > 0) cpu = string.Join("\n", lines);
        }
        catch { /* best-effort identity */ }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,DriverVersion,DriverDate,Status FROM Win32_VideoController");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            var lines = new List<string>();
            foreach (var item in searcher.Get())
            {
                var date = item["DriverDate"] is DateTime d ? d.ToString("yyyy-MM-dd") : "";
                lines.Add($"{item["Name"]} — driver {item["DriverVersion"]} {date} [{item["Status"]}]".TrimEnd());
            }
            if (lines.Count > 0) gpu = string.Join("\n", lines);
        }
        catch { /* best-effort identity */ }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity,Speed,Manufacturer,PartNumber FROM Win32_PhysicalMemory");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            var lines = new List<string>();
            ulong totalBytes = 0;
            foreach (var item in searcher.Get())
            {
                var capacity = item["Capacity"] is ulong bytes ? bytes : 0;
                totalBytes += capacity;
                var part = $"{item["Manufacturer"]} {item["PartNumber"]}".Trim();
                lines.Add($"{capacity / 1024 / 1024 / 1024} GB @ {item["Speed"]} MHz — {part}");
            }
            if (totalBytes > 0)
                lines.Insert(0, $"Total {totalBytes / 1024 / 1024 / 1024} GB across {lines.Count} module(s)");
            if (lines.Count > 0) memory = string.Join("\n", lines);
        }
        catch { /* best-effort identity */ }

        try
        {
            var lines = new List<string>();
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer,Product,Version FROM Win32_BaseBoard"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                foreach (var item in searcher.Get())
                    lines.Add($"{item["Manufacturer"]} {item["Product"]} {item["Version"]}".Trim());
            }
            using (var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                foreach (var item in searcher.Get())
                {
                    var date = item["ReleaseDate"] is DateTime d ? d.ToString("yyyy-MM-dd") : "";
                    lines.Add($"BIOS {item["SMBIOSBIOSVersion"]} {date}".Trim());
                }
            }
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                foreach (var item in searcher.Get())
                    lines.Add($"{item["Manufacturer"]} {item["Model"]}".Trim());
            }
            lines.RemoveAll(string.IsNullOrWhiteSpace);
            if (lines.Count > 0) board = string.Join("\n", lines);
        }
        catch { /* best-effort identity */ }

        return (cpu, gpu, memory, board);
    }

    private void OpenRawSensorsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new RawSensorWindow { Owner = Window.GetWindow(this) };
        window.Show();
    }

    private void OpenFloatingBarButton_Click(object sender, RoutedEventArgs e)
        => FloatingBarWindow.ShowSingle();

    private void RefreshSensors(IReadOnlyList<float?>? values)
    {
        if (_allSensors.Count == 0)
        {
            _allSensors = _service.SensorCatalog.ToList();
        }

        SensorList.ItemsSource = RawSensorView.Build(_allSensors, values, _filter);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text ?? "";
        RefreshSensors(null);
    }
}
