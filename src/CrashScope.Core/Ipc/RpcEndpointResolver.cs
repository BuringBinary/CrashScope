using System.Text.Json;
using CrashScope.Contracts;

namespace CrashScope.Core.Ipc;

/// <summary>
/// Resolves the agent's RPC pipe name: an explicit name wins; otherwise the
/// <c>rpc-endpoint.json</c> discovery file in the agent state directory is read.
/// Shared by the probe, the desktop monitor view model and any future client.
/// </summary>
public static class RpcEndpointResolver
{
    public static bool TryResolve(string? stateDirectory, out string? pipeName, out string error, string? explicitPipeName = null)
    {
        pipeName = explicitPipeName;
        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            error = "No pipe name given and no state directory to read rpc-endpoint.json from.";
            return false;
        }

        var endpointPath = Path.Combine(stateDirectory, "rpc-endpoint.json");
        if (!File.Exists(endpointPath))
        {
            error = $"No endpoint file at {endpointPath} - the agent is not running or RPC is disabled.";
            return false;
        }

        try
        {
            var endpoint = JsonSerializer.Deserialize<RpcEndpoint>(File.ReadAllText(endpointPath), RpcFraming.Options);
            pipeName = endpoint?.PipeName;
            if (!string.IsNullOrWhiteSpace(pipeName))
            {
                error = string.Empty;
                return true;
            }

            error = "The endpoint file did not contain a pipe name.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Could not read the endpoint file: {ex.Message}";
            return false;
        }
    }
}
