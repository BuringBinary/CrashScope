using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using CrashScope.Desktop.Services;
using Application = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace CrashScope.Desktop;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CrashScope.Desktop.SingleInstance";
    private const string ShowWindowEventName = @"Local\CrashScope.Desktop.ShowWindow";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private RegisteredWaitHandle? _showWindowWait;
    private bool _shuttingDown;

    public static new App Current => (App)Application.Current;
    public AppService Service { get; } = new();
    public LocalizationService Localization { get; }
    public ThemeService Theme { get; }
    public UiSettingsService UiSettings { get; } = new();

    public App()
    {
        StartupLog.WriteBanner();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            StartupLog.Error("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            WriteLegacyCrashLog($"Unhandled: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupLog.Error("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        DispatcherUnhandledException += (_, e) =>
        {
            StartupLog.Error("DispatcherUnhandledException", e.Exception);
            WriteLegacyCrashLog($"Dispatcher: {e.Exception}");
            e.Handled = true;
            if (_shuttingDown) return;
            _shuttingDown = true;
            WpfMessageBox.Show(
                Loc.F("app.fatalUnhandled", e.Exception.Message, StartupLog.LogPath),
                "CrashScope", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };

        try
        {
            Localization = new LocalizationService();
            StartupLog.Info("LocalizationService ready");
        }
        catch (Exception ex)
        {
            StartupLog.Error("LocalizationService failed to initialise", ex);
            throw;
        }

        try
        {
            Theme = new ThemeService(this);
            StartupLog.Info("ThemeService ready");
        }
        catch (Exception ex)
        {
            StartupLog.Error("ThemeService failed to initialise", ex);
            throw;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupLog.Info("OnStartup");

        if (!TryAcquireSingleInstance())
        {
            StartupLog.Info("Another instance is already running; signalled it to show its window and exiting.");
            Shutdown(0);
            return;
        }

        MainWindow window;
        try
        {
            window = new MainWindow();
            StartupLog.Info("MainWindow constructed");
        }
        catch (Exception ex)
        {
            StartupLog.Error("MainWindow construction failed", ex);
            WriteLegacyCrashLog($"MainWindow ctor: {ex}");
            WpfMessageBox.Show(
                Loc.F("app.fatalMainWindow", ex.Message, StartupLog.LogPath),
                "CrashScope", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow = window;
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray keeps us alive while the window is hidden
        window.Show();
        window.Activate();
        StartupLog.Info($"MainWindow shown (hwnd={new System.Windows.Interop.WindowInteropHelper(window).Handle})");
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);

            if (!createdNew)
            {
                _showWindowEvent.Set();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return false;
            }

            _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
                _showWindowEvent,
                (_, _) => Dispatcher.InvokeAsync(ShowMainWindowFromSignal),
                null, Timeout.Infinite, executeOnlyOnce: false);
            return true;
        }
        catch (Exception ex)
        {
            // Single-instance is a convenience; never let it block startup.
            StartupLog.Error("Single-instance guard failed; continuing without it", ex);
            return true;
        }
    }

    private void ShowMainWindowFromSignal()
    {
        if (MainWindow is not MainWindow window) return;
        StartupLog.Info("Show-window signal received from a second instance");
        window.ShowFromTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StartupLog.Info($"OnExit code={e.ApplicationExitCode}");
        try { _showWindowWait?.Unregister(null); } catch { }
        try { _showWindowEvent?.Dispose(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        try { _singleInstanceMutex?.Dispose(); } catch { }
        try { Service.Dispose(); }
        catch (Exception ex) { StartupLog.Error("Service.Dispose failed during exit", ex); }
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        StartupLog.Info($"OnSessionEnding reason={e.ReasonSessionEnding}");
        if (MainWindow is MainWindow mainWindow)
            mainWindow.RequestExit();

        base.OnSessionEnding(e);
    }

    /// <summary>Keeps the older crash.log locations populated for anyone who already knows to look there.</summary>
    private static void WriteLegacyCrashLog(string text)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashScope");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"), $"[{DateTime.Now}] {text}\n");
        }
        catch { }
    }
}
