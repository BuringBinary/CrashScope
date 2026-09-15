using CrashScope.Core.Models;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

public partial class LiveMonitorPage : UserControl
{
    private readonly AppService _service;
    private readonly List<LiveEventItem> _events = new();
    private const int MaxEvents = 500;

    public LiveMonitorPage()
    {
        InitializeComponent();
        _service = App.Current.Service;

        _service.ForegroundChanged += OnForegroundChanged;
        _service.ProcessEventReceived += OnProcessEvent;
        _service.GpuEngineUpdated += OnGpuEngineUpdated;
        _service.DisplayChanged += OnDisplayChanged;
        _service.PowerSchemeChanged += OnPowerSchemeChanged;
        _service.RemoteSessionChanged += OnRemoteSessionChanged;
    }

    private void OnForegroundChanged(ForegroundEvent e) => AddEvent("Foreground",
        $"PID {e.ProcessId} — {e.ProcessName} (idle {e.IdleSeconds}s)");

    private void OnProcessEvent(ProcessEvent e) => AddEvent("Process",
        $"{e.Event}: {e.ProcessName} (PID {e.ProcessId})");

    private void OnGpuEngineUpdated(GpuEngineSample e) => AddEvent("GPU Engine",
        $"{e.Processes.Count} processes using GPU");

    private void OnDisplayChanged(DisplayTopologyEvent e) => AddEvent("Display",
        $"{e.Change}: {e.Description}");

    private void OnPowerSchemeChanged(PowerSchemeEvent e) => AddEvent("Power",
        $"{e.Change}: {e.SchemeName}");

    private void OnRemoteSessionChanged(RemoteSessionEvent e) => AddEvent("Remote",
        e.Description);

    private void AddEvent(string category, string message)
    {
        Dispatcher.Invoke(() =>
        {
            _events.Add(new LiveEventItem
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                Category = category,
                Message = message
            });

            while (_events.Count > MaxEvents)
                _events.RemoveAt(0);

            var showTelemetry = FilterTelemetry.IsChecked == true;
            var showForeground = FilterForeground.IsChecked == true;
            var showProcess = FilterProcess.IsChecked == true;
            var showGpuEngine = FilterGpuEngine.IsChecked == true;
            var showDisplay = FilterDisplay.IsChecked == true;
            var showPower = FilterPower.IsChecked == true;

            EventListBox.ItemsSource = _events
                .Where(e => e.Category switch
                {
                    "Foreground" => showForeground,
                    "Process" => showProcess,
                    "GPU Engine" => showGpuEngine,
                    "Display" => showDisplay,
                    "Power" => showPower,
                    "Remote" => showForeground,
                    _ => true
                })
                .Reverse()
                .ToList();

            EventListBox.ScrollIntoView(EventListBox.Items[0]);
        });
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _events.Clear();
        EventListBox.ItemsSource = null;
    }
}

public sealed class LiveEventItem
{
    public string Timestamp { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}