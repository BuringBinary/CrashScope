using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CrashScope.Desktop.Pages;
using CrashScope.Core.Infrastructure;
using CrashScope.Desktop.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using RadioButton = System.Windows.Controls.RadioButton;
using SessionEndingCancelEventArgs = System.Windows.SessionEndingCancelEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop;

public partial class MainWindow : Window
{
    private readonly AppService _service;
    private readonly TrayIconManager? _tray;
    private readonly RetentionService? _retention;
    private readonly LocalizationService _localization;
    private bool _forceClose;
    private UserControl? _currentPage;
    private HomePage? _homePage;
    private bool _updatingNavigation;

    public MainWindow()
    {
        // Only InitializeComponent (XAML) and the two core services are allowed to fail the
        // window. Everything else degrades to a status-bar message: a broken tray icon or
        // retention scan must never turn into "double-click, nothing happens".
        InitializeComponent();
        AgentNavigationToggle.Checked += AgentNavigationToggle_StateChanged;
        AgentNavigationToggle.Unchecked += AgentNavigationToggle_StateChanged;
        StartupLog.Info("MainWindow.InitializeComponent ok");

        _service = App.Current.Service;
        _localization = App.Current.Localization;

        var startupWarnings = new List<string>();

        try { _retention = new RetentionService(_service.RootDirectory); }
        catch (Exception ex)
        {
            StartupLog.Error("RetentionService unavailable", ex);
            startupWarnings.Add(Loc.F("statusbar.retentionDisabled", ex.Message));
        }

        try { _tray = new TrayIconManager(this, _service); }
        catch (Exception ex)
        {
            StartupLog.Error("Tray icon unavailable", ex);
            startupWarnings.Add(Loc.F("statusbar.trayDisabled", ex.Message));
        }

        _localization.LocaleChanged += OnLocaleChanged;
        _service.StatusChanged += OnRecordingStatusChanged;
        _service.SensorUpdated += OnSensorUpdated;
        RefreshSidebarRecorder();

        try { NavHome.IsChecked = true; }
        catch (Exception ex)
        {
            StartupLog.Error("Home page failed to load; falling back to Settings", ex);
            startupWarnings.Add(Loc.F("statusbar.homeFailed", ex.Message));
            try { NavigateTo("Settings"); }
            catch (Exception ex2) { StartupLog.Error("Settings page also failed", ex2); }
        }

        if (startupWarnings.Count > 0)
            StatusBarText.Text = string.Join(" | ", startupWarnings);

        CheckPreviousSessionOnStartup();
    }

