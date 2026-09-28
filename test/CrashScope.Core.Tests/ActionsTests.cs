using System.Diagnostics;
using System.Text.Json;
using CrashScope.Contracts;
using CrashScope.Core.Actions;
using CrashScope.Core.Ipc;
using CrashScope.Core.Monitoring;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class FakePowerPlans : IPowerPlanService
{
    public bool SetSucceeds { get; set; } = true;
    private string _active = "balanced";

    public IReadOnlyList<PowerPlanInfo> GetPlans()
    {
        return
        [
            new PowerPlanInfo("balanced", "Balanced", _active == "balanced"),
            new PowerPlanInfo("high", "High performance", _active == "high")
        ];
    }

    public string? GetActive() => _active;

    public bool SetActive(string guid)
    {
        if (!SetSucceeds || (guid != "balanced" && guid != "high"))
            return false;
        _active = guid;
        return true;
    }
}

public sealed class ActionJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"crashscope-jrn-{Guid.NewGuid():N}");

    public ActionJournalTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* temp cleaner gets the rest. */ }
    }

    private static ActionRecord Record(string id, string action = "set-power-plan", string status = "applied") =>
        new(id, DateTimeOffset.Now, action, "target", "old", "new", "caller", "reason", status, null, true);

    [Fact]
    public void Append_ThenReadRecent_NewestFirst()
    {
        var journal = new ActionJournal(_dir);
        journal.Append(Record("a1"));
        journal.Append(Record("a2"));
        journal.Append(Record("a3"));

        var records = journal.ReadRecent();

        Assert.Equal(["a3", "a2", "a1"], records.Select(r => r.ActionId).ToArray());
    }

    [Fact]
    public void ReadRecent_SkipsCorruptLines()
    {
        // The corrupt line goes in first: the journal's write-through handle
        // (FileShare.Read) stays open, so nothing else may write after it exists.
        Directory.CreateDirectory(_dir);
        File.AppendAllText(Path.Combine(_dir, "actions.jsonl"), "{ this is not json\n");
        var journal = new ActionJournal(_dir);
        journal.Append(Record("good-1"));
        journal.Append(Record("good-2"));

        var records = journal.ReadRecent();

        Assert.Equal(["good-2", "good-1"], records.Select(r => r.ActionId).ToArray());
    }

    [Fact]
    public void ReadRecent_MissingFile_ReturnsEmpty()
    {
        var journal = new ActionJournal(_dir);
        Assert.Empty(journal.ReadRecent());
    }
}

public sealed class SystemActionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"crashscope-act-{Guid.NewGuid():N}");

    public SystemActionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* temp cleaner gets the rest. */ }
    }

    private SystemActions CreateActions(FakePowerPlans? power = null) =>
        new(power ?? new FakePowerPlans(), _dir);

    [Fact]
    public void SetPowerPlan_ThenRollback_RestoresActiveAndJournalsBoth()
    {
        var power = new FakePowerPlans();
        var actions = CreateActions(power);

        var applied = actions.SetPowerPlan("high", "test-caller", "make it fast");
        Assert.Equal("high", power.GetActive());
        Assert.Equal("balanced", applied.OldValue);

        var rollback = actions.Rollback(applied.ActionId, "test-caller", "undo it");
        Assert.Equal("balanced", power.GetActive());
        Assert.Equal(applied.ActionId, rollback.Target);

        var journal = actions.RecentActions();
        Assert.Equal(2, journal.Count);
        Assert.Equal("rollback", journal[0].Action);
        Assert.Equal("applied", journal[1].Status);
    }

    [Fact]
    public void SetPowerPlan_UnknownPlan_Throws()
    {
        var ex = Assert.Throws<ActionException>(() => CreateActions().SetPowerPlan("nope", "c", "r"));
        Assert.Equal("unknown-plan", ex.Code);
    }

    [Fact]
    public void SetPowerPlan_AlreadyActive_Throws()
    {
        var ex = Assert.Throws<ActionException>(() => CreateActions().SetPowerPlan("balanced", "c", "r"));
        Assert.Equal("already-active", ex.Code);
    }

    [Fact]
    public void SetPowerPlan_BlankReason_Throws()
    {
        var ex = Assert.Throws<ActionException>(() => CreateActions().SetPowerPlan("high", "c", "  "));
        Assert.Equal("reason-required", ex.Code);
    }

    [Fact]
    public void SetPowerPlan_FailedSet_IsJournaledAsFailed()
    {
        var power = new FakePowerPlans { SetSucceeds = false };
        var actions = CreateActions(power);

        var ex = Assert.Throws<ActionException>(() => actions.SetPowerPlan("high", "c", "r"));
        Assert.Equal("set-failed", ex.Code);

        var journal = actions.RecentActions();
        var failed = Assert.Single(journal);
        Assert.Equal("failed", failed.Status);
        Assert.False(failed.Reversible);
    }

    [Fact]
    public void SetPriority_OnOwnProcess_ThenRollback_RestoresPriority()
    {
        var actions = CreateActions();
        var pid = Environment.ProcessId;
        try
        {
            var applied = actions.SetProcessPriority(pid, "AboveNormal", "c", "test");
            Assert.Equal("Normal", applied.OldValue);
            Assert.Equal(ProcessPriorityClass.AboveNormal, Process.GetProcessById(pid).PriorityClass);

            actions.Rollback(applied.ActionId, "c", "undo");
            Assert.Equal(ProcessPriorityClass.Normal, Process.GetProcessById(pid).PriorityClass);
        }
        finally
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
        }
    }

    [Fact]
    public void SetPriority_RealTime_IsRejected()
    {
        var ex = Assert.Throws<ActionException>(
            () => CreateActions().SetProcessPriority(Environment.ProcessId, "RealTime", "c", "r"));
        Assert.Equal("unknown-priority", ex.Code);
    }

    [Fact]
    public void SetPriority_DeadPid_ThrowsProcessNotFound()
    {
        var ex = Assert.Throws<ActionException>(() => CreateActions().SetProcessPriority(int.MaxValue, "High", "c", "r"));
        Assert.Equal("process-not-found", ex.Code);
    }

    [Fact]
    public void Rollback_UnknownId_Throws()
    {
        var ex = Assert.Throws<ActionException>(() => CreateActions().Rollback("no-such-id", "c", "r"));
        Assert.Equal("rollback-target-not-found", ex.Code);
    }
}

