using CrashScope.Core.Monitoring;
using CrashScope.Core.Ipc;
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
string? pipeNameOverride = null;
foreach (var arg in args)
{
    if (arg.StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
        dataRootOverride = arg["--data-root=".Length..];
    else if (arg.StartsWith("--pipe-name=", StringComparison.OrdinalIgnoreCase))
        pipeNameOverride = arg["--pipe-name=".Length..];
    else if (arg is "--no-rpc")
        rpcEnabled = false;
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
                      $"(confidence {leading?.Confidence ?? 0:P0}). Details: summary.md");
}

engine.Start();

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
    rpc = new AgentRpcServer(engine, stateDirectory, pipeNameOverride);
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

engine.Stop();
rpc?.Stop(); // close the control plane after the recorder: a UI sees status flip, then a clean disconnect
Console.WriteLine($"Stopped cleanly. Samples recorded: {engine.SampleCount}");
return 0;

static async Task<int> RunProbeAsync(string[] args)
{
    string? pipeName = null;
    string? stateDir = null;
    var listen = TimeSpan.FromSeconds(2);
    foreach (var arg in args)
    {
        if (arg.StartsWith("--pipe-name=", StringComparison.OrdinalIgnoreCase))
            pipeName = arg["--pipe-name=".Length..];
        else if (arg.StartsWith("--state-dir=", StringComparison.OrdinalIgnoreCase))
            stateDir = arg["--state-dir=".Length..];
        else if (arg.StartsWith("--duration=", StringComparison.OrdinalIgnoreCase))
            listen = TimeSpan.FromSeconds(double.Parse(arg["--duration=".Length..], CultureInfo.InvariantCulture));
        else if (arg is "-h" or "--help")
        {
            Console.WriteLine("Usage: CrashScope.Agent probe [--pipe-name=<name>] [--state-dir=<path>] [--duration=<seconds>]");
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
    Console.WriteLine("  --pipe-name=<name>  Serve RPC on <name> instead of the default PID-suffixed pipe.");
    Console.WriteLine("  probe               Inspect a running agent over RPC; see: probe --help.");
    Console.WriteLine("  -h, --help          Show this help.");
    Console.WriteLine();
    Console.WriteLine("Run elevated (Administrator) to capture the ETW kernel sessions.");
}
