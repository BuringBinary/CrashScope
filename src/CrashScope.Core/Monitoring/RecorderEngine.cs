using CrashScope.Core.Diagnostics;
using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using CrashScope.Core.Platform;
using CrashScope.Core.Analysis;
using System.Text.Json;

namespace CrashScope.Core.Monitoring;

public sealed class RecorderEngine : IDisposable
{
    private readonly string _sessionDirectoryPrefix;
    private string _sessionDirectory;
    private readonly RecorderRuntimeHooks? _hooks;
    private volatile DataRootLease? _owner;
    private Task? _stopTask;
    private volatile RecorderState _state = RecorderState.Stopped;
    private long _lastSampleTicks;
    private readonly string _stateDirectory;
    private readonly string _incidentsRoot;
    private readonly RecorderOptions _options;

    private CollectorHealthTracker? _collectorHealth;
    private HardwareMonitor? _hardware;
    private SessionJournal? _journal;
    private ProcessTracker? _processTracker;
    private GpuEngineTracker? _gpuEngineTracker;
    private DisplayTopologyTracker? _displayTracker;
    private PowerTracker? _powerTracker;
    private StorageTracker? _storageTracker;
    private GpuMemoryTracker? _gpuMemoryTracker;
    private MemoryPressureTracker? _memoryPressureTracker;
    private DwmStateTracker? _dwmTracker;
    private ResourceExhaustionEventTracker? _resourceExhaustionTracker;
    private SelfOverheadTracker? _selfOverheadTracker;
    private PageFileTracker? _pageFileTracker;
    private CpuFallbackTracker? _cpuFallbackTracker;
    private EtwDxgKrnlMonitor? _etwMonitor;
    private EtwRealTimeMonitor? _etwSystemMonitor;

    private CrashSafeJsonlWriter? _telemetryWriter;
    private CrashSafeJsonlWriter? _foregroundWriter;
    private CrashSafeJsonlWriter? _processEventWriter;
    private CrashSafeJsonlWriter? _processSnapshotWriter;
    private CrashSafeJsonlWriter? _gpuEngineWriter;
    private CrashSafeJsonlWriter? _displayWriter;
    private CrashSafeJsonlWriter? _powerWriter;
    private CrashSafeJsonlWriter? _remoteSessionWriter;
    private CrashSafeJsonlWriter? _storageWriter;
    private CrashSafeJsonlWriter? _gpuMemoryWriter;
    private CrashSafeJsonlWriter? _memoryPressureWriter;
    private CrashSafeJsonlWriter? _dwmStateWriter;
    private CrashSafeJsonlWriter? _resourceExhaustionWriter;
    private CrashSafeJsonlWriter? _selfOverheadWriter;
    private CrashSafeJsonlWriter? _pageFileWriter;
    private CrashSafeJsonlWriter? _cpuFallbackWriter;
    private TickProfiler? _tickProfiler;

    private CancellationTokenSource? _cts;
    private Task? _recordingTask;

    private ForegroundState? _previousForeground;
    private int _processPollCounter;
    private int _processSnapshotCounter;
    private int _heartbeatCounter;
    private int _displayCounter;
    private int _powerCounter;
    private int _etwDrainCounter;
    private int _retentionCounter;
    private int _selfOverheadCounter;
    private int _pageFileCounter;
    private CommitPressureLevel _commitPressureLevel;
    private bool _etwStoppedForPressure;

    /// <summary>
    /// Set by Stop() before anything is disposed so an in-flight Tick() bails out instead
    /// of writing through disposed writers or dereferencing nulled trackers.
    /// </summary>
    private volatile bool _stopping;

    private const int RetentionIntervalSeconds = 86400;

    private readonly object _lock = new();

    public RecorderEngine(
        string sessionDirectory,
        string stateDirectory,
        string incidentsRoot,
        RecorderOptions? options = null)
    {
        _sessionDirectoryPrefix = Path.GetFullPath(sessionDirectory);
        _sessionDirectory = _sessionDirectoryPrefix;
        _stateDirectory = stateDirectory;
        _incidentsRoot = incidentsRoot;
        _options = options ?? new RecorderOptions();
        _options.Validate();
    }

    // Injection seam for deterministic lifecycle tests; production always uses the real collectors.
    internal RecorderEngine(string sessionDirectory, string stateDirectory, string incidentsRoot, RecorderRuntimeHooks hooks)
        : this(sessionDirectory, stateDirectory, incidentsRoot, options: null) => _hooks = hooks;

