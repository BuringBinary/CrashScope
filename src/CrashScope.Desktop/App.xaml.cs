using System.Windows;
using CrashScope.Desktop.Services;
using Application = System.Windows.Application;

namespace CrashScope.Desktop;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;
    public AppService Service { get; } = new();
    public LocalizationService Localization { get; }
    public ThemeService Theme { get; }

    public App()
    {
        Localization = new LocalizationService();
        Theme = new ThemeService(this);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Service.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows logoff / shutdown must not be turned into "hide to tray": close the main
        // window through its real-close path so the tray icon is removed and the recorder
        // stops cleanly, marking the session journal as a clean shutdown.
        if (MainWindow is MainWindow mainWindow)
            mainWindow.RequestExit();

        base.OnSessionEnding(e);
    }
}