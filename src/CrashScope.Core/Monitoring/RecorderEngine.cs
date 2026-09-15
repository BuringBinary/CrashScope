using CrashScope.Core.Diagnostics;
using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using CrashScope.Core.Platform;
using System.Text.Json;

namespace CrashScope.Core.Monitoring;

public sealed class RecorderEngine : IDisposable
{
    private readonly string _sessionDirectory;
    private readonly string _stateDirectory;
    private readonly string _incidentsRoot;

    private HardwareMonitor? _hardware;
    private SessionJournal? _journal;
    private ProcessTracker? _processTracker;
    private GpuEngineTracker? _gpuEngineTracker;
    private DisplayTopologyTracker? _displayTracker;
    private PowerTracker? _powerTracker;

    private CrashSafeJsonlWriter? _telemetryWriter;
    private CrashSafeJsonlWriter? _foregroundWriter;
    private CrashSafeJsonlWriter? _processEventWriter;
    private CrashSafeJsonlWriter? _processSnapshotWriter;
    private CrashSafeJsonlWriter? _gpuEngineWriter;
    private CrashSafeJsonlWriter? _displayWriter;
    private CrashSafeJsonlWriter? _powerWriter;
    private CrashSafeJsonlWriter? _remoteSessionWriter;

    private CancellationTokenSource? _cts;
    private Task? _recordingTask;

    private ForegroundState? _previousForeground;
    private int _processPollCounter;
    private int _processSnapshotCounter;
    private int _heartbeatCounter;
    private int _displayCounter;
    private int _powerCounter;

    private readonly object _lock = new();

    public RecorderEngine(
        string sessionDirectory,
        string stateDirectory,
        string incidentsRoot)
    {
        _sessionDirectory = sessionDirectory;
        _stateDirectory = stateDirectory;
        _incidentsRoot = incidentsRoot;
    }

    public bool IsRecording { get; private set; }
    public SessionState? CurrentSession => _journal?.Current;
    public SessionState? PreviousSession => _journal?.PreviousState;
    public int SampleCount { get; private set; }
    public IReadOnlyList<SensorCatalogEntry> SensorCatalog => _hardware?.Catalog ?? Array.Empty<SensorCatalogEntry>();

    public event Action? StatusChanged;
    public event Action<IReadOnlyList<float?>>? SensorUpdated;
    public event Action<ForegroundEvent>? ForegroundChanged;
    public event Action<ProcessEvent>? ProcessEventReceived;
    public event Action<GpuEngineSample>? GpuEngineUpdated;
    public event Action<DisplayTopologyEvent>? DisplayChanged;
    public event Action<PowerSchemeEvent>? PowerSchemeChanged;
    public event Action<RemoteSessionEvent>? RemoteSessionChanged;