    public IReadOnlyList<CollectorHealth> CollectorHealth => _collectorHealth?.Snapshot() ?? [];
    public RecorderState State => _state;
    public bool IsRecording => _state == RecorderState.Recording;
    public bool HasActiveResources => _owner is not null;
    public string? LastError { get; private set; }
    public string? RecoveryError { get; private set; }
    public DateTimeOffset? LastSampleAt => Interlocked.Read(ref _lastSampleTicks) is var ticks && ticks > 0
        ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    public SessionState? CurrentSession => _journal?.Current;
    public SessionState? PreviousSession => _journal?.PreviousState;
    public int SampleCount { get; private set; }
    public IReadOnlyList<SensorCatalogEntry> SensorCatalog => _hardware?.Catalog ?? Array.Empty<SensorCatalogEntry>();

    /// <summary>
    /// The GPU adapter that reported GPU facts come from, or null when no GPU sensors exist.
    /// Exposed so hosts can surface it: on a hybrid machine the iGPU is enumerated first and
    /// silently reporting its load instead of the discrete card misleads the reader.
    /// </summary>
    public string? PrimaryGpuHardwareName => SensorIndices.PrimaryGpuHardwareName(SensorCatalog);

    public event Action? StatusChanged;
    public event Action<IReadOnlyList<float?>>? SensorUpdated;
    public event Action<ForegroundEvent>? ForegroundChanged;
    public event Action<ProcessEvent>? ProcessEventReceived;
    public event Action<GpuEngineSample>? GpuEngineUpdated;
    public event Action<DisplayTopologyEvent>? DisplayChanged;
    public event Action<PowerSchemeEvent>? PowerSchemeChanged;
    public event Action<RemoteSessionEvent>? RemoteSessionChanged;
    public event Action<StorageSample>? StorageUpdated;
    public event Action<GpuMemorySample>? GpuMemoryUpdated;
    public event Action<MemoryPressureSample>? MemoryPressureUpdated;
    public event Action<DwmStateEvent>? DwmStateChanged;
    public event Action<ResourceExhaustionEvent>? ResourceExhaustionObserved;
    public event Action<EtwDxgKrnlEvent>? EtwEventReceived;
    public event Action<EtwProviderEvent>? EtwSystemEventReceived;

    public void Start()
    {
        lock (_lock)
        {
            if (IsRecording) return;
            if (_owner is not null || _stopTask is { IsCompleted: false })
                throw new InvalidOperationException("The previous recording is stopping or faulted. Stop it fully before starting again.");

            _owner = DataRootLease.Acquire(_stateDirectory);
            _state = RecorderState.Starting;
            LastError = null;
            RecoveryError = null;
            _journal = null;
            _collectorHealth = null;
            SampleCount = 0;
            Interlocked.Exchange(ref _lastSampleTicks, 0);
            try
            {
                // Preserve first; recovery failure must not destroy the pending evidence.
                SessionRecovery.Preserve(_stateDirectory, SessionJournal.ReadPrevious(_stateDirectory));
                try { SessionRecovery.RecoverOwned(_stateDirectory, _incidentsRoot, _hooks?.Recover); }
                catch (Exception ex) { RecoveryError = ex.Message; }

                // A fresh identity and directory for every Start, including RPC restarts.
                _sessionDirectory = _sessionDirectoryPrefix + "-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(_sessionDirectory);
                _journal = new SessionJournal(_stateDirectory, _sessionDirectory);
                if (_hooks is not null) _hooks.Initialize();
                else InitializeCollectors();

                _previousForeground = null;
                _processPollCounter = _processSnapshotCounter = _heartbeatCounter = 0;
                _displayCounter = _powerCounter = _etwDrainCounter = _retentionCounter = _selfOverheadCounter = _pageFileCounter = 0;
                _commitPressureLevel = CommitPressureLevel.Normal;
                _etwStoppedForPressure = false;
                _stopping = false;
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _state = RecorderState.Recording;
                _recordingTask = Task.Run(() => RunLoopAsync(token));
            }
            catch (Exception ex)
            {
                ReportFault(ex);
                CleanupResources();
                _owner.Dispose();
                _owner = null;
                Notify(StatusChanged);
                throw;
            }
        }
        Notify(StatusChanged);
    }

