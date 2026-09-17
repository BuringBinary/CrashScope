using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;

namespace CrashScope.Core.Monitoring;

public sealed class EtwRealTimeMonitor : IDisposable
{
    /// <summary>
    /// Per-provider cap on the in-memory hand-off queue; the JSONL file is the durable
    /// record. See EtwDxgKrnlMonitor.MaxBufferedEvents for why this must be bounded.
    /// </summary>
    private const int MaxBufferedEventsPerProvider = 5000;

    private readonly List<ProviderConfig> _providers;
    private readonly Dictionary<string, CrashSafeJsonlWriter?> _writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConcurrentQueue<EtwProviderEvent>> _buffers = new(StringComparer.OrdinalIgnoreCase);

    private TraceEventSession? _session;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;

    private volatile bool _disposed;
    private int _droppedEvents;

    public bool IsRunning { get; private set; }

    /// <summary>Events dropped from the in-memory queues because a provider hit its cap.</summary>
    public int DroppedEventCount => Volatile.Read(ref _droppedEvents);

    public event Action<EtwProviderEvent>? EventReceived;

    public EtwRealTimeMonitor(params ProviderConfig[] providers)
    {
        if (providers.Length == 0)
            throw new ArgumentException("At least one provider must be configured.");

        _providers = providers.ToList();
    }

    public void Start(string sessionDirectory)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EtwRealTimeMonitor));
        if (IsRunning) return;

        foreach (var cfg in _providers)
        {
            var filePath = Path.Combine(sessionDirectory, cfg.OutputFile);
            _writers[cfg.OutputFile] = new CrashSafeJsonlWriter(filePath);
            _buffers[cfg.OutputFile] = new ConcurrentQueue<EtwProviderEvent>();
        }

        _cts = new CancellationTokenSource();
        // Unique per recorder instance so concurrent hosts (service + desktop)
        // never dispose each other's live ETW sessions.
        _session = CreateSession($"CrashScope-SystemRealtime-{Environment.ProcessId}-{Path.GetFileName(sessionDirectory)}");
        _session.Source.Dynamic.All += OnEvent;

        IsRunning = true;
        var token = _cts.Token;

        _processingTask = Task.Run(() =>
        {
            try
            {
                _session.Source.Process();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                try { System.Diagnostics.Trace.WriteLine($"[CrashScope:EtwRTM] session fault: {ex.Message}"); }
                catch { }
            }
        }, token);
    }

    public IReadOnlyList<EtwProviderEvent> DrainBuffer(string outputFile)
    {
        if (!_buffers.TryGetValue(outputFile, out var buffer))
            return [];

        var list = new List<EtwProviderEvent>();
        while (buffer.TryDequeue(out var evt))
            list.Add(evt);
        return list;
    }

    public int BufferedEventCount(string outputFile)
        => _buffers.TryGetValue(outputFile, out var b) ? b.Count : 0;

    public void Stop()
    {
        if (!IsRunning) return;

        try { _cts?.Cancel(); } catch { }

        try { _session?.Dispose(); } catch { }
        _session = null;

        try
        {
            if (_processingTask is not null && !_processingTask.IsCompleted)
                _processingTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException) { }

        FlushAll();
        foreach (var w in _writers.Values)
            w?.Dispose();
        _writers.Clear();
        _buffers.Clear();

        IsRunning = false;
    }

    private void FlushAll()
    {
        foreach (var (file, buffer) in _buffers)
        {
            if (!_writers.TryGetValue(file, out var writer) || writer is null)
                continue;
            while (buffer.TryDequeue(out var evt))
                writer.Write(evt);
        }
    }

    private void OnEvent(TraceEvent traceEvent)
    {
        if (_disposed || !IsRunning) return;

        var providerName = traceEvent.ProviderName ?? "";
        var cfg = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        if (cfg is null) return;
        if (!cfg.IsInteresting(traceEvent)) return;

        var level = traceEvent.Level switch
        {
            TraceEventLevel.Critical => "Critical",
            TraceEventLevel.Error => "Error",
            TraceEventLevel.Warning => "Warning",
            TraceEventLevel.Informational => "Info",
            TraceEventLevel.Verbose => "Verbose",
            _ => null
        };

        var etwEvent = new EtwProviderEvent(
            providerName,
            traceEvent.TimeStamp,
            (int)traceEvent.ID,
            traceEvent.TaskName,
            level,
            traceEvent.ProcessID,
            traceEvent.ProcessName,
            BuildDetail(traceEvent));

        if (_writers.TryGetValue(cfg.OutputFile, out var writer) && writer is not null)
        {
            try { writer.Write(etwEvent); } catch { }
        }
        if (_buffers.TryGetValue(cfg.OutputFile, out var buffer))
        {
            buffer.Enqueue(etwEvent);
            while (buffer.Count > MaxBufferedEventsPerProvider && buffer.TryDequeue(out _))
                Interlocked.Increment(ref _droppedEvents);
        }

        try { EventReceived?.Invoke(etwEvent); } catch { }
    }

    private TraceEventSession CreateSession(string sessionName)
    {
        try { TraceEventSession.GetActiveSession(sessionName)?.Dispose(); } catch { }

        var session = new TraceEventSession(sessionName, null) { StopOnDispose = true };

        foreach (var cfg in _providers)
        {
            try
            {
                session.EnableProvider(cfg.ProviderGuid, TraceEventLevel.Verbose, cfg.MatchAnyKeyword);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[CrashScope:ETW] Failed to enable {cfg.ProviderName}: {ex.Message}");
            }
        }

        return session;
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
        _cts?.Dispose();
        _session?.Dispose();
        foreach (var w in _writers.Values)
            w?.Dispose();
    }
}

public sealed record ProviderConfig(
    Guid ProviderGuid,
    string ProviderName,
    string OutputFile,
    Func<TraceEvent, bool> IsInteresting,
    ulong MatchAnyKeyword = 0xFFFFFFFFFFFFFFFF);