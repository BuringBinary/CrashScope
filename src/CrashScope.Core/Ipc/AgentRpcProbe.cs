using System.Text.Json;
using CrashScope.Contracts;

namespace CrashScope.Core.Ipc;

public sealed record RpcProbeOptions
{
    /// <summary>Explicit pipe name. When null, resolved from the endpoint file in <see cref="StateDirectory"/>.</summary>
    public string? PipeName { get; init; }

    /// <summary>Agent state directory holding rpc-endpoint.json.</summary>
    public string? StateDirectory { get; init; }

    /// <summary>How long to listen for pushed telemetry before finishing. Zero disables the wait.</summary>
    public TimeSpan Listen { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed record RpcProbeResult(
    bool Connected,
    string? PipeName,
    string? Error,
    WelcomePayload? Welcome,
    StatusPayload? Status,
    CatalogPayload? Catalog,
    int TelemetryUpdates,
    TelemetryPayload? LatestTelemetry);

/// <summary>
/// Read-only inspection client for a running agent: resolves the endpoint, handshakes,
/// pulls status / sensor catalog / latest telemetry, counts live updates for the listen
/// window. Never throws for operational failures - the result carries a plain-language
/// error instead, so CLI wrappers and tests can assert on it directly.
/// </summary>
public static class AgentRpcProbe
{
    public static async Task<RpcProbeResult> RunAsync(RpcProbeOptions options, CancellationToken ct = default)
    {
        string? pipeName = options.PipeName;
        try
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                if (!RpcEndpointResolver.TryResolve(options.StateDirectory, out pipeName, out var resolveError))
                    return Fail(pipeName, resolveError);
            }

            using var client = new AgentRpcClient();
            try
            {
                await client.ConnectAsync(pipeName!, 4000, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Fail(pipeName, $"Could not connect to '{pipeName}': {ex.Message}");
            }

            var telemetryUpdates = 0;
            TelemetryPayload? latest = null;
            client.EventReceived = (type, payload) =>
            {
                if (type == RpcMessages.TelemetryUpdate && payload is { } raw)
                {
                    var sample = raw.Deserialize<TelemetryPayload>(RpcFraming.Options);
                    if (sample is not null)
                    {
                        latest = sample;
                        Interlocked.Increment(ref telemetryUpdates);
                    }
                }

                return Task.CompletedTask;
            };

            var welcome = Payload<WelcomePayload>(await client
                .SendRequestAsync(RpcMessages.Hello, new HelloPayload("rpc-probe", RpcProtocol.Version), ct: ct)
                .ConfigureAwait(false));
            var status = Payload<StatusPayload>(await client
                .SendRequestAsync(RpcMessages.GetStatus, ct: ct).ConfigureAwait(false));
            var catalog = Payload<CatalogPayload>(await client
                .SendRequestAsync(RpcMessages.GetSensorCatalog, ct: ct).ConfigureAwait(false));

            if (options.Listen > TimeSpan.Zero)
                await Task.Delay(options.Listen, ct).ConfigureAwait(false);

            var buffered = Payload<TelemetryPayload>(await client
                .SendRequestAsync(RpcMessages.GetLatestTelemetry, ct: ct).ConfigureAwait(false));
            latest ??= buffered;

            return new RpcProbeResult(true, pipeName, null, welcome, status, catalog, telemetryUpdates, latest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail(pipeName, $"Probe failed: {ex.Message}");
        }

        static RpcProbeResult Fail(string? pipeName, string error) =>
            new(false, pipeName, error, null, null, null, 0, null);
    }

    private static T? Payload<T>(JsonElement? element) where T : class
        => element is { } e ? e.Deserialize<T>(RpcFraming.Options) : null;
}