    private void InitializeCollectors()
    {
        _collectorHealth = new CollectorHealthTracker(Path.Combine(_sessionDirectory, "collector-health.json"));
        _hardware = new HardwareMonitor();
        _collectorHealth.Set("Sensors", _hardware.Catalog.Count > 0 ? "AwaitingSample" : "Unavailable",
            _hardware.Catalog.Count > 0 ? "Catalog loaded; waiting for readings." : "No supported sensors detected.");

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
        _storageWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "storage.jsonl"));
        _gpuMemoryWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "gpu-memory.jsonl"));
        _memoryPressureWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "memory-pressure.jsonl"));
        _dwmStateWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "dwm-state.jsonl"));
        _resourceExhaustionWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "resource-exhaustion.jsonl"));
        _selfOverheadWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "collector-overhead.jsonl"));
        _pageFileWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "pagefile.jsonl"));
        _cpuFallbackWriter = new CrashSafeJsonlWriter(Path.Combine(_sessionDirectory, "cpu-fallback.jsonl"));
        _tickProfiler = new TickProfiler(Path.Combine(_sessionDirectory, "tick-timing.jsonl"),
            (c, st, d) => _collectorHealth?.Set(c, st, d));

        _processTracker = new ProcessTracker();
        _gpuEngineTracker = new GpuEngineTracker();
        _collectorHealth.Set("GPU Engines", _gpuEngineTracker.IsAvailable ? "AwaitingSample" : "Unavailable",
            _gpuEngineTracker.IsAvailable ? "Waiting for the first GPU engine heartbeat." : "GPU Engine counter category is unavailable.");
        _displayTracker = new DisplayTopologyTracker();
        _powerTracker = new PowerTracker();
        _storageTracker = new StorageTracker();
        _collectorHealth.Set("Storage", _storageTracker.IsAvailable ? "AwaitingSample" : "Unavailable",
            _storageTracker.IsAvailable ? "Waiting for disk metrics." : "Storage collector is unavailable.");
        _gpuMemoryTracker = new GpuMemoryTracker();
        _memoryPressureTracker = new MemoryPressureTracker();
        _dwmTracker = new DwmStateTracker();
        var dwmBaseline = _dwmTracker.Poll(DateTimeOffset.Now);
        _collectorHealth.Set("DWM", _dwmTracker.HealthStatus, _dwmTracker.HealthDetail);
        if (dwmBaseline is not null)
            _dwmStateWriter.Write(dwmBaseline);
        _resourceExhaustionTracker = new ResourceExhaustionEventTracker();
        _selfOverheadTracker = new SelfOverheadTracker();
        _pageFileTracker = new PageFileTracker();
        var pageFileBaseline = _pageFileTracker.Capture(DateTimeOffset.Now, commitLimitBytes: null, "Baseline");
        _pageFileWriter!.Write(pageFileBaseline);
        _collectorHealth.Set("Page File", _pageFileTracker.HealthStatus, _pageFileTracker.HealthDetail);
        _cpuFallbackTracker = new CpuFallbackTracker();
        _collectorHealth.Set("CPU Fallback", _cpuFallbackTracker.HealthStatus, _cpuFallbackTracker.HealthDetail);
        _resourceExhaustionTracker.Start();
        RefreshResourceExhaustionHealth();

        var elevated = _journal?.Current.Elevated == true;
        _collectorHealth.Set("Permission Preflight", elevated ? "Elevated" : "StandardUser",
            elevated ? "Administrator token is active; privileged ETW may be attempted."
                : "Standard token: privileged System ETW is disabled before provider startup; core counters and event-log watchers remain available.");

        if (_options.EnableDxgKrnlEtw)
        {
            _etwMonitor = new EtwDxgKrnlMonitor();
            try
            {
                _etwMonitor.Start(_sessionDirectory);
                RefreshEtwHealth();
            }
            catch (Exception ex)
            {
                RefreshEtwHealth();
                System.Diagnostics.Trace.WriteLine($"[CrashScope] ETW GPU session start failed: {ex.Message}");
            }
        }
        else
        {
            _collectorHealth.Set("ETW GPU", "Disabled",
                "Disabled by the safe default. EnableDxgKrnlEtw is an explicit diagnostic opt-in.");
        }

        var kernelPowerCfg = new ProviderConfig(
            ProviderGuid: new Guid("331C3B3A-2005-44C2-AC5E-77220C37D6B4"),
            ProviderName: "Microsoft-Windows-Kernel-Power",
            OutputFile: "etw-kernel-power.jsonl",
            IsInteresting: traceEvent =>
                traceEvent.TaskName is "SuspendStart" or "SuspendEnd" or "ResumeStart" or "ResumeEnd"
                || ((int)traceEvent.ID >= 10 && (int)traceEvent.ID <= 30)
                || traceEvent.TaskName.Contains("Thermal", StringComparison.OrdinalIgnoreCase)
                || traceEvent.TaskName.Contains("Power", StringComparison.OrdinalIgnoreCase));
        var wheaCfg = new ProviderConfig(
            ProviderGuid: new Guid("C26C4F3C-3F66-4E99-8F8A-39405CFED220"),
            ProviderName: "Microsoft-Windows-WHEA-Logger",
            OutputFile: "etw-whea.jsonl",
            IsInteresting: _ => true);
        if (_options.EnableSystemEtw && elevated)
        {
            _etwSystemMonitor = new EtwRealTimeMonitor(kernelPowerCfg, wheaCfg);
            try
            {
                _etwSystemMonitor.Start(_sessionDirectory);
                RefreshEtwHealth();
            }
            catch (Exception ex)
            {
                RefreshEtwHealth();
                System.Diagnostics.Trace.WriteLine($"[CrashScope] ETW system session start failed: {ex.Message}");
            }
        }
        else
        {
            var status = _options.EnableSystemEtw ? "PermissionRequired" : "Disabled";
            var detail = _options.EnableSystemEtw
                ? "Not started: administrator elevation is required for the real-time System ETW session."
                : "Disabled by RecorderOptions.EnableSystemEtw.";
            _collectorHealth.Set("ETW Power", status, detail);
            _collectorHealth.Set("ETW WHEA", status, detail);
        }
    }

    private void RefreshEtwHealth()
    {
        void Set(string name, EtwCoverageSnapshot coverage) => _collectorHealth?.Set(name,
            _etwStoppedForPressure ? "StoppedByPressure" : EtwCoverageTracker.Status(coverage),
            (_etwStoppedForPressure ? "Stopped after system commit crossed the throttle threshold. " : "")
                + EtwCoverageTracker.Detail(coverage), coverage);
        if (_etwMonitor is not null) Set("ETW GPU", _etwMonitor.Coverage);
        if (_etwSystemMonitor is not null)
            foreach (var coverage in _etwSystemMonitor.Coverage)
                Set(coverage.Provider == "Microsoft-Windows-Kernel-Power" ? "ETW Power" : "ETW WHEA", coverage);
    }

    private void RefreshResourceExhaustionHealth()
    {
        if (_resourceExhaustionTracker is null) return;
        var error = _resourceExhaustionTracker.LastError;
        _collectorHealth?.Set("Resource Exhaustion 2004", error is not null ? "Degraded" :
            _resourceExhaustionTracker.IsRunning ? "Watching" : "Stopped",
            error ?? "Watching the System event log for Resource-Exhaustion-Detector event 2004.");
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_hooks?.Interval ?? TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (_hooks is null) Tick(token);
                else
                {
                    _hooks.Tick(token);
                    SampleCount++;
                    Interlocked.Exchange(ref _lastSampleTicks, DateTimeOffset.UtcNow.Ticks);
                    _journal!.Heartbeat(DateTimeOffset.Now);
                    Notify(SensorUpdated, (IReadOnlyList<float?>)Array.Empty<float?>());
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Fail closed, visibly. Do not spin on a full disk or claim the recorder is healthy.
            ReportFault(ex);
            Notify(StatusChanged);
        }
    }

    private void ReportFault(Exception ex)
    {
        LastError = $"{ex.GetType().Name}: {ex.Message}";
        _state = RecorderState.Faulted;
        try { _journal?.MarkFault(LastError); }
        catch (Exception persistenceError)
        {
            LastError += $" (could not persist recorder fault: {persistenceError.Message})";
        }
        try { System.Diagnostics.Trace.WriteLine($"[CrashScope] recorder fault: {LastError}"); } catch { }
    }

    // Observers must not be able to terminate the acquisition loop. UI observers must marshal asynchronously.
    private static void Notify(Action? handlers)
    {
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch (Exception ex) { TraceObserverFailure(ex); }
    }

    private static void Notify<T>(Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (Action<T> handler in handlers.GetInvocationList())
            try { handler(value); } catch (Exception ex) { TraceObserverFailure(ex); }
    }

    private static void TraceObserverFailure(Exception ex)
    {
        try { System.Diagnostics.Trace.WriteLine($"[CrashScope] observer failed: {ex.Message}"); } catch { }
    }

    private void Tick(CancellationToken token)
    {
        if (_stopping)
            return;

        token.ThrowIfCancellationRequested();
        var now = DateTimeOffset.Now;
        var profiler = _tickProfiler;
        var gap = profiler?.BeginTick();
        try
        {
            TickCore(token, now, profiler);
        }
        finally
        {
            profiler?.EndTick(now, gap);
        }
    }

    // Each stage is wrapped so a blocked synchronous call (WMI, PDH, process enumeration, disk flush)
    // is attributable in tick-timing.jsonl. Keep stage names stable: they are grep targets.
    private static TickProfiler.StageScope? Stage(TickProfiler? p, string name) => p?.Stage(name);

    private static T Timed<T>(TickProfiler? p, string name, Func<T> action)
    {
        if (p is null) return action();
        using (p.Stage(name)) return action();
    }

    private void TickCore(CancellationToken token, DateTimeOffset now, TickProfiler? p)
    {

        var memPressure = Timed(p, "memory-pressure", () => _memoryPressureTracker!.Capture(now));
        if (memPressure is not null)
        {
            _memoryPressureWriter!.Write(memPressure);
            Notify(MemoryPressureUpdated, memPressure);
            ApplyCommitPressure(memPressure);
            var limitChange = _pageFileTracker!.ObserveCommitLimit(now, memPressure.CommitLimitBytes);
            if (limitChange is not null)
                _pageFileWriter!.Write(limitChange);
        }
        var criticalPressure = _commitPressureLevel == CommitPressureLevel.Critical;
        var throttled = _commitPressureLevel == CommitPressureLevel.Throttled;

        var cpuFallback = Timed(p, "cpu-fallback", () => criticalPressure ? new CpuFallbackSample(now, null, "WindowsPdhProcessorTotal", "SuppressedByPressure", "CPU fallback polling is intentionally suspended at critical commit pressure.") : _cpuFallbackTracker!.Capture(now));
        using (Stage(p, "write:cpu-fallback")) _cpuFallbackWriter!.Write(cpuFallback);
        _collectorHealth?.Set("CPU Fallback", cpuFallback.Status, cpuFallback.Error ?? _cpuFallbackTracker!.HealthDetail);

        var foreground = Timed(p, "foreground", () => WindowsActivity.CaptureForeground());
        var sensorValues = Timed(p, "sensors", () => criticalPressure ? _hardware!.CaptureGpuTemperatureValues() : _hardware!.CaptureValues());
        if (criticalPressure)
        {
            _collectorHealth?.Set("Sensors", "Throttled", "Critical commit pressure: only GPU temperature readings are retained.");
            _collectorHealth?.Set("CPU", "Throttled", "Critical commit pressure: CPU sensor polling is suspended.");
        }
        else
        {
            var suspect = SensorValueQuality.HasSuspectReadings(SensorCatalog, sensorValues);
            _collectorHealth?.Set("Sensors", suspect ? "Degraded" : sensorValues.Any(v => v.HasValue) ? "Collecting" : "NoData",
                suspect ? "Some CPU readings are zero/non-finite and suspect; shown as unavailable. Raw telemetry is retained. Check permissions/driver support."
                    : sensorValues.Any(v => v.HasValue) ? "Sensor readings received; unsupported values remain unavailable." : "No sensor readings available.");
            var cpuHealth = CpuSensorAssessment.Assess(SensorCatalog, sensorValues);
            if (cpuHealth.Status != "Collecting" && cpuFallback.TotalUtilizationPercent.HasValue)
                _collectorHealth?.Set("CPU", "FallbackActive",
                    cpuHealth.Detail + $" Windows PDH fallback total utilization={cpuFallback.TotalUtilizationPercent:F1}%.");
            else
                _collectorHealth?.Set("CPU", cpuHealth.Status, cpuHealth.Detail);
        }
        using (Stage(p, "write:telemetry")) _telemetryWriter!.Write(new TelemetrySnapshot(now, foreground, sensorValues));
        SampleCount++;
        Interlocked.Exchange(ref _lastSampleTicks, now.UtcTicks);
        // The first durable sample is enough to make this session recoverable (not five ticks later).
        if (SampleCount == 1) _journal!.Heartbeat(now);

        using (Stage(p, "notify:sensors")) Notify(SensorUpdated, sensorValues);

        if (_previousForeground is null ||
            _previousForeground.ProcessId != foreground.ProcessId ||
            !string.Equals(_previousForeground.ProcessName, foreground.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            var fe = new ForegroundEvent(now, foreground.ProcessId, foreground.ProcessName, foreground.IdleSeconds);
            _foregroundWriter!.Write(fe);
            Notify(ForegroundChanged, fe);
            _previousForeground = foreground;
        }

        foreach (var resourceEvent in _resourceExhaustionTracker?.Drain() ?? [])
        {
            _resourceExhaustionWriter!.Write(resourceEvent);
            Notify(ResourceExhaustionObserved, resourceEvent);
        }

        if (!criticalPressure && ++_processPollCounter >= (throttled ? 5 : 2))
        {
            _processPollCounter = 0;
            var processEvents = Timed(p, "process-poll", () => _processTracker!.Poll(now).ToList());
            using (Stage(p, "write:process-events"))
                foreach (var processEvent in processEvents)
                {
                    _processEventWriter!.Write(processEvent);
                    Notify(ProcessEventReceived, processEvent);
                }

            var engineSample = Timed(p, "gpu-engines", () => _gpuEngineTracker!.Capture(now));
            using (Stage(p, "write:gpu-engines")) _gpuEngineWriter!.Write(engineSample);
            _collectorHealth?.Set("GPU Engines", _gpuEngineTracker!.HealthStatus, _gpuEngineTracker.HealthDetail);
            Notify(GpuEngineUpdated, engineSample);
        }
        else if (criticalPressure && SampleCount % 5 == 0)
        {
            var suppressed = new GpuEngineSample(now, [], "SuppressedByPressure", 0,
                "GPU engine counters were intentionally not queried at critical commit pressure.");
            _gpuEngineWriter!.Write(suppressed);
            _collectorHealth?.Set("GPU Engines", suppressed.Status, suppressed.Error!);
            Notify(GpuEngineUpdated, suppressed);
        }

        if (!criticalPressure && ++_displayCounter >= (throttled ? 10 : 5))
        {
            _displayCounter = 0;
            var displayChange = Timed(p, "display", () => _displayTracker!.Poll(now));
            if (displayChange is not null)
            {
                _displayWriter!.Write(displayChange);
                Notify(DisplayChanged, displayChange);
            }

            var remoteChange = Timed(p, "remote-session", () => _powerTracker!.PollRemoteSession(now));
            if (remoteChange is not null)
            {
                _remoteSessionWriter!.Write(remoteChange);
                Notify(RemoteSessionChanged, remoteChange);
            }

            if (_storageTracker is not null)
            {
                var storageSample = Timed(p, "storage", () => _storageTracker.Capture(now));
                _collectorHealth?.Set("Storage", _storageTracker.HealthStatus, _storageTracker.HealthDetail);
                using (Stage(p, "write:storage")) _storageWriter!.Write(storageSample);
                Notify(StorageUpdated, storageSample);
            }

            if (_gpuMemoryTracker is { IsAvailable: true })
            {
                var gpuMemSample = Timed(p, "gpu-memory", () => _gpuMemoryTracker.Capture(now));
                if (gpuMemSample is not null)
                {
                    using (Stage(p, "write:gpu-memory")) _gpuMemoryWriter!.Write(gpuMemSample);
                    Notify(GpuMemoryUpdated, gpuMemSample);
                }
            }

            var dwmChange = Timed(p, "dwm", () => _dwmTracker!.Poll(now));
            _collectorHealth?.Set("DWM", _dwmTracker!.HealthStatus, _dwmTracker.HealthDetail);
            if (dwmChange is not null)
            {
                _dwmStateWriter!.Write(dwmChange);
                Notify(DwmStateChanged, dwmChange);
            }
        }

        if (!criticalPressure && ++_powerCounter >= (throttled ? 20 : 10))
        {
            _powerCounter = 0;
            var schemeChange = Timed(p, "power-scheme", () => _powerTracker!.PollScheme(now));
            if (schemeChange is not null)
            {
                _powerWriter!.Write(schemeChange);
                Notify(PowerSchemeChanged, schemeChange);
            }
        }

        if (!criticalPressure && ++_processSnapshotCounter >= 5)
        {
            _processSnapshotCounter = 0;
            var snapshot = Timed(p, "process-snapshot", () => _processTracker!.CaptureSnapshot(now));
            using (Stage(p, "write:process-snapshot")) _processSnapshotWriter!.Write(snapshot);
        }

        if (++_selfOverheadCounter >= 5)
        {
            _selfOverheadCounter = 0;
            var overhead = Timed(p, "self-overhead", () => _selfOverheadTracker!.Capture(now, CurrentEtwCoverage()));
            using (Stage(p, "write:self-overhead")) _selfOverheadWriter!.Write(overhead);
            var cpu = overhead.TotalCpuPercent is float value ? $"{value:F2}%" : "warming up";
            var maxEtwRate = overhead.Etw.Where(e => e.ReceivedPerSecond.HasValue)
                .Select(e => e.ReceivedPerSecond!.Value).DefaultIfEmpty(0).Max();
            _collectorHealth?.Set("Collector Overhead", "Collecting",
                $"hosts={overhead.Processes.Count}, cpu={cpu}, private={overhead.TotalPrivateBytes / 1048576.0:F1} MiB, " +
                $"workingSet={overhead.TotalWorkingSetBytes / 1048576.0:F1} MiB, maxEtwReceived={maxEtwRate:F1}/s.");
        }

        if (!criticalPressure && ++_pageFileCounter >= 30)
        {
            _pageFileCounter = 0;
            var pageFile = Timed(p, "pagefile", () => _pageFileTracker!.Capture(now, memPressure?.CommitLimitBytes, "Periodic"));
            _pageFileWriter!.Write(pageFile);
            _collectorHealth?.Set("Page File", _pageFileTracker!.HealthStatus, _pageFileTracker.HealthDetail);
        }

        if (++_heartbeatCounter >= 5)
        {
            _heartbeatCounter = 0;
            using (Stage(p, "heartbeat")) _journal!.Heartbeat(now);
            using (Stage(p, "etw-health")) { RefreshEtwHealth(); RefreshResourceExhaustionHealth(); }
            using (Stage(p, "notify:status")) Notify(StatusChanged);
        }

        if (++_retentionCounter >= RetentionIntervalSeconds)
        {
            _retentionCounter = 0;
            using (Stage(p, "retention")) EnforceRetention();
        }

        if (++_etwDrainCounter >= 3)
        {
            _etwDrainCounter = 0;
            try
                {
                    var etwEvents = _etwMonitor?.DrainBuffer() ?? [];
                    foreach (var e in etwEvents)
                        Notify(EtwEventReceived, e);
                }
                catch { }

                if (_etwSystemMonitor is not null)
                {
                    foreach (var file in new[] { "etw-kernel-power.jsonl", "etw-whea.jsonl" })
                    {
                        try
                        {
                            var sysEvents = _etwSystemMonitor.DrainBuffer(file);
                            foreach (var e in sysEvents)
                                Notify(EtwSystemEventReceived, e);
                        }
                        catch { }
                    }
                }
            }
    }

    private IReadOnlyList<EtwCoverageSnapshot> CurrentEtwCoverage()
    {
        var result = new List<EtwCoverageSnapshot>();
        if (_etwMonitor is not null) result.Add(_etwMonitor.Coverage);
        if (_etwSystemMonitor is not null) result.AddRange(_etwSystemMonitor.Coverage);
        return result;
    }

    private void ApplyCommitPressure(MemoryPressureSample sample)
    {
        _commitPressureLevel = CommitPressurePolicy.Classify(sample.CommittedPercent, _options);
        var status = _commitPressureLevel.ToString();
        var detail = $"Commit is {sample.CommittedPercent:F2}% ({sample.CommitBytes}/{sample.CommitLimitBytes} bytes). "
            + (_commitPressureLevel switch
            {
                CommitPressureLevel.Normal => "Normal collection policy.",
                CommitPressureLevel.Warning => "Warning threshold crossed; collection remains active.",
                CommitPressureLevel.Throttled => "High-cost collectors are slowed and ETW is stopped.",
                _ => "Only commit, GPU temperatures, foreground changes and critical Windows events are retained."
            });
        _collectorHealth?.Set("Commit Pressure", status, detail);

        if (_commitPressureLevel < CommitPressureLevel.Throttled || _etwStoppedForPressure)
            return;
        if (_etwMonitor?.IsRunning != true && _etwSystemMonitor?.IsRunning != true)
            return;

        _etwStoppedForPressure = true;
        try { _etwMonitor?.Stop(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[CrashScope] pressure stop for GPU ETW failed: {ex.Message}"); }
        try { _etwSystemMonitor?.Stop(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[CrashScope] pressure stop for system ETW failed: {ex.Message}"); }
        RefreshEtwHealth();
    }

    /// <summary>
    /// Compatibility for process-exit/IDisposable callers. A slow stop retains ownership and resources;
    /// completion proceeds in the background. Normal hosts should await StopAsync.
    /// </summary>
    public void Stop()
    {
        var stop = StopAsync();
        stop.Wait(TimeSpan.FromSeconds(3));
    }

    public Task StopAsync()
    {
        Task stop;
        lock (_lock)
        {
            if (_stopTask is not null) return _stopTask;
            if (_owner is null) return Task.CompletedTask;
            _stopping = true;
            if (_state != RecorderState.Faulted) _state = RecorderState.Stopping;
            _cts?.Cancel();
            var recording = _recordingTask;
            _stopTask = Task.Run(async () =>
            {
                // Never dispose collectors, mark clean, or admit another Start while Tick is alive.
                if (recording is not null) await recording.ConfigureAwait(false);
                lock (_lock)
                {
                    CleanupResources();
                    if (LastError is null)
                    {
                        try { _journal?.MarkClean(); }
                        catch (Exception ex) { ReportFault(ex); }
                    }
                    _cts?.Dispose();
                    _cts = null;
                    _recordingTask = null;
                    _owner?.Dispose();
                    _owner = null;
                    _state = LastError is null ? RecorderState.Stopped : RecorderState.Faulted;
                    _stopTask = null;
                }
                Notify(StatusChanged);
            });
            stop = _stopTask;
        }
        Notify(StatusChanged); // Publish Stopping immediately, even when a native Tick is still blocked.
        return stop;
    }

    private void CleanupResources()
    {
        // Attempt every cleanup even if an earlier resource throws. Startup failures use the same path.
        void Release(IDisposable? resource)
        {
            try { resource?.Dispose(); }
            catch (Exception ex) { ReportFault(ex); }
        }
        Release(_etwMonitor);
        Release(_etwSystemMonitor);
        Release(_resourceExhaustionTracker);
        try { RefreshEtwHealth(); } catch (Exception ex) { ReportFault(ex); }
        _etwMonitor = null;
        _etwSystemMonitor = null;
        _resourceExhaustionTracker = null;
        _selfOverheadTracker = null;
        _pageFileTracker = null;
        Release(_cpuFallbackTracker); _cpuFallbackTracker = null;
        Release(_tickProfiler); _tickProfiler = null;
        Release(_hardware); _hardware = null;
        Release(_telemetryWriter); _telemetryWriter = null;
        Release(_foregroundWriter); _foregroundWriter = null;
        Release(_processEventWriter); _processEventWriter = null;
        Release(_processSnapshotWriter); _processSnapshotWriter = null;
        Release(_gpuEngineWriter); _gpuEngineWriter = null;
        Release(_displayWriter); _displayWriter = null;
        Release(_powerWriter); _powerWriter = null;
        Release(_remoteSessionWriter); _remoteSessionWriter = null;
        Release(_storageWriter); _storageWriter = null;
        Release(_gpuMemoryWriter); _gpuMemoryWriter = null;
        Release(_memoryPressureWriter); _memoryPressureWriter = null;
        Release(_dwmStateWriter); _dwmStateWriter = null;
        Release(_resourceExhaustionWriter); _resourceExhaustionWriter = null;
        Release(_selfOverheadWriter); _selfOverheadWriter = null;
        Release(_pageFileWriter); _pageFileWriter = null;
        Release(_cpuFallbackWriter); _cpuFallbackWriter = null;
        Release(_storageTracker); _storageTracker = null;
        Release(_gpuMemoryTracker); _gpuMemoryTracker = null;
        Release(_gpuEngineTracker); _gpuEngineTracker = null;
        if (_hooks is not null)
            try { _hooks.Dispose(); } catch (Exception ex) { ReportFault(ex); }
    }

    public IncidentAnalysis? CheckPreviousUncleanSession()
    {
        lock (_lock)
        {
            if (_owner is not null) return null; // Never inspect an actively owned session as a crash.
            try
            {
                RecoveryError = null;
                return SessionRecovery.Recover(_stateDirectory, _incidentsRoot, _hooks?.Recover);
            }
            catch (Exception ex)
            {
                RecoveryError = ex.Message;
                return null; // The durable pending queue is retried by Start or the next launch.
            }
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Bounds sessions/ and incidents/ growth for unattended deployments. Retention used to
    /// run only when the Desktop app started or the user opened Settings, so a Service-only
    /// or Agent-only host grew without limit.
    /// </summary>
    /// <remarks>
    /// Runs under the recorder's data-root lease. Active and pending-recovery sessions
    /// are excluded before any recursive deletion; external retention skips a busy root.
    /// </remarks>
    private void EnforceRetention()
    {
        try
        {
            var root = Directory.GetParent(_stateDirectory)?.FullName;
            if (string.IsNullOrEmpty(root))
                return;

            var (sessions, incidents, bytes) = new RetentionService(root).EnforceOwned();
            if (sessions > 0 || incidents > 0)
                System.Diagnostics.Trace.WriteLine(
                    $"[CrashScope] retention: removed {sessions} session(s), {incidents} incident(s), freed {bytes / (1024 * 1024)} MB");
        }
        catch (Exception ex)
        {
            try { System.Diagnostics.Trace.WriteLine($"[CrashScope] retention failed: {ex.Message}"); }
            catch { }
        }
    }
}

public enum RecorderState { Stopped, Starting, Recording, Stopping, Faulted }

internal sealed record RecorderRuntimeHooks(Action Initialize, Action<CancellationToken> Tick, Action Dispose, TimeSpan Interval,
    Func<SessionState, string, IncidentAnalysis>? Recover = null);
