using System.Collections.ObjectModel;
using System.Text.Json;
using CrashScope.Contracts;
using CrashScope.Core.Ipc;
using CrashScope.Desktop.Services;

namespace CrashScope.Desktop;

/// <summary>
/// UI-independent view model for the actions plane: connects like the monitor VM
/// but drives the mutation verbs. Every mutation requires a reason; the server
/// enforces it too, and RPC failures land in <see cref="StatusMessage"/> instead
/// of surfacing as exceptions.
/// </summary>
public sealed class AgentActionsViewModel : IDisposable
{
    private readonly Action<Action> _marshal;
    private readonly object _gate = new();
    private AgentRpcClient? _client;

    public ObservableCollection<PowerPlanDto> Plans { get; } = [];
    public ObservableCollection<ProcessDto> Processes { get; } = [];
    public ObservableCollection<ActionRecordDto> Actions { get; } = [];

    public bool IsConnected { get; private set; }
    public string PipeName { get; private set; } = string.Empty;
    // Loc falls back to the raw key when no WPF App is loaded (unit tests assert the keys).
    public string StatusMessage { get; private set; } = Loc.T("common.notConnected");
    /// <summary>Raised after StatusMessage changes (on the marshal thread).</summary>
    public event Action? StatusChanged;

    public AgentActionsViewModel(Action<Action>? marshal = null)
        => _marshal = marshal ?? (action => action());

    public async Task<bool> ConnectAsync(string? stateDirectory = null, string? pipeName = null, CancellationToken ct = default)
    {
        Disconnect();
        if (!RpcEndpointResolver.TryResolve(stateDirectory, out var resolved, out var resolveError, pipeName))
        {
            SetStatus(resolveError);
            return false;
        }

        try
        {
            var client = new AgentRpcClient();
            await client.ConnectAsync(resolved!, 4000, ct).ConfigureAwait(false);
            lock (_gate)
                _client = client;
            PipeName = resolved!;
            await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("desktop-actions", RpcProtocol.Version), ct: ct)
                .ConfigureAwait(false);
            IsConnected = true;
            SetStatus(Loc.F("common.connected", PipeName));
            await RefreshAllAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("agentActions.status.connectFailed", ex.Message));
            DisposeClient();
            return false;
        }
    }

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        var client = Client ?? throw new InvalidOperationException(Loc.T("common.notConnected"));
        var plans = Deserialize<PowerPlansPayload>(
            await client.SendRequestAsync(RpcMessages.GetPowerPlans, ct: ct).ConfigureAwait(false));
        var processes = Deserialize<ProcessesPayload>(
            await client.SendRequestAsync(RpcMessages.GetProcesses, ct: ct).ConfigureAwait(false));
        var actions = Deserialize<ActionsPayload>(
            await client.SendRequestAsync(RpcMessages.GetActions, ct: ct).ConfigureAwait(false));

        _marshal(() =>
        {
            Replace(Plans, plans?.Plans ?? []);
            Replace(Processes, processes?.Processes ?? []);
            Replace(Actions, actions?.Actions ?? []);
        });
    }

    public async Task SetPowerPlanAsync(string planGuid, string reason, CancellationToken ct = default)
    {
        await SendMutationAsync(
            () => new RpcRequest(RpcMessages.SetPowerPlan, new SetPowerPlanRequest(planGuid, reason)),
            Loc.T("agentActions.status.powerPlanSet"), ct).ConfigureAwait(false);
    }

    public async Task SetPriorityAsync(int pid, string priority, string reason, CancellationToken ct = default)
    {
        await SendMutationAsync(
            () => new RpcRequest(RpcMessages.SetProcessPriority, new SetProcessPriorityRequest(pid, priority, reason)),
            Loc.T("agentActions.status.prioritySet"), ct).ConfigureAwait(false);
    }

    public async Task RollbackAsync(string actionId, string reason, CancellationToken ct = default)
    {
        await SendMutationAsync(
            () => new RpcRequest(RpcMessages.RollbackAction, new RollbackActionRequest(actionId, reason)),
            Loc.T("agentActions.status.rolledBack"), ct).ConfigureAwait(false);
    }

    private readonly record struct RpcRequest(string Type, object Payload);

    private async Task SendMutationAsync(Func<RpcRequest> requestFactory, string successLabel, CancellationToken ct)
    {
        try
        {
            var client = Client ?? throw new InvalidOperationException(Loc.T("common.notConnected"));
            var request = requestFactory();
            await client.SendRequestAsync(request.Type, request.Payload, ct: ct).ConfigureAwait(false);
            SetStatus(Loc.F("agentActions.status.done", successLabel));
            await RefreshAllAsync(ct).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            SetStatus(Loc.F("agentActions.status.rejected", ex.Code, ex.Message));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("agentActions.status.failed", ex.Message));
        }
    }

    public void Disconnect() => DisposeClient();

    public void Dispose() => DisposeClient();

    private AgentRpcClient? Client
    {
        get { lock (_gate) return _client; }
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        StatusChanged?.Invoke();
    }

    private void DisposeClient()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            IsConnected = false;
        }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }
    private static T? Deserialize<T>(JsonElement? element) where T : class
        => element is { } e ? e.Deserialize<T>(RpcFraming.Options) : null;
}
