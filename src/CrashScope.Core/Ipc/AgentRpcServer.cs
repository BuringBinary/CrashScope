using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using CrashScope.Contracts;
using CrashScope.Core.Actions;
using CrashScope.Core.Analysis;
using CrashScope.Core.Monitoring;

namespace CrashScope.Core.Ipc;

/// <summary>
/// Named-pipe RPC surface over a RecorderEngine - the Agent's observation and control
/// plane (see docs/rpc-protocol.md). The recorder itself stays passive and unchanged;
/// this class only exposes it: status, sensor catalog, latest telemetry, start/stop.
///
/// Security model: the pipe is created with <see cref="PipeOptions.CurrentUserOnly"/>,
/// so only processes of the same Windows user can connect - a UI in the user's session
/// yes, everyone else no. Handshake enforces the protocol version before any verb.
/// </summary>
public sealed class AgentRpcServer : IDisposable
{
    private const int MaxConcurrentClients = 4;

    private readonly RecorderEngine _engine;
    private readonly string? _incidentsRoot;
    private readonly ISystemActions? _actions;
    private readonly string _endpointPath;
    private readonly object _gate = new();
    private readonly Dictionary<int, ClientConnection> _clients = new();
    private TelemetryPayload? _latestTelemetry;
    private CancellationTokenSource? _cts;
    private bool _running;
    private int _clientSeq;

    public string PipeName { get; }

