using System.Text.Json;
using CrashScope.Contracts;
using CrashScope.Core.Analysis;
using CrashScope.Core.Infrastructure;
using CrashScope.Core.Ipc;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

/// <summary>No hardware collectors, ETW sessions, process priority changes, or real data roots.</summary>
public sealed class ReliabilityConvergenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "crashscope-reliability-" + Guid.NewGuid().ToString("N"));
    private string State => Path.Combine(_root, "state");
    private string Incidents => Path.Combine(_root, "incidents");
    private string Sessions => Path.Combine(_root, "sessions");
    private string StateFile => Path.Combine(State, "last-session.json");

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private SessionJournal Seed(string name = "previous")
    {
        var directory = Path.Combine(Sessions, name);
        Directory.CreateDirectory(directory);
        var journal = new SessionJournal(State, directory);
        journal.Heartbeat(DateTimeOffset.Now);
        return journal;
    }

    private static IncidentAnalysis CollectFake(SessionState previous, string directory)
    {
        Directory.CreateDirectory(directory);
        var analysis = new IncidentAnalysis(previous, previous.LastHeartbeatAt, 0, 0, null, [], [], [], [], GraphicsContext.Empty);
        File.WriteAllText(Path.Combine(directory, "analysis.json"), JsonSerializer.Serialize(analysis, AtomicJsonFile.Options));
        File.WriteAllText(Path.Combine(directory, "summary.md"), "Summary");
        File.WriteAllText(Path.Combine(directory, "timeline.md"), "Timeline");
        return analysis;
    }

    private RecorderEngine Engine(Action? initialize = null, Action<CancellationToken>? tick = null,
        Action? dispose = null, Func<SessionState, string, IncidentAnalysis>? collect = null)
        => new(Path.Combine(Sessions, "same-prefix"), State, Incidents,
            new RecorderRuntimeHooks(initialize ?? (() => { }), tick ?? (_ => { }), dispose ?? (() => { }),
                TimeSpan.FromMilliseconds(10), collect ?? CollectFake));

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public void ReadPrevious_IsReadOnly()
    {
        var previous = Seed().Current;
        var bytes = File.ReadAllBytes(StateFile);
        Assert.Equal(previous, SessionJournal.ReadPrevious(State));
        Assert.Equal(bytes, File.ReadAllBytes(StateFile));
        Assert.False(Directory.Exists(SessionRecovery.PendingDirectory(State)));
    }

    [Fact]
    public void CorruptState_IsNotOverwrittenByNewSession()
    {
        Directory.CreateDirectory(State);
        File.WriteAllText(StateFile, "{broken");
        Assert.Throws<JsonException>(() => new SessionJournal(State, Path.Combine(Sessions, "new")));
        Assert.Equal("{broken", File.ReadAllText(StateFile));
    }

    [Fact]
    public void ReplacingUncleanState_PreservesDurableRecoveryJob()
    {
        var previous = Seed().Current;
        _ = new SessionJournal(State, Path.Combine(Sessions, "next"));
        var job = Path.Combine(SessionRecovery.PendingDirectory(State), previous.SessionId + ".json");
        var saved = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(job), AtomicJsonFile.Options);
        Assert.Equal(previous, saved);
    }

    [Fact]
    public void FailedHeartbeat_DoesNotPublishUncommittedMemoryState()
    {
        var journal = new SessionJournal(State, Path.Combine(Sessions, "new"));
        var before = journal.Current;
        using var blocker = new FileStream(StateFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var failure = Record.Exception(() => journal.Heartbeat(DateTimeOffset.Now));
        // Windows may expose a denied replacement as access denied or a sharing violation.
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Equal(before, journal.Current);
        Assert.True(journal.Current.NeverStarted);
    }

    [Fact]
    public void CorruptPendingJob_DoesNotAllowOverwritingLastSession()
    {
        var previous = Seed().Current;
        SessionRecovery.Preserve(State, previous);
        File.WriteAllText(Path.Combine(SessionRecovery.PendingDirectory(State), previous.SessionId + ".json"), "broken");
        var original = File.ReadAllBytes(StateFile);
        Assert.Throws<JsonException>(() => new SessionJournal(State, Path.Combine(Sessions, "next")));
        Assert.Equal(original, File.ReadAllBytes(StateFile));
    }

    [Fact]
    public async Task CleanupFailure_RemainsFaultedAndUnclean_ButReleasesOwnership()
    {
        using var engine = Engine(dispose: () => throw new IOException("cleanup failed"));
        engine.Start();
        await engine.StopAsync();
        Assert.Equal(RecorderState.Faulted, engine.State);
        Assert.False(SessionJournal.ReadPrevious(State)!.CleanShutdown);
        Assert.Contains("cleanup failed", engine.LastError);
        using var next = DataRootLease.Acquire(State);
    }

    [Fact]
    public void Lease_RejectsSecondOwner_AndReleases()
    {
        using (var first = DataRootLease.Acquire(State))
        {
            Assert.Null(DataRootLease.TryAcquire(State));
            Assert.Throws<InvalidOperationException>(() => DataRootLease.Acquire(State));
        }
        using var next = DataRootLease.Acquire(State);
        Assert.NotNull(next);
    }

    [Fact]
    public void Recovery_WhileOwned_DoesNotRewriteOrCollect()
    {
        Seed();
        var bytes = File.ReadAllBytes(StateFile);
        using var owner = DataRootLease.Acquire(State);
        Assert.Null(SessionRecovery.Recover(State, Incidents, (_, _) => throw new Exception("must not collect")));
        Assert.Equal(bytes, File.ReadAllBytes(StateFile));
        Assert.False(Directory.Exists(Incidents));
    }

    [Fact]
    public void HostPreStartRecovery_WorksWithoutInitializingHardware_AndIsIdempotent()
    {
        var previous = Seed().Current;
        var count = 0;
        using var engine = Engine(initialize: () => throw new Exception("must not initialize"), collect: (s, d) =>
        { count++; return CollectFake(s, d); });
        Assert.Equal(previous.SessionId, engine.CheckPreviousUncleanSession()!.PreviousSession.SessionId);
        Assert.Null(engine.CheckPreviousUncleanSession());
        Assert.Equal(1, count);
        Assert.Single(Directory.GetDirectories(Incidents));
        Assert.Equal(previous, SessionJournal.ReadPrevious(State));
    }

    [Fact]
    public void FailedRecovery_SurvivesNewSession_AndRetries()
    {
        var previous = Seed().Current;
        Assert.Throws<IOException>(() => SessionRecovery.Recover(State, Incidents, (_, _) => throw new IOException("fake disk failure")));
        var current = new SessionJournal(State, Path.Combine(Sessions, "new"));
        current.MarkClean();
        var recovered = SessionRecovery.Recover(State, Incidents, CollectFake);
        Assert.Equal(previous.SessionId, recovered!.PreviousSession.SessionId);
        Assert.Empty(Directory.GetFiles(SessionRecovery.PendingDirectory(State), "*.json"));
        Assert.Null(SessionRecovery.Recover(State, Incidents, (_, _) => throw new Exception("must not repeat")));
    }

    [Fact]
    public void PublishedIncidentWithoutReceipt_IsFinalizedWithoutRecollection()
    {
        var previous = Seed().Current;
        SessionRecovery.Preserve(State, previous);
        CollectFake(previous, Path.Combine(Incidents, "recovered-" + previous.SessionId));
        var recovered = SessionRecovery.Recover(State, Incidents, (_, _) => throw new Exception("already published"));
        Assert.Equal(previous.SessionId, recovered!.PreviousSession.SessionId);
        Assert.Empty(Directory.GetFiles(SessionRecovery.PendingDirectory(State), "*.json"));
    }

    [Fact]
    public void IncompleteRecovery_IsNotPublished()
    {
        Seed();
        Assert.Throws<InvalidDataException>(() => SessionRecovery.Recover(State, Incidents, (s, d) =>
        {
            var analysis = CollectFake(s, d);
            File.Delete(Path.Combine(d, "timeline.md"));
            return analysis;
        }));
        Assert.False(Directory.Exists(Incidents));
        Assert.Single(Directory.GetFiles(SessionRecovery.PendingDirectory(State), "*.json"));
    }

    [Fact]
    public async Task Start_PerformsRecoveryEvenWithoutPreStartCheck()
    {
        var previous = Seed().Current;
        using var engine = Engine();
        engine.Start();
        await engine.StopAsync();
        Assert.True(File.Exists(Path.Combine(Incidents, "recovered-" + previous.SessionId, "analysis.json")));
        Assert.NotEqual(previous.SessionId, engine.CurrentSession!.SessionId);
    }

    [Fact]
    public async Task Start_ContinuesWithDurablePendingEvidence_WhenCollectorFails()
    {
        var previous = Seed().Current;
        using var engine = Engine(collect: (_, _) => throw new IOException("collector unavailable"));
        engine.Start();
        Assert.True(engine.IsRecording);
        Assert.Contains("collector unavailable", engine.RecoveryError);
        Assert.True(File.Exists(Path.Combine(SessionRecovery.PendingDirectory(State), previous.SessionId + ".json")));
        await engine.StopAsync();
    }

    [Fact]
    public async Task Restart_UsesDistinctDirectoryAndSessionId()
    {
        using var engine = Engine();
        engine.Start();
        await Until(() => engine.SampleCount > 0);
        var first = engine.CurrentSession!;
        File.WriteAllText(Path.Combine(first.SessionDirectory, "sentinel.txt"), "original evidence");
        await engine.StopAsync();
        engine.Start();
        var second = engine.CurrentSession!;
        await engine.StopAsync();
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.NotEqual(first.SessionDirectory, second.SessionDirectory);
        Assert.Equal("original evidence", File.ReadAllText(Path.Combine(first.SessionDirectory, "sentinel.txt")));
        Assert.True(engine.CurrentSession!.CleanShutdown);
    }

    [Fact]
    public async Task FirstSample_IsRecoverableBeforeFiveTicks()
    {
        using var engine = Engine();
        engine.Start();
        await Until(() => engine.SampleCount > 0 && engine.CurrentSession?.NeverStarted == false);
        Assert.False(SessionJournal.ReadPrevious(State)!.NeverStarted);
        Assert.NotNull(engine.LastSampleAt);
        await engine.StopAsync();
    }

    [Fact]
    public async Task FailedTick_IsFaulted_NotRecording_AndNotMarkedClean()
    {
        using var engine = Engine(tick: _ => throw new IOException("simulated storage failure"));
        engine.Start();
        await Until(() => engine.State == RecorderState.Faulted);
        await engine.StopAsync();
        Assert.False(engine.IsRecording);
        Assert.Contains("simulated storage failure", engine.LastError);
        var persisted = SessionJournal.ReadPrevious(State)!;
        Assert.False(persisted.CleanShutdown);
        Assert.Contains("simulated storage failure", persisted.RecorderError);
        Assert.False(engine.HasActiveResources);
    }

    [Fact]
    public async Task ThrowingObserver_DoesNotEndSampling()
    {
        using var engine = Engine();
        engine.StatusChanged += () => throw new Exception("bad status observer");
        engine.SensorUpdated += _ => throw new Exception("bad sensor observer");
        engine.Start();
        await Until(() => engine.SampleCount >= 3);
        Assert.True(engine.IsRecording);
        Assert.Null(engine.LastError);
        await engine.StopAsync();
        Assert.True(engine.CurrentSession!.CleanShutdown);
    }

    [Fact]
    public void InitializationFailure_CleansResourcesAndReleasesLease()
    {
        var disposed = false;
        using var engine = Engine(initialize: () => throw new IOException("init failed"), dispose: () => disposed = true);
        Assert.Throws<IOException>(engine.Start);
        Assert.True(disposed);
        Assert.Equal(RecorderState.Faulted, engine.State);
        Assert.False(engine.HasActiveResources);
        using var next = DataRootLease.Acquire(State);
    }

    [Fact]
    public async Task SlowStop_RetainsResourcesAndOwnershipUntilTickExits()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var disposed = false;
        using var engine = Engine(tick: _ => { entered.Set(); release.Wait(); }, dispose: () => disposed = true);
        var stoppingPublished = false;
        engine.StatusChanged += () => { if (engine.State == RecorderState.Stopping) stoppingPublished = true; };
        engine.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            await Task.Run(engine.Stop); // compatibility stop returns after its bounded 3-second wait
            Assert.Equal(RecorderState.Stopping, engine.State);
            Assert.True(stoppingPublished);
            Assert.False(disposed);
            Assert.Null(DataRootLease.TryAcquire(State));
            Assert.False(engine.CurrentSession!.CleanShutdown);
            Assert.Throws<InvalidOperationException>(engine.Start);
        }
        finally
        {
            release.Set();
            await engine.StopAsync();
        }
        Assert.True(disposed);
        Assert.Equal(RecorderState.Stopped, engine.State);
        using var next = DataRootLease.Acquire(State);
    }

    [Fact]
    public async Task SecondEngine_CannotOverwriteActiveState()
    {
        using var first = Engine();
        using var second = Engine();
        first.Start();
        var id = first.CurrentSession!.SessionId;
        Assert.Throws<InvalidOperationException>(second.Start);
        Assert.Equal(id, SessionJournal.ReadPrevious(State)!.SessionId);
        await first.StopAsync();
    }

    [Fact]
    public void Retention_WhileOwned_SkipsAllDeletion()
    {
        var previous = Seed().Current;
        File.WriteAllBytes(Path.Combine(previous.SessionDirectory, "evidence.bin"), new byte[1024 * 1024]);
        var retention = new RetentionService(_root);
        retention.Settings.MaxTotalSizeMb = 1;
        using var owner = DataRootLease.Acquire(State);
        Assert.Equal((0, 0, 0L), retention.Enforce());
        Assert.True(File.Exists(Path.Combine(previous.SessionDirectory, "evidence.bin")));
    }

    [Fact]
    public void Retention_ProtectsCurrentAndPending_ButDeletesUnprotectedEvidence()
    {
        var pending = Seed("pending").Current;
        var current = new SessionJournal(State, Path.Combine(Sessions, "current"));
        var old = Path.Combine(Sessions, "old");
        foreach (var directory in new[] { pending.SessionDirectory, current.Current.SessionDirectory, old })
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "evidence.bin"), new byte[700_000]);
            Directory.SetCreationTime(directory, DateTime.Now.AddDays(-90));
        }
        var retention = new RetentionService(_root);
        retention.Settings.MaxTotalSizeMb = 1;
        retention.Enforce();
        Assert.True(File.Exists(Path.Combine(pending.SessionDirectory, "evidence.bin")));
        Assert.True(File.Exists(Path.Combine(current.Current.SessionDirectory, "evidence.bin")));
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void Retention_CorruptProtectionState_FailsClosed()
    {
        var previous = Seed().Current;
        File.WriteAllBytes(Path.Combine(previous.SessionDirectory, "evidence.bin"), new byte[1024 * 1024]);
        File.WriteAllText(StateFile, "corrupted");
        var retention = new RetentionService(_root);
        retention.Settings.MaxTotalSizeMb = 1;
        Assert.Equal((0, 0, 0L), retention.Enforce());
        Assert.True(Directory.Exists(previous.SessionDirectory));
    }

    [Fact]
    public async Task Rpc_ReportsFaultAndSupportsIsolatedRestart()
    {
        var shouldFail = false;
        using var engine = Engine(tick: _ => { if (shouldFail) throw new IOException("RPC fault test"); });
        using var server = new AgentRpcServer(engine, State, "crashscope-reliability-" + Guid.NewGuid().ToString("N"));
        server.Start();
        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName);
        await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("reliability-tests", RpcProtocol.Version));
        await client.SendRequestAsync(RpcMessages.StartRecording);
        var first = engine.CurrentSession!.SessionId;
        await client.SendRequestAsync(RpcMessages.StopRecording);
        await client.SendRequestAsync(RpcMessages.StartRecording);
        Assert.NotEqual(first, engine.CurrentSession!.SessionId);
        shouldFail = true;
        await Until(() => engine.State == RecorderState.Faulted);
        var payload = await client.SendRequestAsync(RpcMessages.GetStatus);
        var status = payload!.Value.Deserialize<StatusPayload>(RpcFraming.Options)!;
        Assert.False(status.Recording);
        Assert.Equal("Faulted", status.RecorderState);
        Assert.Contains("RPC fault test", status.RecorderError);
        await client.SendRequestAsync(RpcMessages.StopRecording);
    }

    [Fact]
    public void LegacyStatusPayload_StillDeserializes()
    {
        var status = JsonSerializer.Deserialize<StatusPayload>("{\"recording\":false,\"sampleCount\":0,\"uptimeSeconds\":0,\"agentVersion\":\"0.9\"}", RpcFraming.Options);
        Assert.NotNull(status);
        Assert.Null(status.RecorderState);
    }

    [Fact]
    public void RecorderFault_IsExplicitInIncidentSummary()
    {
        var previous = Seed().Current with { RecorderError = "IO failure" };
        var directory = Path.Combine(Incidents, "report-test");
        var analysis = CollectFake(previous, directory);
        ReportGenerator.WriteSummary(analysis, directory);
        var summary = File.ReadAllText(Path.Combine(directory, "summary.md"));
        Assert.Contains("Recorder fault", summary);
        Assert.Contains("not proof of a system crash", summary);
    }
}
