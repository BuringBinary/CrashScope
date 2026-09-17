using System.IO.Pipes;
using System.Text.Json;
using CrashScope.Contracts;

namespace CrashScope.Core.Ipc;

/// <summary>
/// Client half of the agent RPC: connects to the agent's named pipe, performs the
/// hello handshake, matches responses to requests by id, and surfaces pushed events
/// (status-changed / telemetry-update) through <see cref="EventReceived"/>.
/// One outstanding request at a time keeps the skeleton simple; the UI issues a
/// handful of calls per interaction, not a firehose.
/// </summary>
public sealed class AgentRpcClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private readonly Dictionary<string, TaskCompletionSource<RpcEnvelope>> _pending = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _readCts;

    /// <summary>Called for server-pushed events (Id == null). Keep handlers quick.</summary>
    public Func<string, JsonElement?, Task>? EventReceived { get; set; }

    public bool IsConnected => _pipe?.IsConnected ?? false;

    public async Task ConnectAsync(string pipeName, int timeoutMs = 5000, CancellationToken ct = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
        _pipe = pipe;
        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    /// <summary>
    /// Sends a request and awaits its matching response. Throws
    /// <see cref="RpcException"/> when the server answers with an error envelope and
    /// <see cref="TimeoutException"/> when no response arrives in time.
    /// </summary>
    public async Task<JsonElement?> SendRequestAsync(string type, object? payload = null, int timeoutMs = 10000, CancellationToken ct = default)
    {
        if (_pipe is null || !_pipe.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<RpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _pending[id] = completion;

        try
        {
            var envelope = new RpcEnvelope(
                RpcProtocol.Version,
                id,
                type,
                DateTimeOffset.Now,
                payload is null ? null : JsonSerializer.SerializeToElement(payload, RpcFraming.Options));
            await RpcFraming.WriteEnvelopeAsync(_pipe, envelope, ct).ConfigureAwait(false);

            var finished = await Task.WhenAny(completion.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (finished != completion.Task)
                throw new TimeoutException($"RPC request '{type}' ({id}) timed out after {timeoutMs} ms.");

            var response = await completion.Task.ConfigureAwait(false);
            if (response.Type == RpcMessages.Error)
            {
                var error = RpcFraming.DeserializePayload<ErrorPayload>(response);
                throw new RpcException(error?.Code ?? "error", error?.Message ?? "Unspecified RPC error.", id);
            }

            return response.Payload;
        }
        finally
        {
            lock (_gate)
                _pending.Remove(id);
        }
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // Dispose is best-effort here.
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var envelope = await RpcFraming.ReadEnvelopeAsync(_pipe!, ct).ConfigureAwait(false);
                if (envelope is null)
                    break;

                if (envelope.Id is not null)
                {
                    TaskCompletionSource<RpcEnvelope>? completion = null;
                    lock (_gate)
                    {
                        if (_pending.Remove(envelope.Id, out var pending))
                            completion = pending;
                    }
                    completion?.TrySetResult(envelope);
                }
                else
                {
                    var handler = EventReceived;
                    if (handler is not null)
                        await handler(envelope.Type, envelope.Payload).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Cancelled or the pipe went away; Dispose ends this loop by design.
        }
    }
}
