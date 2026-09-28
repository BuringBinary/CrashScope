using System.Windows;
using CrashScope.Desktop.Services;
using System.Windows.Controls;
using System.Windows.Media;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using UserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;

namespace CrashScope.Desktop.Pages;

public partial class HomePage : UserControl, IRefreshableLocale
{
    private readonly AppService _service;
    private readonly System.Windows.Threading.DispatcherTimer _sampleTimer;
    private int _incidentLoadVersion;
    private bool _incidentLoading;
    private bool _restoreNavigation;


    public event Action<string>? NavigateRequested;
    public event Action<string>? IncidentOpenRequested;
    private double _savedOffset;
    private string? _openedIncidentId;

    public HomePage()
    {
        InitializeComponent();
        _service = App.Current.Service;
        _sampleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sampleTimer.Tick += (_, _) => { if (IsLoaded) { RefreshRecorder(); RefreshHardware(); } };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _service.StatusChanged -= OnStatusChanged;
        _service.StatusChanged += OnStatusChanged;
        _service.SensorUpdated -= OnSensorUpdated;
        _service.SensorUpdated += OnSensorUpdated;
        _restoreNavigation = true;
        RefreshAll();
    }

    private void RestoreNavigation()
    {
        if (!_restoreNavigation) return;
        _restoreNavigation = false;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded) return;
            if (_openedIncidentId is not null)
                foreach (var button in FindButtons(IncidentsList))
                    if (button.DataContext is IncidentPresentation row && row.Id == _openedIncidentId)
                    { button.Focus(); break; }
            HomeScroll.ScrollToVerticalOffset(_savedOffset);
        }));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _sampleTimer.Stop();
        _incidentLoadVersion++;
        _incidentLoading = false;
        _service.StatusChanged -= OnStatusChanged;
        _service.SensorUpdated -= OnSensorUpdated;
    }

    private void OnStatusChanged()
    {
        Dispatcher.InvokeAsync(() => { if (IsLoaded) { RefreshRecorder(); RefreshHardware(); RefreshDataRoot(); } });
    }

    private void OnSensorUpdated(IReadOnlyList<float?> values) =>
        Dispatcher.InvokeAsync(() => { if (IsLoaded) RefreshHardware(); });

    public void CaptureNavigationState() => _savedOffset = HomeScroll.VerticalOffset;

    public void RefreshLocale() { CaptureNavigationState(); _restoreNavigation = true; RefreshAll(); }

    private void RefreshAll()
    {
        RefreshRecorder();
        RefreshHardware();
        _ = RefreshIncidentsAsync();
        RefreshDataRoot();
    }

    private void RefreshRecorder()
    {
        var state = _service.DisplayRecorderState;
        var stateKey = "overviewUi." + state.ToString().ToLowerInvariant();
        var stateLabel = Loc.T(stateKey);
        RecorderStateText.Text = stateLabel == stateKey ? state.ToString() : stateLabel;
        SessionText.Text = Loc.F("common.session", _service.DataDirectory);
        SamplesText.Text = Loc.F("common.samples", _service.SampleCount);

        var isRecording = state == RecorderState.Recording;

        // Session counters merged in from the retired Recorder page.
        var session = _service.CurrentSession;
        if (isRecording && session is not null)
        {
            SessionIdText.Text = session.SessionId.Length >= 12 ? session.SessionId[..12] : session.SessionId;
            var elapsed = DateTimeOffset.Now - session.StartedAt;
            UptimeText.Text = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
                : $"{elapsed.Minutes}m {elapsed.Seconds}s";
            SampleCountText.Text = _service.SampleCount.ToString("N0");
        }
        else
        {
            SessionIdText.Text = "--";
            UptimeText.Text = "--";
            SampleCountText.Text = "0";
        }

        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            isRecording ? "SuccessColor" : state == RecorderState.Faulted ? "DangerColor" : "TextSecondary");

        StartStopButton.Content = isRecording ? Loc.T("common.stop") : Loc.T("common.start");
        StartStopButton.SetResourceReference(StyleProperty, isRecording ? "DangerButton" : "AccentButton");
        StartStopButton.IsEnabled = !_service.RecorderOperationPending && state is not (RecorderState.Starting or RecorderState.Stopping);
        if (state is RecorderState.Starting or RecorderState.Stopping) StartStopButton.Content = Loc.T("liveUi." + state);

        var actionError = _service.RecorderError ?? _service.RecorderActionError;
        if (!string.IsNullOrWhiteSpace(actionError))
        {
            ErrorText.Text = Loc.F("home.errorLine", actionError);
            ErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(_service.RecoveryError))
        {
            RecoveryText.Text = Loc.F("home.recoveryLine", _service.RecoveryError);
            RecoveryText.Visibility = Visibility.Visible;
        }
        else
        {
            RecoveryText.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshHardware()
    {
        var snapshot = _service.LatestSample;
        var current = LiveSamplePresentation.BelongsToSession(snapshot, _service.CurrentSession?.SessionId);
        var age = current ? snapshot!.Age : (TimeSpan?)null;
        var state = LiveSamplePresentation.Evaluate(_service.DisplayRecorderState, current, age, _service.SampleInterval);
        var values = state == LiveDataState.Fresh ? snapshot!.Values : null;
        SampleStateText.Text = Loc.T("liveUi." + state);
        SampleStateText.SetResourceReference(TextBlock.ForegroundProperty, state is LiveDataState.Stale or LiveDataState.Faulted ? "WarningColor" : "TextSecondary");
        SampleAgeText.Text = current && age is not null && age >= TimeSpan.Zero
            ? Loc.F("liveUi.age", snapshot!.SampleAt.LocalDateTime.ToString("HH:mm:ss"), Math.Floor(age.Value.TotalSeconds))
            : current ? Loc.T("liveUi.clock") : "";
        // UI freshness only: runs while this page is loaded and the recorder is recording.
        _sampleTimer.IsEnabled = IsLoaded && _service.DisplayRecorderState == RecorderState.Recording;
        var catalog = state == LiveDataState.Fresh ? snapshot!.Catalog : _service.SensorCatalog;
        var map = DesktopSensorMap.Build(catalog);
        var missing = _service.IsRecording ? Loc.T("home.unavailable") : Loc.T("home.notRecording");
        CpuText.Text = map.CpuModel >= 0 ? catalog[map.CpuModel].HardwareName : missing;
        GpuText.Text = map.GpuModel >= 0 ? catalog[map.GpuModel].HardwareName : missing;
        CpuValueText.Text = OverviewMetricText.Format(values, map.CpuLoad, _service.IsRecording, "%");
        GpuValueText.Text = OverviewMetricText.Format(values, map.GpuLoad, _service.IsRecording, "%");
        RamValueText.Text = OverviewMetricText.Format(values, map.RamUsed, _service.IsRecording, "GB");
        RamText.Text = !_service.IsRecording ? missing : RamValueText.Text == "—"
            ? Loc.T("overviewUi.waiting") : Loc.T("overviewUi.memory");
        CpuAvailabilityText.Text = CpuValueText.Text == "—" ? state == LiveDataState.Fresh ? Loc.T("liveUi.missingMetric") : Loc.T("liveUi." + state) : "";
        GpuAvailabilityText.Text = GpuValueText.Text == "—" ? state == LiveDataState.Fresh ? Loc.T("liveUi.missingMetric") : Loc.T("liveUi." + state) : "";
        RamAvailabilityText.Text = RamValueText.Text == "—" ? state == LiveDataState.Fresh ? Loc.T("liveUi.missingMetric") : Loc.T("liveUi." + state) : "";
        RefreshCollectionHealth();
    }

    private void RefreshCollectionHealth()
    {
        var health = _service.CollectorHealth;
        CollectionHealthText.Text = !_service.IsRecording ? Loc.T("home.collectionStoppedLong")
            : health.Count == 0 ? Loc.T("home.collectionPending")
            : Loc.T("home.collectionPrefix") + string.Join(" · ", health.Select(h => $"{h.Collector}: {h.Status}"));
        CollectionHealthText.ToolTip = health.Count == 0 ? Loc.T("home.collectionTip")
            : string.Join("\n", health.Select(h => $"{h.Collector}: {h.Detail}"));
    }

    private void RefreshDataRoot()
    {
        DataRootText.Text = Loc.F("home.dataLine", _service.RootDirectory);
        ElevationText.Text = Loc.F("home.privilegeLine", _service.IsElevated ? Loc.T("common.administrator") : Loc.T("common.standardUser"), typeof(AppService).Assembly.GetName().Version, Environment.Version);

        // Previous-session outcome, merged in from the retired Recorder page.
        var previous = _service.PreviousSession;
        if (previous is not null)
        {
            var status = previous.CleanShutdown ? Loc.T("recorder.cleanShutdown") : Loc.T("recorder.uncleanShutdown");
            PreviousSessionText.Text = $"{Loc.T("recorder.previousSession")}: {previous.LastHeartbeatAt:yyyy-MM-dd HH:mm:ss} ({status})";
            PreviousSessionText.Foreground = previous.CleanShutdown
                ? (Brush)FindResource("TextSecondary")
                : SeverityPalette.Resolve(SeverityPalette.High);
        }
        else
        {
            PreviousSessionText.Text = $"{Loc.T("recorder.previousSession")}: {Loc.T("common.none")}";
            PreviousSessionText.SetResourceReference(ForegroundProperty, "TextSecondary");
        }

        // Capture cadence, derived from the live sample interval rather than the static
        // placeholders the retired Recorder page used to show.
        var interval = _service.SampleInterval;
        CadenceText.Text = interval > TimeSpan.Zero
            ? $"{Loc.T("recorder.writeThroughput")}: {1.0 / interval.TotalSeconds:0.##} Hz"
            : $"{Loc.T("recorder.writeThroughput")}: --";
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _service.RootDirectory;
        if (System.IO.Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
        else
            WpfMessageBox.Show(Loc.F("recorder.dataDirNotFound", path), Loc.T("home.dlgRecorder"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task RefreshIncidentsAsync()
    {
        if (_incidentLoading) return;
        _incidentLoading = true;
        var version = ++_incidentLoadVersion;
        IncidentReadNotice.Text = Loc.T("readUi.loading");
        IncidentsEmptyText.Visibility = Visibility.Collapsed;
        ReloadIncidentsButton.IsEnabled = false;
        IncidentsList.IsEnabled = false;
        try
        {
            IncidentReadResult result;
            try { result = await _service.ReadIncidentsAsync(); }
            catch (Exception ex) { result = IncidentReadResult.Failed(ex); }
            if (!IsLoaded || version != _incidentLoadVersion) return;
            IncidentsList.ItemsSource = result.Items.Take(5).Select(i => IncidentPresentation.Create(i, App.Current.Localization.CurrentLocale, Loc.T)).ToList();
            IncidentReadNotice.SetResourceReference(TextBlock.ForegroundProperty, result.HasIssues ? "WarningColor" : "TextSecondary");
            IncidentReadNotice.Text = result.Summary(Loc.T, App.Current.Localization.CurrentLocale);
            IncidentReadDetails.Text = result.Details(Loc.T);
            IncidentReadIssues.Visibility = result.HasIssues ? Visibility.Visible : Visibility.Collapsed;
            IncidentsEmptyText.Visibility = Visibility.Collapsed; // complete empty text is included in the read result
            RestoreNavigation();
        }
        finally
        {
            if (version == _incidentLoadVersion) { _incidentLoading = false; ReloadIncidentsButton.IsEnabled = true; IncidentsList.IsEnabled = true; }
        }
    }

    private async void ReloadIncidents_Click(object sender, RoutedEventArgs e) => await RefreshIncidentsAsync();

    private static IEnumerable<WpfButton> FindButtons(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is WpfButton button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }

    private void OpenIncident_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: IncidentPresentation row })
        {
            _savedOffset = HomeScroll.VerticalOffset;
            _openedIncidentId = row.Id;
            IncidentOpenRequested?.Invoke(row.Id);
        }
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service.RecorderOperationPending) return;
        try { await _service.SetRecordingAsync(!_service.IsRecording); }
        catch (Exception ex)
        {
            if (IsLoaded) WpfMessageBox.Show(ex.Message, Loc.T("home.dlgRecorder"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { if (IsLoaded) { RefreshRecorder(); RefreshHardware(); } }
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        CheckButton.Content = Loc.T("home.checking");

        IncidentAnalysis? analysis;
        try
        {
            analysis = await Task.Run(_service.CheckPreviousUncleanSession);
        }
        finally
        {
            // Without finally a throw would strand the button greyed out at "检查中..."
            // until the page is rebuilt. AppService swallows today, but the guarantee
            // belongs here, next to the code that disabled the button.
            CheckButton.Content = Loc.T("home.checkLast");
            CheckButton.IsEnabled = true;
        }

        if (analysis != null)
        {
            NavigateRequested?.Invoke("Incidents");
        }
        else if (_service.RecoveryError != null)
        {
            WpfMessageBox.Show(Loc.F("home.recoveryDeferred", _service.RecoveryError), Loc.T("home.dlgRecovery"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            WpfMessageBox.Show(Loc.T("home.noUncleanSession"), Loc.T("home.dlgCheck"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void QuickNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is string tag)
        {
            NavigateRequested?.Invoke(tag);
        }
    }

    private void ViewAllIncidents_Click(object sender, RoutedEventArgs e)
    {
        NavigateRequested?.Invoke("Incidents");
    }
}
