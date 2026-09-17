using System.Text.Json;
using CrashScope.Contracts;
using CrashScope.Core.Analysis;
using CrashScope.Core.Ipc;
using CrashScope.Core.Monitoring;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class IncidentCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crashscope-cat-{Guid.NewGuid():N}");

    public IncidentCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* temp cleaner gets the rest. */ }
    }

    internal string SeedIncidentsRoot()
    {
        var root = Path.Combine(_root, "incidents");
        Directory.CreateDirectory(root);

        var good = Path.Combine(root, "20260917-120000-abc12345");
        Directory.CreateDirectory(good);
        File.WriteAllText(Path.Combine(good, "analysis.json"),
            "{\"previousSession\":{\"sessionId\":\"abc12345\",\"startedAt\":\"2026-09-17T11:00:00+08:00\",\"lastHeartbeatAt\":\"2026-09-17T12:00:00+08:00\"}," +
            "\"hypotheses\":[{\"mode\":\"Tdr\",\"score\":3,\"confidence\":0.7,\"evidence\":[]}]}");
        File.WriteAllText(Path.Combine(good, "summary.md"), "# CrashScope Incident Summary\n\nSeeded bundle for tests.");
        File.WriteAllText(Path.Combine(good, "timeline.md"), "# CrashScope Incident Timeline\n\n| Time | Category | Description |");

        var corrupt = Path.Combine(root, "20260916-110000-deadbeef");
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "analysis.json"), "{ this is not json");

        Directory.CreateDirectory(Path.Combine(root, "20260915-100000-noanalysis"));
        return root;
    }

    [Fact]
    public void List_MissingRoot_ReturnsEmpty()
    {
        Assert.Empty(IncidentCatalog.List(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void List_ParsesSortsAndSkipsBrokenBundles()
    {
        var root = SeedIncidentsRoot();

        var entries = IncidentCatalog.List(root);

        var entry = Assert.Single(entries);
        Assert.Equal("20260917-120000-abc12345", entry.Id);
        Assert.Equal("Tdr", entry.LeadingMode);
        Assert.Equal(0.7, entry.Confidence);
        Assert.Equal(1, entry.HypothesisCount);
        Assert.True(entry.HasSummary);
        Assert.True(entry.HasTimeline);
    }

    [Fact]
    public void Readers_RejectBadIdsAndReadContent()
    {
        var root = SeedIncidentsRoot();

        Assert.Null(IncidentCatalog.ReadSummary(root, "../evil"));
        Assert.Null(IncidentCatalog.ReadSummary(root, "no-such-dir"));
        Assert.Contains("Seeded bundle", IncidentCatalog.ReadSummary(root, "20260917-120000-abc12345"));
        Assert.StartsWith("# CrashScope Incident Timeline",
            IncidentCatalog.ReadTimeline(root, "20260917-120000-abc12345"));
    }
}

public sealed class IncidentRpcTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crashscope-irpc-{Guid.NewGuid():N}");

    public IncidentRpcTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* Windows may still hold a pipe handle for a moment. */ }
    }

    [Fact]
    public async Task Incidents_OverPipe_ListDetailAndGuards()
    {
        var incidentsRoot = new IncidentCatalogTests().SeedIncidentsRoot();
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        var server = new AgentRpcServer(
            engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8], incidentsRoot);
        using var _e = engine;
        using var _s = server;
        server.Start();

        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);
        await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("tests", RpcProtocol.Version));

        var list = (await client.SendRequestAsync(RpcMessages.GetIncidents))!.Value
            .Deserialize<IncidentListPayload>(RpcFraming.Options);
        var entry = Assert.Single(list!.Incidents);
        Assert.Equal("20260917-120000-abc12345", entry.Id);
        Assert.Equal("Tdr", entry.LeadingMode);

        var detail = (await client.SendRequestAsync(
                RpcMessages.GetIncidentDetail, new IncidentDetailRequest(entry.Id)))!.Value
            .Deserialize<IncidentDetailPayload>(RpcFraming.Options);
        Assert.NotNull(detail);
        Assert.Contains("Seeded bundle", detail!.Summary);
        Assert.StartsWith("# CrashScope Incident Timeline", detail.Timeline);

        var traversal = await Assert.ThrowsAsync<RpcException>(() => client.SendRequestAsync(
            RpcMessages.GetIncidentDetail, new IncidentDetailRequest("../evil")));
        Assert.Equal("bad-incident-id", traversal.Code);

        var missing = await Assert.ThrowsAsync<RpcException>(() => client.SendRequestAsync(
            RpcMessages.GetIncidentDetail, new IncidentDetailRequest("no-such-dir")));
        Assert.Equal("incident-not-found", missing.Code);
    }

    [Fact]
    public async Task Incidents_NoRootConfigured_ReturnsEmpty()
    {
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        var server = new AgentRpcServer(engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8]);
        using var _e = engine;
        using var _s = server;
        server.Start();

        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);
        await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("tests", RpcProtocol.Version));

        var list = (await client.SendRequestAsync(RpcMessages.GetIncidents))!.Value
            .Deserialize<IncidentListPayload>(RpcFraming.Options);
        Assert.NotNull(list);
        Assert.Empty(list!.Incidents);
    }
}
