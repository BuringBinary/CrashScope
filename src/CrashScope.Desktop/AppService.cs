using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using System.Diagnostics;
using CrashScope.Core.Monitoring;
using System.IO;
using System.Text.Json;

namespace CrashScope.Desktop;

public sealed class AppService : IDisposable, ILiveMonitorEventSource
{
    private readonly string _root;
    private readonly string _stateDirectory;
    private readonly string _sessionsRoot;
    private readonly string _incidentsRoot;

    private readonly IncidentCatalogReader _incidentReader = new();
    private readonly RecorderOperationGate _operation = new();
    private LiveSampleSnapshot? _latestSample;
    private RecorderEngine? _engine;
    private readonly object _lifecycleGate = new();

    public AppService()
    {
        _operation.Changed += () => StatusChanged?.Invoke();
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashScope");
        _stateDirectory = Path.Combine(_root, "state");
        _sessionsRoot = Path.Combine(_root, "sessions");
        _incidentsRoot = Path.Combine(_root, "incidents");
    }

    public RecorderState RecorderState => _engine?.State ?? RecorderState.Stopped;
    public IReadOnlyList<CollectorHealth> CollectorHealth => _engine?.CollectorHealth ?? [];
    public LiveSampleSnapshot? LatestSample => Volatile.Read(ref _latestSample);
    public TimeSpan SampleInterval => _engine?.SampleInterval ?? TimeSpan.FromSeconds(1);
    public bool RecorderOperationPending => _operation.Pending != 0;
    public string? RecorderActionError => _operation.LastError;
    public RecorderState DisplayRecorderState => _operation.Pending switch
    {
        1 => RecorderState.Starting,
        2 => RecorderState.Stopping,
        _ => RecorderState
    };

    // Background start/stop is shared by the Home and Recorder pages. Existing engine locking remains authoritative.
    public Task<bool> SetRecordingAsync(bool start) => _operation.RunAsync(start, () => Task.Run(async () =>
    {
        if (start && IsRecording) return;
        await StopRecordingAsync(); // Also release a faulted recorder before a new start.
        if (start) StartRecording();
    }));

    public bool IsStopping => RecorderState == RecorderState.Stopping;
    public string? RecorderError => _engine?.LastError;
    public string? RecoveryError { get; private set; }
    public bool IsRecording => _engine?.IsRecording ?? false;
    public SessionState? CurrentSession => _engine?.CurrentSession;
    public SessionState? PreviousSession => _engine?.PreviousSession;
    public int SampleCount => _engine?.SampleCount ?? 0;
    public string DataDirectory => _engine?.CurrentSession?.SessionDirectory ?? "--";
    public bool IsElevated => _engine?.CurrentSession?.Elevated ?? false;
    public string RootDirectory => _root;
    public string IncidentsRoot => _incidentsRoot;
    public IReadOnlyList<SensorCatalogEntry> SensorCatalog => _engine?.SensorCatalog ?? Array.Empty<SensorCatalogEntry>();

    public event Action? StatusChanged;
    public event Action<IReadOnlyList<float?>>? SensorUpdated;
    public event Action<StorageSample>? StorageUpdated;
    public event Action<ForegroundEvent>? ForegroundChanged;
    public event Action<ProcessEvent>? ProcessEventReceived;
    public event Action<GpuEngineSample>? GpuEngineUpdated;
    public event Action<DisplayTopologyEvent>? DisplayChanged;
    public event Action<PowerSchemeEvent>? PowerSchemeChanged;
    public event Action<RemoteSessionEvent>? RemoteSessionChanged;

    public void StartRecording()
    {
        lock (_lifecycleGate)
        {
            if (_engine?.IsRecording == true) return;
            if (_engine?.HasActiveResources == true)
                throw new InvalidOperationException("The previous recorder is stopping or faulted; stop it before restarting.");
            Volatile.Write(ref _latestSample, null);
            _engine?.Dispose();
            Directory.CreateDirectory(_stateDirectory);
            Directory.CreateDirectory(_sessionsRoot);
            Directory.CreateDirectory(_incidentsRoot);

            var sessionDirectory = Path.Combine(_sessionsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            _engine = new RecorderEngine(sessionDirectory, _stateDirectory, _incidentsRoot);
            _engine.StatusChanged += () => StatusChanged?.Invoke();
            var sourceEngine = _engine;
            _engine.SensorUpdated += values =>
            {
                if (!ReferenceEquals(_engine, sourceEngine)) return;
                var sampleAt = sourceEngine.LastSampleAt ?? DateTimeOffset.UtcNow;
                var snapshot = new LiveSampleSnapshot(sourceEngine.CurrentSession?.SessionId,
                    sourceEngine.SensorCatalog, values.ToArray(), sampleAt, Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow - sampleAt);
                Volatile.Write(ref _latestSample, snapshot);
                SensorUpdated?.Invoke(snapshot.Values);
            };
            _engine.StorageUpdated += s => StorageUpdated?.Invoke(s);
            _engine.ForegroundChanged += e => ForegroundChanged?.Invoke(e);
            _engine.ProcessEventReceived += e => ProcessEventReceived?.Invoke(e);
            _engine.GpuEngineUpdated += e => GpuEngineUpdated?.Invoke(e);
            _engine.DisplayChanged += e => DisplayChanged?.Invoke(e);
            _engine.PowerSchemeChanged += e => PowerSchemeChanged?.Invoke(e);
            _engine.RemoteSessionChanged += e => RemoteSessionChanged?.Invoke(e);

            _engine.Start();
            RecoveryError = _engine.RecoveryError;
        }
    }

    public void StopRecording()
    {
        lock (_lifecycleGate)
        {
            _engine?.Stop();
            // Retain fault details and an in-flight stop; a later Start may replace a fully stopped engine.
        }
        StatusChanged?.Invoke();
    }

    public async Task StopRecordingAsync()
    {
        Task stop;
        lock (_lifecycleGate) stop = _engine?.StopAsync() ?? Task.CompletedTask;
        await stop;
        // Preserve fault details for the UI; StartRecording replaces this stopped engine.
        StatusChanged?.Invoke();
    }

    public IncidentAnalysis? CheckPreviousUncleanSession()
    {
        try
        {
            RecoveryError = null;
            // SessionRecovery holds the same lease as the Agent. A live Agent is never an incident.
            return _engine is not null
                ? _engine.CheckPreviousUncleanSession()
                : SessionRecovery.Recover(_stateDirectory, _incidentsRoot);
        }
        catch (Exception ex)
        {
            RecoveryError = ex.Message;
            return null;
        }
    }

    public IncidentReadResult ReadIncidents() => _incidentReader.Read(_incidentsRoot);
    public Task<IncidentReadResult> ReadIncidentsAsync() => Task.Run(ReadIncidents);
    // Transitional shim: HEAD pages still call this until the UI batch lands; that batch removes it.
    public IReadOnlyList<IncidentInfo> GetIncidents() => ReadIncidents().Items;

    public void Dispose()
    {
        StopRecording();
    }
}

public sealed record IncidentInfo(
    string DirectoryName,
    string FullPath,
    DateTimeOffset Timestamp,
    string Mode,
    double Confidence,
    IReadOnlyList<string> OpenQuestions,
    IncidentAnalysis Analysis);