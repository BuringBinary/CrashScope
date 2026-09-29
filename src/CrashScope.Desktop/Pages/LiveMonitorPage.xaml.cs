using CrashScope.Core.Models;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

public partial class LiveMonitorPage : UserControl
{
    private readonly ILiveMonitorEventSource _service;
    private readonly List<LiveEventItem> _events = new();
    private const int MaxEvents = 500;
    private bool _listening;

    public LiveMonitorPage() : this(App.Current.Service) { }

    public LiveMonitorPage(ILiveMonitorEventSource service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        foreach (var filter in new[] { FilterForeground, FilterProcess, FilterGpuEngine, FilterDisplay, FilterPower })
        {
            filter.Checked += FilterChanged;
            filter.Unchecked += FilterChanged;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_listening) return;
        _service.ForegroundChanged += OnForegroundChanged;
        _service.ProcessEventReceived += OnProcessEvent;
        _service.GpuEngineUpdated += OnGpuEngineUpdated;
        _service.DisplayChanged += OnDisplayChanged;
        _service.PowerSchemeChanged += OnPowerSchemeChanged;
        _service.RemoteSessionChanged += OnRemoteSessionChanged;
        _service.StatusChanged += OnStatusChanged;
        _listening = true;
        RefreshEvents();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_listening) return;
        // Queued dispatcher updates from the old page must not touch it after navigation.
        _listening = false;
        _service.StatusChanged -= OnStatusChanged;
        _service.ForegroundChanged -= OnForegroundChanged;
        _service.ProcessEventReceived -= OnProcessEvent;
        _service.GpuEngineUpdated -= OnGpuEngineUpdated;
        _service.DisplayChanged -= OnDisplayChanged;
        _service.PowerSchemeChanged -= OnPowerSchemeChanged;
        _service.RemoteSessionChanged -= OnRemoteSessionChanged;
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
        Dispatcher.InvokeAsync(() =>
        {
            // Parent is cleared synchronously when MainWindow swaps pages, while the
            // Unloaded broadcast (and the _listening reset) only runs later at Loaded
            // priority. Without the Parent check a Normal-priority callback queued just
            // before navigation would still update the detached page.
            if (!_listening || !IsLoaded || Parent is null) return;
            _events.Add(new LiveEventItem
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                Category = category,
                Message = message
            });

            while (_events.Count > MaxEvents)
                _events.RemoveAt(0);

            RefreshEvents();
        });
    }

    private void OnStatusChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (_listening && IsLoaded && Parent is not null) RefreshEvents();
    });

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshEvents();

    private void RefreshEvents()
    {
        var showForeground = FilterForeground.IsChecked == true;
        var showProcess = FilterProcess.IsChecked == true;
        var showGpuEngine = FilterGpuEngine.IsChecked == true;
        var showDisplay = FilterDisplay.IsChecked == true;
        var showPower = FilterPower.IsChecked == true;

        var visible = _events
            .Where(e => e.Category switch
            {
                "Foreground" => showForeground,
                "Process" => showProcess,
                "GPU Engine" => showGpuEngine,
                "Display" => showDisplay,
                "Power" => showPower,
                "Remote" => showForeground,
                _ => false
            })
            .Reverse()
            .ToList();

        EventListBox.ItemsSource = visible;
        LiveStatusText.SetResourceReference(TextBlock.TextProperty, "L." + MonitoringPresentation.EventStatusKey(_service.DisplayRecorderState));
        EmptyEventsText.SetResourceReference(TextBlock.TextProperty, "L." + MonitoringPresentation.EventEmptyKey(_service.DisplayRecorderState,
            _events.Count, showForeground || showProcess || showGpuEngine || showDisplay || showPower));
        EmptyEventsText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (visible.Count > 0)
            EventListBox.ScrollIntoView(visible[0]);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _events.Clear();
        RefreshEvents();
    }
}

public sealed class LiveEventItem
{
    public string Timestamp { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}