using System.IO;
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
        ConnectionText.Text = "Connecting...";
        var ok = await _vm.ConnectAsync(StateDirBox.Text, pipeName: null);
        ConnectionText.Text = ok
            ? $"Connected: {_vm.PipeName}"
            : $"Not connected: {_vm.ConnectionError}";
        RenderState();
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.Disconnect();
        ConnectionText.Text = "Disconnected";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            ConnectionText.Text = $"Start failed: {ex.Message}";
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "Stop the agent's recording session? It is marked clean only after successful shutdown; faults remain recorded.",
            "CrashScope", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
            return;
        try
        {
            await _vm.StopRecordingAsync();
        }
        catch (Exception ex)
        {
            ConnectionText.Text = $"Stop failed: {ex.Message}";
        }
    }

    private void RenderState()
    {
        RecordingText.Text = _vm.Recording ? "\u25CF recording" : $"\u25CF {_vm.RecorderState ?? "idle"}";
        RecordingText.ToolTip = _vm.RecorderError ?? _vm.RecoveryError;
        RecordingText.Foreground = _vm.Recording
            ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0xD6, 0x8F))
            : (System.Windows.Media.Brush)FindResource("TextSecondary");
        SamplesText.Text = $"Samples: {_vm.SampleCount}";
        UptimeText.Text = $"Uptime: {_vm.UptimeSeconds:0} s";
        GpuText.Text = $"GPU: {_vm.PrimaryGpu ?? "n/a"}";
        VersionText.Text = $"Agent: {_vm.AgentVersion ?? "n/a"}";
        SessionText.Text = $"Session: {(_vm.SessionId is null ? "-" : _vm.SessionId[..8])}";
        LastUpdateText.Text = _vm.LastUpdateAt is { } at ? $"Last update: {at:HH:mm:ss}" : string.Empty;
    }
}
