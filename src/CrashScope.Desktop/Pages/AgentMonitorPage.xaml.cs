using System.IO;
using CrashScope.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

/// <summary>
/// Live view of a running agent through the RPC control plane. Deliberately thin:
/// state changes are rendered from the view model, all logic lives in
/// <see cref="AgentMonitorViewModel"/>. Created fresh on every navigation
/// (MainWindow rebuilds pages), so connect on Loaded and disconnect on Unloaded.
/// </summary>
public partial class AgentMonitorPage : UserControl
{
    private readonly AgentMonitorViewModel _vm;

    public AgentMonitorPage()
    {
        InitializeComponent();
        _vm = new AgentMonitorViewModel(action => Dispatcher.Invoke(action));
        DataContext = _vm;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal static string DefaultStateDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashScope", "state");

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.StateChanged += () => RenderState();
        if (string.IsNullOrWhiteSpace(StateDirBox.Text))
            StateDirBox.Text = DefaultStateDirectory();
        RenderState();
        _ = ConnectAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _vm.Disconnect();

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        ConnectionText.Text = Loc.T("common.connecting");
        var ok = await _vm.ConnectAsync(StateDirBox.Text, pipeName: null);
        ConnectionText.Text = ok
            ? Loc.F("common.connected", _vm.PipeName)
            : Loc.F("common.notConnectedDetail", _vm.ConnectionError);
        RenderState();
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.Disconnect();
        ConnectionText.Text = Loc.T("common.disconnected");
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            ConnectionText.Text = Loc.F("agentMonitor.startFailed", ex.Message);
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            Loc.T("agentMonitor.confirmStop"),
            Loc.T("app.title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
            return;
        try
        {
            await _vm.StopRecordingAsync();
        }
        catch (Exception ex)
        {
            ConnectionText.Text = Loc.F("agentMonitor.stopFailed", ex.Message);
        }
    }

    private void RenderState()
    {
        RecordingText.Text = _vm.Recording ? Loc.T("agentMonitor.recordingDot") : _vm.RecorderState is null ? Loc.T("agentMonitor.idleDot") : $"\u25CF {_vm.RecorderState}";
        RecordingText.ToolTip = _vm.RecorderError ?? _vm.RecoveryError;
        // Themed keys, not the retired hex palette: the old #00D68F measured 1.9:1 on light cards.
        RecordingText.Foreground = SeverityPalette.Resolve(_vm.Recording ? "SuccessColor" : "TextSecondary");
        SamplesText.Text = Loc.F("common.samples", _vm.SampleCount);
        UptimeText.Text = Loc.F("common.uptimeSeconds", _vm.UptimeSeconds);
        GpuText.Text = Loc.F("agentMonitor.gpuLine", _vm.PrimaryGpu ?? Loc.T("common.na"));
        VersionText.Text = Loc.F("agentMonitor.agentLine", _vm.AgentVersion ?? Loc.T("common.na"));
        SessionText.Text = Loc.F("common.session", _vm.SessionId is null ? "-" : _vm.SessionId[..8]);
        LastUpdateText.Text = _vm.LastUpdateAt is { } at ? Loc.F("agentMonitor.lastUpdate", at.ToString("HH:mm:ss")) : string.Empty;
    }
}
