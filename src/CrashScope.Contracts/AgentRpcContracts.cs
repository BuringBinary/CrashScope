using System.Text.Json;

namespace CrashScope.Contracts;

public static class AgentRpcProtocol
{
    public const string PipeName = "CrashScope.Agent.v1";

    public const string StatusCommand = "status";
    public const string LatestTelemetryCommand = "latest-telemetry";
    public const string SensorCatalogCommand = "sensor-catalog";
    public const string ListIncidentsCommand = "list-incidents";
    public const string IncidentDetailCommand = "incident-detail";
}

public sealed record AgentRpcRequest(
    string Command,
    string? IncidentId = null);

public sealed record AgentRpcResponse(
    bool Ok,
    string? Error = null,
    AgentStatusDto? Status = null,
    TelemetryDto? Telemetry = null,
    IReadOnlyList<SensorCatalogDto>? SensorCatalog = null,
    IReadOnlyList<IncidentSummaryDto>? Incidents = null,
    IncidentDetailDto? IncidentDetail = null);

public sealed record AgentStatusDto(
    string Version,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeatAt,
    bool Elevated,
    string SessionDirectory,
    DateTimeOffset? LatestTelemetryAt,
    int SensorCount);

public sealed record TelemetryDto(
    DateTimeOffset Timestamp,
    ForegroundDto Foreground,
    IReadOnlyList<float?> SensorValues);

public sealed record ForegroundDto(
    int? ProcessId,
    string? ProcessName,
    long IdleSeconds);

public sealed record SensorCatalogDto(
    int Id,
    string HardwareType,
    string HardwareName,
    string SensorType,
    string SensorName,
    string Identifier);

public sealed record IncidentSummaryDto(
    string Id,
    DateTimeOffset? LastHeartbeatAt,
    string? SessionId,
    bool KernelPower41,
    bool UnexpectedShutdown6008,
    bool WheaEvidence,
    bool DisplayTdrEvidence,
    string DirectoryPath);

public sealed record IncidentDetailDto(
    IncidentSummaryDto Summary,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    bool SensorCatalogAvailable,
    IReadOnlyList<IncidentTimelineEventDto> Timeline,
    IReadOnlyList<IncidentMetricSeriesDto> Metrics);

public sealed record IncidentTimelineEventDto(
    DateTimeOffset Timestamp,
    string Kind,
    string Title,
    string Detail,
    string Severity);

public sealed record IncidentMetricSeriesDto(
    string Key,
    string Label,
    string Unit,
    double Minimum,
    double Maximum,
    IReadOnlyList<IncidentMetricPointDto> Points);

public sealed record IncidentMetricPointDto(
    DateTimeOffset Timestamp,
    double Value);

public sealed record ActionEventDto(
    DateTimeOffset Timestamp,
    string Source,
    string Category,
    string Operation,
    string Target,
    JsonElement? Before,
    JsonElement? After,
    bool Reversible,
    JsonElement? Rollback = null);
