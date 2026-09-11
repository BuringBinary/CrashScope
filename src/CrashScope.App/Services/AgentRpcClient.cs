using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CrashScope.Contracts;

namespace CrashScope.App.Services;

public sealed class AgentRpcClient
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentRpcResponse> SendAsync(string command, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            AgentRpcProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(900));

        await pipe.ConnectAsync(timeout.Token);

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);

        var request = new AgentRpcRequest(command);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, _jsonOptions));

        var line = await reader.ReadLineAsync(timeout.Token);
        if (string.IsNullOrWhiteSpace(line))
            throw new IOException("CrashScope.Agent returned no response.");

        return JsonSerializer.Deserialize<AgentRpcResponse>(line, _jsonOptions)
               ?? throw new IOException("CrashScope.Agent returned an invalid response.");
    }
}
