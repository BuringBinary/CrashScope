using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CrashScope.Core.Monitoring;

public sealed class EtwDxgKrnlMonitor : IDisposable
{
    private static readonly Guid DxgKrnlProviderGuid = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    private readonly ConcurrentQueue<EtwDxgKrnlEvent> _buffer = new();
    private CrashSafeJsonlWriter? _writer;
    private TraceEventSession? _session;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;

    public event Action<EtwDxgKrnlEvent>? EventReceived;

    public bool IsRunning { get; private set; }

    public int BufferedEventCount => _buffer.Count;

    public void Start(string sessionDirectory)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EtwDxgKrnlMonitor));
        if (IsRunning) return;

        _writer = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "etw-gpu.jsonl"));
        _cts = new CancellationTokenSource();

        _session = CreateSession();
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
                try
                {
                    System.Diagnostics.Trace.WriteLine($"[CrashScope:EtwDxgKrnlMonitor] session fault: {ex.Message}");
                }
                catch { }
            }
        }, token);
    }

    public IReadOnlyList<EtwDxgKrnlEvent> DrainBuffer()
    {
        var list = new List<EtwDxgKrnlEvent>();
        while (_buffer.TryDequeue(out var evt))
            list.Add(evt);
        return list;
    }

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

        FlushRemaining();
        _writer?.Dispose();
        _writer = null;

        IsRunning = false;
    }

    private void FlushRemaining()
    {
        var remaining = DrainBuffer();
        foreach (var evt in remaining)
            _writer?.Write(evt);
    }

    private void OnEvent(TraceEvent traceEvent)
    {
        if (_disposed || !IsRunning) return;

        var taskName = traceEvent.TaskName;
        if (!IsInteresting(taskName, (int)traceEvent.ID))
            return;

        var detail = BuildDetail(traceEvent);
        var etwEvent = new EtwDxgKrnlEvent(
            traceEvent.TimeStamp,
            (int)traceEvent.ID,
            taskName,
            traceEvent.ProcessID,
            traceEvent.ProcessName,
            detail);

        _buffer.Enqueue(etwEvent);

        try { _writer?.Write(etwEvent); } catch { }

        try { EventReceived?.Invoke(etwEvent); } catch { }
    }

    private static TraceEventSession CreateSession()
    {
        var sessionName = "CrashScope-GpuKernel";

        try { TraceEventSession.GetActiveSession(sessionName)?.Dispose(); } catch { }

        var session = new TraceEventSession(sessionName, null) { StopOnDispose = true };
        session.EnableProvider(DxgKrnlProviderGuid, TraceEventLevel.Verbose, 0xFFFFFFFFFFFFFFFF);
        return session;
    }

    private static bool IsInteresting(string taskName, int eventId)
    {
        if (string.IsNullOrEmpty(taskName))
            return eventId switch
            {
                >= 1 and <= 10 => true,
                250 or 253 or 400 or 497 or 500 or 600 or 700 or 800 => true,
                _ => false
            };

        return taskName switch
        {
            "TdrTimedOut" or "TdrPrepareRecovery" or "TdrRecoverFromTimeout" => true,
            "TdrResetFromTimeout" or "TdrEngineResetPrepare" or "TdrEngineReset" => true,
            "AdapterStateChange" or "DeviceStateChange" or "AdapterReset" => true,
            "PowerComponentStateChange" or "PowerComponentState" => true,
            "HwQueuePreempt" or "SubmitCommandBuffer" => true,
            _ => false
        };
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
        _writer?.Dispose();
    }
}