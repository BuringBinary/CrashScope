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

    public MainWindow()
    {
        // Only InitializeComponent (XAML) and the two core services are allowed to fail the
        // window. Everything else degrades to a status-bar message: a broken tray icon or
        // retention scan must never turn into "double-click, nothing happens".
        InitializeComponent();
        StartupLog.Info("MainWindow.InitializeComponent ok");

        _service = App.Current.Service;
        _localization = App.Current.Localization;

        var startupWarnings = new List<string>();

        try { _retention = new RetentionService(_service.RootDirectory); }
        catch (Exception ex)
        {
            StartupLog.Error("RetentionService unavailable", ex);
            startupWarnings.Add($"Retention disabled: {ex.Message}");
        }

        try { _tray = new TrayIconManager(this, _service); }
        catch (Exception ex)
        {
            StartupLog.Error("Tray icon unavailable", ex);
            startupWarnings.Add($"Tray icon disabled: {ex.Message}");
        }

        _localization.LocaleChanged += OnLocaleChanged;
        _service.StatusChanged += OnRecordingStatusChanged;

        try { NavigateTo("Home"); }
        catch (Exception ex)
        {
            StartupLog.Error("Home page failed to load; falling back to Settings", ex);
            startupWarnings.Add($"Home page failed: {ex.Message}");
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
        });
    }

    private void RefreshWindowLocale()
    {
        if (_service.IsRecording)
        {
            StatusText.Text = _localization.T("recorder.status.running");
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"));
        }
        else
        {
            StatusText.Text = _localization.T("recorder.status.stopped");
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"));
        }
    }

    private void EnforceRetentionOnStartup()
    {
        if (_retention is null) return;
        var (sessions, incidents, bytes) = _retention.Enforce();
        var usageMb = _retention.GetCurrentUsage() / (1024.0 * 1024.0);

        StatusBarText.Text = $"Data: {usageMb:F0} MB / {_retention.Settings.MaxTotalSizeMb} MB quota";
        if (sessions > 0 || incidents > 0)
        {
            StatusBarText.Text += $" | Cleaned: {sessions} sessions, {incidents} incidents ({bytes / 1024.0 / 1024.0:F1} MB)";
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
            catch (Exception ex) { StatusBarText.Text = $"Retention skipped: {ex.Message}"; }
            if (_service.RecoveryError is not null)
                StatusBarText.Text = $"Recovery deferred: {_service.RecoveryError}";
            if (analysis is not null)
            {
                var leading = analysis.Hypotheses.FirstOrDefault(h => h.Score > 0);
                var mode = leading?.Mode.ToString() ?? "Unknown";
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
            StatusBarText.Text = $"Previous-session check failed: {ex.Message}";
        }
    }

    private void OnRecordingStatusChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_service.IsRecording)
            {
                StatusText.Text = "Recorder: Active";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D68F"));
            }
            else
            {
                StatusText.Text = $"Recorder: {_service.RecorderState}";
                StatusText.ToolTip = _service.RecorderError;
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"));
            }
        });
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
        var page = new HomePage();
        page.NavigateRequested += tag =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Update nav radio
                var radio = tag switch
                {
                    "Home" => NavHome,
                    "Recorder" => NavRecorder,
                    "Dashboard" => NavDashboard,
                    "LiveMonitor" => NavLiveMonitor,
                    "Hardware" => NavHardware,
                    "AgentMonitor" => NavAgentMonitor,
                    "AgentIncidents" => NavAgentIncidents,
                    "AgentActions" => NavAgentActions,
                    "Incidents" => NavIncidents,
                    "Timeline" => NavTimeline,
                    "Settings" => NavSettings,
                    _ => null
                };
                if (radio != null) radio.IsChecked = true;
                NavigateTo(tag);
            });
        };
        return page;
    }


    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string page)
            NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        ContentArea.Children.Clear();
        UserControl content = page switch
        {
            "Home" => CreateHomePage(),
            "Recorder" => new RecorderStatusPage(),
            "Dashboard" => new DashboardPage(),
            "LiveMonitor" => new LiveMonitorPage(),
            "Hardware" => new HardwareOverviewPage(),
            "AgentMonitor" => new AgentMonitorPage(),
            "AgentIncidents" => new AgentIncidentsPage(),
            "AgentActions" => new AgentActionsPage(),
            "Incidents" => new IncidentsPage(),
            "Timeline" => new TimelinePage(),
            "Settings" => new SettingsPage(),
            _ => CreateHomePage()
        };
        _currentPage = content;
        ContentArea.Children.Add(content);
        StatusBarText.Text = $"{page} loaded";
    }
}