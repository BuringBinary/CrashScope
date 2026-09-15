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
    string AgentVersion);

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
    string Vendor);
