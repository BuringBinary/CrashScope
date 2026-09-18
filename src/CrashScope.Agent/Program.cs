using System.Text.Json;
using CrashScope.Core.Monitoring;
using CrashScope.Core.Ipc;
using CrashScope.Core.Actions;
using CrashScope.Contracts;
using System.Globalization;

// CrashScope CLI agent.
//
// This used to run its own inline 1 Hz capture loop, which duplicated RecorderEngine and
// silently omitted the three real-time ETW sessions (DxgKrnl / Kernel-Power / WHEA). A field
// deployment via the CLI agent therefore missed every piece of kernel-level evidence - exactly
// the evidence the project exists to capture. The agent is now a thin host over RecorderEngine,
// so CLI, Service and Desktop share one recording path.

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("CrashScope currently supports Windows only.");
    return 2;
}

if (args.Length > 0 && args[0] is "probe")
{
    return await RunProbeAsync(args.Skip(1).ToArray());
}

string? dataRootOverride = null;
var rpcEnabled = true;
var actionsEnabled = true;
string? pipeNameOverride = null;
foreach (var arg in args)
{
    if (arg.StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
        dataRootOverride = arg["--data-root=".Length..];
    else if (arg.StartsWith("--pipe-name=", StringComparison.OrdinalIgnoreCase))
        pipeNameOverride = arg["--pipe-name=".Length..];
    else if (arg is "--no-rpc")
        rpcEnabled = false;
    else if (arg is "--no-actions")
        actionsEnabled = false;
    else if (arg is "-h" or "--help")
    {
        PrintUsage();
        return 0;
    }
}

// Default is per-user local app data, not %ProgramData%: the recommended field deployment is a
// scheduled task running in the user session (RunLevel Highest), which keeps foreground-window
// and idle-time fidelity. A per-user root stays writable even when the agent is started without
// elevation. Pass --data-root to share a root with the Service.
var root = string.IsNullOrWhiteSpace(dataRootOverride)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashScope")
    : Path.GetFullPath(dataRootOverride);

var stateDirectory = Path.Combine(root, "state");
var sessionsRoot = Path.Combine(root, "sessions");
var incidentsRoot = Path.Combine(root, "incidents");
Directory.CreateDirectory(stateDirectory);
Directory.CreateDirectory(sessionsRoot);
Directory.CreateDirectory(incidentsRoot);

var sessionDirectory = Path.Combine(sessionsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

using var engine = new RecorderEngine(sessionDirectory, stateDirectory, incidentsRoot);
using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    // Take over shutdown: letting the process be killed here would leave the session journal
    // marked unclean and fabricate an incident on the next start.
    e.Cancel = true;
    shutdown.Cancel();
};

// Also covers closing the console window and `taskkill` without /F.
AppDomain.CurrentDomain.ProcessExit += (_, _) => engine.Stop();

var version = typeof(RecorderEngine).Assembly.GetName().Version?.ToString(3) ?? "0.9.0";
Console.WriteLine($"CrashScope v{version}");
Console.WriteLine($"Data root: {root}");

var previousIncident = engine.CheckPreviousUncleanSession();
if (previousIncident is not null)
{
    Console.WriteLine("Detected previous unclean session; incident evidence collected.");
    var leading = previousIncident.Hypotheses.FirstOrDefault(h => h.Score > 0);
    Console.WriteLine($"Preliminary ranking: {leading?.Mode.ToString() ?? "Unclassified"} " +
                      $"(heuristic evidence support {leading?.Confidence ?? 0:P0}; not probability). Details: summary.md");
}

engine.StatusChanged += () =>
{
    if (engine.LastError is not null)
        Console.Error.WriteLine($"Recorder {engine.State}: {engine.LastError}");
};
engine.Start();
if (engine.RecoveryError is not null)
    Console.Error.WriteLine($"Recovery deferred (evidence preserved): {engine.RecoveryError}");

var session = engine.CurrentSession;
Console.WriteLine($"Session: {session?.SessionId}");
Console.WriteLine($"Session directory: {sessionDirectory}");
Console.WriteLine($"Elevated: {session?.Elevated}");
Console.WriteLine($"GPU adapter for reported facts: {engine.PrimaryGpuHardwareName ?? "(no GPU sensors)"}");
if (session?.Elevated == false)
    Console.WriteLine("Warning: running without Administrator privileges; the ETW kernel sessions " +
                      "(GPU / power / WHEA) and some event-log details will be unavailable.");
Console.WriteLine("Press Ctrl+C to stop cleanly.");
// The RPC surface is the agent's observation/control plane for the UI shell
// (docs/rpc-protocol.md): status, sensor catalog, live telemetry, start/stop.
// It is read-mostly and never drives the recording path itself; --no-rpc
// restores the pure-recorder posture for validation runs.
AgentRpcServer? rpc = null;
if (rpcEnabled)
{
    var actions = actionsEnabled ? new SystemActions(new PowerPlanService(), stateDirectory) : null;
    rpc = new AgentRpcServer(engine, stateDirectory, pipeNameOverride, incidentsRoot, actions);
    rpc.Start();
    Console.WriteLine($"RPC: named pipe '{rpc.PipeName}' (endpoint: {Path.Combine(stateDirectory, "rpc-endpoint.json")})");
}

try
{
    // RecorderEngine drives its own 1 Hz loop; the agent only has to stay alive.
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C.
}

await engine.StopAsync();
rpc?.Stop(); // close the control plane after the recorder: a UI sees status flip, then a clean disconnect
Console.WriteLine($"Recorder state: {engine.State}. Samples recorded: {engine.SampleCount}");
return engine.LastError is null ? 0 : 2;

static async Task<int> RunProbeAsync(string[] args)
{
    string? pipeName = null;
    string? stateDir = null;
    var listen = TimeSpan.FromSeconds(2);
    var exercise = false;
    foreach (var arg in args)
    {
        if (arg.StartsWith("--pipe-name=", StringComparison.OrdinalIgnoreCase))
            pipeName = arg["--pipe-name=".Length..];
        else if (arg.StartsWith("--state-dir=", StringComparison.OrdinalIgnoreCase))
            stateDir = arg["--state-dir=".Length..];
        else if (arg.StartsWith("--duration=", StringComparison.OrdinalIgnoreCase))
            listen = TimeSpan.FromSeconds(double.Parse(arg["--duration=".Length..], CultureInfo.InvariantCulture));
        else if (arg is "--exercise")
            exercise = true;
        else if (arg is "-h" or "--help")
        {
            Console.WriteLine("Usage: CrashScope.Agent probe [--pipe-name=<name>] [--state-dir=<path>] [--duration=<seconds>] [--exercise]");
            Console.WriteLine();
            Console.WriteLine("  Connects to a running agent's RPC pipe, prints status, a sensor catalog summary");
            Console.WriteLine("  and live telemetry, then exits. Default listen window: 2 s.");
            Console.WriteLine("  Without --pipe-name the endpoint is read from <state-dir>\\rpc-endpoint.json");
            Console.WriteLine("  (default state dir: %LOCALAPPDATA%\\CrashScope\\state).");
            return 0;
        }
    }

    stateDir ??= Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashScope", "state");
    Console.WriteLine($"Probing agent RPC (listen {listen.TotalSeconds:0.#} s)...");
    var result = await AgentRpcProbe.RunAsync(new RpcProbeOptions
    {
        PipeName = pipeName,
        StateDirectory = stateDir,
        Listen = listen
    });

    if (!result.Connected)
    {
        Console.Error.WriteLine($"Probe failed: {result.Error}");
        return 3;
    }

    Console.WriteLine($"Connected: {result.PipeName} (agent {result.Welcome?.AgentVersion}, protocol v{result.Welcome?.ProtocolVersion})");
    var status = result.Status;
    Console.WriteLine(status is null
        ? "Status: unavailable"
        : $"Recording: {(status.Recording ? "yes" : "no")} | Samples: {status.SampleCount} | Uptime: {status.UptimeSeconds:0} s | Primary GPU: {status.PrimaryGpu ?? "n/a"}");
    var entries = result.Catalog?.Entries ?? [];
    var hardware = entries.Select(e => e.HardwareName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    Console.WriteLine($"Sensors: {entries.Count} entries across {hardware} hardware");
    var latest = result.LatestTelemetry;
    Console.WriteLine(latest is not null && latest.Values.Count > 0
        ? $"Telemetry: {result.TelemetryUpdates} live updates; latest {latest.Values.Count} values @ {latest.Timestamp:HH:mm:ss}"
        : $"Telemetry: {result.TelemetryUpdates} live updates; none buffered (recorder idle?)");
    if (exercise && result.PipeName is not null)
        return await RunActionsExerciseAsync(result.PipeName);
    return 0;
}

static async Task<int> RunActionsExerciseAsync(string pipeName)
{
    using var client = new AgentRpcClient();
    await client.ConnectAsync(pipeName, 5000);
    await client.SendRequestAsync(RpcMessages.Hello, new HelloPayload("rpc-probe-exercise", RpcProtocol.Version));
    var options = RpcFraming.Options;
    Console.WriteLine("Actions exercise: every change below is rolled back before exit.");

    var plans = (await client.SendRequestAsync(RpcMessages.GetPowerPlans))!.Value
        .Deserialize<PowerPlansPayload>(options)!;
    var active = plans.Plans.FirstOrDefault(p => p.IsActive);
    var target = plans.Plans.FirstOrDefault(p => !p.IsActive);
    Console.WriteLine($"Power plans: {plans.Plans.Count} (active: {active?.Name ?? "n/a"})");
    if (target is null)
    {
        Console.WriteLine("Plan cycle skipped: the host has a single power plan.");
    }
    else
    {
        await client.SendRequestAsync(RpcMessages.SetPowerPlan, new SetPowerPlanRequest(target.Guid, "probe --exercise"));
        Console.WriteLine($"  set plan '{target.Name}'");
        var journal = (await client.SendRequestAsync(RpcMessages.GetActions))!.Value
            .Deserialize<ActionsPayload>(options)!;
        var applied = journal.Actions.First(a => a.Action == "set-power-plan" && a.Status == "applied");
        await client.SendRequestAsync(RpcMessages.RollbackAction, new RollbackActionRequest(applied.ActionId, "probe --exercise rollback"));
        Console.WriteLine($"  rolled back to '{active?.Name}'");
    }

    var probePid = Environment.ProcessId;
    await client.SendRequestAsync(RpcMessages.SetProcessPriority, new SetProcessPriorityRequest(probePid, "BelowNormal", "probe --exercise"));
    Console.WriteLine($"  set own priority (pid {probePid}) to BelowNormal");
    var journal2 = (await client.SendRequestAsync(RpcMessages.GetActions))!.Value
        .Deserialize<ActionsPayload>(options)!;
    var appliedPriority = journal2.Actions.First(a => a.Action == "set-process-priority" && a.Status == "applied");
    await client.SendRequestAsync(RpcMessages.RollbackAction, new RollbackActionRequest(appliedPriority.ActionId, "probe --exercise rollback"));
    Console.WriteLine("  rolled back priority");
    Console.WriteLine("ACTIONS EXERCISE PASSED (all changes restored).");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("CrashScope agent - Windows flight recorder for crashes, freezes and GPU failures.");
    Console.WriteLine();
    Console.WriteLine("Usage: CrashScope.Agent [--data-root=<path>] [--no-rpc] [--pipe-name=<name>] | probe [...]");
    Console.WriteLine();
    Console.WriteLine("  --data-root=<path>  Record under <path> instead of %LOCALAPPDATA%\\CrashScope.");
    Console.WriteLine("                      Use the Service root (%ProgramData%\\CrashScope) to share data.");
    Console.WriteLine("  --no-rpc            Disable the local RPC control plane (headless recorder).");
    Console.WriteLine("  --no-actions        Disable system-mutation verbs (power plan / process priority).");
    Console.WriteLine("  --pipe-name=<name>  Serve RPC on <name> instead of the default PID-suffixed pipe.");
    Console.WriteLine("  probe               Inspect a running agent over RPC; see: probe --help.");
    Console.WriteLine("  -h, --help          Show this help.");
    Console.WriteLine();
    Console.WriteLine("Run elevated (Administrator) to capture the ETW kernel sessions.");
}