    public void Start()
    {
        lock (_lock)
        {
            if (IsRecording) return;

            Directory.CreateDirectory(_sessionDirectory);

            _journal = new SessionJournal(_stateDirectory, _sessionDirectory);
            _hardware = new HardwareMonitor();

            File.WriteAllText(
                Path.Combine(_sessionDirectory, "sensor-catalog.json"),
                JsonSerializer.Serialize(_hardware.Catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            File.WriteAllText(
                Path.Combine(_sessionDirectory, "driver-inventory.json"),
                JsonSerializer.Serialize(DriverInventory.Capture(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

            _telemetryWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "telemetry.jsonl"));
            _foregroundWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "foreground-events.jsonl"));
            _processEventWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "process-events.jsonl"));
            _processSnapshotWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "process-snapshots.jsonl"));
            _gpuEngineWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "gpu-engines.jsonl"));
            _displayWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "display-events.jsonl"));
            _powerWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "power-events.jsonl"));
            _remoteSessionWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "remote-session-events.jsonl"));

            _processTracker = new ProcessTracker();
            _gpuEngineTracker = new GpuEngineTracker();
            _displayTracker = new DisplayTopologyTracker();
            _powerTracker = new PowerTracker();

            _previousForeground = null;
            _processPollCounter = 0;
            _processSnapshotCounter = 0;
            _heartbeatCounter = 0;
            _displayCounter = 0;
            _powerCounter = 0;
            SampleCount = 0;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsRecording = true;
            StatusChanged?.Invoke();

            _recordingTask = Task.Run(async () =>
            {
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                    {
                        Tick(token);
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    timer.Dispose();
                }
            }, token);
        }
    }

    private void Tick(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = DateTimeOffset.Now;

        var foreground = WindowsActivity.CaptureForeground();
        var sensorValues = _hardware!.CaptureValues();
        _telemetryWriter!.Write(new TelemetrySnapshot(now, foreground, sensorValues));
        SampleCount++;

        SensorUpdated?.Invoke(sensorValues);

        if (_previousForeground is null ||
            _previousForeground.ProcessId != foreground.ProcessId ||
            !string.Equals(_previousForeground.ProcessName, foreground.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            var fe = new ForegroundEvent(now, foreground.ProcessId, foreground.ProcessName, foreground.IdleSeconds);
            _foregroundWriter!.Write(fe);
            ForegroundChanged?.Invoke(fe);
            _previousForeground = foreground;
        }

        if (++_processPollCounter >= 2)
        {
            _processPollCounter = 0;
            foreach (var processEvent in _processTracker!.Poll(now))
            {
                _processEventWriter!.Write(processEvent);
                ProcessEventReceived?.Invoke(processEvent);
            }

            var engineSample = _gpuEngineTracker!.Capture(now);
            if (engineSample is not null)
            {
                _gpuEngineWriter!.Write(engineSample);
                GpuEngineUpdated?.Invoke(engineSample);
            }
        }

        if (++_displayCounter >= 5)
        {
            _displayCounter = 0;
            var displayChange = _displayTracker!.Poll(now);
            if (displayChange is not null)
            {
                _displayWriter!.Write(displayChange);
                DisplayChanged?.Invoke(displayChange);
            }

            var remoteChange = _powerTracker!.PollRemoteSession(now);
            if (remoteChange is not null)
            {
                _remoteSessionWriter!.Write(remoteChange);
                RemoteSessionChanged?.Invoke(remoteChange);
            }
        }

        if (++_powerCounter >= 10)
        {
            _powerCounter = 0;
            var schemeChange = _powerTracker!.PollScheme(now);
            if (schemeChange is not null)
            {
                _powerWriter!.Write(schemeChange);
                PowerSchemeChanged?.Invoke(schemeChange);
            }
        }

        if (++_processSnapshotCounter >= 30)
        {
            _processSnapshotCounter = 0;
            _processSnapshotWriter!.Write(_processTracker!.CaptureSnapshot(now));
        }

        if (++_heartbeatCounter >= 5)
        {
            _heartbeatCounter = 0;
            _journal!.Heartbeat(now);
            StatusChanged?.Invoke();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRecording) return;

            _cts?.Cancel();

            try { _recordingTask?.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { }

            _journal?.MarkClean();
            _hardware?.Dispose();
            _telemetryWriter?.Dispose();
            _foregroundWriter?.Dispose();
            _processEventWriter?.Dispose();
            _processSnapshotWriter?.Dispose();
            _gpuEngineWriter?.Dispose();
            _displayWriter?.Dispose();
            _powerWriter?.Dispose();
            _remoteSessionWriter?.Dispose();

            _cts?.Dispose();
            _cts = null;
            _recordingTask = null;
            IsRecording = false;
            StatusChanged?.Invoke();
        }
    }

    public IncidentAnalysis? CheckPreviousUncleanSession()
    {
        if (_journal?.PreviousState is { CleanShutdown: false } previous)
        {
            var incidentDirectory = Path.Combine(
                _incidentsRoot,
                $"{previous.LastHeartbeatAt:yyyyMMdd-HHmmss}-{previous.SessionId[..8]}");
            try
            {
                return CrashEvidenceCollector.CollectForUncleanSession(previous, incidentDirectory);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
    }
}