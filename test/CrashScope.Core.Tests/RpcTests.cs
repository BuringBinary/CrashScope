using System.Buffers.Binary;
using System.Text.Json;
using CrashScope.Contracts;
using CrashScope.Core.Ipc;
using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class RpcFramingTests
{
    [Fact]
    public void Envelope_JsonRoundTrips()
    {
        var envelope = new RpcEnvelope(
            RpcProtocol.Version, "abc123", RpcMessages.GetStatus, DateTimeOffset.Now,
            JsonSerializer.SerializeToElement(
                new StatusPayload(false, 5, "session", null, 0, "0.9.0", "AMD Radeon RX 9070 XT"),
                RpcFraming.Options));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, RpcFraming.Options);
        var round = JsonSerializer.Deserialize<RpcEnvelope>(bytes, RpcFraming.Options);

        Assert.NotNull(round);
        Assert.Equal(envelope.Type, round!.Type);
        Assert.Equal(envelope.Id, round.Id);
        Assert.Equal(5, round.Payload!.Value.GetProperty("sampleCount").GetInt32());
        Assert.Equal("AMD Radeon RX 9070 XT", round.Payload.Value.GetProperty("primaryGpu").GetString());
    }

    [Fact]
    public async Task Framing_RoundTripsThroughStream()
    {
        using var stream = new MemoryStream();
        var first = new RpcEnvelope(RpcProtocol.Version, "1", RpcMessages.Hello, DateTimeOffset.Now,
            JsonSerializer.SerializeToElement(new HelloPayload("test", 1), RpcFraming.Options));
        var second = new RpcEnvelope(RpcProtocol.Version, null, RpcMessages.StatusChanged, DateTimeOffset.Now, null);

        await RpcFraming.WriteEnvelopeAsync(stream, first, CancellationToken.None);
        await RpcFraming.WriteEnvelopeAsync(stream, second, CancellationToken.None);
        stream.Position = 0;

        var r1 = await RpcFraming.ReadEnvelopeAsync(stream, CancellationToken.None);
        var r2 = await RpcFraming.ReadEnvelopeAsync(stream, CancellationToken.None);
        var r3 = await RpcFraming.ReadEnvelopeAsync(stream, CancellationToken.None);

        Assert.Equal(RpcMessages.Hello, r1!.Type);
        Assert.Equal("1", r1.Id);
        Assert.Equal(RpcMessages.StatusChanged, r2!.Type);
        Assert.Null(r2.Id);
        Assert.Null(r3); // clean EOF at frame boundary
    }

    [Fact]
    public async Task Framing_RejectsBogusLength()
    {
        using var stream = new MemoryStream();
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, uint.MaxValue / 2);
        await stream.WriteAsync(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => RpcFraming.ReadEnvelopeAsync(stream, CancellationToken.None));
    }
}

public sealed class RpcServerClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crashscope-rpc-{Guid.NewGuid():N}");

    public RpcServerClientTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* Windows may still hold a handle for a moment; temp cleaner gets the rest. */ }
    }

    [Fact]
    public async Task Handshake_Status_Catalog_Telemetry_AndUnknownVerb()
    {
        var (engine, server) = StartServer();
        using var _e = engine;
        using var _s = server;
        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);

        var welcome = await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("tests", RpcProtocol.Version));
        var decoded = welcome!.Value.Deserialize<WelcomePayload>(RpcFraming.Options);
        Assert.Equal(RpcProtocol.Version, decoded!.ProtocolVersion);
        Assert.False(decoded.Recording);

        var status = (await client.SendRequestAsync(RpcMessages.GetStatus))!.Value
            .Deserialize<StatusPayload>(RpcFraming.Options);
        Assert.False(status!.Recording);
        Assert.Equal(0, status.SampleCount);

        var catalog = (await client.SendRequestAsync(RpcMessages.GetSensorCatalog))!.Value
            .Deserialize<CatalogPayload>(RpcFraming.Options);
        Assert.NotNull(catalog);
        Assert.Empty(catalog!.Entries); // the engine never started, so no sensor catalog yet

        var telemetry = (await client.SendRequestAsync(RpcMessages.GetLatestTelemetry))!.Value
            .Deserialize<TelemetryPayload>(RpcFraming.Options);
        Assert.NotNull(telemetry);
        Assert.Empty(telemetry!.Values);

        await Assert.ThrowsAsync<RpcException>(() => client.SendRequestAsync("no-such-verb"));
    }

    [Fact]
    public async Task Handshake_VersionMismatch_IsRejected()
    {
        var (engine, server) = StartServer();
        using var _e = engine;
        using var _s = server;
        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("tests", RpcProtocol.Version + 1)));

        Assert.Equal("protocol-version", ex.Code);
    }

    [Fact]
    public async Task StartAndStopRecording_ThroughRpc()
    {
        var (engine, server) = StartServer();
        using var _e = engine;
        using var _s = server;
        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);
        await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("tests", RpcProtocol.Version));

        await client.SendRequestAsync(RpcMessages.StartRecording, timeoutMs: 30000);
        var recording = await WaitForAsync(async () =>
            (await client.SendRequestAsync(RpcMessages.GetStatus, timeoutMs: 15000))!.Value
                .Deserialize<StatusPayload>(RpcFraming.Options)!.Recording);
        Assert.True(recording, "engine never reported Recording after start-recording");

        await client.SendRequestAsync(RpcMessages.StopRecording, timeoutMs: 30000);
        var stopped = await WaitForAsync(async () =>
            !(await client.SendRequestAsync(RpcMessages.GetStatus, timeoutMs: 15000))!.Value
                .Deserialize<StatusPayload>(RpcFraming.Options)!.Recording);
        Assert.True(stopped, "engine never reported stopped after stop-recording");
    }

    [Fact]
    public async Task EndpointFile_IsWrittenAndCleaned()
    {
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        var server = new AgentRpcServer(engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8]);
        using var _e = engine;

        server.Start();
        var endpointPath = Path.Combine(stateDir, "rpc-endpoint.json");
        Assert.True(File.Exists(endpointPath));
        var endpoint = JsonSerializer.Deserialize<RpcEndpoint>(File.ReadAllText(endpointPath), RpcFraming.Options);
        Assert.Equal(server.PipeName, endpoint!.PipeName);
        Assert.Equal(RpcProtocol.Version, endpoint.ProtocolVersion);

        server.Stop();
        Assert.False(File.Exists(endpointPath));
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

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 20000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (await condition().ConfigureAwait(false))
                return true;
            await Task.Delay(100).ConfigureAwait(false);
        }

        return await condition().ConfigureAwait(false);
    }
}
