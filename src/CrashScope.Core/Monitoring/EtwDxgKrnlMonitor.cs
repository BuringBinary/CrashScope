using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace CrashScope.Core.Monitoring;

public sealed class EtwDxgKrnlMonitor : IDisposable
{
    private static readonly Guid DxgKrnlProviderGuid = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private readonly EtwCoverageTracker _coverage = new("Microsoft-Windows-DxgKrnl", DxgKrnlProviderGuid);
    private EtwEventSink<EtwDxgKrnlEvent>? _sink;
    private TraceEventSession? _session;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private bool _started;
    private volatile bool _running;
    public bool IsRunning { get => _running; private set => _running = value; }
    public string? LastError => Coverage.LastError;
    public EtwCoverageSnapshot Coverage
    {
        get { RefreshOsLoss(); return _coverage.Snapshot(); }
    }
    public int BufferedEventCount => _sink?.Count ?? 0;
    public int DroppedEventCount => (int)Math.Min(int.MaxValue, Coverage.BufferDroppedEvents);
    public event Action<EtwDxgKrnlEvent>? EventReceived;

    public void Start(string sessionDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;
        if (_started) throw new InvalidOperationException("Create a new monitor for a new recording session.");
        _started = true;
        try
        {
            var writer = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "etw-gpu.jsonl"));
            _sink = new(_coverage, e => writer.Write(e), writer);
            _cts = new CancellationTokenSource();
            var session = _session = new TraceEventSession(
                $"CrashScope-GpuKernel-{Environment.ProcessId}-{Guid.NewGuid():N}", null) { StopOnDispose = true };
            session.EnableProvider(DxgKrnlProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);
            _coverage.Enabled();
            session.Source.Dynamic.All += OnEvent;
            IsRunning = true;
            _coverage.Processing(true);
            var token = _cts.Token;
            _processingTask = Task.Run(() =>
            {
                try
                {
                    session.Source.Process();
                    if (!token.IsCancellationRequested)
                        _coverage.Error("Processing ended", new InvalidOperationException("ETW processing ended unexpectedly."));
                }
                catch (Exception ex) { if (!token.IsCancellationRequested) _coverage.Error("Processing failed", ex); }
                finally { IsRunning = false; _coverage.Processing(false); }
            });
        }
        catch (Exception ex)
        {
            _coverage.Error("Start failed", ex);
            Stop();
            throw;
        }
    }

    public IReadOnlyList<EtwDxgKrnlEvent> DrainBuffer() => _sink?.Drain() ?? [];

    private void OnEvent(TraceEvent e)
    {
        if (_disposed || !IsRunning || e.ProviderGuid != DxgKrnlProviderGuid) return;
        var at = new DateTimeOffset(e.TimeStamp);
        _coverage.Received(at); // Count before filtering, including events not interesting to diagnosis.
        if (!IsInteresting(e.TaskName, (int)e.ID)) return;
        var item = new EtwDxgKrnlEvent(e.TimeStamp, (int)e.ID, e.TaskName, e.ProcessID, e.ProcessName, BuildDetail(e));
        _sink?.Record(item, at);
        try { EventReceived?.Invoke(item); } catch { }
    }

    public void Stop()
    {
        _cts?.Cancel();
        // Join the processing task before closing its writer. Never mark clean while a callback can still write.
        try { if (_processingTask is not null) _session?.Source.StopProcessing(); } catch (Exception ex) { _coverage.Error("Stop processing failed", ex); }
        RefreshOsLoss();
        try { _session?.Dispose(); } catch (Exception ex) { _coverage.Error("Session disposal failed", ex); }
        _processingTask?.GetAwaiter().GetResult();
        _session = null;
        _processingTask = null;
        IsRunning = false;
        _coverage.Stopped();
        _sink?.Dispose();
        _cts?.Dispose();
        _cts = null;
    }

    private void RefreshOsLoss()
    {
        try { if (_session is not null) _coverage.OsLost(_session.EventsLost); }
        catch { }
    }

    private static bool IsInteresting(string taskName, int eventId)
    {
        if (string.IsNullOrEmpty(taskName))
            // Numeric event ids captured when TraceEvent cannot resolve a task
            // name. Names below were resolved against the Microsoft-Windows-
            // DxgKrnl provider manifest on this OS build (wevtutil gp
            // Microsoft-Windows-DxgKrnl /ge:true /gm:true, probed 2026-09-17):
            //   1   HighPagingLevel         2   ExcessiveMemoryTransfer
            //   3   (no task, level 0)      250 NodeMetadata
            //   253 RuntimePowerManagement (message: PowerPStateComponent)
            //   400 GpuPartitioning         497 SetBaseDesktopDuration
            //   500 CancelPresentAtFlips
            // Ids 4-10, 600, 700 and 800 are absent from this build's manifest
            // and stay whitelisted for other dxgkrnl builds where they exist.
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
    }
}