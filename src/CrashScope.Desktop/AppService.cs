using CrashScope.Core.Models;
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

    private RecorderEngine? _engine;

    public AppService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashScope");
        _stateDirectory = Path.Combine(_root, "state");
        _sessionsRoot = Path.Combine(_root, "sessions");
        _incidentsRoot = Path.Combine(_root, "incidents");
    }

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
    }

    public void StopRecording()
    {
        _engine?.Stop();
        _engine?.Dispose();
        _engine = null;
        StatusChanged?.Invoke();
    }

    public IncidentAnalysis? CheckPreviousUncleanSession()
    {
        if (_engine is not null)
            return _engine.CheckPreviousUncleanSession();

        var tempJournal = new SessionJournal(_stateDirectory, Path.Combine(_sessionsRoot, "temp-check"));
        try
        {
            if (tempJournal.PreviousState is { CleanShutdown: false } previous)
            {
                var incidentDirectory = Path.Combine(
                    _incidentsRoot,
                    $"{previous.LastHeartbeatAt:yyyyMMdd-HHmmss}-{previous.SessionId[..8]}");
                try
                {
                    return CrashScope.Core.Diagnostics.CrashEvidenceCollector.CollectForUncleanSession(previous, incidentDirectory);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
        finally
        {
            if (Directory.Exists(tempJournal.Current.SessionDirectory))
            {
                try { Directory.Delete(tempJournal.Current.SessionDirectory, recursive: true); } catch { }
            }
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
                var json = File.ReadAllText(analysisPath);
                var analysis = JsonSerializer.Deserialize<IncidentAnalysis>(json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (analysis is null) continue;

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