    private void OnLocaleChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshWindowLocale();
            (_currentPage as IRefreshableLocale)?.RefreshLocale();
            StatusBarText.Text = Loc.T("statusbar.ready");
        });
    }

    private void RefreshWindowLocale() => RefreshSidebarRecorder();

    private void RefreshSidebarRecorder()
    {
        var snapshot = _service.LatestSample;
        var current = LiveSamplePresentation.BelongsToSession(snapshot, _service.CurrentSession?.SessionId);
        var age = current ? snapshot!.Age : (TimeSpan?)null;
        var liveState = LiveSamplePresentation.Evaluate(_service.DisplayRecorderState, current, age, _service.SampleInterval);
        var presentation = SidebarRecorderPresentation.From(liveState);

        SidebarStateText.Text = Loc.T("sidebarUi." + presentation.StateKey);
        SidebarStateText.SetResourceReference(TextBlock.ForegroundProperty, SidebarStateBrush(liveState));
        SidebarStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, SidebarStateBrush(liveState));

        var sampleTime = current && age is not null && age >= TimeSpan.Zero
            ? snapshot!.SampleAt.LocalDateTime.ToString("HH:mm:ss")
            : null;
        // The hint used to occupy its own line; the slim row surfaces it on hover instead.
        var hint = presentation.HintKey is "recordingHint" or "staleHint"
            ? sampleTime is null ? Loc.T("liveUi.clock") : Loc.F("sidebarUi." + presentation.HintKey, sampleTime)
            : Loc.T("sidebarUi." + presentation.HintKey);
        SidebarRecorderRow.ToolTip = $"{Loc.T("sidebarUi.title")}: {SidebarStateText.Text}\n{hint}";

        var error = _service.RecorderActionError ?? _service.RecorderError;
        SidebarErrorText.Text = string.IsNullOrWhiteSpace(error) ? "" : Loc.F("sidebarUi.error", error);
        SidebarErrorText.ToolTip = error;
        SidebarErrorText.Visibility = string.IsNullOrWhiteSpace(error) ? Visibility.Collapsed : Visibility.Visible;

        var actionKey = presentation.Action switch
        {
            SidebarRecorderAction.Start => "startRecording",
            SidebarRecorderAction.Stop => "stopRecording",
            SidebarRecorderAction.Retry => "retryRecording",
            _ when liveState == LiveDataState.Stopping => "stoppingAction",
            _ => "startingAction"
        };
        var actionLabel = Loc.T("sidebarUi." + actionKey);
        // Icon-only button: IconTextTemplate drops the label margin when the text part is empty,
        // so keep the leading MDL2 glyph and move the wording into the tooltip.
        SidebarRecordButton.Content = IconOnly(actionLabel);
        SidebarRecordButton.ToolTip = actionLabel.Trim();
        System.Windows.Automation.AutomationProperties.SetName(SidebarRecordButton, actionLabel.Trim());
        // The slim row drops the filled accent/danger fill, so carry the stop affordance on the glyph.
        SidebarRecordButton.SetResourceReference(ForegroundProperty,
            presentation.Action == SidebarRecorderAction.Stop ? "DangerColor" : "TextPrimary");
        SidebarRecordButton.IsEnabled = presentation.Action != SidebarRecorderAction.Busy && !_service.RecorderOperationPending;
    }

    /// <summary>
    /// Keeps only the leading Segoe MDL2 glyph of a localized "glyph + spaces + text" label.
    /// Falls back to the untouched label when the string carries no glyph prefix.
    /// </summary>
    private static string IconOnly(string label)
    {
        if (string.IsNullOrEmpty(label)) return label;
        return label[0] is >= '\uE000' and <= '\uF8FF' ? label[..1] : label;
    }

    private static string SidebarStateBrush(LiveDataState state) => state switch
    {
        LiveDataState.Fresh => "SuccessColor",
        LiveDataState.Faulted => "DangerColor",
        LiveDataState.Starting or LiveDataState.Stopping or LiveDataState.Waiting or LiveDataState.Stale => "WarningColor",
        _ => "TextSecondary"
    };

    private void EnforceRetentionOnStartup()
    {
        if (_retention is null) return;
        var (sessions, incidents, bytes) = _retention.Enforce();
        var usageMb = _retention.GetCurrentUsage() / (1024.0 * 1024.0);

        StatusBarText.Text = Loc.F("statusbar.dataQuota", usageMb, _retention.Settings.MaxTotalSizeMb);
        if (sessions > 0 || incidents > 0)
        {
            StatusBarText.Text += Loc.F("statusbar.cleaned", sessions, incidents, bytes / 1024.0 / 1024.0);
        }
    }

    private async void CheckPreviousSessionOnStartup()
    {
        try
        {
            await Task.Delay(1500); // let UI render first
            var analysis = await Task.Run(_service.CheckPreviousUncleanSession);
            // Recovery first; retention also acquires ownership and protects pending evidence.
            try { EnforceRetentionOnStartup(); }
            catch (Exception ex) { StatusBarText.Text = Loc.F("statusbar.retentionSkipped", ex.Message); }
            if (_service.RecoveryError is not null)
                StatusBarText.Text = Loc.F("statusbar.recoveryDeferred", _service.RecoveryError);
            if (analysis is not null)
            {
                var leading = analysis.Hypotheses.FirstOrDefault(h => h.Score > 0);
                // Localize through the same enum mapping the incidents page uses; the raw
                // enum name (or English "Unknown") must not surface in the tray balloon.
                var mode = IncidentPresentation.Mode(leading?.Mode.ToString() ?? "Unknown", Loc.T);
                var confidence = leading?.Confidence ?? 0;
                _tray?.ShowUncleanSessionBalloon(mode, confidence);

                await Dispatcher.InvokeAsync(() =>
                {
                    // navigate to Incidents page
                    NavIncidents.IsChecked = true;
                    NavigateTo("Incidents");
                });
            }
        }
        catch (Exception ex)
        {
            // async void: an escaped exception here would crash the process after the window is up.
            StartupLog.Error("Previous-session check failed", ex);
            StatusBarText.Text = Loc.F("statusbar.previousCheckFailed", ex.Message);
        }
    }

    private void OnRecordingStatusChanged() =>
        Dispatcher.InvokeAsync(() => { if (IsLoaded) RefreshSidebarRecorder(); });

    private void OnSensorUpdated(IReadOnlyList<float?> _) =>
        Dispatcher.InvokeAsync(() => { if (IsLoaded) RefreshSidebarRecorder(); });

    private async void SidebarRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service.RecorderOperationPending) return;
        try { await _service.SetRecordingAsync(!_service.IsRecording); }
        catch (Exception ex) { StartupLog.Error("Sidebar recorder action failed", ex); }
        finally { if (IsLoaded) RefreshSidebarRecorder(); }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            MaxRestoreButton_Click(sender, e);
        else if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Current.UiSettings.MinimizeToTray)
            HideToTray();
        else
            WindowState = WindowState.Minimized;
    }

    private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <summary>
    /// Keeps the maximise/restore glyph in sync with the real window state.
    /// </summary>
    /// <remarks>
    /// The glyph used to be flipped by hand inside the button handler, so any state change
    /// that did not go through that button - Win+Up, Aero Snap, double-clicking the caption,
    /// restoring from the tray - left the icon lying about the current state.
    /// </remarks>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (MaxRestoreButton is not null)
            MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Current.UiSettings.CloseToTray)
            HideToTray();
        else
            RequestExit();
    }

    private void HideToTray()
    {
        if (_tray is null)
        {
            // No tray icon to come back from: hiding would strand the user. Minimise instead.
            WindowState = WindowState.Minimized;
            return;
        }
        _tray.StopBlink();
        Hide();
    }

    /// <summary>Restores the window from the tray (or from a second-instance launch signal).</summary>
    public void ShowFromTray()
    {
        _tray?.StopBlink();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false; // pull to front without staying topmost
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 and the system menu land here too, so the same "close to tray" preference
        // that drives the title-bar button must be honoured, otherwise the setting only
        // half-works. _forceClose (tray Exit / RequestExit) always wins.
        if (!_forceClose && App.Current.UiSettings.CloseToTray && _tray is not null)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _localization.LocaleChanged -= OnLocaleChanged;
        _service.StatusChanged -= OnRecordingStatusChanged;
        _service.SensorUpdated -= OnSensorUpdated;
        _tray?.Dispose();
        // Service is disposed exactly once by App.OnExit.
        base.OnClosing(e);
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// Closes the window for real instead of hiding it to the tray, so the tray icon is
    /// removed and the recorder is released exactly once.
    /// </summary>
    /// <remarks>
    /// _forceClose was never assigned, so OnClosing always cancelled and hid: the tray
    /// "Exit" item left a ghost tray icon behind and App.OnExit disposed the service a
    /// second time. Alt+F4 and the title-bar close button now follow the user's
    /// "close to tray" preference (UiSettingsService.CloseToTray); this method is the
    /// unconditional exit path and always wins over it.
    /// </remarks>
    public void RequestExit()
    {
        _forceClose = true;
        Close();
    }

    private UserControl CreateHomePage()
    {
        if (_homePage is not null) return _homePage;
        _homePage = new HomePage();
        _homePage.NavigateRequested += tag => NavigateTo(tag);
        _homePage.IncidentOpenRequested += id => NavigateTo("Incidents", id, true);
        return _homePage;
    }

    private IncidentsPage CreateIncidentsPage(string? incidentId, bool returnHome)
    {
        var page = new IncidentsPage(incidentId, returnHome);
        page.BackRequested += () => NavigateTo("Home");
        return page;
    }

    private void AgentNavigationToggle_StateChanged(object sender, RoutedEventArgs e)
    {
        AgentNavigationItems.Visibility = AgentNavigationToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_updatingNavigation && sender is RadioButton radio && radio.Tag is string page)
            NavigateTo(page);
    }

    private void NavigateTo(string page, string? incidentId = null, bool returnHome = false)
    {
        // Keep the advanced group discoverable when a route is opened from the tray,
        // startup recovery, or another page; otherwise the selected route could be hidden.
        if (NavigationLayout.ShouldExpandAdvanced(page))
            AgentNavigationToggle.IsChecked = true;

        _updatingNavigation = true;
        try
        {
            var navName = page == "Home" ? "NavHome" : "Nav" + page;
            if (FindName(navName) is RadioButton radio) radio.IsChecked = true;
        }
        finally { _updatingNavigation = false; }
        if (_currentPage is HomePage home) home.CaptureNavigationState();
        ContentArea.Children.Clear();
        UserControl content = page switch
        {
            "Home" => CreateHomePage(),
            "Dashboard" => new DashboardPage(),
            "LiveMonitor" => new LiveMonitorPage(),
            "Hardware" => new HardwareOverviewPage(),
            "DiskHealth" => new DiskHealthPage(),
            "SpeedTest" => new SpeedTestPage(),
            "AgentMonitor" => new AgentMonitorPage(),
            "AgentIncidents" => new AgentIncidentsPage(),
            "AgentActions" => new AgentActionsPage(),
            "Incidents" => CreateIncidentsPage(incidentId, returnHome),
            "Timeline" => new TimelinePage(),
            "Settings" => new SettingsPage(),
            _ => CreateHomePage()
        };
        _currentPage = content;
        ContentArea.Children.Add(content);
        var navLabel = Loc.T("nav." + char.ToLowerInvariant(page[0]) + page[1..]);
        StatusBarText.Text = Loc.F("statusbar.pageLoaded", Converters.IconTextPartConverter.Split(navLabel).Label);
    }
}