    public AgentRpcServer(RecorderEngine engine, string stateDirectory, string? pipeNameOverride = null, string? incidentsRoot = null, ISystemActions? actions = null)
    {
        _engine = engine;
        _incidentsRoot = incidentsRoot;
        _actions = actions;
        PipeName = pipeNameOverride ?? $"{RpcProtocol.PipeNamePrefix}{Environment.ProcessId}";
        Directory.CreateDirectory(stateDirectory);
        _endpointPath = Path.Combine(stateDirectory, "rpc-endpoint.json");
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
                return;
            _running = true;
            _cts = new CancellationTokenSource();
            _engine.StatusChanged += OnEngineStatusChanged;
            _engine.SensorUpdated += OnEngineSensorUpdated;
            WriteEndpointFile();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_running)
                return;
            _running = false;
            cts = _cts;
            _cts = null;
        }

        // Unsubscribe before cancelling so no event sneaks into dying connections.
        _engine.StatusChanged -= OnEngineStatusChanged;
        _engine.SensorUpdated -= OnEngineSensorUpdated;
        cts?.Cancel();

        List<ClientConnection> toClose;
        lock (_gate)
        {
            toClose = _clients.Values.ToList();
            _clients.Clear();
        }

        foreach (var client in toClose)
            client.Close();

        try
        {
            if (File.Exists(_endpointPath))
                File.Delete(_endpointPath);
        }
        catch
        {
            // Endpoint cleanup is best-effort; a stale file only costs one failed
            // connect attempt before the client sees the pipe is gone.
        }

        // The CTS is deliberately not disposed: its token is still referenced by
        // handler tasks, and a cancelled timer-less CTS holds no unmanaged resource.
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    MaxConcurrentClients,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Transient pipe-name contention or handle exhaustion: back off, retry.
                try { await Task.Delay(500, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Server shutting down, or a client connected and vanished.
                pipe.Dispose();
                continue;
            }

            ClientConnection client;
            lock (_gate)
            {
                client = new ClientConnection(++_clientSeq, pipe, ct);
                _clients[client.Id] = client;
            }

            _ = Task.Run(() => PumpOutboundAsync(client), CancellationToken.None);
            _ = Task.Run(() => ClientLoopAsync(client), CancellationToken.None);
        }
    }

    private async Task ClientLoopAsync(ClientConnection client)
    {
        try
        {
            var first = await RpcFraming.ReadEnvelopeAsync(client.Pipe, client.Token).ConfigureAwait(false);
            if (first is null || first.Type != RpcMessages.Hello)
            {
                await SendErrorAsync(client, "handshake-required",
                    "The first message must be a hello with a protocol version.", first?.Id).ConfigureAwait(false);
                return;
            }

            var hello = RpcFraming.DeserializePayload<HelloPayload>(first);
            if (hello is null || hello.ProtocolVersion != RpcProtocol.Version)
            {
                await SendErrorAsync(client, "protocol-version",
                    $"Client protocol {hello?.ProtocolVersion} != server protocol {RpcProtocol.Version}.", first.Id)
                    .ConfigureAwait(false);
                return;
            }

            client.Handshaken = true;
            client.ClientName = hello?.ClientName ?? "unknown";
            var session = _engine.CurrentSession;
            await client.SendAsync(new RpcEnvelope(
                RpcProtocol.Version,
                first.Id,
                RpcMessages.Welcome,
                DateTimeOffset.Now,
                JsonSerializer.SerializeToElement(new WelcomePayload(
                    RpcProtocol.Version,
                    VersionText(),
                    _engine.IsRecording,
                    session?.SessionId),
                    RpcFraming.Options)),
                client.Token).ConfigureAwait(false);

            while (true)
            {
                var envelope = await RpcFraming.ReadEnvelopeAsync(client.Pipe, client.Token).ConfigureAwait(false);
                if (envelope is null)
                    break; // client disconnected cleanly
                try
                {
                    await DispatchAsync(client, envelope).ConfigureAwait(false);
                }
                catch (ActionException ex)
                {
                    // One catch maps every mutation failure onto the wire.
                    await SendErrorAsync(client, ex.Code, ex.Message, envelope.Id).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Broken pipe / cancellation / malformed frame - all end the connection.
        }
        finally
        {
            lock (_gate)
                _clients.Remove(client.Id);
            client.Close();
        }
    }

    private async Task DispatchAsync(ClientConnection client, RpcEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case RpcMessages.GetStatus:
                await client.SendAsync(Response(RpcMessages.Status, envelope.Id, StatusBody()), client.Token)
                    .ConfigureAwait(false);
                break;

            case RpcMessages.GetSensorCatalog:
                var catalog = new CatalogPayload(_engine.SensorCatalog
                    .Select(e => new SensorEntryDto(e.Id, e.HardwareType, e.HardwareName, e.SensorType, e.SensorName, e.Identifier))
                    .ToList());
                await client.SendAsync(Response(RpcMessages.Catalog, envelope.Id, catalog), client.Token)
                    .ConfigureAwait(false);
                break;

            case RpcMessages.GetLatestTelemetry:
                TelemetryPayload? latest;
                lock (_gate)
                    latest = _latestTelemetry;
                await client.SendAsync(Response(RpcMessages.Telemetry, envelope.Id,
                    latest ?? new TelemetryPayload(DateTimeOffset.Now, [])), client.Token).ConfigureAwait(false);
                break;

            case RpcMessages.StartRecording:
                try
                {
                    _engine.Start();
                    await client.SendAsync(Response(RpcMessages.Ok, envelope.Id, new OkPayload("recording")), client.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await SendErrorAsync(client, "start-failed", ex.Message, envelope.Id).ConfigureAwait(false);
                }
                break;

            case RpcMessages.StopRecording:
                try
                {
                    await _engine.StopAsync().ConfigureAwait(false);
                    await client.SendAsync(Response(RpcMessages.Ok, envelope.Id, new OkPayload("stopped")), client.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await SendErrorAsync(client, "stop-failed", ex.Message, envelope.Id).ConfigureAwait(false);
                }
                break;

            case RpcMessages.GetPowerPlans:
                await client.SendAsync(Response(RpcMessages.PowerPlans, envelope.Id, PowerPlansBody()), client.Token)
                    .ConfigureAwait(false);
                break;
            case RpcMessages.GetProcesses:
                await client.SendAsync(Response(RpcMessages.Processes, envelope.Id, ProcessesBody()), client.Token)
                    .ConfigureAwait(false);
                break;
            case RpcMessages.GetActions:
                await client.SendAsync(Response(RpcMessages.Actions, envelope.Id, ActionsBody()), client.Token)
                    .ConfigureAwait(false);
                break;
            case RpcMessages.SetPowerPlan:
                var planRequest = RpcFraming.DeserializePayload<SetPowerPlanRequest>(envelope);
                RequireActions(planRequest);
                var planRecord = _actions!.SetPowerPlan(planRequest!.PlanGuid, client.ClientName, planRequest.Reason);
                await client.SendAsync(Response(RpcMessages.Ok, envelope.Id, new OkPayload($"power plan set: {planRecord.Target}")), client.Token)
                    .ConfigureAwait(false);
                break;
            case RpcMessages.SetProcessPriority:
                var priorityRequest = RpcFraming.DeserializePayload<SetProcessPriorityRequest>(envelope);
                RequireActions(priorityRequest);
                var priorityRecord = _actions!.SetProcessPriority(priorityRequest!.Pid, priorityRequest.Priority, client.ClientName, priorityRequest.Reason);
                await client.SendAsync(Response(RpcMessages.Ok, envelope.Id, new OkPayload($"priority set: {priorityRecord.NewValue}")), client.Token)
                    .ConfigureAwait(false);
                break;
            case RpcMessages.RollbackAction:
                var rollbackRequest = RpcFraming.DeserializePayload<RollbackActionRequest>(envelope);
                RequireActions(rollbackRequest);
                var rollbackRecord = _actions!.Rollback(rollbackRequest!.ActionId, client.ClientName, rollbackRequest.Reason);
                await client.SendAsync(Response(RpcMessages.Ok, envelope.Id, new OkPayload($"rolled back: {rollbackRecord.Target}")), client.Token)
                    .ConfigureAwait(false);
                break;
            case RpcMessages.Hello:
                await SendErrorAsync(client, "already-handshaken", "This connection already said hello.", envelope.Id)
                    .ConfigureAwait(false);
                break;

            case RpcMessages.GetIncidents:
                var incidents = ListIncidents();
                await client.SendAsync(Response(RpcMessages.IncidentList, envelope.Id, incidents), client.Token)
                    .ConfigureAwait(false);
                break;

            case RpcMessages.GetIncidentDetail:
                var detailRequest = RpcFraming.DeserializePayload<IncidentDetailRequest>(envelope);
                if (detailRequest is null ||
                    string.IsNullOrWhiteSpace(detailRequest.Id) ||
                    Path.GetFileName(detailRequest.Id) != detailRequest.Id ||
                    detailRequest.Id.Contains("..", StringComparison.Ordinal))
                {
                    await SendErrorAsync(client, "bad-incident-id",
                        "The request must carry an incident id (a directory name, never a path).", envelope.Id)
                        .ConfigureAwait(false);
                    break;
                }

                var detail = ReadIncidentDetail(detailRequest.Id);
                if (detail is null)
                {
                    await SendErrorAsync(client, "incident-not-found",
                        $"No incident directory named '{detailRequest.Id}'.", envelope.Id).ConfigureAwait(false);
                    break;
                }

                await client.SendAsync(Response(RpcMessages.IncidentDetail, envelope.Id, detail), client.Token)
                    .ConfigureAwait(false);
                break;

            default:
                await SendErrorAsync(client, "unknown-type", $"No verb named '{envelope.Type}'.", envelope.Id)
                    .ConfigureAwait(false);
                break;
        }
    }

    private async Task PumpOutboundAsync(ClientConnection client)
    {
        try
        {
            await foreach (var envelope in client.Outbound.ReadAllAsync(client.Token).ConfigureAwait(false))
                await client.SendAsync(envelope, client.Token).ConfigureAwait(false);
        }
        catch
        {
            // Channel completed or pipe died: stop pumping; ClientLoop closes the rest.
        }
    }

    private void OnEngineStatusChanged()
    {
        if (!_engine.IsRecording || _engine.SampleCount == 0)
            lock (_gate) _latestTelemetry = null;
        Broadcast(new RpcEnvelope(RpcProtocol.Version, null, RpcMessages.StatusChanged, DateTimeOffset.Now,
            JsonSerializer.SerializeToElement(StatusBody(), RpcFraming.Options)));
    }

    private void OnEngineSensorUpdated(IReadOnlyList<float?> values)
    {
        var payload = new TelemetryPayload(DateTimeOffset.Now, values.ToList());
        lock (_gate)
            _latestTelemetry = payload;
        Broadcast(new RpcEnvelope(RpcProtocol.Version, null, RpcMessages.TelemetryUpdate, DateTimeOffset.Now,
            JsonSerializer.SerializeToElement(payload, RpcFraming.Options)));
    }

    private void Broadcast(RpcEnvelope envelope)
    {
        List<ClientConnection> targets;
        lock (_gate)
            targets = _clients.Values.Where(c => c.Handshaken).ToList();

        foreach (var target in targets)
        {
            // Drop-oldest channel: a stalled UI falls behind instead of stalling the recorder.
            target.TryEnqueue(envelope);
        }
    }

    private StatusPayload StatusBody()
    {
        var session = _engine.CurrentSession;
        return new StatusPayload(
            _engine.IsRecording,
            _engine.SampleCount,
            session?.SessionId,
            session?.StartedAt,
            session is null ? 0 : (DateTimeOffset.Now - session.StartedAt).TotalSeconds,
            VersionText(),
            _engine.PrimaryGpuHardwareName,
            _engine.State.ToString(),
            _engine.LastError,
            _engine.LastSampleAt,
            _engine.RecoveryError);
    }

    private IncidentListPayload ListIncidents()
    {
        if (string.IsNullOrWhiteSpace(_incidentsRoot) || !Directory.Exists(_incidentsRoot))
            return new IncidentListPayload([]);

        var entries = IncidentCatalog.List(_incidentsRoot)
            .Select(e => new IncidentSummaryDto(e.Id, e.Timestamp, e.LeadingMode, e.Confidence, e.HypothesisCount, e.HasSummary, e.HasTimeline))
            .ToList();
        return new IncidentListPayload(entries);
    }

    private IncidentDetailPayload? ReadIncidentDetail(string id)
    {
        if (string.IsNullOrWhiteSpace(_incidentsRoot))
            return null;
        if (!Directory.Exists(Path.Combine(_incidentsRoot, id)))
            return null;

        return new IncidentDetailPayload(
            id,
            IncidentCatalog.ReadSummary(_incidentsRoot, id),
            IncidentCatalog.ReadTimeline(_incidentsRoot, id));
    }
    private PowerPlansPayload PowerPlansBody()
    {
        if (_actions is null)
            return new PowerPlansPayload([]);
        return new PowerPlansPayload(_actions.ListPowerPlans()
            .Select(p => new PowerPlanDto(p.Guid, p.Name, p.IsActive)).ToList());
    }
    private ProcessesPayload ProcessesBody()
    {
        if (_actions is null)
            return new ProcessesPayload([]);
        return new ProcessesPayload(_actions.ListProcesses()
            .Select(p => new ProcessDto(p.Pid, p.Name, p.Priority)).ToList());
    }
    private ActionsPayload ActionsBody()
    {
        if (_actions is null)
            return new ActionsPayload([]);
        return new ActionsPayload(_actions.RecentActions()
            .Select(a => new ActionRecordDto(a.ActionId, a.Timestamp, a.Action, a.Target, a.OldValue, a.NewValue, a.Caller, a.Reason, a.Status, a.Error, a.Reversible))
            .ToList());
    }
    private ISystemActions RequireActions(object? request)
    {
        if (_actions is null)
            throw new ActionException("actions-unavailable", "This agent was started without the actions plane (--no-actions).");
        if (request is null)
            throw new ActionException("bad-request", "The request payload is missing or malformed.");
        return _actions;
    }

    private static RpcEnvelope Response(string type, string? requestId, object payload) =>
        new(RpcProtocol.Version, requestId, type, DateTimeOffset.Now,
            JsonSerializer.SerializeToElement(payload, RpcFraming.Options));

    private static async Task SendErrorAsync(ClientConnection client, string code, string message, string? forId)
    {
        try
        {
            await client.SendAsync(
                Response(RpcMessages.Error, forId, new ErrorPayload(code, message, forId)), client.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // The pipe is gone; the finally in ClientLoop cleans up.
        }
    }

    private void WriteEndpointFile()
    {
        var endpoint = new RpcEndpoint(PipeName, Environment.ProcessId, RpcProtocol.Version, VersionText(), DateTimeOffset.Now);
        var temp = _endpointPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(endpoint, RpcFraming.Options));
        File.Move(temp, _endpointPath, overwrite: true);
    }

    private static string VersionText() =>
        typeof(AgentRpcServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>One connected client: its pipe, its ordered outbound event queue, its write gate.</summary>
    private sealed class ClientConnection(int id, NamedPipeServerStream pipe, CancellationToken token)
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Channel<RpcEnvelope> _outbound = Channel.CreateBounded<RpcEnvelope>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public int Id { get; } = id;

        public NamedPipeServerStream Pipe { get; } = pipe;

        public CancellationToken Token { get; } = token;

        public bool Handshaken { get; set; }
        public string ClientName { get; set; } = "unknown";

        public ChannelReader<RpcEnvelope> Outbound => _outbound.Reader;

        public bool TryEnqueue(RpcEnvelope envelope) => _outbound.Writer.TryWrite(envelope);

        public async Task SendAsync(RpcEnvelope envelope, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await RpcFraming.WriteEnvelopeAsync(Pipe, envelope, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Close()
        {
            _outbound.Writer.TryComplete();
            try
            {
                if (Pipe.IsConnected)
                    Pipe.Disconnect();
            }
            catch
            {
                // Never connected or already gone.
            }

            try
            {
                Pipe.Dispose();
            }
            catch
            {
                // Dispose is best-effort here.
            }
        }
    }
}
