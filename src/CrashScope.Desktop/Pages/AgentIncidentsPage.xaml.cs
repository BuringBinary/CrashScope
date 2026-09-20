using System.Windows;
using CrashScope.Desktop.Services;
using System.Windows.Controls;
using CrashScope.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

/// <summary>
/// Remote viewer for the incident bundles of a running agent. Shares
/// <see cref="AgentMonitorViewModel"/> for connection + data access; the page adds
/// only list selection and detail rendering. Connects on Loaded, disconnects on
/// Unloaded (pages are rebuilt per navigation).
/// </summary>
public partial class AgentIncidentsPage : UserControl
{
    private readonly AgentMonitorViewModel _vm;

    public AgentIncidentsPage()
    {
        InitializeComponent();
        _vm = new AgentMonitorViewModel(action => Dispatcher.Invoke(action));
        DataContext = _vm;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _vm.Disconnect();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.StateChanged += () => RenderDetail();
        if (string.IsNullOrWhiteSpace(StateDirBox.Text))
            StateDirBox.Text = AgentMonitorPage.DefaultStateDirectory();
        await ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        ConnectionText.Text = Loc.T("common.connecting");
        var ok = await _vm.ConnectAsync(StateDirBox.Text, pipeName: null);
        if (!ok)
        {
            ConnectionText.Text = Loc.F("common.notConnectedDetail", _vm.ConnectionError);
            return;
        }

        ConnectionText.Text = Loc.F("common.connected", _vm.PipeName);
        try
        {
            await _vm.LoadIncidentsAsync();
        }
        catch (Exception ex)
        {
            ConnectionText.Text = Loc.F("agentIncidents.loadFailed", ex.Message);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ReconnectAsync();

    private async void IncidentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IncidentList.SelectedItem is not IncidentSummaryDto incident)
            return;

        try
        {
            await _vm.LoadIncidentDetailAsync(incident.Id);
        }
        catch (Exception ex)
        {
            ConnectionText.Text = Loc.F("agentIncidents.detailFailed", ex.Message);
        }
    }

    private void RenderDetail() => DetailBox.Text = _vm.IncidentDetailText ?? string.Empty;
}
