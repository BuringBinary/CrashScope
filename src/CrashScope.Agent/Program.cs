using CrashScope.Core.Monitoring;

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

string? dataRootOverride = null;
foreach (var arg in args)
{
    if (arg.StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
        dataRootOverride = arg["--data-root=".Length..];
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
Console.WriteLine($"Stopped cleanly. Samples recorded: {engine.SampleCount}");
return 0;

static void PrintUsage()
{
    Console.WriteLine("CrashScope agent - Windows flight recorder for crashes, freezes and GPU failures.");
    Console.WriteLine();
    Console.WriteLine("Usage: CrashScope.Agent [--data-root=<path>]");
    Console.WriteLine();
    Console.WriteLine("  --data-root=<path>  Record under <path> instead of %LOCALAPPDATA%\\CrashScope.");
    Console.WriteLine("                      Use the Service root (%ProgramData%\\CrashScope) to share data.");
    Console.WriteLine("  -h, --help          Show this help.");
    Console.WriteLine();
    Console.WriteLine("Run elevated (Administrator) to capture the ETW kernel sessions.");
}
