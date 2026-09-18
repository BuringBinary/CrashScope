namespace CrashScope.Core.Models;

public sealed record SensorCatalogEntry(
    int Id,
    string HardwareType,
    string HardwareName,
    string SensorType,
    string SensorName,
    string Identifier);

public sealed record ForegroundState(
    int? ProcessId,
    string? ProcessName,
    long IdleSeconds);

public sealed record TelemetrySnapshot(
    DateTimeOffset Timestamp,
    ForegroundState Foreground,
    IReadOnlyList<float?> SensorValues);

public sealed record ProcessEvent(
    DateTimeOffset Timestamp,
    string Event,
    int ProcessId,
    string ProcessName);

public sealed record ProcessSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<ProcessInfo> Processes);

public sealed record ProcessInfo(int ProcessId, string ProcessName);

public sealed record ForegroundEvent(
    DateTimeOffset Timestamp,
    int? ProcessId,
    string? ProcessName,
    long IdleSeconds);

public sealed record SessionState(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeatAt,
    bool CleanShutdown,
    string SessionDirectory,
    bool Elevated,
    string AgentVersion,
    // True until the first Heartbeat: the journal was persisted but the recorder
    // never captured anything. An unclean end here is not an incident worth collecting.
    bool NeverStarted = false,
    string? RecorderError = null);

public sealed record WindowsEventEvidence(
    DateTimeOffset? TimeCreated,
    string? Provider,
    int Id,
    string? Level,
    string? Message,
    IReadOnlyDictionary<string, string?>? EventData = null);

public sealed record EngineUsage(string EngineType, float UtilizationPercent);

public sealed record ProcessGpuUsage(
    int ProcessId,
    string? ProcessName,
    IReadOnlyList<EngineUsage> Engines);

public sealed record GpuEngineSample(
    DateTimeOffset Timestamp,
    IReadOnlyList<ProcessGpuUsage> Processes);

public sealed record DisplayInfo(
    string DeviceName,
    string MonitorName,
    bool IsVirtual,
    int Width,
    int Height);

public sealed record DisplayTopologyEvent(
    DateTimeOffset Timestamp,
    string Change,
    IReadOnlyList<DisplayInfo> Before,
    IReadOnlyList<DisplayInfo> After,
    string Description);

public sealed record PowerSchemeEvent(
    DateTimeOffset Timestamp,
    string Change,
    string SchemeGuid,
    string SchemeName,
    string Description);

public sealed record RemoteSessionEvent(
    DateTimeOffset Timestamp,
    bool IsRemote,
    string Description);

public sealed record DriverInventoryEntry(
    string AdapterName,
    string? MatchingDeviceId,
    string? DriverVersion,
    DateTimeOffset? DriverDate,
    string Vendor,
    bool? IsDiscrete = null);

public sealed record EtwDxgKrnlEvent(
    DateTimeOffset Timestamp,
    int EventId,
    string EventName,
    int? ProcessId,
    string? ProcessName,
    string? Detail);

public sealed record EtwProviderEvent(
    string ProviderName,
    DateTimeOffset Timestamp,
    int EventId,
    string EventName,
    string? Level,
    int? ProcessId,
    string? ProcessName,
    string? Detail);

public sealed record StorageSample(
    DateTimeOffset Timestamp,
    IReadOnlyList<DiskMetrics> Disks);

public sealed record DiskMetrics(
    string Name,
    string? Model,
    float? TemperatureCelsius,
    string? HealthStatus,
    float? DiskTimePercent,
    float? AvgReadLatencyMs,
    float? AvgWriteLatencyMs,
    float? QueueDepth,
    int? DiskIndex = null,
    string? PerformanceSource = null,
    string? SensorSource = null);

public sealed record ProcessGpuMemory(
    int ProcessId,
    string? ProcessName,
    long DedicatedBytes,
    long SharedBytes);

public sealed record GpuMemorySample(
    DateTimeOffset Timestamp,
    IReadOnlyList<ProcessGpuMemory> Processes);

public sealed record MemoryPressureSample(
    DateTimeOffset Timestamp,
    long CommitBytes,
    long CommitLimitBytes,
    long AvailableBytes,
    float CommittedPercent);

public sealed record DwmStateEvent(
    DateTimeOffset Timestamp,
    bool IsHardwareRendering,
    bool IsCompositionEnabled,
    string Description);