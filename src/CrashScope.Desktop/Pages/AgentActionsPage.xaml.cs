using System.Windows;
using System.Windows.Controls;
using CrashScope.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

/// <summary>
/// The mutation surface UI: power plans, process priority, and the append-only
/// action journal with rollback. All behavior lives in <see cref="AgentActionsViewModel"/>.
/// </summary>
public partial class AgentActionsPage : UserControl
{
    private readonly AgentActionsViewModel _vm;

    public AgentActionsPage()
    {
        InitializeComponent();
        _vm = new AgentActionsViewModel(action => Dispatcher.Invoke(action));
        DataContext = _vm;
        _vm.StatusChanged += () => StatusText.Text = _vm.StatusMessage;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _vm.Disconnect();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = _vm.StatusMessage;
        if (string.IsNullOrWhiteSpace(StateDirBox.Text))
            StateDirBox.Text = AgentMonitorPage.DefaultStateDirectory();
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        StatusText.Text = "Connecting...";
        await _vm.ConnectAsync(StateDirBox.Text, pipeName: null);
        StatusText.Text = _vm.StatusMessage;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void ApplyPlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlanList.SelectedItem is not PowerPlanDto plan)
        {
            StatusText.Text = "Select a power plan first.";
            return;
        }

        var reason = PlanReasonBox.Text.Trim();
        if (reason.Length == 0)
        {
            StatusText.Text = "A reason is required - that is the forensic contract.";
            return;
        }

        await _vm.SetPowerPlanAsync(plan.Guid, reason);
        StatusText.Text = _vm.StatusMessage;
    }

    private async void ApplyPriorityButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is not ProcessDto process)
        {
            StatusText.Text = "Select a process first.";
            return;
        }

        var priority = (PriorityBox.SelectedItem as ComboBoxItem)?.Content as string ?? "Normal";
        var reason = PriorityReasonBox.Text.Trim();
        if (reason.Length == 0)
        {
            StatusText.Text = "A reason is required - that is the forensic contract.";
            return;
        }

        await _vm.SetPriorityAsync(process.Pid, priority, reason);
        StatusText.Text = _vm.StatusMessage;
    }

    private async void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActionList.SelectedItem is not ActionRecordDto action)
        {
            StatusText.Text = "Select a journal row first.";
            return;
        }

        if (!action.Reversible || action.Status != "applied")
        {
            StatusText.Text = $"Action {action.ActionId[..8]} is not rollback-able (status: {action.Status}).";
            return;
        }

        var reason = RollbackReasonBox.Text.Trim();
        if (reason.Length == 0)
        {
            StatusText.Text = "A reason is required - that is the forensic contract.";
            return;
        }

        await _vm.RollbackAsync(action.ActionId, reason);
        StatusText.Text = _vm.StatusMessage;
    }
}
