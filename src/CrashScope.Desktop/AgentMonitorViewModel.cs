using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrashScope.Contracts;
using CrashScope.Core.Ipc;
using CrashScope.Desktop.Services;

namespace CrashScope.Desktop;

/// <summary>
/// UI-independent view model behind the agent monitor page. It owns the
/// <see cref="AgentRpcClient"/>, applies pushed status and telemetry to bindable
/// state, and merges the sensor catalog with live values. The page supplies only
/// a UI-thread marshal and button clicks; all logic lives here so tests can drive
/// it without WPF.
/// </summary>
public sealed class AgentMonitorViewModel : IDisposable
{
    /// <summary>Display-only unit hints by sensor type; sensors outside the map show a bare number.</summary>
    private static readonly Dictionary<string, string> UnitHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Clock"] = "MHz",
        ["Temperature"] = "\u00B0C",
        ["Power"] = "W",
        ["Load"] = "%",
        ["Level"] = "%",
        ["Control"] = "RPM",
        ["Fan"] = "RPM",
        ["Flow"] = "L/h",
        ["Voltage"] = "V",
        ["Current"] = "A",
        ["SmallData"] = "MB",
        ["Data"] = "GB",
        ["Energy"] = "mWh",
        ["Noise"] = "dBA",
        ["Humidity"] = "%"
    };

    private readonly Action<Action> _marshal;
    private readonly object _gate = new();
    private AgentRpcClient? _client;
    private CatalogPayload? _catalog;

    /// <summary>Raised on the marshal target thread after any state mutation.</summary>
    public event Action? StateChanged;

    public ObservableCollection<SensorRow> Rows { get; } = [];
    public ObservableCollection<Contracts.IncidentSummaryDto> Incidents { get; } = [];

    public bool IsConnected { get; private set; }
    public bool Recording { get; private set; }
    public string? RecorderState { get; private set; }
    public string? RecorderError { get; private set; }
    public string? RecoveryError { get; private set; }
    public int SampleCount { get; private set; }
    public double UptimeSeconds { get; private set; }
    public string? SessionId { get; private set; }
    public string? PrimaryGpu { get; private set; }
    public string? AgentVersion { get; private set; }
    public string? ConnectionError { get; private set; }
    public string? IncidentDetailText { get; private set; }
    public DateTimeOffset? LastUpdateAt { get; private set; }
    public string PipeName { get; private set; } = string.Empty;

    public AgentMonitorViewModel(Action<Action>? marshal = null)
        => _marshal = marshal ?? (action => action());

    /// <summary>
    /// Connects to the agent (explicit pipe name, or resolved from the state
    /// directory's discovery file), handshakes and pulls an initial snapshot.
    /// Returns false with <see cref="ConnectionError"/> set on failure.
    /// </summary>
    public async Task<bool> ConnectAsync(string? stateDirectory = null, string? pipeName = null, CancellationToken ct = default)
    {
        Disconnect();
        ConnectionError = null;

        if (!RpcEndpointResolver.TryResolve(stateDirectory, out var resolved, out var resolveError, pipeName))
        {
            ConnectionError = resolveError;
            RaiseStateChanged();
            return false;
        }

        try
        {
            var client = new AgentRpcClient();
            client.EventReceived = (type, payload) =>
            {
                _marshal(() => HandleEvent(type, payload));
                return Task.CompletedTask;
            };
            await client.ConnectAsync(resolved!, 4000, ct).ConfigureAwait(false);
            _client = client;
            PipeName = resolved!;

            var welcome = await client
                .SendRequestAsync(RpcMessages.Hello, new HelloPayload("desktop-monitor", RpcProtocol.Version), ct: ct)
                .ConfigureAwait(false);
            ApplyWelcome(Deserialize<WelcomePayload>(welcome));
            await RefreshAsync(ct).ConfigureAwait(false);

            IsConnected = true;
            RaiseStateChanged();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ConnectionError = ex.Message;
            DisposeClient();
            RaiseStateChanged();
            return false;
        }
    }

    /// <summary>Re-polls status, catalog and latest telemetry (also the manual reconnect path).</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException(Loc.T("common.notConnected"));
        ApplyStatus(Deserialize<StatusPayload>(
            await client.SendRequestAsync(RpcMessages.GetStatus, ct: ct).ConfigureAwait(false)));
        ApplyCatalog(Deserialize<CatalogPayload>(
            await client.SendRequestAsync(RpcMessages.GetSensorCatalog, ct: ct).ConfigureAwait(false)));
        ApplyTelemetry(Deserialize<TelemetryPayload>(
            await client.SendRequestAsync(RpcMessages.GetLatestTelemetry, ct: ct).ConfigureAwait(false)));
    }

    public async Task StartRecordingAsync(CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException(Loc.T("common.notConnected"));
        await client.SendRequestAsync(RpcMessages.StartRecording, ct: ct).ConfigureAwait(false);
    }

    public async Task StopRecordingAsync(CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException(Loc.T("common.notConnected"));
        await client.SendRequestAsync(RpcMessages.StopRecording, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Reloads the incident list from the connected agent and clears the detail pane.</summary>
    public async Task LoadIncidentsAsync(CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException(Loc.T("common.notConnected"));
        var payload = Deserialize<IncidentListPayload>(
            await client.SendRequestAsync(RpcMessages.GetIncidents, ct: ct).ConfigureAwait(false));
        Incidents.Clear();
        if (payload is not null)
        {
            foreach (var incident in payload.Incidents)
                Incidents.Add(incident);
        }

        IncidentDetailText = null;
        RaiseStateChanged();
    }

    public async Task LoadIncidentDetailAsync(string id, CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException(Loc.T("common.notConnected"));
        var payload = Deserialize<IncidentDetailPayload>(
            await client.SendRequestAsync(RpcMessages.GetIncidentDetail, new IncidentDetailRequest(id), ct: ct)
                .ConfigureAwait(false));
        IncidentDetailText = payload is null
            ? null
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { payload.Summary, payload.Timeline }.Where(s => !string.IsNullOrWhiteSpace(s)));
        RaiseStateChanged();
    }

    public void Disconnect() => DisposeClient();

    public void Dispose() => DisposeClient();

    /// <summary>Entry point for server-pushed events. Runs on the marshal target thread.</summary>
    public void HandleEvent(string type, JsonElement? payload)
    {
        switch (type)
        {
            case RpcMessages.StatusChanged:
                ApplyStatus(Deserialize<StatusPayload>(payload));
                break;
            case RpcMessages.TelemetryUpdate:
                ApplyTelemetry(Deserialize<TelemetryPayload>(payload));
                break;
        }

        RaiseStateChanged();
    }

    public void ApplyStatus(StatusPayload? status)
    {
        if (status is null)
            return;
        Recording = status.Recording;
        RecorderState = status.RecorderState;
        RecorderError = status.RecorderError;
        RecoveryError = status.RecoveryError;
        SampleCount = status.SampleCount;
        UptimeSeconds = status.UptimeSeconds;
        SessionId = status.SessionId;
        PrimaryGpu = status.PrimaryGpu;
        AgentVersion = status.AgentVersion;
    }

    public void ApplyWelcome(WelcomePayload? welcome)
    {
        if (welcome is null)
            return;
        AgentVersion = welcome.AgentVersion;
        Recording = welcome.Recording;
        SessionId = welcome.SessionId;
    }

    /// <summary>Rebuilds the rows from the catalog. Row order matches value indices 1:1.</summary>
    public void ApplyCatalog(CatalogPayload? catalog)
    {
        if (catalog is null)
            return;
        _catalog = catalog;
        Rows.Clear();
        foreach (var entry in catalog.Entries)
            Rows.Add(new SensorRow(entry.HardwareName, entry.SensorName, entry.SensorType));
    }

    /// <summary>Merges a value vector into the rows; indices align with the catalog.</summary>
    public void ApplyTelemetry(TelemetryPayload? telemetry)
    {
        if (telemetry is null)
            return;
        LastUpdateAt = telemetry.Timestamp;
        var types = _catalog?.Entries.Select(e => e.SensorType).ToList();
        for (var i = 0; i < Rows.Count && i < telemetry.Values.Count; i++)
        {
            var unit = types is null ? string.Empty : UnitHints.GetValueOrDefault(types[i], string.Empty);
            Rows[i].SetValue(FormatValue(telemetry.Values[i], unit));
        }
    }

    internal static string FormatValue(float? value, string unit)
        => value.HasValue
            ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) + unit
            : "-";

    private void RaiseStateChanged() => StateChanged?.Invoke();

    private void DisposeClient()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            IsConnected = false;
            Recording = false;
        }

        RaiseStateChanged();
    }

    private static T? Deserialize<T>(JsonElement? element) where T : class
        => element is { } e ? e.Deserialize<T>(RpcFraming.Options) : null;

    /// <summary>One bindable sensor value; identity is fixed, only the text mutates per tick.</summary>
    public sealed class SensorRow : INotifyPropertyChanged
    {
        private string _valueText = "-";

        public SensorRow(string hardwareName, string sensorName, string sensorType)
        {
            HardwareName = hardwareName;
            SensorName = sensorName;
            SensorType = sensorType;
        }

        public string HardwareName { get; }
        public string SensorName { get; }
        public string SensorType { get; }

        public string ValueText
        {
            get => _valueText;
            private set
            {
                if (_valueText == value)
                    return;
                _valueText = value;
                OnPropertyChanged();
            }
        }

        public void SetValue(string text) => ValueText = text;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
