using CrashScope.Contracts;
using CrashScope.Core.Ipc;
using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class ProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crashscope-probe-{Guid.NewGuid():N}");

    public ProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* Windows may still hold a pipe handle for a moment. */ }
    }

    [Fact]
    public async Task Probe_AgainstLiveServer_ReportsStatusAndCatalog()
    {
        var (engine, server) = StartServer();
        using var _e = engine;
        using var _s = server;

        var result = await AgentRpcProbe.RunAsync(new RpcProbeOptions
        {
            PipeName = server.PipeName,
            Listen = TimeSpan.FromMilliseconds(400)
        });

        Assert.True(result.Connected, result.Error);
        Assert.Equal(server.PipeName, result.PipeName);
        Assert.NotNull(result.Welcome);
        Assert.Equal(RpcProtocol.Version, result.Welcome!.ProtocolVersion);
        Assert.NotNull(result.Status);
        Assert.False(result.Status!.Recording);
        Assert.NotNull(result.Catalog);
        Assert.Empty(result.Catalog!.Entries); // engine never started
        Assert.NotNull(result.LatestTelemetry);
        Assert.Empty(result.LatestTelemetry!.Values); // no recording, no telemetry
    }

    [Fact]
    public async Task Probe_ResolvesEndpointFile_FromStateDirectory()
    {
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        var server = new AgentRpcServer(engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8]);
        using var _e = engine;
        using var _s = server;
        server.Start();

        var result = await AgentRpcProbe.RunAsync(new RpcProbeOptions
        {
            StateDirectory = stateDir,
            Listen = TimeSpan.Zero
        });

        Assert.True(result.Connected, result.Error);
        Assert.Equal(server.PipeName, result.PipeName);
    }

    [Fact]
    public async Task Probe_MissingEndpointFile_ReportsFriendlyError()
    {
        var emptyDir = Path.Combine(_root, "empty-state");
        Directory.CreateDirectory(emptyDir);

        var result = await AgentRpcProbe.RunAsync(new RpcProbeOptions
        {
            StateDirectory = emptyDir,
            Listen = TimeSpan.Zero
        });

        Assert.False(result.Connected);
        Assert.Contains("rpc-endpoint", result.Error);
        Assert.Contains("not running", result.Error);
    }

    [Fact]
    public async Task Probe_BogusPipeName_ReportsConnectFailure()
    {
        var result = await AgentRpcProbe.RunAsync(new RpcProbeOptions
        {
            PipeName = "crashscope-no-such-pipe-" + Guid.NewGuid().ToString("N")[..8],
            Listen = TimeSpan.Zero
        });

        Assert.False(result.Connected);
        Assert.Contains("Could not connect", result.Error);
    }

    [Fact]
    public async Task Server_RejectsRequestBeforeHandshake()
    {
        var (engine, server) = StartServer();
        using var _e = engine;
        using var _s = server;
        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);

        var ex = await Assert.ThrowsAsync<RpcException>(() => client.SendRequestAsync(RpcMessages.GetStatus));

        Assert.Equal("handshake-required", ex.Code);
    }

    private (RecorderEngine Engine, AgentRpcServer Server) StartServer()
    {
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        var server = new AgentRpcServer(engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8]);
        server.Start();
        return (engine, server);
    }
}