public sealed class ActionsRpcTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crashscope-arpc-{Guid.NewGuid():N}");

    public ActionsRpcTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* Windows may still hold a pipe handle for a moment. */ }
    }

    private (RecorderEngine Engine, AgentRpcServer Server, SystemActions Actions) StartServer(bool withActions)
    {
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        SystemActions? actions = withActions ? new SystemActions(new FakePowerPlans(), stateDir) : null;
        var server = new AgentRpcServer(engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8], null, actions);
        server.Start();
        return (engine, server, actions!);
    }

    [Fact]
    public async Task FullMutationCycle_OverPipe()
    {
        var (engine, server, _) = StartServer(withActions: true);
        using var _e = engine;
        using var _s = server;
        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);
        await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("tests", RpcProtocol.Version));

        var plans = (await client.SendRequestAsync(RpcMessages.GetPowerPlans))!.Value
            .Deserialize<PowerPlansPayload>(RpcFraming.Options);
        Assert.Equal(2, plans!.Plans.Count);
        Assert.Single(plans.Plans, p => p.IsActive);

        await client.SendRequestAsync(RpcMessages.SetPowerPlan, new SetPowerPlanRequest("high", "rpc test"));

        var actions = (await client.SendRequestAsync(RpcMessages.GetActions))!.Value
            .Deserialize<ActionsPayload>(RpcFraming.Options);
        var applied = Assert.Single(actions!.Actions);
        Assert.Equal("set-power-plan", applied.Action);
        Assert.Equal("tests", applied.Caller);

        var reasonRequired = await Assert.ThrowsAsync<RpcException>(() => client.SendRequestAsync(
            RpcMessages.RollbackAction, new RollbackActionRequest(applied.ActionId, "")));
        Assert.Equal("reason-required", reasonRequired.Code);

        await client.SendRequestAsync(RpcMessages.RollbackAction, new RollbackActionRequest(applied.ActionId, "undo"));
        var after = (await client.SendRequestAsync(RpcMessages.GetActions))!.Value
            .Deserialize<ActionsPayload>(RpcFraming.Options);
        Assert.Equal(2, after!.Actions.Count);
        Assert.Equal("rollback", after.Actions[0].Action);
    }

    [Fact]
    public async Task Mutations_WithoutActionsPlane_AreRefused()
    {
        var (engine, server, _) = StartServer(withActions: false);
        using var _e = engine;
        using var _s = server;
        using var client = new AgentRpcClient();
        await client.ConnectAsync(server.PipeName, 15000);
        await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("tests", RpcProtocol.Version));

        var ex = await Assert.ThrowsAsync<RpcException>(() => client.SendRequestAsync(
            RpcMessages.SetPowerPlan, new SetPowerPlanRequest("high", "r")));
        Assert.Equal("actions-unavailable", ex.Code);

        var plans = (await client.SendRequestAsync(RpcMessages.GetPowerPlans))!.Value
            .Deserialize<PowerPlansPayload>(RpcFraming.Options);
        Assert.NotNull(plans);
        Assert.Empty(plans!.Plans);
    }
}

public sealed class AgentActionsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crashscope-avm-{Guid.NewGuid():N}");

    public AgentActionsViewModelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* Windows may still hold a pipe handle for a moment. */ }
    }

    [Fact]
    public async Task ConnectRefreshMutateRollback_OverLiveServer()
    {
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        var server = new AgentRpcServer(
            engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8], null,
            new SystemActions(new FakePowerPlans(), stateDir));
        using var _e = engine;
        using var _s = server;
        server.Start();

        var vm = new AgentActionsViewModel();
        Assert.True(await vm.ConnectAsync(stateDir), vm.StatusMessage);
        Assert.Equal(2, vm.Plans.Count);
        Assert.NotEmpty(vm.Processes);

        await vm.SetPowerPlanAsync("high", "vm test");
        // Without a WPF App, Loc.F falls back to the raw resource key, so a successful
        // mutation surfaces as the success-status key rather than formatted text.
        Assert.Equal("agentActions.status.done", vm.StatusMessage);
        Assert.Single(vm.Actions);

        await vm.RollbackAsync(vm.Actions[0].ActionId, "vm undo");
        Assert.Equal(2, vm.Actions.Count);

        vm.Disconnect();
        Assert.False(vm.IsConnected);
    }
}
