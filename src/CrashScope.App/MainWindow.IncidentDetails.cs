using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CrashScope.App.ViewModels;

namespace CrashScope.App;

public partial class MainWindow
{
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        if (IncidentsPage.Visibility != Visibility.Visible || e.OriginalSource is not DependencyObject source)
            return;

        var incident = FindIncidentDataContext(source);
        if (incident is null)
            return;

        var window = new IncidentDetailsWindow(incident.Id)
        {
            Owner = this
        };
        window.Show();
        e.Handled = true;
    }

    private static IncidentCardViewModel? FindIncidentDataContext(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is IncidentCardViewModel incident)
                return incident;

            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }
}
