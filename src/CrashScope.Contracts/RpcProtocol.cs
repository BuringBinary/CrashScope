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
    string? PrimaryGpu);

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

/// <summary>Raised client-side when the server answers a request with an error envelope.</summary>
public sealed class RpcException(string code, string message, string? forId = null)
    : Exception($"[{code}] {message}")
{
    public string Code { get; } = code;
    public string? ForId { get; } = forId;
}
