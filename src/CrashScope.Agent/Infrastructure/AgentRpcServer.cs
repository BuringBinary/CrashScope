using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CrashScope.Contracts;

namespace CrashScope.Agent.Infrastructure;

public sealed class AgentRpcServer
{
    private readonly Func<AgentStatusDto> _statusProvider;
    private readonly Func<TelemetryDto?> _telemetryProvider;
    private readonly Func<IReadOnlyList<SensorCatalogDto>> _sensorCatalogProvider;
    private readonly Func<IReadOnlyList<IncidentSummaryDto>> _incidentProvider;
    private readonly Func<string, IncidentDetailDto?> _incidentDetailProvider;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public AgentRpcServer(
        Func<AgentStatusDto> statusProvider,
        Func<TelemetryDto?> telemetryProvider,
        Func<IReadOnlyList<SensorCatalogDto>> sensorCatalogProvider,
        Func<IReadOnlyList<IncidentSummaryDto>> incidentProvider,
        Func<string, IncidentDetailDto?> incidentDetailProvider)
    {
        _statusProvider = statusProvider;
        _telemetryProvider = telemetryProvider;
        _sensorCatalogProvider = sensorCatalogProvider;
        _incidentProvider = incidentProvider;
        _incidentDetailProvider = incidentDetailProvider;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ServeOneClientAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"RPC server error: {ex.Message}");
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private async Task ServeOneClientAsync(CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeServerStream(
            AgentRpcProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync(cancellationToken);

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
            return;

        AgentRpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<AgentRpcRequest>(line, _jsonOptions);
            response = request is null
                ? Error("Invalid request.")
                : Handle(request);
        }
        catch (JsonException ex)
        {
            response = Error($"Invalid JSON: {ex.Message}");
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, _jsonOptions));
    }

    private AgentRpcResponse Handle(AgentRpcRequest request) => request.Command switch
    {
        AgentRpcProtocol.StatusCommand => new AgentRpcResponse(
            Ok: true,
            Status: _statusProvider()),

        AgentRpcProtocol.LatestTelemetryCommand => new AgentRpcResponse(
            Ok: true,
            Telemetry: _telemetryProvider()),

        AgentRpcProtocol.SensorCatalogCommand => new AgentRpcResponse(
            Ok: true,
            SensorCatalog: _sensorCatalogProvider()),

        AgentRpcProtocol.ListIncidentsCommand => new AgentRpcResponse(
            Ok: true,
            Incidents: _incidentProvider()),

        AgentRpcProtocol.IncidentDetailCommand when !string.IsNullOrWhiteSpace(request.IncidentId) =>
            _incidentDetailProvider(request.IncidentId) is { } detail
                ? new AgentRpcResponse(Ok: true, IncidentDetail: detail)
                : Error($"Incident not found: {request.IncidentId}"),

        AgentRpcProtocol.IncidentDetailCommand => Error("incident-detail requires incidentId."),

        _ => Error($"Unknown command: {request.Command}")
    };

    private static AgentRpcResponse Error(string message) =>
        new(Ok: false, Error: message);
}
