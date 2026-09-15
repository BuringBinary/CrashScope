using CrashScope.Core.Monitoring;

namespace CrashScope.Service;

public sealed class SessionRecorderWorker : BackgroundService
{
    private readonly ILogger<SessionRecorderWorker> _logger;
    private readonly string _root;
    private readonly string _stateDirectory;
    private readonly string _sessionsRoot;
    private readonly string _incidentsRoot;

    public SessionRecorderWorker(ILogger<SessionRecorderWorker> logger)
    {
        _logger = logger;
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CrashScope");
        _stateDirectory = Path.Combine(_root, "state");
        _sessionsRoot = Path.Combine(_root, "sessions");
        _incidentsRoot = Path.Combine(_root, "incidents");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CrashScope Service starting at {Time}", DateTimeOffset.Now);

        Directory.CreateDirectory(_stateDirectory);
        Directory.CreateDirectory(_sessionsRoot);
        Directory.CreateDirectory(_incidentsRoot);

        var sessionDirectory = Path.Combine(_sessionsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        using var engine = new RecorderEngine(sessionDirectory, _stateDirectory, _incidentsRoot);

        engine.StatusChanged += () =>
        {
            var session = engine.CurrentSession;
            if (session is not null)
                _logger.LogDebug("Heartbeat: session {SessionId}, samples {Count}",
                    session.SessionId, engine.SampleCount);
        };

        var previousIncident = engine.CheckPreviousUncleanSession();
        if (previousIncident is not null)
        {
            var leading = previousIncident.Hypotheses.FirstOrDefault(h => h.Score > 0);
            _logger.LogWarning(
                "Previous unclean session detected. Mode: {Mode}, Confidence: {Confidence:P0}",
                leading?.Mode.ToString() ?? "Unclassified",
                leading?.Confidence ?? 0);
        }

        engine.Start();
        _logger.LogInformation("Recording started. Session: {SessionId}, Directory: {Dir}",
            engine.CurrentSession?.SessionId, sessionDirectory);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CrashScope Service stopping at {Time}", DateTimeOffset.Now);
        }
        finally
        {
            engine.Stop();
            _logger.LogInformation("Recording stopped. Total samples: {Count}", engine.SampleCount);
        }
    }
}