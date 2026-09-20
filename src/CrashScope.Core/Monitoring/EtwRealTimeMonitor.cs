using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace CrashScope.Core.Monitoring;

public sealed class EtwRealTimeMonitor : IDisposable
{
    private readonly ProviderConfig[] _providers;
    private readonly Dictionary<Guid, EtwCoverageTracker> _coverage;
    private readonly Dictionary<string, EtwEventSink<EtwProviderEvent>> _sinks = new(StringComparer.OrdinalIgnoreCase);
    private TraceEventSession? _session;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private bool _started;
    private volatile bool _running;
    public bool IsRunning { get => _running; private set => _running = value; }
    public IReadOnlyList<EtwCoverageSnapshot> Coverage
    {
        get
        {
            RefreshOsLoss();
            return _providers.Select(p => _coverage[p.ProviderGuid].Snapshot()).ToArray();
        }
    }
    public string? LastError => Coverage.FirstOrDefault(c => c.LastError is not null)?.LastError;
    public int DroppedEventCount => (int)Math.Min(int.MaxValue, Coverage.Sum(c => c.BufferDroppedEvents));
    public event Action<EtwProviderEvent>? EventReceived;

    public EtwRealTimeMonitor(params ProviderConfig[] providers)
    {
        if (providers.Length == 0 || providers.Select(p => p.ProviderGuid).Distinct().Count() != providers.Length
            || providers.Select(p => p.OutputFile).Distinct(StringComparer.OrdinalIgnoreCase).Count() != providers.Length)
            throw new ArgumentException("Configure at least one provider, with unique provider IDs and output files.");
        _providers = providers.ToArray();
        _coverage = providers.ToDictionary(p => p.ProviderGuid, p => new EtwCoverageTracker(p.ProviderName, p.ProviderGuid));
    }

    public void Start(string sessionDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;
        if (_started) throw new InvalidOperationException("Create a new monitor for a new recording session.");
        _started = true;
        try
        {
            foreach (var cfg in _providers)
            {
                var writer = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, cfg.OutputFile));
                _sinks[cfg.OutputFile] = new(_coverage[cfg.ProviderGuid], e => writer.Write(e), writer);
            }
            _cts = new CancellationTokenSource();
            var session = _session = new TraceEventSession(
                $"CrashScope-SystemRealtime-{Environment.ProcessId}-{Guid.NewGuid():N}", null) { StopOnDispose = true };
            foreach (var cfg in _providers)
            {
                try
                {
                    session.EnableProvider(cfg.ProviderGuid, TraceEventLevel.Verbose, cfg.MatchAnyKeyword);
                    _coverage[cfg.ProviderGuid].Enabled();
                }
                catch (Exception ex) { _coverage[cfg.ProviderGuid].Error("Provider enable failed", ex); }
            }
            if (!Coverage.Any(c => c.Enabled)) { Stop(); return; }
            session.Source.Dynamic.All += OnEvent;
            IsRunning = true;
            foreach (var c in _coverage.Values.Where(c => c.Snapshot().Enabled)) c.Processing(true);
            var token = _cts.Token;
            _processingTask = Task.Run(() =>
            {
                try
                {
                    session.Source.Process();
                    if (!token.IsCancellationRequested)
                        ProcessingError(new InvalidOperationException("ETW processing ended unexpectedly."));
                }
                catch (Exception ex) { if (!token.IsCancellationRequested) ProcessingError(ex); }
                finally
                {
                    IsRunning = false;
                    foreach (var c in _coverage.Values) c.Processing(false);
                }
            });
        }
        catch (Exception ex)
        {
            foreach (var c in _coverage.Values) c.Error("Session start failed", ex);
            Stop();
            throw;
        }
    }

    private void ProcessingError(Exception ex)
    {
        foreach (var c in _coverage.Values.Where(c => c.Snapshot().Enabled)) c.Error("Processing failed", ex);
    }

    private void OnEvent(TraceEvent e)
    {
        if (_disposed || !IsRunning) return;
        // Provider names can be unresolved or localized; the manifest GUID is the stable identity.
        var cfg = _providers.FirstOrDefault(p => p.ProviderGuid == e.ProviderGuid);
        if (cfg is null) return;
        var at = new DateTimeOffset(e.TimeStamp);
        _coverage[cfg.ProviderGuid].Received(at);
        if (!cfg.IsInteresting(e)) return;
        var item = new EtwProviderEvent(cfg.ProviderName, e.TimeStamp, (int)e.ID, e.TaskName,
            e.Level == TraceEventLevel.Informational ? "Info" : e.Level.ToString(), e.ProcessID, e.ProcessName, BuildDetail(e));
        _sinks[cfg.OutputFile].Record(item, at);
        try { EventReceived?.Invoke(item); } catch { }
    }

    public IReadOnlyList<EtwProviderEvent> DrainBuffer(string outputFile)
        => _sinks.TryGetValue(outputFile, out var sink) ? sink.Drain() : [];
    public int BufferedEventCount(string outputFile) => _sinks.TryGetValue(outputFile, out var sink) ? sink.Count : 0;

    public void Stop()
    {
        _cts?.Cancel();
        try { if (_processingTask is not null) _session?.Source.StopProcessing(); } catch (Exception ex) { ProcessingError(ex); }
        RefreshOsLoss();
        try { _session?.Dispose(); } catch (Exception ex) { ProcessingError(ex); }
        _processingTask?.GetAwaiter().GetResult();
        _session = null;
        _processingTask = null;
        IsRunning = false;
        foreach (var c in _coverage.Values) c.Stopped();
        foreach (var sink in _sinks.Values) sink.Dispose();
        _cts?.Dispose();
        _cts = null;
    }

    private void RefreshOsLoss()
    {
        try
        {
            if (_session is null) return;
            var lost = _session.EventsLost;
            foreach (var coverage in _coverage.Values) coverage.OsLost(lost);
        }
        catch { }
    }

    private static string? BuildDetail(TraceEvent traceEvent)
    {
        try
        {
            var parts = new List<string>();
            foreach (var name in traceEvent.PayloadNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var value = traceEvent.PayloadByName(name)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;
                var clean = value.Length > 128 ? value[..128] + "..." : value;
                parts.Add($"{name}={clean}");
            }
            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
public sealed record ProviderConfig(
    Guid ProviderGuid,
    string ProviderName,
    string OutputFile,
    Func<TraceEvent, bool> IsInteresting,
    ulong MatchAnyKeyword = 0xFFFFFFFFFFFFFFFF);