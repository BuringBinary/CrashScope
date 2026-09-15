using System.Windows;
using Application = System.Windows.Application;

namespace CrashScope.Desktop;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;
    public AppService Service { get; } = new();

    protected override void OnExit(ExitEventArgs e)
    {
        Service.Dispose();
        base.OnExit(e);
    }
}