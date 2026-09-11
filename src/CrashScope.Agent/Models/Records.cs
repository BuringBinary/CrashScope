namespace CrashScope.Agent.Models;

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
    string? Message);
