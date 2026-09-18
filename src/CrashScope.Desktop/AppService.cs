using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using System.Collections.Concurrent;
using CrashScope.Core.Monitoring;
using System.IO;
using System.Text.Json;

namespace CrashScope.Desktop;

public sealed class AppService : IDisposable
{
    private readonly string _root;
    private readonly string _stateDirectory;
    private readonly string _sessionsRoot;
    private readonly string _incidentsRoot;

    private readonly ConcurrentDictionary<string, (DateTime Modified, long Length, IncidentAnalysis Analysis)> _incidentCache = new();
    private RecorderEngine? _engine;
    private readonly object _lifecycleGate = new();

    public AppService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashScope");
        _stateDirectory = Path.Combine(_root, "state");
        _sessionsRoot = Path.Combine(_root, "sessions");
        _incidentsRoot = Path.Combine(_root, "incidents");
    }

    public RecorderState RecorderState => _engine?.State ?? RecorderState.Stopped;
    public IReadOnlyList<CollectorHealth> CollectorHealth => _engine?.CollectorHealth ?? [];
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
            _engine?.Dispose();
            Directory.CreateDirectory(_stateDirectory);
            Directory.CreateDirectory(_sessionsRoot);
            Directory.CreateDirectory(_incidentsRoot);

            var sessionDirectory = Path.Combine(_sessionsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            _engine = new RecorderEngine(sessionDirectory, _stateDirectory, _incidentsRoot);
            _engine.StatusChanged += () => StatusChanged?.Invoke();
            _engine.SensorUpdated += v => SensorUpdated?.Invoke(v);
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

    public IReadOnlyList<IncidentInfo> GetIncidents()
    {
        if (!Directory.Exists(_incidentsRoot))
            return Array.Empty<IncidentInfo>();

        var result = new List<IncidentInfo>();
        foreach (var dir in Directory.GetDirectories(_incidentsRoot))
        {
            var analysisPath = Path.Combine(dir, "analysis.json");
            if (!File.Exists(analysisPath)) continue;

            try
            {
                var info = new FileInfo(analysisPath);
                IncidentAnalysis analysis;
                if (_incidentCache.TryGetValue(analysisPath, out var cached)
                    && cached.Modified == info.LastWriteTimeUtc && cached.Length == info.Length)
                    analysis = cached.Analysis;
                else
                {
                    var stored = JsonSerializer.Deserialize<IncidentAnalysis>(File.ReadAllText(analysisPath),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (stored is null) continue;
                    analysis = IncidentAnalyzer.ReevaluateReadOnly(stored, dir);
                    _incidentCache[analysisPath] = (info.LastWriteTimeUtc, info.Length, analysis);
                }

                var leading = analysis.Hypotheses.FirstOrDefault(h => h.Score > 0);
                var dirName = Path.GetFileName(dir);
                result.Add(new IncidentInfo(
                    dirName,
                    dir,
                    analysis.PreviousSession.LastHeartbeatAt,
                    leading?.Mode.ToString() ?? "Unclassified",
                    leading?.Confidence ?? 0,
                    analysis.OpenQuestions,
                    analysis));
            }
            catch
            {
                // skip corrupted incidents
            }
        }

        result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return result;
    }

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