using System.Text.Json;

namespace CrashScope.Contracts;

/// <summary>
/// Central constants of the CrashScope local RPC protocol (JSON over named pipes).
/// The protocol is deliberately boring: length-prefixed UTF-8 JSON frames so any
/// language with a JSON parser can talk to the agent without a code generator.
/// </summary>
public static class RpcProtocol
{
    /// <summary>Wire protocol version. Bump on any breaking envelope or payload change.</summary>
    public const int Version = 1;

    /// <summary>
    /// Pipe name prefix. The live endpoint carries a <c>-&lt;pid&gt;</c> suffix so two
    /// recorders (agent + service) never collide; clients discover the exact name
    /// through <c>state/rpc-endpoint.json</c>, not by guessing.
    /// </summary>
    public const string PipeNamePrefix = "crashscope-agent-v";

    /// <summary>Hard cap for one frame; protects against garbage lengths on a desynchronized stream.</summary>
    public const int MaxFrameBytes = 8 * 1024 * 1024;
}

/// <summary>Message type strings on the wire.</summary>
public static class RpcMessages
{
    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string GetStatus = "get-status";
    public const string Status = "status";
    public const string GetSensorCatalog = "get-sensor-catalog";
    public const string Catalog = "catalog";
    public const string GetLatestTelemetry = "get-latest-telemetry";
    public const string Telemetry = "telemetry";
    public const string StartRecording = "start-recording";
    public const string StopRecording = "stop-recording";
    public const string GetIncidents = "get-incidents";
    public const string IncidentList = "incidents";
    public const string GetIncidentDetail = "get-incident-detail";
    public const string IncidentDetail = "incident-detail";
    public const string GetPowerPlans = "get-power-plans";
    public const string PowerPlans = "power-plans";
    public const string GetProcesses = "get-processes";
    public const string Processes = "processes";
    public const string GetActions = "get-actions";
    public const string Actions = "actions";
    public const string SetPowerPlan = "set-power-plan";
    public const string SetProcessPriority = "set-process-priority";
    public const string RollbackAction = "rollback-action";
    public const string Ok = "ok";
    public const string Error = "error";
    public const string StatusChanged = "status-changed";
    public const string TelemetryUpdate = "telemetry-update";
}

/// <summary>
/// Length-prefixed JSON envelope. Requests carry an Id; responses echo it; events have
/// a null Id. V enables future version negotiation without a format break.
/// </summary>
public sealed record RpcEnvelope(
    int V,
    string? Id,
    string Type,
    DateTimeOffset Ts,
    JsonElement? Payload);

public sealed record RpcEndpoint(
    string PipeName,
    int ProcessId,
    int ProtocolVersion,
    string AgentVersion,
    DateTimeOffset StartedAt);

public sealed record HelloPayload(string ClientName, int ProtocolVersion);

public sealed record WelcomePayload(
    int ProtocolVersion,
    string AgentVersion,
    bool Recording,
    string? SessionId);

public sealed record StatusPayload(
    bool Recording,
    int SampleCount,
    string? SessionId,
    DateTimeOffset? StartedAt,
    double UptimeSeconds,
    string AgentVersion,
    string? PrimaryGpu,
    string? RecorderState = null,
    string? RecorderError = null,
    DateTimeOffset? LastSampleAt = null,
    string? RecoveryError = null);

public sealed record SensorEntryDto(
    int Id,
    string HardwareType,
    string HardwareName,
    string SensorType,
    string SensorName,
    string Identifier);

public sealed record CatalogPayload(IReadOnlyList<SensorEntryDto> Entries);

public sealed record TelemetryPayload(DateTimeOffset Timestamp, IReadOnlyList<float?> Values);

public sealed record OkPayload(string? Message = null);

public sealed record ErrorPayload(string Code, string Message, string? ForId = null);

public sealed record IncidentSummaryDto(
    string Id,
    DateTimeOffset Timestamp,
    string LeadingMode,
    double Confidence,
    int HypothesisCount,
    bool HasSummary,
    bool HasTimeline);

public sealed record IncidentListPayload(IReadOnlyList<IncidentSummaryDto> Incidents);

public sealed record IncidentDetailRequest(string Id);

public sealed record IncidentDetailPayload(string Id, string? Summary, string? Timeline);
public sealed record PowerPlanDto(string Guid, string Name, bool IsActive);
public sealed record PowerPlansPayload(IReadOnlyList<PowerPlanDto> Plans);
public sealed record SetPowerPlanRequest(string PlanGuid, string Reason);
public sealed record ProcessDto(int Pid, string Name, string Priority);
public sealed record ProcessesPayload(IReadOnlyList<ProcessDto> Processes);
public sealed record SetProcessPriorityRequest(int Pid, string Priority, string Reason);
public sealed record ActionRecordDto(
    string ActionId,
    DateTimeOffset Timestamp,
    string Action,
    string Target,
    string? OldValue,
    string NewValue,
    string Caller,
    string Reason,
    string Status,
    string? Error,
    bool Reversible);
public sealed record ActionsPayload(IReadOnlyList<ActionRecordDto> Actions);
public sealed record RollbackActionRequest(string ActionId, string Reason);

/// <summary>Raised client-side when the server answers a request with an error envelope.</summary>
public sealed class RpcException(string code, string message, string? forId = null)
    : Exception($"[{code}] {message}")
{
    public string Code { get; } = code;
    public string? ForId { get; } = forId;
}
